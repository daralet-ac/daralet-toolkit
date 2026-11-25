# Session handoff: plugin-drawn far land, then lighting (November 19, 2026)

## NOW (November 25, 2026): option (2) below done, built to $TEMP/fl only, not yet in bin or the game

- Billboard pictures: dats read on the game's thread, decode/draw/mips on up to two pool threads
  (`BillboardModels.StartDrawing`, `Collect` 3 ms a frame); placing waits for `Pending == 0`, so no pop-in.
  Log: `[billboards] model ...: drawn in N ms on a worker`. Batches with a null texture (failed drawing) are skipped.
- Far land's login setup: `FarLand.StartTerrain` reads textures and alpha maps, a worker decodes and shrinks;
  far land waits on `terrainReady`. Log: `[far-land] terrain pictures and alpha maps made in N ms on a worker`.
- Tried in the game (09:41 teleport into a forest, the user's own build): billboard pictures 1-24 ms each on workers,
  no hitch from them. But 3D tree / far building models took 3.3 s of the game's thread (527 models, 316 textures:
  decode 1.2 s, mips+upload 1.3 s, geometry 0.8 s; worst 349 ms), the 44-116 ms "billboards" hitches.
- Then moved (built to $TEMP/fl, tests pass, not in the game): `FarTreeModels.TextureFor` reads the surface, a worker
  decodes and mips, `Collect` uploads 3 ms a frame; placing waits for both `Pending`. New `FarTreeTexture` shared by
  surface; `FarTreePart.Texture`/`CutOut` read through it. Log: `[far-trees] N textures made: ... ms on workers,
  uploads ... ms`; each model line gives its game-thread ms. Left on the game's thread: geometry and buffers (up to
  96 ms a model): next if hitches remain, split a model's dat reading from its buffer making.
- To try: build into bin (ask first), teleport into a forest, compare `[hitch]` lines with 09:41's.

## Earlier (November 23, 2026, 11:15): lag spikes while running around

Staged, not committed (the user commits): the user's "hangs and lag spikes every few seconds while running".
- **Fixed, tried in the game:** at every landblock crossing the billboards' vertex buffer (about 258,000 billboards,
  99 MB) was written in one frame, 430 ms: frames of 444-457 ms in the frame rate log. Now `Ui/DrawDistance.Writing.cs`
  sorts the billboards by model run (`writeOrder`) and writes 4 ms a frame (`WriteSome`), the old ones drawn until
  `SwapBillboards`. In the game: the write spread over about 95 frames, the worst crossing frame 40-83 ms.
- **Fixed:** the address-space walk (`FreePieces`, 22-26 ms, now timed in the `[device]` line) and Process.Refresh ran
  on the game's thread every 10 s (a 40 ms frame); now on the thread pool (`FrameRateWindow.Memory.cs`, `.Device.cs`).
- **New:** `Ui/HitchLog.cs`: a `[hitch]` line in log.txt for every frame over 40 ms with the game in front: frame length,
  any garbage collection, heap size, the plugin's timed pieces, and the rest.
- **Left (run 11:11-11:13 in log.txt):** hitches only at crossings, about every 20 s (not 3-5 s): 40-80 ms, the
  plugin's measured code 5-11 ms, the rest 30-70 ms with nearly always a gen 1 collection: the game's own landblock
  load or the collection, not yet told apart. Next, the user's pick between: (1) a scripted walk (`walk S`, `measure S`)
  with far land, billboards and grass off, then on, to see whether the crossing hitches are the game's; if they're the
  plugin's, cut what the rebuilds allocate (or rebuild only changed blocks, as grass chunks do). (2) Billboard model
  pictures drawn in software on the game's thread, 30-170 ms each when first met (new trees in view, login): move to a
  worker thread. Also at login: the far land's one-time setup took 594 ms in one frame (terrain textures, blend maps,
  atlases): spread it over frames.

## NEXT (November 22, 2026): lighting pass, starting with the flicker on trees and buildings

The user, in their words: "There is so much flickering. Mostly on landscape objects, such as trees and buildings. When
approaching them the entire object appears to either gain or lose lighting in an instant. Backing up beyond that
threshold changes it back, and forth." A distance threshold per object, whole objects at once, by day too; seen "all
over the place", e.g. `/teleloc 0x7F900026 [104.903023 133.630280 144.004990] -0.972592 0 0 -0.702191`.

Suspects, to test one at a time before changing anything: (1) sun shadows: the user's Distance is 1000 m, so the far
map's texels are about 1 m and its normal offset (2 to 5 texels) metres; the near map covers 40 m (`NearReach`), the
close map 8 m; an object crossing a map's edge switches maps and can flip lit/shadowed as a whole (`DepthShading.
Shadows.cs`, `shadow.ps.hlsl`, check the near-to-far blend); (2) lamp light's handover (`GameLampSwitch.cs`,
`DepthShading.Lamps*.cs`: lamps entering and leaving the six with cube maps, the game's per-vertex lamp light dimmed or
refused for those), untested in the game; (3) the game's own levels of detail, and the far trees / billboards rings;
(4) occlusion and night darkening. Test with run.txt steps (`set sunshadows|sundistance|lamps|lampshadows|shading|
occlusion`, `walk`, `face`, `screenshot`, `flicker S`), or ask the user to flip one setting and look. Ask the user before
queuing test runs or building into `bin`; build tests to a temp folder (`-p:OutputPath=<temp>/ -p:BaseOutputPath=<temp>/`)
since a plain `dotnet test` builds into `bin` and hot-reloads their game. Then the rest of the lighting list below ("Next",
item 2). Stage, never commit.

## NOW (November 21, 2026, 19:40): the ImGui lead closed

The `cimgui.dll+0x1FDF72` stop under page heap backwards is **page heap running the game out of address space**, not
an ImGui bug: `imgui-memory.sh` (page heap backwards, `Test Runs\memory-watch.txt`, the plugin logging free memory
once a second from its own thread) showed 2,027 MB free at the plugin's start, 369 MB at 3 s, 12 MB and no free piece
of 1 MB at 4 s, then stuck there with the game hung (the "ImGui error" box the user saw). With ProcDump attached
(`imgui-capture.sh`, the plugin held at start by `Test Runs\hold-for-debugger.txt` until a debugger is in) the game
hung the same way and no dump came. So full page heap is no use for this 32-bit game; minimal mode wasn't run (moot).
Left: page heap on part of the allocations (`/size`, `/random`), or TTD on a local server. The helper
(`tools\debug-service.ps1`) now also sets page heap itself from `Test Runs\pageheap.txt` (backwards|off). Setup
restored after: page heap off (key gone), flag files gone, the game relaunched on the Default profile.

## Earlier (17:55): the teleport crash hunt, where it stands

**Nothing running.** The user's setup is back: Default profile and the new Quality preset (restored 17:51), Daralet
Admin's loader on again (HKCU NetworkFilters `{B3E0A5D2-...}` Enabled 1), Content Override left off (their choice),
page heap off (`pageheap-off.ps1` run), the crash watcher and keep-awake helpers stopped, Test Runs' flag files gone
(minimal.txt, dumps.txt, debug-mode.txt). Kept: two full crash dumps and the Environment's two copies in `D:\Dumps`.

**Answer to "does the plugin raise the crash rate?": not shown.** The morning's numbers (minimal 4 in 204 against bare
9 in 202; chains minimal 1 in 50, minimal plus WorldPass's hook 3 in 49) came from hours when the game crashed a lot;
the afternoon's long interleaved test, bare profile with the hook out (`F-fixbare`) against forced in
(`H-barehook`), 20 chains each, came out **5 in 90 against 6 in 87**. The game crashes on its own (with Decal and only
the test runner), at a rate that drifts over hours. WorldPass now hooks only while something draws there (kept: it's
harmless), and CLAUDE.md and its comment say the hook isn't shown to matter.

**What the crash is** (full dumps, read above): a dat file's unpack under `AsyncCache::BlockingLoadInto` reads data
that's wrong in memory (one dword zeroed in Environment 0x0D0003CF's buffer; a motion table's read running into a
freed, zeroed region): a write through a stale pointer, or a free, of memory just reused for a load. Not two threads
racing (one core crashes as often), not the cache flushing (forced flushes: 0 in 10 against 1 in 12).

**Next leads**, each needing the elevated helper (`tools\debug-service.ps1`, run as administrator and never clicked
in: a click pauses it): (1) ProcDump on the `cimgui.dll+0x1FDF72` fault under `pageheap-backwards.ps1` (switch the
helper to procdump the moment log.txt shows `[session] Daralet Toolkit`, ~10 s before it), to see whether ImGui's font
atlas really writes past its buffer, and then the same in minimal mode; (2) more ProcDump dumps of teleport crashes
(`pd-loop.sh`, the helper needed) to see whether it's always a few zeroed bytes in a load buffer, which would tell the
writer's kind. TTD can't record it on the live server (see below).

## Earlier today (about 10:00): the teleport crash hunt, mid-run

**Minimal mode finished** (09:52): 4 crashes in 204 teleports (fresh-40 29, 31 10, 36/38/35/39 clean 40, 41 2,
42 3), about 1 in 51. The bare profile on fresh routes (B-bare, O-bare, no-Content-Override B-bare): 9 in 202, 1 in 22.
On the four routes both ran (31, 36, 39, 40): bare 6 in 68, minimal 2 in 119 (one-sided p about 0.03 conditional on
8 crashes). Minimal's crashes are at the same game addresses (0x539153 make_plane, 0x526A66 AnimData::UnPack,
0x52718B). So **the game with Decal crashes on its own, and the plugin's ordinary running roughly doubles to triples
it.** The RestrictionDB lead is weak: daralet-ACE's `RestrictionDB` writer is upstream ACE's verbatim, which retail
clients read fine; that dump is likely memory already gone bad.

**Running now** (started 09:53, `tools\hook-vs-bare.sh fresh-41 fresh-42 fresh-38 fresh-35 fresh-36 fresh-31`, from
`overnight-run11.sh`, log `tools\hook-vs-bare.log`): `W-hook` (minimal mode plus WorldPass's hook alone, `set
worldpasshook 2`, calling straight through to FlushAlphaList: the one call from inside the game's frame that the bare
profile always has, since every graphics piece subscribes in its constructor) interleaved with `B-bare`. overnight.sh
now makes `minimal.txt` for M- and W- jobs and removes it for others and before the restore job. Tally section
"--- hook vs bare". If W-hook crashes like bare, the hook (a managed call from inside the game's render) is it; if like
minimal, bisect the hud's other pieces.

**Chains** (10:13 to 11:40, `tools\chain.sh`, seeds `chain-N` in overnight.sh: from a game just started, Standing Stone
(69.2n,15.3e), Zaikal Lifestone, Khayyaban, Sawato, Undead Shrine: the teleports that crashed most on the tours, found
by grouping every teleport in results.txt by from and to; Standing Stone to Zaikal ~1 in 3, Khayyaban to Sawato 3 in 6,
Sawato to Undead Shrine 3 in 7). M-minimal, B-bare and W-hook in turn: see the tally's "--- chains" section. 7 of the
first 9 trials crashed (all three configs), then 18 clean in a row: the rate drifts over time, so configs must be
interleaved. One crash came 1.4 s into portal space with no CreateObject yet, so it isn't the objects sent.

**The game loads on a second thread.** The crash dump's thread list (DumpRead now prints every thread's stack:
stream 3) has one in `ThreadedCache::Startup` (0x678960, really the worker loop: takes a request, runs it through the
cache's vtable +0x58, queues it finished, waits), and the main thread works the finished ones off for up to 25 ms a
frame (0x678A10, vtable +0x5C OnRequestFinished). The crashing main thread is in `AsyncCache::BlockingLoadInto`
(0x417C60: vtable +0x9C loads into a local buffer, +0x80 SerializeFromCachePack unpacks). Disk reads are locked
(`CThreadsafeDiskController::LoadDataEx`, 0x4F8B00, lock at +0x750), so a shared file position isn't it; a blocking
get racing a queued get of the same object is the guess (AsyncCache's pending-get hash: AddToAsyncGet,
UnhashPendingGet, OnAsyncGetFinished). That fits everything: random, clustered in time, worse with more to load
(Scenery 2, Ground 4) and with more main-thread work per frame (the plugin's hook). No string in acclient.exe turns
the threaded cache off. (DumpRead's `sub_00792CB0+...` lines in stacks are .rdata values, not frames.)

**First full dump of a teleport crash (14:43, ProcDump; D:\Dumps\acclient.exe_261121_144344.dmp):** `make_plane`
(0x539153) in `CEnvCell::UnPack` → `DBObj::Get` → `AsyncCache::BlockingGet` → `BlockingLoadInto` →
`CEnvironment::UnPack` → `CCellStruct::UnPack` → `CPolygon::UnPack`, Environment **0x0D0003CF** (0x11138 bytes, its
buffer at 0x8B15085C). The buffer, saved from the dump (`.writemem`), matches the dat file (`DatCheck raw`) byte for
byte **but one dword at offset 0xA3C, zeroed** (dat `6E 6E 50 42`, the float 52.1; memory `00 00 00 00`), 0x90 bytes
before the unpack's read position. The zero threw the parse off (a polygon of "32" points, vertex ids out of range,
pointers into freed heap) and it died there. So: **something wrote a 0 through a stale pointer into memory just reused
as a dat read buffer**, a use-after-free write; the other crash sites (MotionData/AnimData unpack, the hash table at
0x58712F, the RestrictionDB HashList free) fit the same family. One core didn't help, so it isn't two threads at once;
the writer is likely the main thread itself (a stale object cleared or unlinked after its memory was freed). To read a
dump: `cdbX64.exe -z <dmp>`, then `.load wow64exts`, `!sw` (32-bit view; `.ecxr` gives the 64-bit WOW record, useless),
`r`, `dps esp L80` (names from `$TEMP\datcheck\entrypoints.txt`). Recording (TTD) failed three ways: attached whole, the
server dropped the slowed game; `-module` on attach records nothing (only starts on entering the module); `-monitor`
from launch keeps Decal out of the game (no auto-login), as does any debugger attached at launch, ProcDump too. So
ProcDump attaches after login (debug-service.ps1, procdump mode, switched by tools\pd-run.sh).

**Afternoon (14:45 to 15:25):** a second ProcDump dump (14:48, `AnimData::UnPack` under `CMotionTable::UnPack` of
0x09000003, its read position run off into a freed, zeroed region); the read gets no further without the cache's own
allocator worked out (`!heap` doesn't know that memory). **Cache flushing isn't it**: minimal mode, the chain with the
game's cache flushed 3 times in every load (`chainflush-N` seeds, `GameCache.FlushFree`, which now gave back 252 MB a
flush) crashed 0 in 10 teleports against 1 in 12 plain. **Page heap backwards** (`tools\pageheap-backwards.ps1`,
PageHeapFlags 0x13 = full, traces, backwards; the 0x10 is right: Decal's read past an end no longer stopped it) stopped
every game ~20 s after launch in **`cimgui.dll+0x1FDF72`**, a WRITE: ImGui's font atlas turned to RGBA32
(`GetTexDataAsRGBA32`'s loop, `IM_COL32(255,255,255,a)`, ecx the pixel count) writing `[edx-4]`; backwards, a block
starts at a page and the next block's guard follows its end, so a sequential write faulting means **ImGui (through
UtilityBelt.Service) wrote past its atlas buffer, or into a freed one**, at every start. An atlas is megabytes, its
own VirtualAlloc region, so an overrun lands in whatever's next, the game's memory included. Untested whether minimal
mode (no plugin fonts) does it too; the plugin's chat fonts make UtilityBelt rebuild the atlas (`ChatFonts.MakeWanted`,
`FontManager.Create`). Not captured: the elevated helper had been closed, so no ProcDump. **Next**: the helper
(`tools\debug-service.ps1`, as administrator) and pageheap-backwards again, ProcDump switched on once the plugin logs
its start (`[session] Daralet Toolkit`, ~10 s before the fault) to get the dump: the atlas size against the buffer's;
then the same in minimal mode, which says whether it's UtilityBelt's own atlas or the plugin's fonts.
Then (15:24) `tools\fix-chain.sh 20`: F-fixbare against H-barehook at Scenery 1, ends with your settings restored.

### Earlier (09:05)

**Running unattended right now** (nothing in the session needed): `Test Runs\tools\minimal.sh fresh-40 fresh-31 fresh-36
fresh-38 fresh-35 fresh-39 fresh-41 fresh-42` (from `overnight-run10.sh`, config `M-minimal`): the plugin in **minimal
mode** (`Test Runs\minimal.txt` present: `PluginCore.Startup` makes only the TestRunner, nothing else) touring 40
fresh outdoor places per seed, with `set objectlog 1` writing every CreateObject after each teleport to
`Test Runs\objects.txt` (`Ui/ObjectWatch.cs`, `Feed/CreateObjectSummary.cs`). Results: one line a job in
`Test Runs\overnight-tally.txt` (section "--- minimal mode"). The crash watcher `tools\watch-game.ps1` runs detached
(closes crash boxes, ends frozen games; stop it before the user plays: `Get-CimInstance Win32_Process | ? CommandLine
-match watch-game | % { Stop-Process $_.ProcessId }`). When minimal.sh ends it deletes minimal.txt, restores the
user's settings (Default profile, their lighting, **the new Quality preset**: Scenery 1, Ground 1, Landscape 8, far land
24 + near, 3D trees 4) and restarts the game whole. **Still to put back by hand afterwards:** Daralet Admin's loader,
switched off for these tests: `HKCU\SOFTWARE\Decal\NetworkFilters\{B3E0A5D2-7C41-4F8E-A96D-21F4C8E0B5A3}` Enabled
back to 1 (`tools\admin-loader-was.txt` says it was 1). Content Override Service the user turned off themself (HKCU
Services `{FAACFA40-...}` Enabled 0): leave it. **Decal's on/off switches are per user in `HKCU\SOFTWARE\Decal`** (no
admin needed); HKLM only registers.

**What's known** (details in CLAUDE.md, "The teleport crashes", and `overnight-tally.txt`):
- Crashes come only on long outdoor-to-outdoor teleports to places not loaded this session, about 0.3 s after the
  server's burst of CreateObjects; never on dungeon teleports or round trips. Quality's Scenery 2 / Ground 4 made it
  ~4x worse (presets now keep both at 1). The plugin's drawing and lighting don't matter.
- Bare profile still crashed ~1 in 26; with only Decal + toolkit + Daralet Admin enabled (all other add-ons were
  already off per HKCU). Content Override and Daralet Admin off: still crashes. Minimal mode, first route (fresh-40):
  crashed after 29 where bare crashed after 2-4 every time: maybe the plugin's ordinary running raises the rate.
- **Crash dumps exist**: `%LOCALAPPDATA%\CrashDumps\acclient.exe.*.dmp` (WER keeps 10). Reader:
  `$TEMP\dumpread` (`DumpRead.exe entrypoints.txt dump...`: exception, registers, stack return addresses named by
  AcClient entrypoints from `$TEMP\datcheck\entrypoints.txt`, and dat ids on the stack). Every crash is in the
  game's loading path (`AsyncCache::BlockingGetFromDisk` → a file's `UnPack`: CMotionTable/MotionData, CEnvironment
  /BSPNODE, CEnvCell/get_portal, CPolygon::make_plane), or freeing a CreateObject's `RestrictionDB` (house access) in
  `CM_Physics::DispatchSB_CreateObject` → `PublicWeenieDesc` destructor → `HashList::delete_contents` null. Reads go
  to page starts (past a buffer) or garbage pointers. The files named are retail and consistent: `$TEMP\datcheck`
  (`DatCheck.exe <dats> env <envs> <cells>`, `pairs <objects.txt>`, unpack-all with no args) checked every motion
  table, animation, setup, environment (polygons, vertices, BSP, portals) and the crash cells: all fine (one setup,
  0x02001C57, is 4 bytes short). So: good data, bad memory, in the game's loader.
- No single object precedes every crash (objects.txt compared), nor does the object count.

**Next ideas:** finish the minimal-mode comparison (if minimal crashes far less than bare, bisect what bare still runs:
the hud's windows, radar's ClientRadar reads, ClientElements calls, game panel hiders, the shared window; if it
crashes as often, it's the game + Decal, and the RestrictionDB crash suggests checking daralet-ACE's house restriction
serialization (`RestrictionDB` in CreateObject) against the client's reader). The lamp crossfade
(`GameLampSwitch` SetLight hook, `EaseLampShare`) is in bin, measured (sudden steps down to a third) but not looked
at by eye.

Read `CLAUDE.md` first (the graphics sections from "Draw distance" on, "Memory for teleports", and "Far land").
Untracked: never commit this file. Kept up to date as the work goes (the user's call).

## Where the work is

- Branch `graphics` (pushed to `origin/graphics` up to `5381c20`). Everything since is **staged, not committed**. The
  user commits with GitHub Desktop; stage new files (`git add`) as you go.
- Build into the game (hot reload): `dotnet build src/DaraletPlayer/DaraletPlayer.csproj -c Release -nodeReuse:false`.
  **Mid-test, build elsewhere** (`-p:OutputPath=$TEMP/fl/`) so the running game doesn't reload. Shaders:
  `cmd //c "Shaders\\build.cmd"` from `src/DaraletPlayer`, then build. Tests (the plugin project sets its own
  OutputPath to the repo's `bin`, so without `-p:OutputPath` even `dotnet test` hot-reloads the game): `dotnet test
  tests/DaraletPlayer.Tests/DaraletPlayer.Tests.csproj -nodeReuse:false -p:OutputPath=$TEMP/flt/` (665 pass).
- The game runs from ShadowLauncher with Auto relaunch. The character is an admin: `/tele 37s,67w`,
  `/teledungeon <name>`, `/teleloc ...`. Holtburg overlook (the usual shot spot, 1 m back from the edge):
  `/teleloc 0xA9B4002A [131.845000 47.010000 94.005005] 0.996917 0.000000 0.000000 -0.078459`.
- A throwaway disassembler (Iced) is in the scratchpad: `dis.exe <hexaddr> <count> [stop]` over acclient.exe; the
  client's function names and addresses come from UtilityBelt.Service's `AcClient.Entrypoint`
  (`ilspycmd -t AcClient.Entrypoint` on its dll; a copy was in `$TEMP/acdec/ent.cs`). Rebuild them if the scratchpad
  is gone.

## Test tools (in `Documents\Decal Plugins\Daralet Toolkit\Test Runs\tools`, outside the repo)

- `run.txt` steps: see `Feed/TestScript.cs`. New this session: `set farland N`, `set farlandnear 0|1`,
  `set farlanddebug 1` (the plugin's land tinted red, to see where it starts), `set charsoftness N`, `set lighttrace 1`
  (see Lighting), `gohighest N` (teleport to the highest ground within N landblocks), `gowaterside N`
  (teleport to a beach by broad water) then `facesaved` (face across it). `set lighttrace 1` logs the lighting passes
  frame by frame. Write run.txt to a temp file and move it in.
- Suites: `make-suite.sh <seed>` (the old one), `make-suite-farland.sh <seed>` (landscape 6, far land 18, takeover on;
  churn through landscape 4/6/8 with far land), `make-suite-farland-nonear.sh <seed>` (the same, takeover off).
- Watcher: `powershell -File watch-game.ps1` under Monitor (crash windows closed, freezes ended, run end reported).
  It re-reports the last "game stopped" line when started; that's old news.
- Shot spots used for retail-vs-plugin vistas: snowy peak `/tele 68.6s,25.4e` + `gohighest 4`, face 270; river
  beach `/tele 27.2n,47.3e` + `gowaterside 2` + `facesaved`; forest `/tele 29.7n,25.5e`, face 270; desert near Tufa
  `/tele 14.2s,5.4e` + `gohighest 6`, face 0; Olthoi North `/tele 67.9n,67e` + `gohighest 5`, face 270.

## Done this session (all in CLAUDE.md)

- **Far land** (`Ui/FarLand*.cs`, `Feed/TerrainBlend.cs`, `Shaders/farland.*`): the land past the game's landscape in
  full detail, drawn with its own camera only where the game drew nothing (stencil); textures baked into a video-memory
  atlas with the game's own terrain blending (alpha maps); billboards stand on it; light matched to the game's ground
  (`GroundLight` 0.88, measured at the seam with the red tint). Landscape 4-6 plus far land looks like 11+ for less
  memory (fresh restarts: landscape 5 1,736 MB free vs 11 1,626 MB).
- **Takeover of the game's coarse rings** (`FarLand.Takeover.cs`, `farLandNear`): see Open below; being changed.
- Far land settings rows (Far land slider, "Full detail inside Landscape") in `OptionsPanel.DrawDistance.cs`, cost
  entries `GraphicsCost.FarLand` / `FarLandBillboards`, presets reworked (`Feed/GraphicsPresets.cs`: Performance 1/1/5
  + far land 19, Balanced 2/2/6 + 18, Quality 2/4/8 + 24, takeover on). Built, tests pass, **not yet tried in the game**.
- Water: reflection fades out by 740 m; reflections no longer take the water surface for a hit (the dark band along a
  far bank); far water uses each water type's own texture.
- Shadows: your character alone in the close shadow map with its own Character softness (2 cm), trees keep Softness.

## Open: crashes in the far land suite (in progress)

- `make-suite-farland.sh 11` crashed twice, both in the game's own resource cache using an object already freed:
  `AnimData::UnPack` (0x526A66, one second into a teleport; the earlier 0x526A8A crash is the same function) and
  `AsyncCache` (0x4189A1, a vtable call on a freed object, 30 s after login at Holtburg).
- Changed so far: the automatic game-cache flush is off (`DrawDistance.AutoFlushCache`, false; it never gave back a
  megabyte; the test step `flushcache` still works).
- Suspect: the takeover skipped `RenderDeviceD3D::DrawBlock` (0x5A28D0, vtable slot 0x7E6638) for coarse blocks,
  which also skips the game's bookkeeping in it (perhaps marking resources used, so the cache frees them). Running now:
  `make-suite-farland-nonear.sh 11` (takeover off): **ran to the end, no crash** (17:26 to 17:48; free memory at
  the overlook after churn about 1.15 GB, largest free piece 600 MB in 87 pieces: fragmented). Now running:
  `make-suite-farland.sh 11` with the new hook (DrawLandCell only): crashed 17 s after the restart's login at
  0x58712F (the game emptying a hash table in its cache) **before the takeover was even on** (the suite turns it on
  after its first wait; no "taking over" line in that session). So not every crash is the takeover's: all three came
  within seconds of the game loading (login or teleport) while the plugin changed what's loaded; at login the plugin
  cut the landscape 15 to 6 four seconds in. Fix: `DrawDistanceRamp.ArrivalQuietSeconds` (12): no draw distance
  change either way for 12 s after logging in or leaving portal space (`DrawDistance.Ramp.cs`, `NextStep`). Now
  `make-suite-farland.sh 12` (takeover on, new hook, quiet arrival): crashed again at 0x58712F 2 s into a teleport
  (Standing Stone to Zaikal, 9 teleports in), with 1,184 MB free and the largest free piece 983 MB; the no-takeover run
  had 1,320 MB there and survived. Likely memory, not the hook: the far land's build grew about 30 MB of large-object
  heap at every rebuild (the largest free piece fell 1,418 to 983 over the run), and it rebuilt at the moment of each
  teleport. Fixed: `FarLand.Build` writes straight into the locked buffers block by block (`WriteBlock` lists of one
  block, reused), and far land is let go and nothing read, baked or built while `DrawDistance.Arriving` (portal space,
  or 12 s after arriving). Run again: crashed on the very first teleport at 0x539153 (make_plane). Tally so far: no
  takeover, 21 teleports and no crash; takeover (skipping the draw), crashes at teleports 1, 8 and 9. So the skipped
  draw itself: it does work the game relies on later. Now: the cell's ground is drawn by the game as ever, through an
  empty scissor rectangle (`DrawLandCellHook`: the device's own GetRenderState/SetRenderState for SCISSORTESTENABLE
  174 and Get/SetScissorRect, vtable 58, 57, 76, 75, put back after), so nothing reaches the screen. Looks the same at
  the overlook (shot `scissor-takeover`; a faint thin line across the hill at the game's full-detail edge, for later).
  Now running: `make-suite-farland.sh 12` with that. Its opening restart (the game exiting) crashed in
  `CSurface::PurgeResource` (0x536D8D) as the game closed: harmless to play but logged as unclean; see if it repeats
  and whether exits crashed before the takeover.
  The scissor run crashed at 0x539153 on the same teleport as run 4, Standing Stone (72n,22w) to Zaikal (13.8n,0.5e),
  with memory fine (1,626 MB free, largest piece 1,501 MB): so neither memory nor skipping the draw. Running now:
  that teleport back and forth 4 times with the takeover off, then 4 with it on (`log off cycle` / `log on cycle`
  lines in results.txt). Result: all 8 round trips clean, off and on (scissor). So the crashes are intermittent:
  without the takeover 29 teleports clean; with it, a crash about every 10 in full suites. Now: `make-suite-farland.sh 13`
  (takeover on, scissor). If it crashes, take the takeover out of the presets and mark it experimental.
  Result: crashed 33 s in, just after a teleport, at 0x4730C0, inside the game's interface code (sub_00473020):
  the crashes come from several places, not only the landscape. **Decided:** the takeover is out of the presets
  (`GraphicsPresets.Apply` sets FarLandNear false; Matching wants it off) and its row says it's experimental and has
  crashed the game on teleports. Still open: why the plugin's suites crash at all (UI code too); a baseline suite
  with far land off would say whether far land matters. The takeover now lets DrawBlock
  run and skips only `DrawLandCell` (0x5A0230, slot 0x7E663C, thiscall (device, cell), ret 4) for the plugin's
  blocks (`skipCells`), which also keeps creatures in those blocks drawn.
- The no-takeover run had two accidental hot reloads (my `dotnet test` before the OutputPath note above): it carried on,
  but from then on with the newer code in it (takeover off, so the new hook wasn't used).
- **Crash-ready log.txt** (the user's call; built and tested, not yet tried in the game; `Ui/SessionLog.cs`,
  `Feed/CrashReport.cs`, `Feed/GraphicsSummary.cs`): log.txt trimmed past 4 MB into log.old.txt; `session.txt` marker
  (process id, start) deleted on a clean shutdown, so the next start can say the last session didn't end cleanly; a
  `[session]` header at login (version, screen, free memory, every graphics setting); and, **opt in** (Config,
  Troubleshooting, "Read the game's crash records", `readCrashRecords`, off to start), Windows' Application log read
  off the game's thread for acclient.exe's crash and hang entries since that session, each written as `[crash]` with
  the address and the client's function there (AcClient.Entrypoint). Tested with the real AnimData::UnPack entry.
  In the README (Problems) and CLAUDE.md ("log.txt for crash reports").
- Free memory at the overlook was low in the second run (1,253 MB after login, private 2,125 MB); worth watching.

- **Likely cause found (Nov 20, 03:45-04:27 and 07:19 runs): Detail (Degrade Distance) at 1000 m.** One setting at
  a time, round trips Holtburg overlook <-> 0xE74E001D: draw distance off 5/5 clean; Detail at the game's (everything
  else as the crashing runs) 5/5 clean; every run with Detail 1000 crashed in the first or second trip (0x58712F,
  0x539153, 0x52718B), far land, fires, scenery, prelogin each off in turn changing nothing. Holding Detail at the
  game's in portal space and 12 s after arriving did NOT help (crashed on trip 1's way back, 0x58712F), so it isn't
  the load itself: likely the finer models kept out to 1000 m fill the game's object cache it later trips over. The
  hold was reverted. The presets never set Detail; only the test settings had it at 1000 (and Far scenery 3000).
  Shots at the overlook, Detail 50 vs 1000 m (`shots/...detail-*`): the same but for clouds. **Detail removed** (the
  user's call): setting, slider, cost entry, test step `detail` (a run.txt using it now fails that step). Next: rerun
  `make-suite-farland.sh` (takeover off, then on) to see whether the crashes are gone.
- **Suites after Detail's removal (Nov 20, seed 12), crashes are NOT gone**: far land on, takeover off: crashed at
  teleport 5 (0x526A70). Baseline (far land off), lighting on: teleport 1 (0x539153). Baseline, lighting all off:
  teleport 8 (0x526A8A). Draw distance off, far land off, lighting off, fog 100 (water reflections still on, the
  user's settings): teleport 19 (0x58712F). So crashes come with nearly every plugin graphics feature off, rarer the
  less is on; teleports here are every ~17 s. Last night's baseline (00:48, Detail 1000, far land off) ran clean.
  True retail graphics (water, grass, image passes off too), 13:24-13:45: **all 26 teleports clean**. One run each, so
  weak, but: every crashing run had something reading the dats round teleports (billboards' SceneryLayout and
  LandblockInfo, far land, the water map, placed lights); the clean one had nothing. Suspect: the plugin's dat reading
  (UtilityBelt's readers, on the game thread) while the game streams the new place in. Next test: water reflections
  alone (the smallest dat reader) for a full suite, then the same with the water map's reads held while Arriving.
  (The water map already waits while Arriving, so that test was skipped.) **The hook is the suspect**: everything off
  but WorldPass hooked with an empty piece (test step `set worldpasshook 1`): crashed at teleport 11 (0x515123);
  hooked and calling straight through to the game's FlushAlphaList, nothing else (`set worldpasshook 2`,
  `WorldPass.Bare`): crashed at teleport 2 (0x526A7E). True retail: 26 clean. So a call from the game's frame into
  the plugin's managed code, by itself, goes with the teleport crashes (one run each). Not the calling convention
  (cdecl, declared right). Guesses, unchecked: the CLR's transition changing the x87 control word the game's loading
  relies on; or the hook's thunk. Other hooks called during loading (on_road for every scenery object, fog,
  DrawLandCell, the shadow and lamp device hooks) would carry the same risk. Both test steps are test-only.
  **Retracted (14:40):** WorldPass is hooked at startup whatever the settings (DepthShading, DrawDistance, FarLand and
  Grass all `WorldPass.Add` in their constructors), so the clean "true retail" run had the hook in too. A second bare
  run (`set worldpasshook 3`, meant for the FPU probe, which never installed because the hook was already in) ran 26
  of 26 clean where the first crashed at teleport 2. **One run per configuration is noise**: the crash rate with
  little on looks like one in 20 to 50 teleports, at random. Telling configurations apart needs several runs each
  (hours), or a different approach. `FpuProbe.cs` and the `worldpasshook` step are test-only and unproven: the probe
  must take the hook out and put itself in, not skip when the hook's already there.
- **FPU ruled out** (Nov 20, 15:31): FpuProbe's native stub round the hook saw x87 027F and MXCSR 1FA4 before, inside
  .NET and after, unchanged over 1,430 calls through two teleports. Overnight driver (`tools/overnight.sh`: A retail,
  B balanced, C takeover, D everything, seeds interleaved, tally in `overnight-tally.txt`) written but stopped by the
  user's call before its first job finished; it outlived its parent task once (kill the bash by command line). A run
  loaded in the game is ended by putting `Test Runs\stop.txt` there.
- **Seam at the far land (Nov 20)**: the user's screenshot line was the plugin's land starting (Near land on) at a
  64 px-a-block atlas; now the first three rings past the seam are 256 px a block (CLAUDE.md, "Fine rings at the
  seam"). Kept, the user's call. Their settings were restored to their 12:17 login (the session header in log.txt)
  after the test runs had changed them.
- **Graphics settings cleanup (the user's call, Nov 20)**: taken out Far scenery (`DrawDistance.Lod.cs` deleted),
  Shading's short contact Shadows (shader too), Texture filtering (`DepthShading.Anisotropy.cs` deleted; the game's own
  Config page has anisotropic); Video memory now always on (ScenePool always default pool); lamp "In the game's place"
  folded into Lamp shadows. Tried in the game: overlook by day (sun shadows, far land) and the Holtburg interior at
  night with lamps, both fine, no errors. **Near land (takeover)** waits on the suite: if it runs clean with Detail
  gone, drop its "experimental, crashed" warning (and maybe put it back in the presets); if not, remove it.

## Next (the user's list, in order)

1. Finish the crash work above, then try the settings rows and presets in the game.
2. **Lighting pass** (the user's call):
   - **Local light should lift the sun's shadow** (the user, November 21, 19:55): where a lamp or fire lights a place,
     the sun shadow there should be dimmed accordingly. Today the shadow pass darkens the picture by the sun's share
     (`DepthShading.Shadows.cs`), then the lamp pass multiplies it by one plus the lamp's light (`lamps.ps.hlsl`), so
     in shadow a lamp only adds to the ambient part and the shadow stays as dark relative to the light around it.
     Lit additively it would be ambient + sun × visibility + lamps: the lamp pass would need the shadow factor (or
     the shadow pass the lamps' light) to add the lamp's light on the unshadowed base.
   - **Placed lights don't line up with their lamps** (the user, November 21, 19:56: Hebian-to's lamp posts, "probably
     true many other places too"): the lights read from the dats (`Ui/PlacedLights.cs`: LandblockInfo's Objects and
     Buildings, each Setup's `Lights`, turned and placed as their objects) land away from the posts. Check the light's
     frame inside the Setup (its origin and turn, and the part it hangs on), the object's quaternion turn and scale,
     and the landblock offset against where the game draws the post (`depthview 5` colours each pixel by its light).
   - Holtburg tavern: walking in at the door, every light and shadow went off for a moment, then came back further
     in. A trace is written (`Ui/DepthShading.Trace.cs`: path drawn in the game's frame or on RenderFrame, lamps found
     and in each of the game's lists, camera and player cells, `[light-trace]` lines in log.txt on change), wired to
     `set lighttrace 1`; built only to `$TEMP/fl/` so far. Next: walk in at the tavern door with `walk` steps and
     screenshots, trace on. The tavern isn't in locations.xml: asked the user for a `/loc` at its door.
     A Holtburg interior used for lamp tests before: `/teleloc 0xA9B40163 [104 39 97]`. Suspects: the game's static light list empties as it switches between drawing
     outdoors and indoors; or a frame where neither pass draws.
   - Light from an open door spilling outside at night: the game registers a building's interior lights only when
     its cells are in view; outdoors they aren't in its lists. Likely needs the lights read from the building's
     objects (Setup lights) in the dats.
   - **Time of day for testing** (`Ui/TimeOfDay.cs`, test step `set timeofday F`, F 0 to 1, -1 to let go; testing
     only, never a player setting, the user's call): the client's GameTime at `[0x8EE9C8]` (`UseTime` 0x5A7800:
     timer double 0x8379A8 + offsets +0x78, +0x80; day begin +0x50 double, length +0x0C float, fraction +0x48), the
     +0x80 offset set each frame so the clock reads the pinned fraction; put back when let go and at shutdown.
     Tried: 0 is midnight (stars), 0.25 to 0.875 is day, 0.5 a safe noon; the day is 7,620 s. (`set` rounds its
     number; `timeofday` takes `number` unrounded.)
   - Night mode: darker everything (ground, objects, sky, characters) with lights (torches, campfires) more dramatic.
     **Tried, works** (midnight in Holtburg and at the overlook, shot `grid-night2`): `LampLightOptions.NightDarkness` (0 to 80%, 0 off) and
     `NightGlow` (100 to 400%, 200), rows "Night" and "Night lamps" under Lamp light, test steps `set night N`,
     `set nightglow N`. In the lamp pass (`DepthShading.Lamps.cs` `Night()`, `Shaders/lamps.ps.hlsl` c22): night is the
     game's own period flag (GameTime +0x14 periods, +0x74 current, a period's +8 night flag), eased over 20 s; the
     sun's height was tried first and did nothing (the moon keeps the light vector up at midnight); outdoors only; the light pass's blend is now picture times twice the output (DestColor, SourceColor), so
     it darkens by the night and lifts by the lamps (times the glow at night); the sky darkens 85% as much. Needs Lamp
     light on. Try at night at the Holtburg overlook and by a campfire.
   - **Lamps rework, done and tried** (the user's call: "Full", light out of doors and windows but not through
     walls): `Ui/GameLampSwitch.cs` turns the game's point and spot lights off (its LightEnable, vtable 53, hooked;
     `LampLightOptions.Replace`, on, with Shadows); six cube maps kept between frames (`LampsWithShadows` 6, a new
     lamp drawn at once, two kept ones redrawn each frame in turn); the shader looks each map up once before the
     lights' loop (`MapSeen`, map lamps in c24-c29: six lookups inside the loop ran out of registers), and a lamp
     with no map lights nothing while replacing. `Reach` (100 to 300% of the game's, 150 to start; row Reach, test
     step `set lampreach N`): at 300% the tavern's west door throws a beam outside with your shadow in it. The
     shadow test looks up a little off the surface along its normal, not with a bigger allowance (light leaked
     round the walls' feet); the leftover line at the walls' feet went with the lookup also 30 cm toward the camera, the least light of four neighbour normals, and a soft check 30 cm above near walls (terrain isn't in the maps); soft shadows and fade added (shot final-night). Tried: tavern
     interior evenly lit (no dark patches), `[lamps]` lines report maps and draws left out (none). Time of day: 0
     reads as a day period to the game (the darkening goes), 0.9 and 0.95 night; use `set timeofday 0.95`.
     `usedoor` toggles, so it shuts an open door. Characters lose the game lamps' warm tint.
   - **Overnight, November 20** (the user asleep, work until 8:20 am; commit `11816fa` first):
     - Faceted characters under lamps: the light's dependence on how a face turns is softened (`turned`, 0.35 to
       0.85 by facing, faded to none for faces turned well away) and lamp colours deepened (`Warmth` 1.6) so lit
       characters keep the game lamps' warm look. Tried in the tavern: facets mostly gone.
     - Fog on far billboards: FarLand turned fog off for its shader and drew the far billboards after it, unfogged;
       fog is now on again for them (`FarLand.cs`, before `DrawFarLandBillboards`). Tried at the overlook: they fade
       into the haze with the hills.
     - Far buildings on the far land: the far trees' runs have a `Far` flag; buildings past the game's landscape go
       in those, drawn in FarLand's pass with its camera (`DrawFarTrees(device, shift, projection)`). The far land's
       blocks are worked out with far buildings on even with billboards off.
     - Far land buffers follow Video memory only (`ScenePool.Buffers`), let go before a device reset and built again.
       Far land 24 rings measured at about 70 to 100 MB of address space (23 MB of it managed).
     - The game cell-patches from the server (a black screen after a teleport, the user's word): give teleports
       45 s before a screenshot, and don't restart during one.
     - Time 0 reads as a day period to the game: use `set timeofday 0.95` for night.
     - **The "line under the wall" was your own light**: the game hangs its light round you about 3.7 m ahead and
       above (it sat inside the tavern's door frame), so `yours` (3 m) missed it, and it was taken for a full lamp,
       mapped, at 45 m reach (300%): it lit everything outside and leaked under the walls. `yours` is now dynamic,
       under intensity 10 and within 6 m. Found with a new debug view, `set depthview 5`: each pixel coloured by
       the light giving it most (red 0, green 1, blue 2, yellow 3, magenta 4, cyan 5, orange 6, white 7), the
       lights listed once in log.txt (`[lamps] debug light`). The nearWall check was widened (0.6 to 1 m) on the way.
     - **Replace leaves far lamps to the game**: `GameLampSwitch` refused every point and spot light, so lamps past
       the six with maps lit nothing (a dungeon's, a town's at night). Now it refuses only those at the places of
       the mapped lamps (`GameLampSwitch.Places`, set each frame from `which`; D3DLIGHT9 Position at +52); the
       first four point lights the game enables are logged with whether they matched. Built to $TEMP/fl only while
       the baseline suite ran; check the log's match lines and a dungeon after.
     - Baseline suite (far land off, `make-suite-baseline.sh 13` in Test Runs\tools): running to see whether the
       suites crash without far land at all. No exit crashes in tonight's two restarts (event log).
     - **Fires and lamp posts** (Hebian-to, night): the bonfire ("Bliz's Eternal Bonfire") and the lamp posts are in
       none of the game's light lists, so they lit nothing. `FireLights` (world objects named like a fire) and
       `PlacedLights` (the dats' Setup lights of placed objects, 3x3 landblocks round you) now give them light;
       `fireLight`, on. Seen: the bonfire lights the square and trees, which cast shadows away from it; each post a
       pool. Reach 300% (left from the door tests) washed the town orange; set back to 150 in the test settings.
     - Hebian-to Sewers with Replace: lit as before, 181 fps.
     - **A crash on teleport** (03:27, Hebian-to to the tavern): 0x539153 (make_plane) in the same second the plugin
       patched the full-detail rings 2 to 1 for the portal, after about two hours and ten hot reloads in one game.
       Now a teleport only brings rings past 2 down, to 2 (`PortalRings`), so the presets' 2 are left alone. Tested
       with six round trips Holtburg overlook to Hebian-to (results.txt `round trip` lines).
     - Lamp light costs about 8 fps at Hebian-to at night (113 with, 121 without; fires about 4 of it).
     - A teleport right after a long suite hit the game's own error box (likely out of memory: largest free piece
       about 930 MB); the watcher ended it and Auto relaunch restarted it.
3. Later: far buildings on the far land (they stop at the game's landscape now, so a low landscape loses far towns),
   animated or reflective water in the plugin's rings, the 90 MB free-memory drop when far land builds.
