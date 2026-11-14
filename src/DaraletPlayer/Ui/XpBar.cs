using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The XP bar: your progress through your level, in the XP Bar window (xp-bar, hidden to start, sized to fit) and at
    /// the top of the Character tab's stats. Its look and what it shows are XpBarOptions (the Character tab's Settings).
    /// Your XP is Decal's, so the bar moves the moment a kill lands; the level's bounds and your rested experience, shaded
    /// ahead of your XP, are the server's (@playerui xp), since the client has no XP table and rested experience is kept by
    /// the server (daralet-ACE's docs/rested-xp.md). XP an hour is worked out here, from Decal's XP as it changes.
    /// </summary>
    internal partial class XpBar : IDisposable {
        /// <summary>The server adds to rested experience every heartbeat, about 5 seconds: no sooner is any use.</summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Once XP has come in since the last reply: soon, for the rested experience it spent, or the next level's bounds.
        /// Not every kill: in a fast fight that would be a request every half second.
        /// </summary>
        private static readonly TimeSpan AfterXpInterval = TimeSpan.FromSeconds(1.5);

        /// <summary>How often Decal's XP is noted for XP an hour.</summary>
        private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint TickColor = PackedColor.Pack(0, 0, 0, 120);
        private static readonly uint BorderColor = PackedColor.Pack(0, 0, 0, 220);
        private static readonly uint TextColor = PackedColor.Pack(255, 255, 255);
        private static readonly uint TextOutline = PackedColor.Pack(0, 0, 0, 230);

        private static readonly Vector2[] Outline = {
            new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1),
        };

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly FloatingWindow window;
        private readonly XpRate rate = new XpRate();

        /// <summary>Whose XP the rate is of: it starts again for another character.</summary>
        private string rateCharacter;
        private DateTime lastSample = DateTime.MinValue;

        /// <summary>The reply last seen, and Decal's XP then: XP since then means it's worth asking again soon.</summary>
        private DateTime replySeen = DateTime.MinValue;
        private long xpAtReply;

        private bool disposed;

        public XpBar(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;

            window = new FloatingWindow("xp-bar", "XP Bar", settings, new Vector2(20, 360), RenderWindow,
                visibleByDefault: false, inUse: WindowInUse, menuItems: RenderMenu);
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        public FloatingWindow Window => window;

        private XpBarOptions Options => settings.Data.XpBar ?? (settings.Data.XpBar = new XpBarOptions());

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            window.Dispose();
        }

        /// <summary>Notes Decal's XP once a second, window or not, so XP an hour is ready whenever it's shown.</summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // Decal's hooks read the game's memory, which isn't there until login is over
                if (disposed || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                var now = DateTime.UtcNow;

                if (now - lastSample < SampleInterval) {
                    return;
                }

                lastSample = now;
                var name = CoreManager.Current.CharacterFilter.Name;
                var xp = DecalXp();

                if (name != rateCharacter) {
                    rateCharacter = name;
                    rate.Reset(now, xp);
                }
                else {
                    rate.Note(now, xp);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// Asked every frame while the window's on, shown or not, so it keeps asking the server even while Only when needed
        /// hides it: something below the highest level.
        /// </summary>
        private object WindowInUse() {
            Ask();
            var xp = feed.Xp;
            return xp != null && !xp.MaxLevel ? xp : null;
        }

        /// <summary>
        /// Asks the server for the level's bounds and rested experience: soon after XP has come in, else now and then.
        /// </summary>
        private void Ask() {
            if (feed.XpUpdatedAt != replySeen) {
                replySeen = feed.XpUpdatedAt;
                xpAtReply = DecalXp();
            }

            feed.Refresh(PlayerFeed.KindXp, DecalXp() != xpAtReply ? AfterXpInterval : RefreshInterval);
        }

        /// <summary>Your total XP as Decal has it, which changes the moment the server says.</summary>
        private static long DecalXp() {
            return CoreManager.Current.CharacterFilter.TotalXP;
        }

        /// <summary>Your total XP now: Decal's, or the server's if it's further on.</summary>
        private static long CurrentXp(XpInfo xp) {
            return Math.Max(DecalXp(), xp.TotalXp);
        }

        /// <summary>Rested experience now: grown while you rest, less what kills since the reply likely spent.</summary>
        private long RestedNow(XpInfo xp) {
            var rested = xp.Rested;

            if (rested == null || !rested.Enabled) {
                return 0;
            }

            return rested.RestedAfter((DateTime.UtcNow - feed.XpUpdatedAt).TotalSeconds, DecalXp() - xpAtReply);
        }

        /// <summary>The window: the bar, then the lines asked for.</summary>
        private void RenderWindow() {
            if (!Ready(out var xp)) {
                return;
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) {
                window.OpenMenu();
            }

            var options = Options;
            var total = CurrentXp(xp);
            var rested = RestedNow(xp);

            DrawBar(xp, total, rested, options.Compact);
            Widgets.Tip(string.Join("\n", Lines(xp, total, rested)));
            RenderWidthGrips();

            // Compact is the bar alone: everything else is in its tooltip
            if (options.Compact) {
                return;
            }

            if (options.ShowRested && options.ShowRestedLine && RestedShown(xp)) {
                RenderRestedLine(xp.Rested, rested);
            }

            if (options.ShowRate) {
                Widgets.TextDisabled(RateText(xp, total, options.ShowTimeToLevel));
                Widgets.Tip($"XP an hour {options.RateWindowText}, worked out from your XP as it comes in. Right-click to "
                    + "start it again.");
            }
        }

        /// <summary>The Character tab's section: the bar and every line.</summary>
        public void RenderSection() {
            Widgets.TextColored(ItemTooltip.Heading, "Experience");
            Widgets.Tip("Your progress through your level, and your rested experience: built up while you're logged out, or "
                + "in a town out of combat mode, it gives your kills more experience until it's used up. Type /rested to "
                + "see it in chat.");

            // Decal's hooks read the game's memory, which isn't there until login is over
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not logged in yet.");
                return;
            }

            Ask();

            if (!Ready(out var xp)) {
                return;
            }

            var total = CurrentXp(xp);
            var rested = RestedNow(xp);
            DrawBar(xp, total, rested, compact: false);

            foreach (var line in Lines(xp, total, rested)) {
                Widgets.TextWrapped(line);
            }
        }

        /// <summary>Says why there's nothing to show yet, if there isn't.</summary>
        private bool Ready(out XpInfo xp) {
            xp = feed.Xp;

            if (!feed.Supports(PlayerFeed.KindXp)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for the XP bar.");
                return false;
            }

            if (xp == null) {
                Widgets.TextDisabled("Loading...");
                return false;
            }

            return true;
        }

        private static bool RestedShown(XpInfo xp) {
            return xp.Rested != null && xp.Rested.Enabled && !xp.MaxLevel;
        }

        /// <summary>
        /// The bar, drawn by hand: the background, rested experience from your XP on, your XP, the dividers, a border, and
        /// its text outlined in the middle.
        /// </summary>
        private void DrawBar(XpInfo xp, long total, long rested, bool compact) {
            var options = Options;
            var text = compact ? "" : options.BarText(xp, total);
            var width = Widgets.Scaled(options.ClampedWidth);
            var height = Widgets.Scaled(options.ClampedHeight);

            // never shorter than its text, or the text spills over what's under it
            if (text.Length > 0) {
                height = Math.Max(height, ImGui.GetTextLineHeight() + 2);
            }

            var min = ImGui.GetCursorScreenPos();
            min = new Vector2((float)Math.Round(min.X), (float)Math.Round(min.Y));
            var max = min + new Vector2((float)Math.Round(width), (float)Math.Round(height));
            var drawList = ImGui.GetWindowDrawList();

            float X(float fraction) => (float)Math.Round(min.X + (max.X - min.X) * fraction);

            drawList.AddRectFilled(min, max, options.BackgroundColor);

            var progress = xp.Progress(total);

            if (options.ShowRested && RestedShown(xp) && rested > 0) {
                var end = xp.RestedEnd(total, rested);

                if (end > progress) {
                    drawList.AddRectFilled(new Vector2(X(progress), min.Y), new Vector2(X(end), max.Y), options.RestedColor);
                }
            }

            if (progress > 0) {
                drawList.AddRectFilled(min, new Vector2(X(progress), max.Y), options.FillColor);
            }

            var ticks = options.ClampedTicks;

            for (var i = 1; i < ticks; i++) {
                var x = X((float)i / ticks);
                drawList.AddLine(new Vector2(x, min.Y + 1), new Vector2(x, max.Y - 1), TickColor);
            }

            drawList.AddRect(min, max, BorderColor);

            if (text.Length > 0) {
                var size = ImGui.CalcTextSize(text);
                var at = new Vector2((float)Math.Round(min.X + (max.X - min.X - size.X) / 2),
                    (float)Math.Round(min.Y + (max.Y - min.Y - size.Y) / 2));

                foreach (var offset in Outline) {
                    drawList.AddText(at + offset, TextOutline, text);
                }

                drawList.AddText(at, TextColor, text);
            }

            ImGui.Dummy(max - min);
        }

        /// <summary>Whether you're resting (or why not), then when it's full, or the bonus while you have some.</summary>
        private static void RenderRestedLine(RestedInfo rested, long left) {
            Widgets.TextColored(StateColor(rested), rested.StateText);
            Widgets.Tip(string.Join("\n", RestedLines(rested, left)));

            var toFull = rested.SecondsToFull(left);

            if (rested.IsResting && toFull > 0) {
                ImGui.SameLine();
                Widgets.TextDisabled($"full in {Widgets.Duration(toFull.Value)}");
            }
            else if (left > 0) {
                ImGui.SameLine();
                Widgets.TextDisabled($"{rested.BonusText}, {XpNumbers.Short(left)} left");
            }
        }

        /// <summary>XP an hour, and how long the next level takes at it.</summary>
        private string RateText(XpInfo xp, long total, bool withTimeToLevel) {
            var options = Options;
            var perHour = rate.PerHour(DateTime.UtcNow, options.RateWindow);

            if (perHour == null) {
                return "XP/hour: measuring...";
            }

            var text = $"{options.Number(perHour.Value)} XP/hour";
            var toNext = xp.ToNextLevel(total);

            if (withTimeToLevel && !xp.MaxLevel && perHour.Value > 0 && toNext > 0) {
                text += $", level in {Widgets.Duration(toNext / perHour.Value * 3600)}";
            }

            return text;
        }

        /// <summary>
        /// Everything: the level, XP an hour, and rested experience, as the tooltip and the Character tab say it.
        /// </summary>
        private List<string> Lines(XpInfo xp, long total, long rested) {
            var options = Options;
            var lines = new List<string>();

            if (xp.MaxLevel) {
                lines.Add($"Level {xp.Level}, the highest.");
            }
            else {
                var percent = (xp.Progress(total) * 100).ToString("0.0");
                lines.Add($"Level {xp.Level}: {xp.IntoLevel(total):N0} of {xp.LevelCost:N0} ({percent}%), "
                    + $"{xp.ToNextLevel(total):N0} to level {xp.Level + 1}.");
            }

            lines.Add($"{RateText(xp, total, true)} ({options.RateWindowText}).");

            if (rate.Started) {
                lines.Add($"{rate.Gained:N0} XP since {rate.StartedAt.ToLocalTime():t}.");
            }

            if (RestedShown(xp)) {
                lines.Add("");
                lines.Add("Rested experience:");
                lines.AddRange(RestedLines(xp.Rested, rested));
            }

            return lines;
        }

        /// <summary>The same as /rested says, in short.</summary>
        private static IEnumerable<string> RestedLines(RestedInfo rested, long left) {
            var percent = rested.Max > 0 ? Math.Min(100, left * 100 / rested.Max) : 0;
            var toFull = rested.SecondsToFull(left);
            var slowdown = Math.Round(Math.Min(1, rested.SlowdownPerLevel) * 100);

            return new[] {
                rested.HasMost ? $"{left:N0} of {rested.Max:N0} ({percent}%)." : $"{left:N0}{LevelsWorth(rested, left)}.",
                left > 0 ? $"Your kills get {rested.BonusText}, taken from it, until it's used up."
                    : !rested.CanRest ? "" : $"With some, your kills would get {rested.BonusText}.",
                rested.StateText + (rested.IsResting && toFull > 0 ? $", full in {Widgets.Duration(toFull.Value)}." : "."),
                rested.PerHour > 0 && rested.CanRest ? WhereYouRest(rested, left) : "",
                rested.IsSlowed(left) && rested.CanRest
                    ? $"It builds up {slowdown:0}% slower for each full level's worth you've rested." : "",
                rested.EarnedThisSession > 0
                    ? $"Earned {rested.EarnedThisSession:N0} more from kills since you logged in." : "",
            }.Where(line => line.Length > 0);
        }

        /// <summary>How fast in town, and logged out at the server's offline rate.</summary>
        private static string WhereYouRest(RestedInfo rested, long left) {
            var inTown = rested.RateAt(left);
            var offline = Math.Max(0, rested.OfflineRate ?? 1);

            if (Math.Abs(offline - 1) < 1e-9) {
                return $"Resting adds {inTown:N0} an hour: in a town out of combat mode, or logged out.";
            }

            if (offline <= 0) {
                return $"Resting adds {inTown:N0} an hour in a town out of combat mode.";
            }

            return $"Resting adds {inTown:N0} an hour in a town out of combat mode, {inTown * offline:N0} logged out.";
        }

        /// <summary>", 1.5 levels' worth", or nothing when the level's cost isn't known (an older server).</summary>
        private static string LevelsWorth(RestedInfo rested, long left) {
            return rested.LevelCost > 0 ? $", {(double)left / rested.LevelCost:0.0#} levels' worth" : "";
        }

        private static Vector4 StateColor(RestedInfo rested) {
            switch (rested.State) {
                case RestedInfo.Resting:
                case RestedInfo.Full:
                    return Colors.Good;
                case RestedInfo.InCombat:
                    return Colors.Warning;
                default:
                    return Colors.Muted;
            }
        }

        /// <summary>The window's right-click menu: Compact, and XP an hour.</summary>
        private void RenderMenu() {
            var options = Options;

            if (ImGui.MenuItem("Compact", "", options.Compact)) {
                options.Compact = !options.Compact;
                settings.Changed();
            }

            if (ImGui.MenuItem("Show XP/hour", "", options.ShowRate)) {
                options.ShowRate = !options.ShowRate;
                settings.Changed();
            }

            if (ImGui.MenuItem("Reset XP/hour")) {
                ResetRate();
            }

            ImGui.Separator();
        }

        private void ResetRate() {
            if (CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                rate.Reset(DateTime.UtcNow, DecalXp());
            }
        }
    }
}
