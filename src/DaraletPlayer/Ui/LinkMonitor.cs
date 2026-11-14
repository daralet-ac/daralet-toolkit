using System;
using System.Diagnostics;
using DaraletPlayer.Feed;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Link Status window's measuring: the game's own ping (AcClient's CM_Character.Event_RequestPing, game action
    /// 0x01E9, the one the game's link panel sends), every interval while wanted, timed to the server's PingResponse (game
    /// event 0x01EA, PingMessages) as Decal's EchoFilter hears it, one out at a time (LinkPinger), into a LinkHistory.
    /// Another character, or logging out, starts the history again. Spikes and lost pings go in log.txt as [link] lines.
    /// </summary>
    internal sealed class LinkMonitor : IDisposable {
        /// <summary>The game's link indicator, whose state (good, fair, poor, lost) each ping notes.</summary>
        private const uint LinkIndicator = 0x100000F8;

        /// <summary>At most this many [link] lines a session: a bad connection would spike all evening.</summary>
        private const int MaxLogLines = 200;

        private readonly LinkPinger pinger = new LinkPinger();
        private readonly Func<bool> wanted;
        private readonly Func<int> interval;
        private readonly Func<int> spikeMargin;

        private int character;
        private int logLines;
        private bool broken;

        /// <param name="wanted">Whether to ping now.</param>
        /// <param name="interval">Seconds between pings.</param>
        /// <param name="spikeMargin">Milliseconds over the usual that make a spike, for log.txt.</param>
        public LinkMonitor(Func<bool> wanted, Func<int> interval, Func<int> spikeMargin) {
            this.wanted = wanted;
            this.interval = interval;
            this.spikeMargin = spikeMargin;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        public LinkHistory History { get; } = new LinkHistory();

        /// <summary>Seconds, on the clock the history's times are on.</summary>
        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>When the ping out now was sent (Now's clock); null with none out.</summary>
        public double? Waiting => pinger.Waiting;

        /// <summary>The ping can't be sent in this client: nothing will be measured.</summary>
        public bool Broken => broken;

        /// <summary>The game's own link indicator now, or null when it can't be read.</summary>
        public static uint? GameState() {
            return ClientElements.GetState(LinkIndicator);
        }

        public void Clear() {
            History.Clear();
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                var now = Now;
                var filter = CoreManager.Current.CharacterFilter;

                if (filter.LoginStatus != 3) {
                    if (character != 0) {
                        pinger.Reset(now);
                        character = 0;
                    }

                    return;
                }

                if (filter.Id != character) {
                    History.Clear();
                    pinger.Reset(now);
                    character = filter.Id;
                }

                var lost = pinger.TimedOut(now);

                if (lost != null) {
                    History.Add(lost.Value, null, GameState());
                    Log($"no answer to a ping in {LinkPinger.LostAfter:0}s");
                }

                if (broken || !wanted() || !pinger.ShouldSend(now, interval())) {
                    return;
                }

                AcClient.CM_Character.Event_RequestPing();
                pinger.Sent(now);
            }
            catch (Exception ex) {
                // the client's function not being where UtilityBelt says would throw every frame
                broken = true;
                PluginCore.LogFile("[link] couldn't send the game's ping, so the Link Status window can't measure: " + ex);
            }
        }

        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != CooldownMessages.GameEvent || !PingMessages.IsPingResponse(e.Message.RawData)) {
                    return;
                }

                var sent = pinger.Waiting;
                var ms = pinger.Answered(Now);

                if (sent == null || ms == null) {
                    return;
                }

                var sample = History.Add(sent.Value, ms, GameState());

                if (sample.IsSpike(spikeMargin())) {
                    Log($"spike: {ms:0} ms, usually {sample.Baseline:0}");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[link] " + ex);
            }
        }

        private void Log(string text) {
            if (logLines >= MaxLogLines) {
                return;
            }

            logLines++;
            PluginCore.LogFile("[link] " + text + (logLines == MaxLogLines ? " (no more [link] lines this session)" : ""));
        }

        public void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
        }
    }
}
