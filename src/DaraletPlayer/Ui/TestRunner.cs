using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DaraletPlayer.Feed;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Runs a test script in the game (Feed/TestScript.cs says what a script holds), so a build can be checked without
    /// someone at the keyboard (the user's call, November 2026). The developer launches the game once from
    /// ShadowLauncher with a default character and Auto relaunch on; a build lands by the loader's hot reload; a script
    /// in Documents\Decal Plugins\Daralet Toolkit\Test Runs\run.txt is run once logged in, one step at a time on Decal's
    /// RenderFrame, and what happened goes in results.txt beside it, each line timed.
    /// <para>
    /// Crashes: the step being run is written to progress.txt before it starts and the file deleted when the script
    /// ends. Found at login (after Auto relaunch brought the game back), it means the game died on that step: that goes
    /// in the results and the script is dropped, so a step that crashes the game can't do it again and again.
    /// restart is the one deliberate exit: the steps after it are written back to run.txt, progress.txt deleted, and
    /// the game exited (the Options window's Exit Game, ClientMenu.ExitGame), so they run after the relaunch.
    /// </para>
    /// `[test-run]` lines in log.txt too. Nothing happens without a run.txt, so players never see any of it.
    /// </summary>
    internal sealed partial class TestRunner : IDisposable {
        private const double SettleQuietSeconds = 3;

        private static readonly string Folder = DataFolder.For("Test Runs");
        private static readonly string RunFile = Path.Combine(Folder, "run.txt");
        private static readonly string ResultsFile = Path.Combine(Folder, "results.txt");
        private static readonly string ProgressFile = Path.Combine(Folder, "progress.txt");

        private readonly Settings settings;
        private readonly DrawDistance drawDistance;
        private readonly Action changed;

        private List<TestStep> steps;
        private int index;
        private DateTime stepStarted;
        private DateTime atTargetSince = DateTime.MaxValue;
        private DateTime nextLookForRun;
        private bool disposed;

        /// <summary>
        /// Until when a chat step's answer is noted in results.txt: what /loc says (for /teleloc back to the same
        /// place and facing), what a command says went wrong.
        /// </summary>
        private DateTime noteChatUntil = DateTime.MinValue;

        /// <summary>RenderFrames since a probe started: it's finished on the next one.</summary>
        private int probeFrames;

        /// <summary>
        /// Set by restart: the game is closing, so nothing more is run in it. The first build looked for run.txt again
        /// in the moment before the game closed, started the steps meant for after the relaunch, and the relaunched game
        /// then took the half-run step for a crash.
        /// </summary>
        private bool exiting;

        public TestRunner(Settings settings, DrawDistance drawDistance, Action changed) {
            this.settings = settings;
            this.drawDistance = drawDistance;
            this.changed = changed;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            CoreManager.Current.ChatBoxMessage += Core_ChatBoxMessage;
        }

        private void Core_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                // not the plugins' own replies (DAP2|..., DAA2|...), which the feed hides from chat anyway
                if (DateTime.Now < noteChatUntil && !string.IsNullOrWhiteSpace(e.Text)
                    && !ReplyAssembler.LooksLikeReplyPiece(e.Text) && !e.Text.StartsWith("DA")) {
                    Result("chat reply: " + e.Text.Trim());
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[test-run] chat reply: " + ex.Message);
            }
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed || exiting || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                if (steps == null) {
                    LookForRun();
                    return;
                }

                Step();
            }
            catch (Exception ex) {
                Result("stopped: the runner threw " + ex.GetType().Name + ": " + ex.Message);
                PluginCore.LogFile("[test-run] " + ex);
                End();
            }
        }

        /// <summary>Once a second: a run.txt waiting, or a run the game died in.</summary>
        private void LookForRun() {
            if (DateTime.UtcNow < nextLookForRun) {
                return;
            }

            nextLookForRun = DateTime.UtcNow.AddSeconds(1);

            if (File.Exists(ProgressFile)) {
                var died = File.ReadAllText(ProgressFile).Trim();
                Result("the game stopped during: " + died + " (the script is dropped; see log.txt and the event log)");
                File.Delete(ProgressFile);

                if (File.Exists(RunFile)) {
                    File.Delete(RunFile);
                }

                return;
            }

            if (!File.Exists(RunFile)) {
                return;
            }

            steps = TestScript.Parse(File.ReadAllLines(RunFile));
            index = 0;
            stepStarted = DateTime.MinValue;
            Result($"started: {steps.Count} steps, {drawDistance.Loaded}, {settings.Data.DrawDistance.Summary()}");
        }

        private void Step() {
            if (index >= steps.Count) {
                Result("done");
                End();
                return;
            }

            var step = steps[index];
            var now = DateTime.UtcNow;

            if (stepStarted == DateTime.MinValue) {
                stepStarted = now;
                atTargetSince = DateTime.MaxValue;
                File.WriteAllText(ProgressFile, $"line {step.Line}: {step.Text}");

                if (!Begin(step)) {
                    return;
                }
            }

            if (!Finished(step, now)) {
                return;
            }

            index++;
            stepStarted = DateTime.MinValue;
        }

        /// <summary>A step's start; false when it ended the run (bad or restart).</summary>
        private bool Begin(TestStep step) {
            switch (step.Kind) {
                case "bad":
                    Result($"line {step.Line} can't be read ({step.Words}): stopped");
                    End();
                    return false;
                case "set":
                    Set(step.Field, step.Number);
                    Result($"set {step.Field} {step.Number.ToString(CultureInfo.InvariantCulture)}: "
                        + GraphicsSummary.Of(settings.Data));
                    return true;
                case "option": {
                    var option = uint.Parse(step.Field);

                    if (step.Number < 0) {
                        Result($"option {option} is {ClientOptions.Get(option)?.ToString() ?? "unknown"}");
                        return true;
                    }

                    var set = ClientOptions.Set(option, step.Number != 0);
                    Result($"option {option} {(step.Number != 0 ? "on" : "off")}: "
                        + (set ? $"now {ClientOptions.Get(option)}" : "couldn't be set"));
                    return true;
                }
                case "usedoor": {
                    // the nearest door within 15 m used, as a click on it would (opening or closing it)
                    var you = CoreManager.Current.Actions;
                    Decal.Adapter.Wrappers.WorldObject nearest = null;
                    var best = 15.0;

                    foreach (var o in CoreManager.Current.WorldFilter.GetLandscape()) {
                        if (o.ObjectClass != Decal.Adapter.Wrappers.ObjectClass.Door) {
                            continue;
                        }

                        // Decal's distance is in map units (a degree of the map, 240 m)
                        var d = CoreManager.Current.WorldFilter.Distance(CoreManager.Current.CharacterFilter.Id, o.Id) * 240;

                        if (d < best) {
                            best = d;
                            nearest = o;
                        }
                    }

                    if (nearest == null) {
                        Result("usedoor: no door within 15 m");
                    }
                    else {
                        you.UseItem(nearest.Id, 0);
                        Result($"usedoor: {nearest.Name} {best:0.0} m off");
                    }

                    return true;
                }
                case "facesaved":
                    CoreManager.Current.Actions.FaceHeading(pendingFace, true);
                    Result($"face {pendingFace:0} (saved)");
                    return true;
                case "gowaterside":
                    GoWaterside((int)step.Number);
                    return true;
                case "gohighest":
                    GoHighest((int)step.Number);
                    return true;
                case "chat":
                    Result("chat: " + step.Words);
                    noteChatUntil = DateTime.Now.AddSeconds(2);
                    CoreManager.Current.Actions.InvokeChatParser(step.Words);
                    return true;
                case "log":
                    Result(step.Words);
                    return true;
                case "find": {
                    // where things of a name are (a test run's way to a place by its NPCs): each nearby object whose name
                    // has the words, with its landcell and place in its landblock, as /teleloc takes them
                    var found = 0;

                    foreach (var wo in CoreManager.Current.WorldFilter.GetLandscape()) {
                        // by name or by template (an NPC's title under its name, "Elder"), which Decal only has once
                        // the thing's been appraised
                        var template = wo.Values((Decal.Adapter.Wrappers.StringValueKey)5, "");

                        if ((wo.Name ?? "").IndexOf(step.Words, StringComparison.OrdinalIgnoreCase) < 0
                            && template.IndexOf(step.Words, StringComparison.OrdinalIgnoreCase) < 0) {
                            continue;
                        }

                        var raw = wo.RawCoordinates();
                        var cell = unchecked((uint)wo.Values(Decal.Adapter.Wrappers.LongValueKey.Landblock, 0));
                        var titled = template.Length > 0 ? $"{wo.Name} ({template})" : wo.Name;
                        Result($"find: {titled} at {cell:X8} [{raw.X:0.00} {raw.Y:0.00} {raw.Z:0.00}]");

                        if (++found >= 10) {
                            break;
                        }
                    }

                    if (found == 0) {
                        // appraised now, so a find after a short wait can match their templates
                        var asked = 0;

                        foreach (var wo in CoreManager.Current.WorldFilter.GetLandscape()) {
                            if ((wo.ObjectClass == Decal.Adapter.Wrappers.ObjectClass.Npc
                                    || wo.ObjectClass == Decal.Adapter.Wrappers.ObjectClass.Vendor) && !wo.HasIdData
                                && asked < 30) {
                                CoreManager.Current.Actions.RequestId(wo.Id);
                                asked++;
                            }
                        }

                        Result($"find: nothing called {step.Words} nearby; appraising {asked} NPCs for their templates");
                    }

                    return true;
                }
                case "restart":
                    Restart();
                    return false;
                case "kill":
                    Restart(kill: true);
                    return false;
                case "where": {
                    // where you are, for coming back (a test run's record; the game's /loc only says it in chat)
                    var actions = CoreManager.Current.Actions;
                    Result($"where: landcell {unchecked((uint)actions.Landcell):X8} at {actions.LocationX:0.00} "
                        + $"{actions.LocationY:0.00} {actions.LocationZ:0.00}, heading {actions.Heading:0}");
                    return true;
                }
                case "memory": {
                    // for leaks: the address space free (what runs the game out), its largest piece, what Windows counts
                    // for the game, and the managed heap after a full collection, so only what's still held counts
                    var (free, largest, pieces) = FrameRateWindow.FreePieces();
                    var process = System.Diagnostics.Process.GetCurrentProcess();
                    var managed = GC.GetTotalMemory(true) >> 20;
                    Result($"memory {step.Text}: {free >> 20} MB free, largest piece {largest >> 20} MB in {pieces}, "
                        + $"private {process.PrivateMemorySize64 >> 20} MB, managed {managed} MB, handles "
                        + $"{process.HandleCount}; {drawDistance.Loaded}; landcell "
                        + $"{unchecked((uint)CoreManager.Current.Actions.Landcell):X8}");
                    return true;
                }
                case "flushcache":
                    Result($"flushcache: {GameCache.FlushFree("a test run")} MB given back");
                    return true;
                case "compare":
                    Result(drawDistance.CompareScenery());
                    return true;
                case "camera": {
                    // where the camera is, as the game keeps it (the Position at 0x81FF10: cell +4, origin +0x3C) and as the
                    // device has it (the view's inverse), against your character: which landblock the device's world is
                    // measured from
                    var device = UtilityBelt.Service.UBService.Huds.D3Ddevice;
                    var actions = CoreManager.Current.Actions;
                    unsafe {
                        var cell = *(uint*)(0x81FF10 + 4);
                        var x = *(float*)(0x81FF10 + 0x3C);
                        var y = *(float*)(0x81FF10 + 0x40);
                        var z = *(float*)(0x81FF10 + 0x44);
                        var inverse = Microsoft.DirectX.Matrix.Invert(device.Transform.View);
                        Result($"camera: the game's cell {cell:X8} at {x:0.00} {y:0.00} {z:0.00}; the device's at "
                            + $"{inverse.M41:0.00} {inverse.M42:0.00} {inverse.M43:0.00}; you {unchecked((uint)actions.Landcell):X8} "
                            + $"at {actions.LocationX:0.00} {actions.LocationY:0.00} {actions.LocationZ:0.00}");
                    }

                    return true;
                }
                case "lights":
                    // the game's own lights near the camera (Ui/GameLights.cs)
                    Result("lights: " + GameLights.Describe(unchecked((uint)CoreManager.Current.Actions.Landcell) >> 16));
                    return true;
                case "probe": {
                    // the game's Direct3D calls over one frame (Ui/DrawProbe.cs), finished on the next RenderFrame
                    var device = UtilityBelt.Service.UBService.Huds.D3Ddevice;

                    if (device == null) {
                        Result("probe: no device");
                        return true;
                    }

                    DrawProbe.Start(device);
                    probeFrames = 0;
                    return true;
                }
                case "face":
                    // the game turns the character itself, over a moment: a wait after lets it finish
                    CoreManager.Current.Actions.FaceHeading(step.Number, true);
                    Result($"face {step.Number:0}");
                    return true;
                case "walkshots":
                    walkShotsTaken = 0;
                    CoreManager.Current.Actions.SetAutorun(true);
                    return true;
                case "walk":
                    // the game's autorun, stopped once the time is up (Finished)
                    CoreManager.Current.Actions.SetAutorun(true);
                    return true;
                case "screenshot": {
                    var name = string.Concat(step.Words.Split(Path.GetInvalidFileNameChars()));
                    var path = Path.Combine(Folder, "shots", $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {name}.png");
                    var problem = GameScreenshot.Save(path);
                    Result(problem == null ? "screenshot: " + path : "screenshot failed: " + problem);
                    return true;
                }
                default:
                    return true;
            }
        }

        /// <summary>Whether a step that takes time is done.</summary>
        private bool Finished(TestStep step, DateTime now) {
            var elapsed = (now - stepStarted).TotalSeconds;

            switch (step.Kind) {
                case "wait":
                    return elapsed >= step.Number;
                case "probe":
                    if (!DrawProbe.Running || ++probeFrames < 2) {
                        return !DrawProbe.Running;
                    }

                    Result("probe: " + DrawProbe.Finish());
                    return true;
                case "walkshots": {
                    var shots = int.Parse(step.Field);

                    // one due every span / shots seconds, the first at once
                    if (walkShotsTaken < shots && elapsed >= walkShotsTaken * step.Number / shots) {
                        walkShotsTaken++;
                        var path = Path.Combine(Folder, "shots", $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} walk-{walkShotsTaken:00}.png");
                        var problem = GameScreenshot.Save(path);
                        var a = CoreManager.Current.Actions;
                        Result($"walk shot {walkShotsTaken}: {(problem ?? "saved")}, landcell {unchecked((uint)a.Landcell):X8} at "
                            + $"{a.LocationX:0.00} {a.LocationY:0.00}");
                    }

                    if (elapsed < step.Number || walkShotsTaken < shots) {
                        return false;
                    }

                    CoreManager.Current.Actions.SetAutorun(false);
                    return true;
                }
                case "walk": {
                    if (elapsed < step.Number) {
                        return false;
                    }

                    var actions = CoreManager.Current.Actions;
                    actions.SetAutorun(false);
                    Result($"walked {step.Number:0} s: landcell {unchecked((uint)actions.Landcell):X8} at "
                        + $"{actions.LocationX:0.00} {actions.LocationY:0.00} {actions.LocationZ:0.00}, "
                        + $"heading {actions.Heading:0}");
                    return true;
                }
                case "measure":
                    if (elapsed < step.Number) {
                        return false;
                    }

                    Measure(step.Number);
                    return true;
                case "settle":
                    // reached what's set, then a few quiet seconds for the last step's loading
                    if (drawDistance.AtTarget) {
                        atTargetSince = atTargetSince == DateTime.MaxValue ? now : atTargetSince;
                    }
                    else {
                        atTargetSince = DateTime.MaxValue;
                    }

                    if (atTargetSince != DateTime.MaxValue && (now - atTargetSince).TotalSeconds >= SettleQuietSeconds) {
                        Result($"settled after {elapsed:0} s: {drawDistance.Loaded}");
                        return true;
                    }

                    if (elapsed >= step.Number) {
                        Result($"didn't settle in {step.Number:0} s: {drawDistance.Loaded}");
                        return true;
                    }

                    return false;
                default:
                    return true;
            }
        }

        private void Set(string field, double number) {
            var options = settings.Data.DrawDistance;
            var value = (int)Math.Round(number);

            switch (field) {
                case "on":
                    options.On = value != 0;
                    break;
                case "scenery":
                    options.SceneryRadius = value;
                    break;
                case "density":
                    options.SceneryDensity = value;
                    break;
                case "landscape":
                    options.LandscapeRadius = value;
                    break;
                case "detail":
                    options.DetailDistance = value;
                    break;
                case "far":
                    options.LowDetailDistance = value;
                    break;
                case "shading":
                    settings.Data.Shading.On = value != 0;
                    break;
                case "occlusion":
                    settings.Data.Shading.Occlusion = value;
                    break;
                case "shadows":
                    settings.Data.Shading.Shadows = value;
                    break;
                case "water":
                    settings.Data.Water.On = value != 0;
                    break;
                case "waterstrength":
                    settings.Data.Water.Strength = value;
                    break;
                case "ripples":
                    settings.Data.Water.Ripples = value != 0;
                    break;
                case "ripplesize":
                    settings.Data.Water.RippleSize = value;
                    break;
                case "fresnel":
                    settings.Data.Water.Fresnel = value != 0;
                    break;
                case "glints":
                    settings.Data.Water.Glints = value != 0;
                    break;
                case "blur":
                    settings.Data.Water.Blur = value != 0;
                    break;
                case "softshore":
                    settings.Data.Water.SoftShore = value != 0;
                    break;
                case "antialias":
                    settings.Data.Image.AntiAlias = value != 0;
                    break;
                case "sharpen":
                    settings.Data.Image.Sharpen = value;
                    break;
                case "grass":
                    settings.Data.Grass.On = value != 0;
                    break;
                case "grassdensity":
                    settings.Data.Grass.Density = value;
                    break;
                case "grassdistance":
                    settings.Data.Grass.Distance = value;
                    break;
                case "grassheight":
                    settings.Data.Grass.Height = value;
                    break;
                case "reeds":
                    settings.Data.Grass.Reeds = value != 0;
                    break;
                case "keptnear":
                    settings.Data.Grass.KeptNearTowns = value != 0;
                    break;
                case "wind":
                    settings.Data.Grass.Wind = value != 0;
                    break;
                case "sunshadows":
                    settings.Data.SunShadows.On = value != 0;
                    break;
                case "sunstrength":
                    settings.Data.SunShadows.Strength = (int)value;
                    break;
                case "sundistance":
                    settings.Data.SunShadows.Distance = (int)value;
                    break;
                case "sunreplay":
                    DepthShading.SkipReplay = value == 0;
                    break;
                case "lamps":
                    settings.Data.LampLight.On = value != 0;
                    break;
                case "lampstrength":
                    settings.Data.LampLight.Strength = (int)value;
                    break;
                case "lampshadows":
                    settings.Data.LampLight.Shadows = value != 0;
                    break;
                case "glow":
                    settings.Data.Image.Glow = (int)value;
                    break;
                case "charsoftness":
                    settings.Data.SunShadows.CharacterSoftness = (int)value;
                    break;
                case "sunsoftness":
                    settings.Data.SunShadows.Softness = (int)value;
                    break;
                case "anisotropy":
                    settings.Data.Image.Anisotropy = (int)value;
                    break;
                case "depthview":
                    DepthShading.DebugView = value;
                    break;
                case "fog":
                    options.Fog = value;
                    break;
                case "billboards":
                    options.Billboards = value != 0;
                    break;
                case "ground":
                    options.GroundRadius = (int)value;
                    break;
                case "videomemory":
                    options.VideoMemory = value != 0;
                    break;
                case "portalreload":
                    DrawDistance.PortalReload = value != 0;
                    break;
                case "fartrees":
                    options.FarTrees = (int)value;
                    break;
                case "farbuildings":
                    options.FarBuildings = value != 0;
                    break;
                case "lampreplace":
                    settings.Data.LampLight.Replace = value != 0;
                    break;
                case "lampreach":
                    settings.Data.LampLight.Reach = (int)value;
                    break;
                case "walkmode":
                    WalkMode(value != 0);
                    break;
                case "timeofday":
                    TimeOfDay.Pin(number);
                    break;
                case "night":
                    settings.Data.LampLight.NightDarkness = (int)value;
                    break;
                case "nightglow":
                    settings.Data.LampLight.NightGlow = (int)value;
                    break;
                case "lighttrace":
                    DepthShading.Trace = value != 0;
                    break;
                case "farlanddebug":
                    FarLand.Debug = value != 0;
                    break;
                case "farlandnear":
                    options.FarLandNear = value != 0;
                    break;
                case "farland":
                    options.FarLand = (int)value;
                    break;
            }

            changed();
        }

        private void Measure(double seconds) {
            var frames = FrameRateWindow.Recent(seconds);
            var rate = frames == null ? "no frame rate (Frame Rate window not running)"
                : $"{frames.Fps:0.0} fps, 1% low {frames.LowFps:0.0}, worst {frames.WorstMs:0} ms, {frames.Hitches} pauses";
            Result($"measure {seconds:0} s: {rate}; {DrawDistance.FreeMegabytes()} MB free; {drawDistance.Loaded}; "
                + drawDistance.BillboardState);
        }

        /// <summary>The steps after this one written back for after the relaunch, and the game exited.</summary>
        /// <remarks>
        /// With kill, the game's process is ended outright instead of asked to exit: a restart's exit once never came
        /// (memory short after many hot reloads), and the game runs as administrator, so nothing outside can end it.
        /// </remarks>
        private void Restart(bool kill = false) {
            var rest = steps.Skip(index + 1).Select(s => s.Text).ToList();

            if (rest.Count > 0) {
                File.WriteAllLines(RunFile, rest);
            }
            else if (File.Exists(RunFile)) {
                File.Delete(RunFile);
            }

            if (File.Exists(ProgressFile)) {
                File.Delete(ProgressFile);
            }

            Result($"{(kill ? "kill" : "restart")}: ending the game, {rest.Count} steps left for after Auto relaunch");
            steps = null;
            exiting = true;

            if (kill) {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }

            ClientMenu.ExitGame();
        }

        /// <summary>The run over: its files cleared, ready for the next run.txt.</summary>
        private void End() {
            steps = null;

            foreach (var file in new[] { RunFile, ProgressFile }) {
                if (File.Exists(file)) {
                    File.Delete(file);
                }
            }
        }

        private static void Result(string line) {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(ResultsFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            PluginCore.LogFile("[test-run] " + line);
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            CoreManager.Current.ChatBoxMessage -= Core_ChatBoxMessage;

            // the device's own calls back before this copy goes
            try {
                if (DrawProbe.Running) {
                    DrawProbe.Finish();
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[test] couldn't end the probe: " + ex.Message);
            }

            // stopped mid-walk, the character would run on by itself
            try {
                if (steps != null && index < steps.Count && (steps[index].Kind == "walk" || steps[index].Kind == "walkshots")) {
                    CoreManager.Current.Actions.SetAutorun(false);
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[test] couldn't stop walking: " + ex.Message);
            }

            // a hot reload (or the plugin stopping) mid-run isn't a crash: the step running and those after it are
            // handed to the next copy, which starts them afresh. The first build left progress.txt, and the new copy
            // dropped the script as if the game had died
            try {
                if (steps != null && !exiting) {
                    var rest = steps.Skip(index).Select(s => s.Text).ToList();
                    File.WriteAllLines(RunFile, rest);

                    if (File.Exists(ProgressFile)) {
                        File.Delete(ProgressFile);
                    }

                    Result($"the plugin stopped (a reload?): {rest.Count} steps handed on");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[test-run] " + ex);
            }
        }
    }
}
