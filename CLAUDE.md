# Working on this plugin

Daralet Toolkit (the player plugin: `DaraletToolkit.dll`, `DaraletPlayer` as its namespace in code, plain "Daralet
Toolkit" wherever players see a name) is a Decal plugin for Asheron's Call, for players on the Daralet server (daralet-ACE). Its sibling, Daralet Admin
(`../daralet-admin-plugin`), is the staff plugin this one was started from: when something here is unclear, see how it's
done there. Until v0.7.0 it was called Daralet, its dll `Daralet.dll` (`DaraletPlayer.dll` until v0.1.0),
and its repo `daralet-player-plugin`; the assembly guid (what Decal knows it by) never changed, and the installer deletes the old dlls. This file is the knowledge that building them took: copy it into any new Daralet Decal plugin and keep it
up to date.

## The stack

- SDK-style project, `net48`, `PlatformTarget` x86, `LangVersion` 12. No C# records: net48 has no `IsExternalInit`.
- UI: [UtilityBelt.Service](https://gitlab.com/utilitybelt/utilitybelt.service) 2.2.12 from NuGet, with
  `ExcludeAssets="runtime; build; contentFiles"`, because it's installed on the client. It's a separate install from
  UtilityBelt (its own installer, a Decal service under Services, in `C:\Games\DecalPlugins\UtilityBelt.Service`);
  players who only ran UtilityBelt's installer get "Could not load file or assembly 'UtilityBelt.Service'", so the
  README links the 2.2.12 installer. 3.x is out but untested here. Windows are
  `UBService.Huds.CreateHud(name)` with `OnRender`, drawn with ImGui (`ImGuiNET`). Images use `ManagedTexture`, made
  lazily the first time it's drawn, once the game's graphics device exists.
- `deps/` holds `Decal.Adapter.dll` and `Decal.Interop.Core.dll` from Decal 2.9.8.3, and `UtilityBelt.Common.dll` from
  UtilityBelt.Service 2.2.12 (installed beside it, with ACE's dat readers in it, `ACE.DatLoader`), all referenced with
  `Private=False`. The dungeon map uses `UtilityBelt.Common` directly; `ItemIcons.EnsurePortalDat` and `Spells` still reach
  it by reflection, from before it was referenced. It's a .NET Standard dll, so a throwaway console app (net48, x86) can
  reference it and open a game folder's dats to try dat code outside the game.
- `Microsoft.DirectX.Direct3D` is referenced from `$(WINDIR)\Microsoft.NET\DirectX for Managed Code\1.0.2902.0\` only so the
  compiler can tell `ManagedTexture`'s constructors apart. UtilityBelt installs it.
- MSB3277 is suppressed: UtilityBelt.Service was built against Decal.Adapter 2.9.8.0 and Decal installs 2.9.7.5 (assembly
  version). Only the installed one is ever loaded.
- Based on UtilityBelt's [HotDecalPluginTemplate](https://gitlab.com/utilitybelt/HotDecalPluginTemplate). The loader
  (`src/DaraletPlayer.Loader`, built as `DaraletToolkit.Loader.dll`) is a Decal network filter that loads `DaraletToolkit.dll` from bytes
  and reloads it on every rebuild.

Build and test:

```bash
dotnet build DaraletPlayer.sln -c Release
dotnet test tests/DaraletPlayer.Tests/DaraletPlayer.Tests.csproj
```

## Gotchas, each found the hard way

- **Decal must be 2.9.8.x.** Decal 3.0 (2.9.7.5) runs plugins on .NET 2.0 and says "Failed to register plugin" for a net48
  dll. The agent's title bar shows the version.
- **Decal registers a .NET plugin by the assembly's `[Guid]`**, under `HKLM\SOFTWARE\WOW6432Node\Decal\Plugins\{guid}`, with
  `Object` = the plugin class's full name. Give every plugin (and loader) its own guid in `Properties/AssemblyInfo.cs`:
  never reuse Daralet Admin's, or the two can't be installed side by side.
- **A hot-reloaded plugin isn't registered with Decal**, so `PluginBase.Host`, `Core` and the base events
  (`ServerDispatch`...) never work for it. Use `CoreManager.Current` everywhere, and
  `CoreManager.Current.EchoFilter.ServerDispatch` for raw network messages. `CharacterFilter.LoginComplete` has already
  fired when a reload starts, so check `CharacterFilter.LoginStatus == 3` instead.
- **A plugin running in the game locks `bin\*.dll`** (acclient and DenAgent). To check a build while it runs, build to
  another folder (`-p:OutputPath=...`), or develop with the loader.
- **The loader must build the same every time.** The game holds `bin\DaraletToolkit.Loader.dll` open, and the SDK writes
  the git commit into a build's version and pdb by default, so after every commit Visual Studio couldn't copy the
  rebuilt loader over it (MSB3027, "locked by acclient.exe"). The loader's csproj turns both off
  (`IncludeSourceRevisionInInformationalVersion`, `EnableSourceLink`).
- **A local ACE server locks daralet-ACE's `dist\apps\server`**, so `dotnet test` there fails to copy `ACE.Server.exe`.
  Build the tests somewhere else instead: `-p:BaseOutputPath=<dir>/ -p:OutputPath=<dir>/`. Built that way, the
  `StartupTests` and physics tests fail for want of `Config.js` and the dat files; the rest are meaningful.
- **Piping `dotnet build`/`dotnet test` into `grep` can hang forever**: MSBuild's reused worker nodes keep the pipe open
  after the build ends. Pass `-nodeReuse:false` (and set `MSBUILDDISABLENODEREUSE=1`), or write to a log file and grep that.
- **Unit tests (net48 MSTest) can't load Decal.Adapter.** Keep Decal types out of anything tested: for example, pass
  logging in as `Action<string>` instead of calling `PluginCore.Log`.
- **`ImGui.GetColorU32` crashes before an ImGui context exists**, so colours in static fields are packed by hand
  (`(a << 24) | (b << 16) | (g << 8) | r`).
- **Modal dialogs opened from a right-click menu** must be opened (`OpenPopup`) at the window level on the next frame, not
  inside the menu: popups are found by ID stack. See `PlayerActions.RenderDialog` / `CommandConfirm` in Daralet Admin.
- **A hot reload must leave the old copy inert.** The loader shuts the running plugin down and starts the new build in the
  same game. If any old window kept drawing, it kept asking the server for data while its chat filter was gone, and the
  `DAP1|` replies flooded the chat box. So shutdown disposes each piece on its own (`PluginCore.ShutdownStep`,
  `PlayerHud.Dispose`), and a disposed feed or window does nothing (`PlayerFeed.Request`, `FloatingWindow.Hud_OnRender`).
- **UtilityBelt can start without the portal dat.** It opens `client_cell_1.dat`, then `client_portal.dat`, by relative path
  from the game's working folder, and stops at the first that fails. ShadowLauncher starts each game in its own
  `AppData\Local\ShadowLauncher\Instances\<id>` folder of that server's dats (hard links into `DatSets`, or another dat
  folder), and a fresh cell dat there left `UBService.PortalDat` null: every `GetIconTexture` threw a
  NullReferenceException. `ItemIcons.EnsurePortalDat` opens `client_portal.dat` from the same working folder (so it's still
  that server's dats, custom icons and all) and gives it to UtilityBelt, by reflection, since `PortalDatDatabase` is in
  `UtilityBelt.Common`, which the plugin doesn't reference.
- **Decal's WorldFilter.GetByContainer and GetInventory go stale.** An item moved into a bank pack after the bank was
  opened (a `/bank deposit`) didn't show in GetByContainer until the bank was reopened, though the client and the item's
  own `LongValueKey.Container` had it. The bank tab groups `WorldFilter.GetAll()` by each object's Container instead
  (`InventoryBrowser.Snapshot`, twice a second, and the frame after Decal hears any object change, `InventoryChanges`, so a move shows the moment the server answers).
- **Decal throws away a vendor's unique item once you buy it.** A unique item (one sold back to a vendor, a market
  listing) keeps its guid when bought, and Decal knows that guid as one of the vendor's items. The server answers a
  purchase with the item's CreateObject (your pack as its container) and then the vendor's list again
  (`vendor.ApproachVendor` at the end of `Player.FinalizeBuyTransaction`), and Decal releases the vendor items missing
  from the new list, guid and all: the bought item, which then stays out of every list until the next login. Moving it
  between packs doesn't help (the server only updates its container, which Decal no longer has); dropping it and
  picking it up does (a fresh CreateObject). The plugin can't make the server send one, so the server does: a tenth
  of a second after the purchase it sends each bought unique item again (`Player.ResendBoughtUniques` in daralet-ACE). For five seconds after a buy or a wield every item message (what the client sent, the server's answers,
  Decal's changes and releases) goes in log.txt as `[inventory]` lines (`InventoryChanges.Watch.cs`).
- **The game drops an item dragged from its pack and let go over a plugin window.** ImGui windows aren't game panels, so
  to the game the button came up over the world. `GameDrag` eats that WM_LBUTTONUP (Decal's `WindowMessage`, `e.Eat`)
  when it's over a window the drag was held over last frame (`GameDrag.Cover`, from `FloatingWindow` and `PlayerHud`) and
  the selection is something you carry, and posts the game one where the drag began, over the item's own place. ImGui
  may not hear the eaten release either, so a hotbar also takes it from `GameDrag.CaughtOverLastItem`. If the game drops
  the item anyway within three seconds, it's moved back to the container and slot it came from (`WatchDropped`).
  `[drag-in]` lines in log.txt.
- **Decal's hooks crash the game before login is over.** `Actions.Heading`, `Landcell`, `LocationX`, `CurrentSelection`,
  `ChatState` and the like read the player straight from the game's memory, which isn't there at character select or
  while logging in: an AccessViolationException, which no catch stops, and acclient dies (seen from the Map window
  drawing during login, `ACHooksClass.get_HeadingDegrees` in the Windows event log). Nothing reads one until
  `CharacterFilter.LoginStatus == 3`: `FloatingWindow` stays hidden until then, `SelfPosition` gives nothing, and
  `Hotkeys.ForTheGame` and `GameDrag` check first. The main window draws at character select, so a tab mustn't read them
  before the feed is connected (it isn't until login).
- **ImGui's mouse cursors (a window edge's resize arrows, a divider's, the I-beam) flashed for a frame and were gone.**
  UtilityBelt.Service passes ImGui every window message but WM_SETCURSOR (`HudManager.WindowMessage` skips 32 and 132),
  ImGui's Windows code only sets a cursor when it changes, and the game answers WM_SETCURSOR, sent on every mouse move,
  with its own. `CursorKeeper` answers it instead (the cursor ImGui's code would pick, `SetCursor`, and `e.Eat` on
  Decal's `WindowMessage`) while ImGui's cursor isn't the plain arrow, which is left to the game. It covers other
  plugins' ImGui windows too: there's one ImGui. Moving and sizing use the game's own cursors (`GameCursors`, the
  user's call, November 2026): its floaty frames' edges each carry one (the panel frame's layout, Cursor media):
  `06006119` the four arrows (move, also for a window's top edge, `FloatingWindow.WantsMoveCursor`), `06005E66` up and
  down, `06006126` and `06006127` the diagonals, `06006128` left and right, 32x32 with the point in the middle,
  `cursor-*.png` made into Windows cursors (`Bitmap.GetHicon`, then `CreateIconIndirect` with the hotspot); Windows'
  own if one can't be made. The text I-beam, the hand and the no-entry sign are still Windows'. While the left button
  is held Windows sends no WM_SETCURSOR, and ImGui's code sets Windows' arrow as its cursor changes at the press, so
  the move cursor was gone the moment a drag began: every FloatingWindow's draw puts it on again while the button's
  held (`CursorKeeper.KeepWhileHeld`). Untested in the game when written.
- **The character ran forward by itself after a click on a plugin window.** UtilityBelt.Service keeps every key message
  (WM_KEYDOWN to WM_CHAR) from the game while `io.WantCaptureKeyboard`, which ImGui sets whenever an item of any window is
  active: the mouse held on a window, a button or a hotbar slot, or a text box typed in. Let go of the forward key during
  that and the game never heard it come up; turning and backing up didn't stop the run, only the stop key or forward again.
  `KeyReleases` notes each key the game saw go down (judged by UB's own rule, not by whether Decal's handler sees the
  message, since the order of Decal's handlers isn't promised), and posts the game a WM_KEYUP for one Windows says has been
  up for 150ms without the game hearing it, once ImGui no longer wants the keyboard. `[keys]` lines in log.txt.
- **Putting ImGui's cursor back after drawing out of place asserts** ("Code uses SetCursorPos()/SetCursorScreenPos() to
  extend window/parent boundaries"), in a dialog over the game, if nothing is drawn after it. Move it to one item gap
  above where it was and draw `ImGui.Dummy(Vector2.Zero)`, which lands it there (`CombatLogTab.DrawCopy`). The same goes
  for the end of a group or child: `GamePanelArt.Centred` put the cursor back after its text even when the text had left
  it there already, and the Vendor window, whose picked line ends a group with two, stopped the game every time it
  opened. It now only moves it when it isn't there. It still puts it back where the line began, so text centred beside
  something (the Vendor window's picked line, beside the kind menu) goes in a group of its own, or what follows starts
  mid-row. And `SameLine(x)` inside a group adds the group's offset to an x that has it already: `GamePanelArt.LeftRight`
  placed its value so, a group's width too far, which pushed the Vendor window's Buy and Sell buttons out of the window;
  it now goes by the distance from the label (`SameLine(0, spacing)`).
- **Never keep Decal's `WorldObject` from one frame to the next.** Decal lets go of the COM object behind it, and the
  kept one then throws InvalidComObjectException ("COM object that has been separated from its underlying RCW"). Thrown
  between an ImGui Begin and its End, that also leaves the stack open, and ImGui stops the game with "BeginGroup/EndGroup
  Mismatch" (the Vendor window's stock did this a second after it opened). Keep the id and the values you need
  (`VendorStockItem`), and look the object up again with `WorldFilter[id]` when you need it.
- **The clipboard goes through `SystemClipboard`**, which uses Windows directly and says if it couldn't, not ImGui's calls,
  which can't say. `[clipboard]` lines in log.txt.
- **A button drawn only while `IsWindowHovered()` never gets clicked.** A pressed button is an active item, which that says
  blocks the window, so the button goes before its release counts. Pass `AllowWhenBlockedByActiveItem` (`CombatLogTab.DrawCopy`).
  But never `AllowWhenDisabled` to `IsWindowHovered`: it's an item's flag only, and ImGui asserts ("Invalid flags for
  IsWindowHovered()!", the XP bar's side grips' first build). A window's hover doesn't care whether its items are
  disabled.
- **ImGui.NET's `ImGuiSizeCallback` crashes the game.** The delegate type has no `UnmanagedFunctionPointer(Cdecl)`, so on
  x86 .NET makes it stdcall, and cimgui calls it cdecl: the argument came off the stack twice, and the game died in
  `igBegin` (an AccessViolationException, cimgui.dll in the Windows event log) the first frame a window drew with
  `ImGui.SetNextWindowSizeConstraints(..., callback)`. Call cimgui's own function with a delegate type of your own marked
  cdecl (`FloatingWindow.SetSizeConstraints`), and check any other ImGui.NET callback type the same way first.
- **Every Decal event handler and render callback is wrapped in try/catch.** An exception escaping into Decal can crash the
  client.
- **JSON:** System.Text.Json writes dictionaries as objects; `DataContractJsonSerializer` only reads them with
  `UseSimpleDictionaryFormat = true` (see `Feed/Json.cs`).

## What the client already gets, and what it doesn't

Read from daralet-ACE; check before relying on it.

- **Fellowship vitals only reach a client whose fellowship panel is open** (`Fellowship.OnVitalUpdate` checks
  `FellowshipPanelOpen`), so the plugin gets them from `@playerui fellowship` instead, once a second.
- **A target's health** comes back when the client asks for it (`QueryHealth`), as a percentage. Its stamina, mana, buffs
  and debuffs are never sent to the client: those need the server.
- **Threat** lives on the monster (`ThreatLevel`, `PositiveThreat`, `NegativeThreat` in `Monster_Awareness.cs`). Players can
  already see it in chat by appraising a monster with the "attempt to perceive threats" option on
  (`Player_Awareness.GetMonsterThreatTable`): a Perception roll against half the monster's Deception, at most once every
  10 seconds, and only as tiers ("Major threat"...), never numbers. The plugin deliberately doesn't follow those rules: it
  shows the exact `ThreatLevel` numbers, with no Perception roll, updated every second (the user's call, October 2026).
- **The bank** is a `Storage` container the player opens in the world. `/bank` (`Commands/PlayerCommands/BankCommand.cs`)
  does the rest: deposit, withdraw, sort, search, combine, balance, packs, inscribe, icon, autosort, log.
- **The market** is run by Market Broker NPCs (`apps/server/Market/MarketBroker.cs`): give one an item to list it, and talk
  to it (`listings`, `cancel <id>`, `payouts`, `claim expired`). Listings live in the database.
- **The Market API** (`apps/market-api`) is the public, read-only HTTP service the website uses for market search
  (`/api/market/listings`) and leaderboards (`/api/leaderboards/...`), at `https://play.daralet.ac/api/...` (nginx only
  passes `/api/` through, so its `/health` isn't reachable from outside). The plugin calls it directly over HTTPS for market
  search and leaderboards: off the game thread, with TLS 1.2 switched on (`ServicePointManager.SecurityProtocol`), since
  net48 doesn't always offer it by default. A player's own listings, sales and expired items aren't public, so those come
  from `@playerui`.
- **The arena** is `/arena` (`Commands/PlayerCommands/ArenaCommand.cs`): queue, challenge, leave, stats, top, maps, watch.
- **Map locations** come from GoArrow's `locations.xml`, in Phenyl's Daralet edition (its "About" entry says so): about 1,700
  `<loc id name type NS EW>` entries, in game coordinates (south and west negative). Types are Town, Dungeon, Landmark,
  Lifestone, Outpost, Bindstone, Vendor, NPC, AllegianceHall, TownPortal / WildernessPortal / UndergroundPortal (with
  `exitNS`/`exitEW`, where the portal goes) and Custom (notes, like the About entries in the corners). Dungeons have a
  `dungeonId`. The text inside an entry is a free note: "Level 30 Capstone" on capstone dungeons, a number on towns, a tier
  ("T2") on others. The website's `mapdata.js` is a smaller set taken from the same data, as image percentages
  (EW = x% × 2.038 − 101.9, NS = 101.9 − y% × 2.038). Daralet Admin's `Map/MapProjection.cs` and `Map/MapView.cs` draw the
  map, in the same game coordinates.

## Talking to the server

There's no custom network message: the client and Decal don't know new opcodes. Instead:

1. The plugin sends a chat command with `CoreManager.Current.Actions.InvokeChatParser("@playerui hello")`.
2. The server replies with system chat lines `DAP2|kind|id|index|count|chunk`: camelCase JSON in base64, split into
   200-character chunks (`PlayerUiProtocol`, which uses `AdminUiProtocol`'s encoding and chunking with its own prefix).
3. The plugin reads them from the raw `0xF7E0` message (`e.Message.Value<string>("text")`), joins and decodes the chunks
   (`Feed/ReplyAssembler.cs`), and hides them from chat with `CoreManager.Current.ChatBoxMessage` + `e.Eat = true`.

**Never put readable text in a chat line meant for a plugin.** Other chat plugins act on it before yours can hide it:
Virindi Chat System posts its own copy of any line with coordinates in it ("Hebian-to (39.0S, 83.0E)", a fellow's
location), and `e.Eat` doesn't stop that copy. That's why the JSON goes out in base64. Before it did, replies showed in
chat, sometimes cut short and starting mid-reply (`lse,"members":...`), which was first taken for the client splitting
long lines. `DAP1` lines (plain JSON) are still read from older servers, and a piece of one that reaches chat is hidden and
logged once to `log.txt` (`ReplyAssembler.LooksLikeReplyPiece`).

Server side, in daralet-ACE: `@playerui` is a Player-level command (`apps/server/Commands/PlayerCommands/PlayerUi.cs`, with
its code in `apps/server/Entity/PlayerUi/`). `PlayerUiProtocol` uses `AdminUiProtocol`'s chunking with its own prefix.
Don't reuse `@adminui`: it needs Sentinel, and it sends data about everyone. The JSON classes in `Feed/Models.cs` mirror the
records in `PlayerUiSnapshots.cs`; tests on both sides pin the names (`PlayerUiProtocolTests`, `ReplyAssemblerTests`).

The `hello` reply lists every request the server answers (`kinds`, from `PlayerUi.Kinds`). Add each new request there, and
have its tab check `PlayerFeed.Supports(kind)`, so a newer plugin on an older server says what's missing instead of waiting.
A change to what an existing command takes goes in `features` instead (`PlayerUi.Features`, checked with
`PlayerFeed.HasFeature`), so the plugin only sends a form the server reads: `packid` is `/bank inscribe` and `/bank icon`
naming a pack by its id.

`/dap` (or `@dap`) shows and hides the window; `/dap hideui` and `/dap showui` are Hide game UI's (below); `/dap chat`
shows and hides the main chat window.

Requests so far: `hello`; `fellowship` (your fellowship with vitals and map places, or `null`); `position` (where you
are on the map, which the plugin only asks indoors); `targets <guid> [guid]` (up to four
creatures your client has been told about (`PlayerUiData.ClientCanSee`: nothing through walls, hidden until a Perception
roll or cloaked): vitals, the spell in effect in each category, attack target and
threat table, or `"status":"gone"`). Another player as a target gets only `"status":"player"` and their name: the plugin
shows the health percentage the client already has (game event `0x01C0`), never their spells or exact vitals. The server
ignores a request of the same kind sooner than 400ms after the last (hello: 2 seconds), and more than 10 a second of every
kind together, in bursts of up to 20 (`PlayerUiLimits`, a token bucket per player; each a `/modifylong playerui_...`
server property, 0 for no limit). The plugin never asks sooner than 500ms (`PlayerFeed.MinInterval`) and sends about 6 a
second at most, with every window open: keep it inside those limits, or its windows wait 3 seconds for a reply that never
comes (`PlayerFeed.RequestTimeout`). Polling goes through `PlayerFeed.Refresh`. `bankwindow` and `lootwindow`
(`on|off`, answering `{"on":...}`) are described below. `rollranges` sends the ranges loot rolls each filter stat in (`{"ranges":[{"category","stat","tier","min","max"}]}`), for
the loot rules' tooltips. `character` sends your character stats (`{"sections":[{"title",
"lines"}]}`), for the Character tab. `xp` sends your level's XP and rested experience (`{"level","totalXp","levelXp","nextLevelXp","maxLevel",
"rested":{"enabled","state","rested","max","bonus","perHour","earnedThisSession"}}`), for the XP bar; the server's
`/rested` says the same from the same `rested`. `lfg` sends looking for group, for Find a group (see the Social tab). `social`
sends your friends, squelches, allegiance and the Appear Offline and fellowship options (`PlayerUiSocialData`), and
`friend`, `squelch`, `allegiance` and `fellowcreate` are the Social tab's buttons (see it). `shareitem <guid>`, `shareagain <key>` and `shared
<key>...` are item links in chat (see Chat windows). `fellowship spells` adds
each fellow's buffs and debuffs as spell ids; every `fellowship` reply has each fellow's Combat Focus (name and icon
layers), and leaves nulls out. The server can also send a reply unasked (`PlayerUiProtocol.Send`),
which the plugin reads the same as one it asked for.

**Map places indoors:** the game's `GetMapCoords` gives nothing for any indoor cell (0x0100 and up), buildings included.
But a building's rooms share their landblock's coordinates, so the server places them anyway (`PlayerUiData.GetMapCoords`,
`inBuilding: true`); only a true dungeon (`Town.HasReliableNearestTown`, the check listplayers uses) has no place on the
map. The client can do the same sums (`Map/Navigation.MapCoords`) but can't tell a building from a dungeon, which is why
it asks `position` when you're indoors.

**The bank:** opening a bank chest sends the client every pack and item in it (`Storage.SendBankVaultInventory`), so
Decal's WorldFilter has them all, and moving one is the game's own move (`Actions.MoveItem`), which the server checks.
What the client can't tell is which open container is a bank: `@playerui bank` says (`open` means open and near enough to
use, like every `/bank` command but balance, log and intro needs) and numbers its packs the way `/bank packs` does.

**The plugin's windows instead of the game's:** a Decal plugin can't stop the client drawing a container's window, but the
server can leave out what makes it: `GameEventViewContents`. `@playerui bankwindow on|off` and `@playerui lootwindow
on|off` set `Player.PlayerUiBankWindow` / `PlayerUiLootWindow` (in memory only: the plugin says which it wants each time it
starts, `PlayerFeed.WantWindow`, every frame until the server agrees). With one on, opening a bank
(`Storage.SendBankVaultInventory`), or a corpse or chest (`Container.SendInventory`), still sends every item's
CreateObject, so Decal has them, but no ViewContents, and pushes the `bank` or `corpse` reply unasked instead; closing
pushes it again (the bank closed, or a corpse with status `closed`). Without ViewContents Decal's `ContainerOpened` doesn't
fire, which is why the server says. Two things the client normally does go missing with the window: it never sends
NoLongerViewingContents when you walk away, so the server closes such a container from the player's heartbeat, even if the
setting has been turned off since it opened (`Player.PlayerUiOpenedContainer`)
(`Player.ClosePlayerUiContainerOutOfReach`); and closing is a Use of the open container (`Actions.UseItem`), which the
plugin's windows send when they're closed. If the plugin is removed mid-session with these on, the game's windows stay away
until the next login.

**The market:** a Market Broker is driven by talking to it (`MarketBroker.HandleTalkDirect`: `listings`, `cancel <id>`,
`claim payouts`, `claim expired`, a price after you give it an item). `@playerui market` sends your listings, sales (last 30
days, from `GetSoldListingsForAccount`) and expired items, and whether a broker is within 30 meters
(`PlayerUiMarketData.BrokerRange`, found from what the player can see). `@playerui broker list|cancel|claim|expired` hands
the same words to that broker, so its checks and Yes/No confirmations apply; with no broker near it says so. Working out a
listing's item name loads the item from the database (`MarketBroker.TryGetListingItemName`), so names are cached by
listing id. The four queries behind `@playerui market` run off the world thread (`PlayerUiMarketData.RequestMarket`, the
reply sent from the world thread once they're in), and their rows are kept per account for 4 seconds (`RowsFor`), or until
a listing or payout of the account's changes (`MarketListingEvents.AccountChanged`, raised by `DbPlayerMarketRepository`'s
writes). The broker answers in tells ("Market Broker tells you, ..."), which `SendCommand` keeps as replies too.

**The arena:** `@playerui arena` (`Arena/ArenaManager.PlayerUi.cs`, a partial like the admin one) sends your state in one
word (off, idle, queued, watching, defeated, accepting, starting, fighting) and in words (`ArenaManager.Status`), your
ratings (`ArenaBoards.All`), duels to watch and the latest results. The buttons send the `/arena` commands themselves.

**Loot:** `@playerui corpse <guid>` describes a corpse's items (you have to be allowed to loot it, `Corpse.HasPermission`,
and near enough to use it), or a chest's or other world container's while you have it open (`Container.Viewer`,
`PlayerUiCorpseData.IsLootableContainer`: a Chest or a plain Container, the static dungeon corpses being the latter; the
bank, house hooks and anything in an inventory are `notcorpse`, which
the looter drops quietly). The looter asks about any corpse, and any container in the world (not in an inventory) but the
open bank; `@playerui describe <guid>...` does the same for up to 20 items you carry (the Loot tab's test);
`@playerui salvage <guid>...` salvages up to 20 carried items with the Ust in your packs, through `Player.HandleSalvaging`
(so pack space, retained and unstable items are checked as ever; `Player.IsSalvageable` is the per-item check both use).
Each item is described by the Market API's own code, shared as linked source: `apps/market-api/MarketRepository.Items.cs`
(`MarketRepository.Describe(IItemFacts)`), `ItemFacts.cs` and `MarketListing.cs` are compiled into the server too. The API
reads the facts from its query's row (`RowFacts`); the server from an item snapshot (`SnapshotItemFacts`, made in memory by
`MarketListingSnapshotSerializer.TryCreateLiveSnapshotJson`, since loot isn't in the database yet). `ItemColumns.Paths` says
where each column is in a snapshot, copied from the SQL; `SharedItemDescriptionTests` checks the two agree, so a column
added to the query needs a row there too. Tiers come from `MarketBroker.GetItemTier`, the market's own. Corpse replies
leave nulls out (`PlayerUiProtocol.BuildMessages(..., omitNulls: true)`): each item is a whole listing.

## The plugin's own pieces

- **The map image** is `Resources/dereth-map.png`, 2048x2048: 2040 pixels of Dereth (10 pixels a map unit, -102 to +102 both
  ways, north up) with 4 pixels of sea round it (`Map/MapProjection.cs`). It came from Daralet Admin's `tools/MapGen`, made
  from the server's dat files, then had some islands and artifacts painted out by hand; `Resources/dereth-map.PSD` is the
  Photoshop source for that edit (not built into the plugin). To change it, edit that file and rebuild. Or, to try an edit first, put a `dereth-map.png`
  next to the plugin dll: it's drawn instead (`MapView.OpenImage`), once the plugin next starts. It has to keep the same
  layout, or places land in the wrong spots, and be square (one that isn't is skipped, with a line in chat).
- **World data** (`Map/WorldData.cs`, `.github/workflows/world-data.yml`): the map image and `locations.xml` update
  without a new plugin. On every push to `main` that changes either file, the workflow uploads them, then a
  `manifest.json` (`format`, `published`, `commit`, and each file's `name`, `sha256` and `size`), to the
  `world-data` pre-release (fixed tag, files replaced with `--clobber`). When the plugin starts, `WorldData` reads the
  manifest from `releases/download/world-data/`, downloads any file whose hash differs from what it has, checks the hash
  and the contents (`Locations.Check`, `WorldData.CheckSquarePng`), and saves it in
  `Documents\Decal Plugins\Daralet Toolkit\world-data`. A file that matches the built-in one isn't downloaded. This all runs off
  the game's thread; `WorldData.Version` going up is the World tab's cue to reload the places and the maps
  (`MapView.Reload`), on the game's thread. Lookup order for both files: next to the dll, then downloaded, then built in.
  Daralet Admin has copies of `WorldData.cs` and `Locations.cs` and reads the same release: keep them the same. Raise
  `WorldData.Format` (and the workflow's `format`) only when old plugins couldn't read the files; they then keep what
  they have.
- **Item tooltips** (`Feed/ItemLines.cs`, `Ui/ItemTooltip.cs`): every stat an item's market description has, as lines in
  sections; the bank, the Loot tab, the Corpse window and market search all draw them, so the layout changes in one place.
  An item you hold or loot is titled with its own name, which the server sends with its material in front
  (`NameWithMaterial`; `ItemLines.WithMaterial` adds it if missing), and its value, burden and stack go on the workmanship
  line.
  The bank asks for descriptions as the mouse passes over items (`PlayerFeed.Describe`, 20 a request; `@playerui describe`
  covers your packs, your open bank and, with the `vendordescribe` feature, the stock of the vendor you last opened,
  found as the game's appraisal finds it, `PlayerUiCorpseData.FindVendorItem`). The server sends nothing for an item it won't
  describe, nor for a request its rate limit drops, so DescribeRequests keeps track: a request with no answer in 3
  seconds is sent again (3 tries), an item a reply left out is not yours (DescribeState.NotYours), and either is asked
  about again after 5 seconds if something still wants it; PlayerFeed.DescribeWaitText says which in place of the
  details. Tiers show as T0-T7 everywhere, the way the Search tab's filter has them.
  Speed is the weapon's WeaponTime (`MarketListing.WeaponTime`) with attacks a second after it, as "atk/s": 60 / WeaponTime,
  so 60 is one a second (`WeaponSpeed.GetSecondsPerHit` in daralet-ACE, at 100 Quickness). The Market API's Dps uses
  the same rule, so DPS and atk/s agree. Casters show no speed. Text is always drawn with `Widgets.Text*`
  (`TextUnformatted`): ImGui.NET's `Text*` read a "%" as a printf format. Items `@playerui` describes carry `rollQuality`: appraisal's Roll Quality section
  (`LootGenerationFactory.GetItemRollRanges`) by stat name, which `ItemLines` matches to the line showing the stat
  (`ItemLine.RollStat`) and `ItemTooltip` draws at the right. Market search has none: the Market API has no live item.
  **Sockets** are a section of their own (`ItemLines.Sockets`), as appraisal lists them (`AppraiseInfo.
  SetJewelcraftingUseText`): "Socket: Black Garnet (5)" or "Empty Jewel Socket", from `socketJewels`
  (`MarketRepository.ComputeSocketJewels`: each `JewelSocket<n>Material` and `Quality`, the first two sockets' legacy
  strings winning). An older server sends only `sockets`, the count, which then goes on the workmanship line.
- **Loot tab** (`Ui/LootTab.cs`, `Ui/Looter.cs`, `Feed/Loot.cs`): named lists (players see them as "loot profiles"; the code
  still says `LootList`) in `SettingsData.Loot`, each character
  picking one (`LootData.ActiveFor`). A rule is a category, tiers, name words and any of `MarketSearch.Filters`, plus an
  outcome (Keep, Salvage, Notify, Ignore); the first enabled rule that matches decides. The rule list is widened by
  dragging the bar between it and the editor (`Panes`, `loot-rules`; a width saved in `LootRuleListWidth`, pixels, from
  before, is moved over the first time it's drawn); a long rule name
  is cut short of its mark, which stays at the row's right end. **Folders** (`LootRule.Folder`, `Feed/LootFolders.cs`,
  `Ui/LootTab.RuleList.cs`) are only a name on rules, and a folder's rules are always kept together where its first one
  is (`LootFolders.Gather`), so the list's order is still the order rules are checked in. Ctrl/Shift+click picks several
  rules (`chosenRules`) for a folder or a drag; rules and whole folders drag to a new place (dropped on a rule: above it,
  in its folder, so on a folder's first rule at its top; on a folder itself: at its end, framed while held there; below the list: the end, out of any folder). Folded folders are
  `LootList.ClosedFolders`. Held near the list's top or bottom
  edge, a drag scrolls the list (`ScrollWhileDragging`). Right-click Duplicate copies a rule (or every picked one) just below itself, or a folder and its rules
  just below it as "Name copy" (`LootFolders.Duplicate`, `DuplicateFolder`). **Add folder** makes an empty one at the top
  (`LootList.EmptyFolders`, since a folder is otherwise only a name on its rules); a rule dropped in goes to the top of the
  list, and the folder leaves `EmptyFolders` once it has one (`LootFolders.Tidy`, after every change to the list). The list's changes wait until it's drawn (`pendingRuleMove`). `Loot.Matches` compares each filter
  with the same field the API's SQL does, per armor slot where the API is (ward level, armor and clothing mods), with
  "price" as the item's vendor value. A filter it doesn't know leaves the rule alone. Some filters are the loot rules' alone
  (`MarketFilter.LootOnly`, which the market search leaves out: the Market API has no such parameter): burden and value
  per burden for every category (burden 0 counting as 1), a weapon's
  **Subtype** (Melee / Thrown, Missile, Caster, from the class, `MarketSearch.SubtypeOf`), the damage stat for each
  (base damage, a launcher's `DamageMod`, a caster's `ElementalDamageMod` and `RestorationMod`) and **Damage element**,
  a multi-choice (`FilterKind.Multi`, comma-separated, any one matches). With a subtype picked only its filters show
  (`MarketFilter.Subtypes`), the Class list only its classes, and a hidden box doesn't count. Under it a weapon
  **Type** (`MarketSearch.WeaponTypes`: Sword, Atlatl, Orb...), which decides the subtype and shows its special
  property's filter (`MarketSearch.SpecialProperties`: Biting Strike, Crushing Blow, Armor and Ward Cleaving, Stamina Cost
  Reduction, `MarketFilter.WeaponTypes`), matched against `MarketListing.PropertyValues`. The type comes only from the
  server (`weaponType`, `PlayerUiWeaponType` in daralet-ACE, by the weapon's skill and its loot tables' caster and thrown
  rows, as `TryMutateWeaponSubtypeBonuses` picks the property), so a rule with a type matches nothing on an older one.
  Note the server's Sceptres roll Biting Strike and its Wands and Batons Crushing Blow. Armor's **Weight** shows only the
  mods that weight class rolls, and jewelry's **Slot** only the two ratings that slot rolls (rings crit damage and its
  resist, bracelets damage and its resist, amulets healing boost and max health; ward on all), both by `MarketFilter.Kinds`
  (from `TryMutateGearRating` and `LootGenerationFactory_Jewelry`; `Loot.KindOf` says which filter picks the kind). `Loot.Shows` decides what's
  shown, and so what counts. The editor shows the filters under headings, Types, Damage, Protection, Mods, Magic and Misc
  (`MarketFilter.Group`, set by `MarketSearch.GroupOf` from the parameter), leaving out a heading with nothing shown.
  **Spell** and **Scroll spell** take several names, comma-separated, any one matching (part of a name, case and spaces
  ignored, `Loot.WordsHold`); the Market API takes one, so the search sends the first (`MarketSearch.FirstSpell`).
  **Name has** takes alternatives after commas, any one of which will do, each words the name has to have, all of them
  (`Loot.NameHas`: "iron, steel sword" is anything iron, or a steel sword), checked against the name as the tooltip
  titles it (`ItemLines.WithMaterial`, the material in front even if the name lacks it). Before that a comma was part of the word, so
  a Salvage rule naming "Iron, Steel" matched nothing and every item fell to the rules below it: a catch-all Salvage rule
  under it salvaged everything: likely the report of names not narrowing a rule (not confirmed). **Material** (`material`, Salvage
  rules only, by their outcome, every category, under Types, `MarketSearch.ItemMaterials`; the user's call, November 2026) is a multi-choice of what the item is made of, by its
  MaterialType name (`CorpseItemInfo.Material`, else a bag's `SalvageMaterial`), which is also what it salvages into; with
  none ticked it says All and takes every item (`Widgets.MultiChoice`'s `noneTicked`), and with some an item of none
  doesn't match. Salvage's own single Material (`salvageMaterial`, the market search's) is hidden in
  Salvage rules unless saved filled in, which still counts; a Keep rule for salvage bags still has it.
  The Mods heading has an **OR** box (`LootRule.ModsAny`): on, any one Mods filter filled in that holds is enough, while
  every other filter still has to.
  **Max speed** (`maxWeaponTime`) is the weapon's own Speed, its WeaponTime, lower being faster; the Market API only filters
  its Speed (2 - WeaponTime / 100, held between 1 and 2.5), so the search sends it as that (`MarketSearch.AddSpeed`). Each filter's tooltip says
  the range loot rolls it in at the rule's tiers (`RollRangesInfo.Describe`), from `@playerui rollranges`
  (`LootGenerationFactory.GetLootFilterRanges` in daralet-ACE, worked out once from the loot formulas: by category and
  Market API parameter without min/max, in the filter's own units, the lowest to the highest any item of the kind rolls).
  Stats whose range depends on the piece itself (armor level, an armor's ward) have none. A Salvage rule only takes items with a
  workmanship (`Loot.Decide`); anything else falls to the rules after it. The looter moves one item at a time
  (`Actions.MoveItem`, the next once the last has left the corpse), and salvages after. The server puts a picked-up item
  only in the pack it was sent to (`TryAddToInventory` with `limitToMainPackOnly`), so a full main pack refused every
  loot while side packs had room, and players were told their main pack was full: each item now goes to a pack with
  room, the main pack first, then the side packs in order (`LootDestination`, `Looter.PacksWithRoom`, counted from
  every object's Container), each retry to the next one in case a specialized pack won't hold it. With no room anywhere
  it stops and says so; an item given up on says why it most likely was (`LootDestination.WhyNot`: packs full, over
  300% burden, the corpse closed first, or the server didn't hand it over). An item counts as looted once it's in your
  packs (`Looter.IsCarried`), not only once it's been seen in the corpse and then not: what was seen is forgotten when
  the corpse closes or another opens (`Forget`), so an item landing after that was waited on for 8 seconds and reported
  as a failure: a player was told "is your main pack full?" with the main pack at 25/102 (likely this; not confirmed). The server
  refuses a pickup that comes while the last is still starting ("You're too busy", `GameEventInventoryServerSaveFailed`,
  event 0x00A0), which back-to-back looting hits, and a move sent while the last is still going can get no answer at all;
  the looter sends the same item again 150ms after a refusal, or after silence for 1.5 times the last clean pickup (at
  least 900ms; a fixed 3 seconds made Loot all take 4 an item), and waits 250ms after each landing before the next, since
  a move sent the moment the last landed was always dropped unanswered,
  giving up after 8 seconds (a full pack). A refusal because of the item's quest (its error says so, `LootRefusal`: a
  stamped item on a corpse can only be taken as often as the quest can be solved, `Player.VerifyQuest`, so of three kill
  task heads one lands) can't work however often it's sent: that item and the others of its name in the queue are left in
  the corpse (`Looter.LeaveQuestItems`). Decal isn't told an item's `Quest`, so this is learned from the refusal; a quest
  with no limit is looted as usual. The Corpse window shows "Looting..." with Cancel while it works, in every mode
  but chat. It starts off; there's no way to tell from a plugin whether VirindiTank's looter is on, so the tab just warns.
  **Loot window** (`SettingsData.LootWindow`, switched under the Config tab's Client UI Override) hides the game's corpse
  and chest window (see above): the Corpse window then
  lists everything in it (`Looter.Everything`), matched or not, with Loot all, and works with the looter off too. **Grid**
  (`SettingsData.LootGrid`) shows it as 32-pixel icons, 8 a row (the window sizes itself), in a section per outcome (Keep,
  Salvage, Notify, Ignore, no rule), with the rule marks and the stack bottom right; Ctrl+click ticks and unticks for
  Confirm loot. The
  server's word that one opened can reach the client before its items do, so an item only counts as looted once it's been
  seen in the corpse and then isn't (`Looter.Left`); before that fix, killed corpses showed empty. `GetCorpse` skips its
  range check for a container the player has open: a corpse's use radius is small, and the game opens it from where
  `WithinUseRadius` said "gone". With Loot window on the looter ignores the client's ContainerOpened and follows the server
  alone, and the window opens for any status, saying why it can't show the items. It logs what it hears to log.txt
  (`[loot]` lines, `Looter.LogCorpse`).
  Each "min" filter for a stat that loot rolls (`Loot.RollStats`, by the names appraisal's Roll Quality section and
  `ItemLines` use) can be switched to a roll quality on its own (`LootRule.RollFilters`, by filter key): its box is then the
  lowest roll quality, 0 to 100, rounded as the tooltip shows it, and an item without that roll doesn't match. Switching
  empties the box. A rule can also mark what it decides (`LootMarks`: `LootRule.Highlight`, a frame colour, and `Icon`, a
  symbol in the top left corner, in `IconColor`, else the frame's colour, else gold). Colours are picked freely (ImGui's picker) or from
  the ready-made swatches beside it (`LootMarks.Highlights`: gold, silver, bronze and more), and saved as `#RRGGBB`; the
  swatches' names, which rules were saved with before the picker, are still read. Shapes are drawn with ImGui shapes; the grade
  letters S to F (`LetterS`...) with `DrawLabel`'s GDI+ text, through the one `ItemIcons` (`ItemIcons.current`), since
  the marks are drawn from static code. The editor's icon picker draws each symbol. Marks are drawn by `ItemIcons.DrawMark` in
  the Corpse window, the Test tab and, with **Loot highlights** (`SettingsData.InventoryLootMarks`), the Inventory tab, which has
  every listed item described to match it. Item tooltips (bank, packs, Loot tab, Corpse window) end with the rule that decides the item, in its outcome's colour (`ItemTooltip.Show(..., rule)`). The first rule that matches decides the mark too, so an earlier rule without
  one hides a later one's.
  **Unlearned spells only** (`LootRule.UnlearnedSpellsOnly`, Misc) takes scrolls whose spell the character doesn't know:
  `Loot.KnowsScrollSpell` is set by `PluginCore` from Decal (`LongValueKey.AssociatedSpell`,
  `CharacterFilter.IsSpellKnown`), and a scroll whose spell can't be told counts as unlearned.
  **Sellable only** (LootRule.SellableOnly, beside Salvageable only) takes only what a Market Broker would list: the
  server sends sellable with each item it describes (MarketBroker.IsSellable: not attuned, character-bound or
  unstable, and of a kind the market takes). An older server sends none, and then every item counts as sellable. Misc's
  **Type** Gems takes only a gem with a material (Loot.MiscTypeOf): the description calls anything of the game's Gem
  kind a gem, and those without one count as Other. The market search still goes by the Market API's word. New
  settings get the
  **Example Loot Profile** (`Feed/ExampleLoot.cs`, read from `Resources/example-loot-profile.json`, a profile as
  To file saves one: to change it, save one and copy it over) after "My loot", then **Example Loot Profile (colorless)**
  (`example-loot-profile-colorless.json`, `ExampleLoot.MakeColorless`), the same rules marked white, 50% gray and black
  instead of gold, silver and bronze: change both together. The Example buttons add them again, and tests check their
  filter keys exist and that the colorless one still matches the other. **To file / From file** save and load profiles as `.json` in `Documents\Decal Plugins\Daralet Toolkit\Loot
  Profiles` (`LootFiles`).

- **Social tab** (`Ui/SocialTab.cs`; once the Fellowship tab, its Fellowship sub tab once Members): sub tabs Allegiance, Fellowship, Find a group,
  Friends and Squelch, and the **Social window** (`social`, hidden to start), the same tabs in a `FloatingWindow`, which
  the toolbar's Social button opens (see Toolbar buttons). Friends, Squelch and Allegiance, and Create Fellowship's
  options, read `@playerui social` every 2 seconds while shown (`SocialActions.RefreshInterval`). The buttons that change
  them are `@playerui friend add <name> | remove <guid>`, `squelch character|account <name> | remove <guid>` (lifting an
  account squelch too), `allegiance swear <guid> | break | kick <guid>` and `fellowcreate <0|1> <name>`
  (`PlayerUiSocialData` in daralet-ACE), each handing on to the game's own handler (`HandleActionAddFriend`,
  `SquelchManager.HandleActionModify...Squelch`, `HandleActionSwearAllegiance`, `HandleActionBreakAllegiance` for a
  patron or a vassal, `FellowshipCreate`), so its checks and messages are the game's: the client's own messages for these
  carry a name (a `PStringBase`), which a plugin can't send. `FellowshipCreate` doesn't check you're out of a
  fellowship (the game's panel has no button then), so `fellowcreate` does. After each, `PlayerFeed.RefreshSocialSoon`.
  Appear Offline and the four fellowship options (Ignore Fellowship Requests, Automatically Accept, Share XP, Share
  Loot) are the client's own `AcClient.CM_Character.Event_PlayerOptionChangedEvent(PlayerOption, value)`, the message
  its checkboxes send (the server's `GameActionSetSingleCharacterOption`, which also tells friends for Appear Offline);
  the client's own copy of its options (`PlayerModule`) isn't changed, so the game's panels show the old value until
  the next login, and Apply in the game's options panel sends them all back. A ticked box shows as ticked for up to 4
  seconds, until a social reply has caught up (`SocialActions.Option`). The fellowship's own buttons are the client's
  own messages (`FellowshipActions`: AcClient's `CM_Fellowship.Event_AssignNewLeader`, `Event_Dismiss`,
  `Event_ChangeFellowOpeness`, `Event_Recruit`, `Event_Quit(0)`, and `Event_Quit(1)` to disband; `[fellowship]` lines in
  log.txt): Decal's `Actions.FellowshipGrantLeader`, `Dismiss`, `SetOpen`, `Recruit`, `Quit` and `Disband` each crashed
  the client. They're greyed out with the reason as their tooltip: Leader and Dismiss need you to lead and a member selected, Open needs you to lead an unlocked one,
  Recruit a selected player not in it and room (`SocialActions.CanRecruit`, the fellowship's `maxMembers` from the server:
  9, or no cap for a Town Defense fellowship, `Fellowship.MaxMembers`), Disband the leader. Friends'
  right-click menu: Invite (`FellowshipActions.Recruit`), Send tell (a popup, `/tell name, text`), Remove. Break and Kick ask
  first in a modal; Kick takes the vassal picked in the list, else the selection when it's a vassal.
  **The fellowship window** (`FellowshipTab.Window.cs`): Side by side puts a fellow's bars in a row, Bar width
  (`FellowshipBarWidth`, 130 to start) sizes each bar, and the name line can carry the Combat Focus's icon, the level,
  the distance and the buffs' (only those with a duration: the server leaves out any that never runs out) and debuffs'
  icons (`FellowshipShowLevel`, `Focus`, `Distance`, `Buffs`, `Debuffs`; the
  spells off to start), wrapping inside the bars' width. Spells are only asked for while the window shows them
  (`PlayerFeed.WantFellowSpells`): every tab asks for the fellowship through `PlayerFeed.RefreshFellowship`, since
  different arguments from different tabs would each count as new and never wait their interval.
  **Big fellowships** (`FellowshipTab.Layout.cs`, `Feed/FellowshipOrder.cs`, the window settings' Big fellowships): a
  Town Defense fellowship has no cap. Layout List scrolls past Scroll after (10), in a child as tall as that many members
  (measured the frame before); Pages is tabs of Page size, a tab red while someone on it is below half health; Grid is
  a tile each (name cut to fit, a health bar, stamina and mana strips), Columns to a row. In all three `FellowshipOrder.Arrange`
  puts pinned members first (right-click Pin to top, `FellowshipPinned`, by name), then Sort (the server's order,
  name, lowest health, nearest), leaves out ones too far to say with Hide far away, and stops at Show up to (0 for
  all); "+N not shown" says how many it left out. Spells are only asked for up to Spells up to (12) members, and a
  fellowship of more than 9 is asked for every 2 seconds, not every second.
  **Find a group** (`Ui/FindGroupTab.cs`, `Feed/LfgChoice.cs`, `Feed/LfgCommands.cs`, `Feed/LfgInfo.cs`), looking for
  group, whose server side came first so it works without Decal (daralet-ACE `docs/lfg.md`, `apps/server/Lfg/`):
  `/lfgroup <activity>` (join the first fellowship looking for more for it), `/lfgroup join <name>`, `/lfmore
  <activity> [manual]` (leaders: players looking for it join, or with manual the leader's asked about each), `/lfmore
  invite <name>` (the same as `/invite`), and `list` and `stop` on each. Not `/lfg`, `/group` or `/fellow`: the client
  keeps `/lfg` and `/clfg` for the LFG channel, and `/party`, `/g`, `/group`, `/fellow`, `/fellows` and `/fellowship`
  for fellowship chat (the command words in `acclient.exe`), so a server command by those names is never reached. The server posts each new lfg or lfm in the LFG channel under the player's
  name. The tab is a form that builds the words a player would type (`LfgChoice.Words`, which `LfgParser` in daralet-ACE reads) and shows the
  commands, then the fellowships and players looking (`@playerui lfg`, every 2 seconds while you're looking, 4
  otherwise), green where they fit you, with Join (Ask for a manual lfm) and Invite. Whether they fit, and why a button
  can't be used (`cantSeek`, `cantList`), is the server's say, from its own matching. Fellowship's "You aren't in a
  fellowship" has a Find a group button, which opens that tab (`Widgets.TabItem(..., select)`: ImGui.NET only takes
  tab flags with a close button's bool, so it calls `ImGuiNative.igBeginTabItem` with none).
  **LFG chat links** (`Ui/LfgChatLinks.cs`, `Feed/LfgChatLine.cs`, `SettingsData.LfgChatLinks`, on to start, under Find a
  group's Settings): each line the server posts ends with what to type to answer it (`- to join: /lfgroup join Arwen`,
  `- to invite: /lfmore invite Holmez`), so the plugin finds those in `ChatBoxMessage`, keeps the line from the chat window
  (`e.Eat`) and adds it again to the same window in the same colour (`AddChatText(text, e.Color, e.Target)`) with a
  `[Join Arwen]` or `[Invite Holmez]` link on the end. Only players with the plugin see the links. A re-added line comes
  back through `ChatBoxMessage`, so a line already carrying one of the plugin's links is left alone, and your own lines get
  none. Links use `PluginCore.ChatLinkId` like the update notice's, but carry only a number the plugin handed out (the last
  100 are kept), never the command: anyone can put that id in a tell, and a made-up link must send nothing. The first line
  linked each session is logged (`[lfg-chat]` in log.txt), with its colour and window.

- **Combat tab** (`Ui/CombatTab.cs`): **Vitals**, the Target tab it used to be (`Ui/TargetTab.cs`, still a class of its
  own), with your own Vitals window's settings above the target's (the user's call); **Combat Bars**, the Power bar's and
  Spell bar's settings (see Game panels); and
  **Hotbars** (`Ui/Hotbars.cs`, `Ui/HotbarWindow.cs`, `Ui/HotbarItems.cs`, `Ui/Hotkeys.cs`, `Feed/Hotbar.cs`,
  `Feed/Keys.cs`). Each bar (`HotbarBar`, in `SettingsData.Hotbars`) is a `FloatingWindow` (`hotbar-<id>`, titled
  `Hotbar <id>`, since UtilityBelt knows a window by its title) of columns by rows on up to ten pages, with its layout,
  looks and hotkeys shared by every character and its items per character (`Slots`, by name). The first bar is there from
  the start, hidden. Colours are packed as ImGui's u32 by hand (`PackedColor`). A slot keeps the item's guid and what it
  is (weenie class, `WorldObject.Type`, and name, and its icon layers): when that one's gone (a stack used up) it takes
  the fullest one like it you carry (`HotbarItems.Resolve`), and meanwhile shows the saved icon, shaded. Items go on by
  dropping them from the inventory's lists (`InventoryBrowser.DragType`, the item in `InventoryBrowser.DraggedItem`) or from the
  game's own pack (`GameDrag`); only what you carry or wear. While one's held over a slot, the slot shows its icon at half
  opacity (`HotbarWindow.DrawDropPreview`, `ItemIcons.DrawAt`'s tint), drawn after the slot itself, or the slot's
  background covers it; the red circle where it can't go. Slots only move in **Edit mode** (dragged, onto another
  slot on any bar to swap, or off every window to empty it) or from their right-click menu (Move..., which picks it up
  for the next slot clicked, and Move to); outside Edit mode a click uses the slot, on the press. Using is
  `ItemUser.UseFromBar`: `ItemUser.Use`, except a tool used on something goes straight onto yourself or your selection
  (`HotbarSlot.UseOn`, `HotbarUseOn`: Auto is yourself if it works on you, else the selection, else the pick cursor).
  A kit's use flags name only a target that isn't you (in the game you select or pick yourself), so Auto never puts one
  on you; **One-click Heal** (`SettingsData.OneClickHeal`, on by default) sends any kit (`ItemUser.IsKit`: Decal's
  `HealingKit`, the server's Healer, whichever vital) to you at once, unless its slot says Your selection or Pick.
  `ItemUser.RenderCursor`, from every bar, keeps a wield's steps and the reticle going with the inventory closed. A
  slot's right-click menu also carries the window's Lock, size, background and Hide (`FloatingWindow.RenderMenuItems`),
  since a bar is all items and the window's own menu doesn't open over items. Bars use less window padding
  (`FloatingWindow`'s `padding`, set and put back like the border).
  **Layouts** (`Feed/HotbarLayout.cs`, `Ui/Hotbars.Layouts.cs`, under the page's bar list): Export layout saves every bar
  (layout, look, keys), each bar's window placement, and with With my items the saving character's items, as a
  `.json` in `Documents\Decal Plugins\Daralet Toolkit\Hotbar Layouts`; Import layout asks, then puts its bars in place
  of yours (`HotbarLayout.Apply`): its items become the loader's, other characters keep theirs on bars of the same number,
  and a layout without items leaves the loader's own. The windows are made again (`Hotbars.RemakeWindows`, as on a
  profile swap).
  **Hotkeys** are a key per place (row and column, whichever page shows), page keys, and Bind keys (hover and press).
  They're read from Windows (`GetAsyncKeyState`, every bindable key, once a frame from `Hotbars.Frame`), not ImGui, which
  only hears keys while it wants them; they fire only with the game's window in front, the chat bar not active
  (`Actions.ChatState`) and no ImGui text box typed in, and a hidden bar's keys do nothing. A plugin can't keep a key from
  the game, so the tooltips say to pick keys it doesn't use. One key does one thing (`HotbarData.Forget`).
  **Cooldowns:** an item's cooldown group and length are `SharedCooldown` (PropertyInt 280) and `CooldownDuration`
  (PropertyFloat 167), which only an appraisal sends (the first look asks, `Actions.RequestId`); Decal keeps them by
  number. A running cooldown is an enchantment on the player whose spell id is `0x8000 | group`
  (`EnchantmentManager.StartCooldown` in daralet-ACE). Decal's `CharacterFilter.Enchantments` doesn't pass those on, so
  `HotbarItems` reads them from the server's own messages (`EchoFilter.ServerDispatch`, game events 0x02C2 to 0x02C8,
  parsed from `Message.RawData` by `Feed/CooldownMessages.cs` in `Enchantment.Write`'s order; time left is the
  duration less the start time's size, the server's own sum), and those come first. Everything is by cooldown group,
  so one use shows on every slot sharing it (the combat ability gems, potions of a kind). `[cooldown]` lines in
  log.txt. A cooldown already running at login isn't seen until it's next started. Decal's list is still read after. In case Decal doesn't
  list those, a use from a bar starts one of its own (`HotbarItems.Used`), but only once the use shows it worked (`Confirm`: a
  use gone, fewer of its kind, or the item gone, within 3 seconds), as the server's does: a kit at full health starts none. Shown as a clock sweep
  (`CooldownShape.Sweep`, drawn as triangles with anti-aliased fill off, or the seams show), fill, dim or bar.
  **Structure** is Decal's `UsesRemaining` / `UsesTotal`.
  **Spells** (`HotbarSlot.SpellId`, `Ui/Spells.cs`, `Ui/SpellDrag.cs`): a slot can hold a spell instead of an item, put
  there by dragging it from the game's spellbook or spell bar, or from a slot's right-click menu (Put a spell here, the
  spells you know by part of their name). A plugin can't see the game's drags, and `GameDrag` takes the selection for what's
  dragged, which put the last selected item on the bar; so `SpellDrag` listens where the game says a drag began,
  `CM_Item::SendNotice_BeginDrag(itemId, spellId, flag)` (static, cdecl, entrypoint 0x6A9640): its three calls in the client
  are pointed at the plugin with UtilityBelt.Service's `AcClient.Hook(entrypoint, callLocation)`, the plugin notes a known
  spell's id (under 0x10000, `IsSpellKnown`) and calls the game's function on unchanged, and `Stop` puts the calls back
  (a hook left in after the plugin's gone would crash the game). `[spell-drag]` lines in log.txt. UtilityBelt.Service
  ships the client's own types and addresses as `AcClient` (`UIElementManager.s_pInstance`, `ClientMagicSystem`,
  `Entrypoint`, `Call`), which is where to look for anything else of the client's. A spell let go over a plugin window is
  kept from the game like an item (`GameDrag`), or the game takes it off its spell bar. What a spell is (name,
  description, school, icon, flags, target type, mana) is the portal dat's spell table, `UBService.PortalDat.SpellTable.
  Spells`, by reflection; Decal's `Decal.FileService.dll` has a spell table too, but it's built against Decal.Adapter
  2.9.8.3 and won't link against the 2.9.7.5 the plugin uses. Casting is Decal's `Actions.CastSpell`, which needs
  spellcasting mode; the bar doesn't switch modes for you (the user's call): out of it, it says "You must be in
  spellcasting mode to cast spells." and does nothing, before any word about the target. The target: none for an untargeted spell, you for a self-only one (SpellFlags.SelfTargeted
  0x8), your selection for a harmful one (not Beneficial 0x4), and your selection or you for the rest. A spell you don't
  know shows shaded.
  **The settings page** (`Ui/Hotbars.Settings.cs`, with its Keys tab in `.Settings.Keys.cs` and the form pieces in
  `.Settings.Controls.cs`): the bars down the left (shown or not,
  New hotbar) with Move slots (Edit mode) and Bind keys as lit buttons; the chosen bar on the right, under a preview of
  made-up slots (a count, a cooldown with uses left, a worn item, empty ones), in tabs Layout, Look, Cooldowns and uses,
  Keys and Items. Every slot anywhere (the bar, the preview, the style and cooldown pickers) is drawn by `SlotPainter`
  from a `SlotLook`, so the preview is what the bar looks like. Look has ready-made styles (`HotbarStyles`: every colour
  and the border at once, `HotbarBar.Style`), and the colours one by one under Custom colours, which makes the style
  Custom. Keys is a grid of the bar's places to click and press a key in, a Key size of its own (`KeyTextScale`, 0 following
  the Text size, `KeyScale`; the cooldown's seconds have a Number size under Cooldowns and uses, `CooldownTextScale`,
  150% to start, and Show Decimals, off to start), with quick sets for a row (1 to 0, Shift + 1
  to 0, F1 to F12). All slot text (hotkeys, counts, cooldown seconds, uses left) is
  `ItemIcons.DrawLabel`: ImGui stretches one small font texture, which blurs and dims bigger text, so each label is drawn
  by GDI+ (Segoe UI bold, white, black outline, cut close round its letters) into a texture at exactly its pixel size,
  tinted to its colour, and kept (400 at most); ImGui's text if one can't be made. Forms are two-column tables (`Form.BeginForm`, `Form.Label`), and rows of checkboxes and pickers wrap to the
  width (`Flow`) rather than running off the side.

- **Character tab** (`Ui/CharacterTab.cs`), and the **Character window** (`character`, hidden to start) that's the same in
  a `FloatingWindow`: first what the game's Character Information panel says, then everything the Combat Focus appraisal
  shows under Character Stats (gear mods, ratings, ward, armor level, resistances, jewel effects), from `@playerui
  character`, every two seconds while either is open. The Character Information part is the client's own, worked out in
  the plugin from Decal's `CharacterFilter` (`Birth`, `Age`, `Deaths`, `Attributes[...].Creation` and `.Base`,
  `EffectiveAttribute`, `BurdenUnits`, and `GetCharProperty` for any int property the player description carries:
  augmentations, chess rank, fishing, luminance augmentations) by `Feed/RetailCharacterInfo.cs`, in the panel's own
  sentences, taken from `client_local_English.dat`'s string table `0x23000001` (played time's units from `0x23000006`,
  365-day years and 30-day months as the server's `/age`). The resistance and regeneration words are the client's steps,
  which `Player.GetNaturalResistanceString` and `GetRegenBonusString` in daralet-ACE copy (unused there); the
  overburdened line is `EncumbranceSystem`'s sums. The panel's code isn't in the data, so the augmentations' order is the
  plugin's, and the Seer's auras (Destruction, Invulnerability, Retribution, Hardening), which share their properties with
  Nalicana's, aren't told apart: each luminance augmentation shows once, as Nalicana's. Server side, `Player.GetStatsSheetSections` (`WorldObjects/Player_StatsSheet.cs`) builds the sections and both the
  appraisal's text (`GetStatsSheetText`) and the reply (`PlayerUiCharacter`) come from it, so they can't disagree. Each
  line is split at its first ": " into stat and value (`StatsSectionInfo.Split`); in a wide window the sections go in two
  columns.

- **XP bar** (`Ui/XpBar.cs`, `XpBar.Settings.cs`, `Feed/XpInfo.cs`, `RestedInfo.cs`, `XpRate.cs`, `XpBarOptions.cs`): your
  progress through your level, in the **XP Bar window** (`xp-bar`, hidden to start, sized to fit) and at the top of the
  Character tab's stats, its settings (`SettingsData.XpBar`, `XpBarOptions`: width, height, text, dividers, colours,
  the lines under it) under the Character tab's Settings. Your XP is Decal's (`CharacterFilter.TotalXP`), so the bar
  moves the moment a kill lands; the level's bounds are the server's (`@playerui xp`), since the client has no XP table:
  past them (a level since the reply) the bar shows full until the next. The bar's drawn by hand on the draw list
  (background, rested, fill, dividers, border, outlined text), not `Widgets.Bar`. **Compact** (`XpBarOptions.Compact`, the
  settings or the window's right-click menu) leaves the window the bar alone, no text on it or lines under it: its tooltip
  has everything (`Lines`). The Character tab's bar is always whole. **Dragging the window's left or right side** widens
  or narrows the bar (`XpBar.Resize.cs`, through `Ui/SideGrips.cs`, which the Vitals, Power bar and Spell bar windows
  share; the user's call, November 2026): the window sizes itself to the bar, so ImGui's
  edges can't, and a strip down each side sets Width as it's dragged, with the resize cursor and a line down that side;
  the left one moves the window too (`FloatingWindow.MoveTo`), so the right side stays put. The first build's grip sat
  at the bar's end, inside the window, where it wasn't found. On a locked window they work with Ctrl held. Ctrl
  disables the contents and makes a press a move (`CtrlDrag`), so the strips are worked by hand
  (`IsMouseHoveringRect`, the press and release), and say when they have the press (`FloatingWindow.CtrlHandleHeld`,
  which `CtrlDrag` and `SnapWhileDragged` check, the contents being drawn first) or the mouse (`HandleHovered`, which
  keeps `EdgeDrag`'s top-edge move off the top corners). Untested in the game when written. It asks every
  5 seconds, and 1.5
  seconds after Decal's XP moves since the last reply (not every kill: in a fast fight that's a request each half
  second). The window asks from its `inUse`, which runs every frame it's on, so Only when needed (hidden at the highest
  level) still keeps it fresh. **Rested experience** is daralet-ACE's (its `docs/rested-xp.md`, `Player_RestedXp.cs`,
  `RestedXpRules`): a pool built up while logged out, or in a town landblock out of combat mode, that kill XP (and a
  fellow's share of a kill) spends, earning `rested_xp_bonus` more until it's gone. The server keeps it; the bar shades
  it ahead of your XP, and between replies grows it while resting and takes off the bonus share of XP earned
  (`RestedInfo.RestedAfter`), a guess the next reply corrects. **XP/hour** (`XpRate`, off to start) is the plugin's own:
  Decal's XP noted once a second on RenderFrame (window or not), over the last 5, 15, 30 or 60 minutes or since login
  (`XpBarOptions.RateMinutes`), starting again for another character, when XP goes down, or from the window's
  right-click menu; with the time the next level takes at it.
- **Quests tab** (`Ui/QuestsTab.cs`): your contracts, labelled as quests, from `@playerui quests` every five seconds while
  open. The client is only sent each contract's id, stage and timers (`GameEventSendClientContractTracker`); the server
  sends the rest of its portal.dat ContractTable entry too (`Entity/PlayerUi/PlayerUiQuestData.cs`): descriptions, the
  start and end NPCs, and their places and the quest area's, placed like buildings (`PlayerUiData.BuildingMapCoords`,
  `Town.IsOnMap` leaving out dungeons). The table's NPC places are only rough (Renald the Eldest's is 38 meters off), so
  the server uses the NPC's own spawn in that landblock, by name, when it finds one (`PlayerUiQuestData.NpcSpot`). Each place's Go makes it the World tab's destination (`WorldTab.GoTo`). Abandon
  (asked twice) is `@playerui abandon <contract id>`, the Contracts panel's own `HandleActionAbandonContract`, which
  keeps the starter contracts until they're done (`Player.CanAbandonContract`). Stages are available, started (a
  progress counter too) and done. The **Quests window** (`quests`, hidden to start) is the same list in a
  `FloatingWindow`, sharing the tab's search and picked quest. The **Quest Tracker** (`QuestsTab.Tracker.cs`,
  `quest-tracker`, hidden to start, switched on under the tab's Settings) is a small self-sizing window of the started
  quests (with Not started too, `QuestTrackerAvailable`, the ones given but not started as well), each a line with its
  progress and time left that opens out to the tab's details but Abandon (`RenderDetails(..., inTracker: true)`, the
  description wrapping at 22 lines of text). A quest is left out by unticking its Track box, the tab's last column (the
  row's Selectable has `AllowOverlap`, or it would take the box's clicks), or with Hide from tracker in the tracker's
  right-click menu, opened from the quest's line (`menuQuest`, `FloatingWindow.OpenMenu`); both are
  `QuestTrackerHidden`, and Show hidden under its settings clears it. Which are open is saved by contract id (`QuestTrackerOpen`); it asks for
  `quests` on the same 5 seconds while shown, and Only when needed hides it with nothing to track.

- **Toolbar buttons** (`Ui/ToolbarButtons.cs`, `Ui/ToolbarButton.cs`, `Ui/ClientElements.cs`): the game toolbar's
  backpack, Contracts (quill), Social (two heads) and Map (compass) buttons, and the Character Information button in the
  indicators beside it (the burden pack), drawn again over the game's own, opening the Inventory, Quests, Social, Map and
  Character windows instead of the game's panels; each on or off by itself, together in the Config tab's **Client UI
  Override** section (`ClientUiOverride`, with Bank window, Loot window, Chat and the game panels (see Game panels);
  `ToolbarButtons.RenderToggles`;
  `SettingsData.ToolbarInventory`, `ToolbarQuests`, `ToolbarSocial`, `ToolbarMap`, `ToolbarCharacter`, off to start),
  all seven in one row under **Enable all** and
  **Disable all**. Enable all asks first when it would turn Bank window or Loot window on (`Windows.ConfirmAllOn`), as
  their own checkboxes do, and leaves them off on a server too old for them; Disable all says what follows when either
  was on (`Windows.NoticeAllOff`). The rest is the game's.
  **Shared window** (`Ui/SharedWindow.cs`, a row of its own in Client UI Override, under a line that sets it apart
  from the panels' checkboxes; `SettingsData.SharedWindow`, on for
  everyone, the user's call, November 2026): the Inventory, Map, Social, Quests, Character, Attributes, Spellbook,
  Options, Buffs, Debuffs and Vitae windows share one place on screen, as the game's panels do, all but those kept
  separate (`SharedWindowSeparate`, by window id). Each window says so for itself, not the Config tab: **Separate** at
  the end of the shared window's tabs takes the one showing out, and **Share with the other panels**, ticked while it
  shares, at the top of its right-click menu's window items (`FloatingWindow.SharedMenu`), takes it out or puts it back.
  Taken out, it stays just where it was drawn (`FloatingWindow.DrawnBox`, the box snapping notes), that box made its
  own placement (`OwnPlacement`, `PlaceAgain`); put back, it shows in the shared window. Settings from before
  (`SharedWindowSeparate` null) are worked out once from what was ticked (`SharedWindowMembers`, read only for that)
  and the shared window switched on (`SharedWindow.Upgrade`, at start and on a profile swap). They stay windows of their own: the one opened last, from
  anywhere, shows, and the others are closed (`FollowOpening`, by each one's Visible from frame to frame); they take one
  position, size, lock, Size and background, the window `shared` (`FloatingWindow.SharedLook`, with Visible and Only
  when needed kept their own; the size is put on every frame the mouse isn't down, so whichever shows takes it), and
  with **Tabs** (`SharedWindowTabs`, on to start) a row of buttons at the top of each to switch
  (`FloatingWindow.Header`). **Attach to toolbar** (`SharedWindowAttach`) holds it right above the toolbar's frame and
  as wide (`ClientElements.TryGetFrameBox(Toolbar)`: TBAR's parent, `UIRegion.m_parent` at `+0xAC`, when it fits snugly
  round it), as tall as the game's panel frame (`ClientElements.PanelFrame`, `0x100005FF`, 310x372 at 800x600, and much
  smaller on a new character) until its free edge is dragged (`SharedWindow.RenderAttachedGrip`,
  `SharedWindowAttachHeight`; a double-click goes back to the game's height, and the game's panels aren't touched), below
  the toolbar when there's no room above, with no title bar or dragging (`FloatingWindow.Anchor`). The edge is sized by
  hand, not ImGui's resizing: the window is put back on the toolbar every frame, so an edge against the toolbar would
  grow without end. Not on the panel frame itself, as the first build had it: that's a `gmFloatyPanelUI`
  (element type `0x1000004F`, a gmPanelUI), a window of its own whose place the game saves (`gmFloatyPanelUI`'s
  attributes `0x10000086`-`89`), so it stayed put when the toolbar was moved. The frame is shown while a game panel is
  open, so it turning on (a toolbar button not drawn over, the skills or options, a game key) closes the shared window;
  and one of its windows opening closes the game's panel (`ClientElements.CloseGamePanel`): what the toolbar's own
  buttons send, `CM_UI::SendNotice_SetPanelVisibility(panel id, 0)` (static cdecl, `0x47A4A0`), the id the open panel's
  in gmPanelUI's notice handler (`+0x5F8` in the element: its (element, id) pairs at `+0x4`, count `+0xC`, the open one
  at `+0x10`, as `gmPanelUI::RecvNotice_SetPanelVisibility`, `0x4BD380`, reads them). `[shared-window]` lines in
  log.txt say when the frame opens and closes, where the window's attached, and when a game panel was closed.
  **Hide hotbar** (`Ui/GameHotbar.cs`, `SettingsData.HideGameHotbar`, off to start; in Client UI Override's row and the
  Hotbars page's Using, one setting): the game's shortcut bar is hidden while every slot is empty
  (`GameShortcuts.AllEmpty`: `gmToolbarUI::IsShortcutSlotAvailable`, `0x4BE270`, 1 for a slot in range whose item list
  holds nothing). Its 18 slots are the toolbar root's own children in layout `0x21000016` (300x122), under its buttons
  (rows at y 58 and 90), and the toolbar's frame (TBAR `0x10000603`, a gmFloatyToolbarUI from layout `0x21000070`, its
  content at 5,5) clips them to its own height: 100 shows the buttons and one row, 132 both, and the game holds it
  between the two (attributes `0x3E` and `0x3C`, the least and most height, which `UIElement::ResizeTo` keeps to). So
  the first build, which hid the slots (`UIElement::SetVisible`), changed nothing to see: the frame stayed as big.
  Now the least height is lowered to 68, the buttons and the borders (`UIElement::SetAttribute_Int`, `0x4606B0`), and
  the frame made that height (`ClientElements.Place`: the base `UIElement::ResizeTo`, `0x463D60`, and `MoveTo`,
  `0x4634C0`, not gmFloatyToolbarUI's own, `0x4D0A90` and `0x4D0C00`, which save the place in the player's UI
  preferences), keeping its bottom edge where it was (its top in the top half of the screen). Nothing of it is saved,
  and logging out makes the toolbar again. Looked at once a second, shrunk again if it grew, and put back when the
  setting goes off, the bar gets an item, or the plugin shuts down. `[hotbar]` lines in log.txt.
  The burden pack is element `0x100000F7` in the indicators' layout `0x21000071` (INDI `0x10000611`, the panel it's
  checked against instead of TBAR), its pictures by burden `060074A2` / `060074A3` / `060074A4` (states E, F, 0x10), with
  `0600749B` framed over it while its panel's open (its child `0x100000F2`'s state 3). The game's cut-offs aren't in the
  data: the plugin goes yellow over 100% and red at 200%, where `GetBurdenMod` is 0 (`RetailCharacterInfo.BurdenLevel`,
  from Decal's `Burden`, a percent). A button's pictures are drawn in layers, chosen each frame (`ToolbarButton.Pictures`).
  Each is a hud of its own with no padding, border or background, placed every frame exactly on the game's button,
  which `ClientElements.TryGetScreenBox` finds by element id (`UIElementManager::GetElement`), with its screen box from
  `UIRegion::GetScreenX0` and friends, and shown only while it and the toolbar (TBAR) are visible. Those are the client's
  own functions, called at the addresses AcClient gives them with plain `void*`s, never through AcClient's types: they
  aren't blittable, so .NET throws on a call taking or returning one ("Pointers cannot reference marshaled structures",
  from `UIElement.GetChildRecursive`, every frame), and their fields' places in memory aren't promised. A call that fails
  is logged once and the lookups stop until the next start. `UIElementManager::s_pInstance` is read from its address
  each time: UtilityBelt.Service's copy is read once, when its type is first used. A click on an ImGui window doesn't reach the game, so the game's button
  underneath does nothing. The pictures (`toolbar-*.png` in `Resources/ui`, normal and `-open`, the game's state 1 and 6)
  are lit while the window's open, as the game lights a panel's button. An item dragged from the plugin's lists onto the
  backpack goes in the main pack; one dragged from the game's pack isn't covered (`GameDrag.Cover`), so the game hears it
  let go over its own backpack underneath. The game draws its drag icon under every ImGui window, so the buttons draw it
  again over themselves while it's held there (`GameDrag.DrawIcon`, as the hotbars do). The ids and pictures came from the toolbar's LayoutDesc, `0x21000016` in
  `client_local_English.dat` (inside `0x21000070`, the frame, inside `0x21000005`, the main UI, as TBAR `0x10000603`):
  backpack `0x100001B1` (`06004CF7`, open `06004CF8`), Contracts `0x1000055A` (`060069AE` / `060069AF`), Map `0x1000019A`
  (`06001116` / `06001118`), Social `0x10000197` (`0600111F` / `06001121`). daralet-ACE's `libs/dat-loader` reads LayoutDescs (`LayoutDesc`, `ElementDesc`, `MediaDesc`)
  and exports textures (`Texture.GetBitmap`); it needs `ConfigManager.Initialize(new MasterConfiguration())`, code page
  1252 registered, and `DatManager.PortalDat` set (for `BaseProperty`), without `DatManager.Initialize`, which fails on
  Daralet's dats reading their iteration.

- **Hide game UI** (`Ui/HideGameUi.cs`, the Config tab's section of that name, `SettingsData.HideUiKey`, F1 to start:
  the game ignores Ctrl and Shift on its own keys, so Ctrl+Shift+F12 opened the inventory and any S stopped running): for screenshots. Each piece of the in-world UI Decal's `UIElementType` names but the 3D view (Smartbox, `0x1000049A`)
  is hidden with `UIElement::SetVisible` (`0x462390`), those that were showing shown again after
  (`ClientElements.GameUiPieces`). The first build hid the in-world UI's root element instead (`ClientElements.SetUiVisible`,
  still called on showing, in case a hot reload follows a build that did), which took the 3D view with it, and a hidden
  element gets no mouse: the right button stopped turning the camera. The root: `UIFlow::m_instance` (`0x83E72C`), its current UI at `+0x94`, which must have `gmGamePlayUI`'s vtable
  (`0x7C3828`), and that UI's root at `+0x9C` (stored by `gmGamePlayUI::SetupChildren`). Two look-alikes that don't work
  (read from `acclient.exe` with capstone): `UIElementManager::SetVisible` (`0x459440`) only handles the cursor and mouse
  capture, so the first build of this hid nothing of the game's; `UIFramework::ForceHidden` (vtable `+0x2c`) only hides
  child frameworks with `m_bCanForceHide` (`+0x7c`), which the constructor clears. It also hides every `FloatingWindow` and
  the main window while `HideGameUi.PluginWindowsHidden` (unless Keep plugin windows, `SettingsData.HideUiKeepPluginWindows`),
  keeping each one's own setting, and the toolbar buttons while `HideGameUi.On` either way (the toolbar they sit on is gone). Other plugins' windows and
  Decal's bar stay. It's hidden again once a second while on, in case the game shows it itself. Never saved: logging out
  clears it (the game sets its UI up again for the next login, so nothing is called then), and the plugin shutting down
  or reloading shows it again. Its key is read straight from Windows on Decal's RenderFrame, not through
  `Hotkeys.Pressed`, which only runs when some window draws, and with every window hidden nothing would. `/dap hideui`
  toggles it and `/dap showui` only shows. `[hide-ui]` lines in log.txt.
- **Game panels** (`Ui/GamePanel.cs`, `Ui/GamePanels.cs`, `Ui/GameElementHider.cs`, `Feed/GamePanelOptions.cs`): plugin
  windows that take the place of a game panel, the start of replacing nearly all of the game's UI (still to do: the
  Options' Chat page and Config's lists).
  Each is a `GamePanel`: a `FloatingWindow` (its key as id, hidden to start), **Replace**
  (`GamePanelOptions.Replace`; ticking it shows the window too), which hides the game's elements while on, and a
  **Look**, Retail (the client's art, the default, and what an unknown value reads as) or Modern (plain ImGui in the
  plugin's theme), also in the window's right-click menu. Replace is a checkbox in Client UI Override's row, and again
  with the look, the window's controls and the panel's own settings where the panel says (`SettingsTab`: Vitals on the
  Combat tab's **Vitals**, the Power bar and Spell bar on its **Combat Bars**, two abreast); a panel with none (the
  Toolbar, the Indicators) has them under Client UI
  Override's folded **Game panels**, which isn't drawn while every panel has a tab. Choices are
  `SettingsData.GamePanels` by key (`SettingsData.GamePanel(key)` makes one). The hiding is `GameElementHider`, Hide
  game chat's way made general: `ClientElements.SetVisible` by element id, looked at again once a second
  (`checkEvery`, every frame for one the game shows often), those that were showing (or that the game has shown since)
  shown again when Replace goes off or the plugin stops (`showAgain` decides for one the game would have hidden since);
  at logout it just forgets, as the game makes its UI again; while Hide game UI is on it does nothing, since that hides
  and shows the whole UI itself. Hiders may share an element (the Combat floaty is both the power bar's and the spell
  bar's): whether each was showing is kept per element for all of them (`WasShown`), and one isn't shown again while
  another hider still wants it hidden, so a change from melee to magic hands it over without a flash. A panel can hide
  the game's only some of the time (`hideWhen`) and say when its window has something to show (`inUse`, for Only when
  needed). `[game-panels]` lines in log.txt. Enable all and Disable all don't touch them, as with Hide hotbar and Chat.
  A new panel is a `GamePanel` subclass made in `GamePanels` (or added with `GamePanels.Add`, after the windows it
  needs: the Toolbar). **Resizing sideways** (`GamePanel.WidthGrip`, the user's call, November 2026): the Power bar,
  Spell bar and Vitals windows size themselves, so they're made wider or narrower by dragging the window's left or
  right side (`Ui/SideGrips.cs`, shared with the XP bar), which changes what's in them (the bar's width; the vitals'
  Bar width, a drag shared among the bars when they're side by side; spells to a row). The first builds' grip, three
  short lines at the right of the contents, unlocked windows only, wasn't found ("I still can't resize the vitals
  bar by dragging the edge"). Not ImGui's window edge, which a window that sizes itself hasn't: a strip down each
  side, worked by hand, on an unlocked window or a locked one with Ctrl held, with the resize cursor and a line down
  that side; the left one keeps the right side put (`FloatingWindow.MoveTo`, from the window's width each frame). It
  snaps as a resized edge does (`FloatingWindow.SnapSideEdge`), and returns how far the contents are to grow each
  frame. A window with them (`FloatingWindow.ContentGrips`) isn't scaled by its edges (EdgeScale).
  **Vitals** (`Ui/VitalsPanel.cs`, `Feed/VitalsOptions.cs`, `SettingsData.Vitals`): replaces the game's vitals panel,
  element `0x100005FA` (Decal's `UIElementType` Vitals, one of Hide game UI's pieces; its layout `0x2100006C`, meters as
  under Retail bars), and the game's other one, `0x100006D5` (layout `0x21000075`, 460x26, the three bars in a row, also
  in the main UI's layout `0x21000005`), which the first build left showing ("0x100005FA wasn't shown" in log.txt while
  the game's vitals were on screen); both are hidden, and Hide game UI hides it too. The numbers are Decal's `Actions.Vital` (`VitalType.CurrentHealth`, `MaximumHealth`...), read from
  the client's memory as its own panel shows them, buffs included, so only once logged in (`CharacterFilter.Health` and
  `EffectiveVital` are Decal's other copy, from the network). Retail draws the meters' art at 16 pixels
  (`Widgets.Bar(..., retail: true)`, which takes the look from the caller instead of Retail bars), Modern ImGui's bar.
  Health, Stamina, Mana, Side by side, Bar width (150 to start, the game's) and Text (numbers, percent rounded down, both
  or none, `VitalsOptions.Format`); Bold numbers is the shared `BoldVitalText`. The window doesn't start where the game's
  panel was: put it there by hand. Untested in the game when written.
  **Power bar** (`Ui/PowerBarPanel.cs`, `Ui/ClientCombat.cs`, `Feed/PowerBarOptions.cs`, `Feed/CombatSnapshot.cs`,
  `SettingsData.PowerBar`): the power bar (melee) or accuracy bar (missile), the desired power, and High, Medium and Low.
  The game's is the Combat floaty, element `0x100006B5` (Decal's `UIElementType` Combat), layout `0x21000073`: a 610x90
  frame holding gmCombatUI (`0x1000005C`, from layout `0x21000007`) for melee and missile and the spell bar
  (`0x10000061`, from `0x1000009F`/`0x21000010`) for magic. So it's hidden only in melee and missile, looked at every
  frame (gmCombatUI::RecvNotice_SetCombatMode, `0x4CD210`, shows itself again at each change to melee or missile, in
  states `0x10000003` and `0x10000004`), and shown again only in a combat mode, since the game hides it in peace. In
  `0x21000007`: the background `0x1000004C`-`4E` (`06004CC2`), the slider `0x1000004F` (type 0xB, 707x14, track
  `060074CA`, its thumb `06001923`) with the meter `0x10000050` (type 7, fill `06001200`) in it, text `0x10000051`-`55`,
  and the height buttons `0x10000057`-`59` (High, Medium, Low; the window stacks them High on top as the game does, or
  in a row Low, Medium, High from the left, the user's call, `PowerBarOptions.HeightsInOrder`; in `0x10000056`, from
  template `0x1000005A`: states 1
  normal `06004D1C`, 3 pressed `06004D1D`, 6 chosen `06004D1E`, 8 chosen and pressed `06004D1F`). The bar's art is 60x13
  tiles the game repeats along it (`ItemIcons.DrawArtTiled`). `0x10000613` (ProgressBar, layout `0x21000072`, the yellow
  `06001354` and cyan `0600195B` fills) is a different bar, likely the jump bar, left alone. Everything shown is the
  client's `ClientCombatSystem` (`s_pCombatSystem` at `0x87166C`, which `GetCombatSystem`, `0x56B210`, returns; fields at
  AcClient's offsets: `combatMode` `+0x1C` (COMBAT_MODE 1 peace, 2 melee, 4 missile, 8 magic), `requestedAttackHeight`
  `+0x24` (1 high, 2 medium, 3 low), `buildInProgress` `+0x30`, `latestPowerBarLevel` `+0x38`, `m_rUIRequestedPower`
  `+0x4C` (the slider's desired power), `m_bAdvancedCombatMode` `+0x50`), read every frame; while it builds the level is
  `GetPowerBarLevel` (`0x56BB20`). What the window does is what gmCombatUI::ListenToElementMessage (`0x4CD020`) does for
  its own: a height pressed (message 0x1C) is `SetRequestedAttackHeight` (`0x56D640`), which starts building; let go
  (message 1) `EndAttackRequest(height, *(float*)0x7B9420)` (`0x56CE30`; that global is -1, the bar's own level, and no
  attack goes past the desired power, advanced combat or not); the slider (message 0xA) stores the desired power at
  `+0x4C`, which the window does too, then calls `CM_Combat::SendNotice_DesiredAttackPowerChanged` (`0x6AAE60`, static
  cdecl) so the game's slider follows. A held button is let go of when the mouse is, read from Windows, in case the
  window went first. Only when needed is ticked the first time it's made (`PowerBarOptions.WindowSetUp`). Its grip
  sets Bar width. Found with
  throwaway tools: Iced over `acclient.exe`, and daralet-ACE's dat-loader dumping LayoutDescs and exporting textures.
  Untested in the game when written.
  **Spell bar** (`Ui/SpellBarPanel.cs`, `Ui/ClientSpellBar.cs`, `Feed/SpellBarOptions.cs`, `SettingsData.SpellBar`): the
  game's eight tabs of favourite spells in magic combat. The game's is gmSpellcastingUI (element type `0x10000015`), the
  Combat floaty's `0x10000061`, layout `0x21000010`: the tab panel `0x100000A2` (a `UIElement_Panel`) with tab buttons
  `0x100000A3`-`A9` and `0x100005C2` (47x20, from template `0x1000048F`: state B `06005EB9` closed, C `06005EB8` open)
  and their pages `0x100000AA`-`B0` and `0x100005C3` (each from `0x100000B4`: an item list `0x100000B6` between scroll
  arrows, `0x10000070` in layout `0x21000008`, `06004CDC`-`DF`), the endowment icon `0x100000B1` (the wielded caster's
  own spell; not shown by the plugin yet), the Cast button `0x100000B2` (79x32, template `0x100002CD` in `0x21000040`:
  ends 32 wide and a 15-pixel middle, state 1 `06004C96`/`99`/`9C`, 3 pressed `95`/`98`/`9B`, D disabled `94`/`97`/`9A`)
  and the spell's name `0x1000048B`. The floaty is hidden only in magic, the power bar's the rest of the time (see the
  hider above). Everything goes through the game's bar (`ClientSpellBar`), so the game's keys (cast, next and previous
  spell and tab) work on what the window shows: gmSpellcastingUI's tab panel is at `+0x5FC`, its open page at the
  panel's `+0x6D8` (as `GetOpenSubMenuIndex`, `0x4C6500`, reads it), and its eight SpellCastSubMenus are `0x1C` bytes
  each from `+0x628` (item list `+0x4`, selected spell `+0xC`, count `+0x10`), each slot a `UIElement_UIItem` with its
  `spellID` at `+0x600`; found with `UIElement::DynamicCast` (vtable `+0x94`) from the element. A tab opens with
  `UIElement_Panel::OpenTab(panel, tab button)` (`0x46C110`); a spell is picked with `SpellCastSubMenu::SetSelected`
  (`0x4C66F0`); Cast is `gmSpellcastingUI::Cast` (`0x4C6C40`, the game's cast key's own, which casts the open tab's
  picked spell with the game's targeting and messages); adding is `SpellCastSubMenu::AddFavorite(spell, place, 1)`
  (`0x4C7C50`, which updates the client's favourites and sends `CM_Character::Event_AddSpellFavorite`), then
  `UpdateEndowmentIcon` and `UpdateCastButtonTooltip` as `AddSpellShortcut` does; removing is `RemoveSpellFromMenu`
  (`0x4C74E0`, which sends `Event_RemoveSpellFavorite`); moving is the two. Decal's `CharacterFilter.SpellBar(n)` and
  `Actions.SpellTabAdd` were passed over: Decal's are its own copy, from the network, and only tell the server. A click
  picks a spell (Click casts: casts it), a double-click and Cast cast; spells dragged from the game's spellbook
  (`SpellDrag`) go in where they're let go, slots drag to another place or another tab, and right-click has Cast, Move
  left, Move right, Copy to tab and Remove. Tabs are I to VIII; Per row, Icon size, Spell name and Cast button are its
  settings; its grip sets Per row, a slot at a time, and a row keeps its whole width with fewer spells in it.
  `[spell-bar]` lines in log.txt. Untested in the game when written.
  **Toolbar** (`Ui/ToolbarPanel.cs`, `Ui/ClientToolbar.cs`, `Feed/ToolbarOptions.cs`, `Feed/ToolbarButtonInfo.cs`,
  `SettingsData.Toolbar`; its settings under Client UI Override's Game panels): replaces the game's toolbar, TBAR
  `0x10000603` (its frame from layout `0x21000070`), shortcut bar and all. Layout `0x21000016` (300x122): the combat
  button, four elements on one spot that the game shows one of, `0x10000192` peace (`06004CEC`, pressed `CED`),
  `0x10000193` melee (`CEE`/`CEF`), `0x10000194` missile (`CF0`/`CF1`), `0x10000195` magic (`CF2`/`CF3`), 55x58 at 0,0;
  the panel buttons along the top, 34x27 (state 1 normal, 6 open), each with its panel's id in property `0x10000029`:
  Social `0x10000197` (0xC) at 55, Spellbook `0x10000198` (0xD, the star, `06001119`/`111B`) at 85, Character
  (attributes and skills) `0x10000199` (0xB, the arm, `06001122`/`1124`) at 115, Contracts `0x1000055A` (0x19) at 145,
  Map `0x1000019A` (0x10) at 175, Options `0x1000019B` (0xA, the checklist, `0600111C`/`111E`) at 204, the backpack
  `0x100001B1` (7) 63x58 at 238; between them on the middle row the selection strip: Use `0x1000019D` (the hand, 23x31
  at 55,27: `06001129`, pressed `112A`, disabled `120E`), the strip `0x1000019E` (140x31 at 78,27, `06001126`) with the
  name `0x1000019F`, the selection's health meter `0x100001A1` (`0600193E` empty, `193F` full) and mana meter
  `0x100001A2` (`060022D5`/`22D6`), the stack box `0x100001A3` and slider `0x100001A4`, and Appraise `0x100001A5` (the
  magnifier, 22x31 at 218,27: `06001127`/`1128`); then the 18 shortcut slots. What the buttons do is
  gmToolbarUI::ListenToElementMessage's (`0x4BFB20`): the combat button `ClientCombatSystem::ToggleCombatMode`
  (`0x56D610`); Appraise `ClientUISystem::ExamineObject` with the selection (`[0x871E54]`), which is
  `CM_Examine::SendNotice_ExamineObject` (`0x6B0510`, static cdecl): an appraisal the game starts itself, so its panel
  opens (the plugin's Appraisal window takes items while Plugin appraisal is on, and Creature Appraisal creatures,
  NPCs and players while it Replaces the game's: `ClientExamine.TryTakeOver` is asked first, since this call skips the
  hook the game's own appraisals pass, and the first build opened the game's panel for them); Use `ClientUISystem::UseObject`, which
  is `ItemHolder::UseObject(id, 0, 0)` (`0x5898B0`, static cdecl); the stack slider `CM_UI::SendNotice_StackSliderChanged`
  (`0x47A550`, the split size at `0x81D7EC` and the stack at `0x81D7F0`), which the window sets through
  `StackSplit.SetAmount`. A panel button is `CM_UI::SendNotice_SetPanelVisibility(id, open)` (`ClientElements.
  SetGamePanel`), lit while `ClientElements.OpenGamePanel` says it's the one open, or opens the plugin's window instead
  where Client UI Override's Inventory, Quests, Social or Map is ticked. Items let go over the backpack (from the
  plugin's lists or the game's pack) go in the main pack (`ToolbarButtons.PutInMainPack`). The strip's health is
  `PlayerFeed.ClientHealth`; the mana meter isn't drawn (Decal doesn't say an item's mana before an appraisal). Retail
  places everything as the game does, scaled with the text; Modern is a row of lit buttons and the strip under it.
  With the toolbar hidden, Toolbar buttons have nothing to sit on and don't show, and Shared window's Attach to toolbar
  sits on this window instead (`SharedWindow.PluginToolbar`, set by `PlayerHud`: its `FloatingWindow.DrawnBox` while
  Replace is on and it's been drawn this frame or the last, else the game's hidden toolbar as before; the first build
  went by the hidden one only). Sitting on it, the shared window's windows `Follows` it: a Ctrl drag of one moves the
  Toolbar window (`FloatingWindow.MoveTo`, which also keeps the Toolbar window from placing itself by the held button,
  `Led`), and the shared window follows, as when the Toolbar window itself is dragged. Untested in the game when written.
  **Indicators** (`Ui/IndicatorsPanel.cs`, `Feed/IndicatorOptions.cs`, `Feed/IndicatorInfo.cs`,
  `SettingsData.Indicators`; its settings under Client UI Override's Game panels): replaces INDI `0x10000611`
  (gmIndicatorsUI, layout `0x21000071`, 150x30 in a 5-pixel frame). Its 20x20 indicators, each a class of the client's
  and each drawn in a state of its own (`UIElement.m_state`, `+0x400`, which `UIElement::SetState`, `0x464FA0`, sets;
  read with `ClientElements.GetState`, so the window shows just what the game's would): link status `0x100000F8` at 5
  (gmUIElement_LinkStatusIndicator: states 0x11 good `06007498`, 0x12 fair `7499`, 0x13 poor and 0x14 lost `749A`, as
  `SetLinkState`, `0x4E7270`, sets them from 1 to 4); beneficial spells `0x100000F5` at 25 and harmful `0x100000F6` at
  45 (gmUIElement_EffectsIndicator, property `0x1000000C` 1 and 2: `0600749C`/`749E` lit, `749D`/`749F` in the dim
  state 0xD when there are none); vitae `0x100000F4` at 65 (`060074A0`, dim `74A1`); the burden pack `0x100000F7` at 85
  (states 0xE, 0xF, 0x10, as under Toolbar buttons); a game of chess `0x100000F3` at 105 (gmUIElement_MiniGameIndicator,
  the knight, `060074A5`, dim `74A6`); and the X `0x100000FA` at 125 (`060074B1`, pressed `74B2`), which logs out:
  gmIndicatorsUI::ListenToElementMessage (`0x4C0710`) calls `CM_UI::SendNotice_EndCharacterSession(1)` (`0x479F40`,
  static cdecl) for it, which the window does too, after asking. Each child `0x100000F2` is the frame drawn while its
  panel's open (`0600749B`). The panels' ids come from the panel frame's layout `0x2100006E`, whose pages each carry one
  in property `0x10000029`, matched to their classes by their root elements' types (each class's
  `GetUIElementType`): 1 gmAbuseUI, 2 gmBookUI, 3 gmCharacterInfoUI (the burden pack's), 4 and 5 gmEffectsUI (both
  layout `0x2100001B`, from `0x1000011F` and `0x10000121`: taken to be beneficial then harmful, by the indicators'
  order), 7 inventory, 8 gmLinkStatusUI, 9 gmMiniGameUI, 0xA options, 0xB character detail, 0xC social, 0xD spells,
  0xE gmUrgentAssistanceUI, 0xF gmVitaeUI, 0x10 map, 0x19 contracts; the tooltips are in `client_local_English.dat`'s
  string table `0x23000005` ("Show/Hide Positive Magic Panel"...). A click opens or closes the panel
  (`ClientElements.SetGamePanel`), the burden pack the plugin's Character window where Client UI Override's Character
  is ticked, and buffs, debuffs and vitae the plugin's windows where those Replace the game's (`WindowFor`). Modern says the same in words: "Link: good", "Buffs 12" (CharacterFilter's enchantments counted by the
  portal dat's Beneficial flag, cooldowns left out), "Burden 45%", dimmed where the game dims. Log out button and One
  under another are its settings. `[indicators]` lines in log.txt. Untested in the game when written.
  **Attributes** (`Ui/AttributesPanel*.cs`, `Ui/StatRow.cs`, `Ui/GamePanelInterceptor.cs`, `Ui/ClientTraining.cs`,
  `Feed/XpTable.cs`, `Feed/AttributesOptions.cs`, `SettingsData.Attributes`; its settings under Client UI Override's
  Game panels): your attributes, vitals and skills, raised with unassigned XP and trained with skill credits, in place
  of the game's Character Detail panel (panel 0xB, layout `0x2100002E`: tabs `0x10000228`, `0x10000229` and
  `0x10000538`, 92x25 from template `0x1000043A`, its ends 17x25 and middle 66x25, closed state B `06005D93`/`95`/`97`,
  open state C `06005D92`/`94`/`96`; its pages attributes `0x1000022B` (`0x2100002C`), skills `0x1000022C`
  (`0x2100002D`) and titles `0x10000539` (`0x2100005E`)). That panel lives in the panel frame with every other, so
  there's nothing of its own to hide: `GamePanelInterceptor` (shared with the Spellbook) closes panel 0xB the moment
  `ClientElements.OpenGamePanel` says it's open, from the toolbar, the game's key or anywhere, and shows the window
  instead; the Toolbar window's Character button opens it too (`GamePanels.Get`). **Titles** is the plugin's own
  (`AttributesPanel.Titles.cs`, `Ui/ClientTitles.cs`, `Feed/TitleMessages.cs`, `CharacterTitles.cs`, `TitleNames.cs`),
  laid out as the game's Titles page, layout `0x2100005E` (a page of its own, no character at the top): "Current
  Display Title:" (`0x1000052E`) and the title (`0x1000052F`), the gold bar `06001420` (`title-separator`), "All
  Available Titles:" and the list (`0x10000532`, 270x455 on the stone with the usual scroll bar; rows from
  `0x10000536`, 270x24, the picked one under `06001AAF`, `title-row-selected`), a search box of the plugin's, the bar
  again and **Set as Display Title** (`0x10000535`, 200x32, the Cast button's art), the words from string table
  `0x23000001`. Which titles you have isn't Decal's: the server's game events are read (`TitleMessages`): at login
  CharacterTitle (`0x0029`: version 1, the display title, the count and each id, daralet-ACE's
  `GameEventCharacterTitle`), then UpdateTitle (`0x002B`: a title, and 1 when it's now the display title) for each one
  granted or picked; so after a hot reload there are none until the next login, which the tab says. Names are the
  game's: the portal dat's EnumMapper `0x22000041` gives a title its string id (`ID_CharacterTitle_Life_Mage`), and
  client_local_English.dat's string table `0x2300000E` the name under that string's hash (`TitleNames.Hash`,
  daralet-ACE's `SpellTable.ComputeHash`), the language dat opened from the game's folder once, the first time a name
  is wanted (UtilityBelt.Common's `LanguageDatDatabase`), as `Player.GetTitle` does. Picking one (the button, or a
  double-click) is the client's own `CM_Social.Event_SetDisplayCharacterTitle(id)` (`0x6A6680`, message 0x002C), and
  the server's UpdateTitle answer moves the display title. `[titles]` lines in log.txt. Untested in the game when
  written. It sizes by
  the player and is one of the **shared window**'s unless kept separate (`attributes`; `SharedWindow` takes more
  windows after its five). **Laid out as the game's pages** (layout `0x21000045`, the user's
  call): the character at the top of both tabs (`AttributesPanel.Header.cs`, the game's `0x10000230`: name, gender
  race and template, player killer status from PropertyInt 134, Total Experience, the XP bar `0x10000236` to the next
  level, its track `060011A6` and red fill `060011A5`, from the server's level bounds as the XP bar has them, Infinity!
  at the top level; then past the gold divider `06004CB8`, "Character Level" and the level, big, drawn by
  `ItemIcons.DrawLabel`), the separator `06004CC7`, the list (`AttributesPanel.Rows.cs`, rows from template
  `0x10000248`, 282x20: a 20x20 icon, the name, the value at the right; picked, the gold-framed bar `06000F93`; values
  above their base green and below it red, the user's call; vitals as current/max), and the picked row's raise
  (`AttributesPanel.Footer.cs`, the game's `0x1000024D`: "Strength: 10039 (+40)", "Experience To Raise:" and
  "Unassigned Experience:", and the +10 arrow `0x100005EB` over the +1 `0x10000246`; an untrained skill's skill credits
  and Train, in Retail the game's own button for it, the +1's green arrow (`06004CB6`), bright with credits enough and
  dulled (`06004CB7`) without, beside the two lines of credits, as the game's; the user's call). Icons are the portal
  dat's: attributes by DID mapper `0x25000006` (Strength `060002C8`, Endurance `2C4`,
  Quickness `2C6`, Coordination `2C9`, Focus `2C5`, Self `2C7`), vitals by `0x25000007` (`06004C3B`/`3C`/`3D`), skills
  their skill table entry's `IconId` (the client's `InfoRegion::SetID`, `0x4F1CF0`, looks them up so). Skills sit
  under the game's group bars (`0x10000249`-`4C`: `06000F90` specialized, `06000F86` trained, `06000F98` untrained,
  `06000F89` unusable). **Skills are Daralet's** (the user's call): the list and the names are the client's portal dat
  skill table's (`UBService.PortalDat.SkillTable`, 31 skills: Physical Defense, Martial Weapons, Bows, Thievery,
  Perception, Portal Magic, Two-Handed Combat, the crafts...), which is what the game's panel shows and matches
  daralet-ACE's `NewSkillNames`; Decal's `SkillInfoWrapper.Name` and its skill list are retail's (Melee Defense, Heavy
  Weapons, Lockpick, and the retired Axe, Sword, Mace...). The numbers are Decal's `CharacterFilter` (`Attributes[...]`,
  its `Exp`, `Base` and `Creation`; `Vitals[...]`, a `SkillInfoWrapper` with `XP` and `Base`; `Skills[...]`, with
  `Training`; `EffectiveAttribute` and `EffectiveSkill` for the values shown; a vital's current and maximum are
  `Actions.Vital`'s, since `CharacterFilter.EffectiveVital` gave the unbuffed base (5246 where the game showed
  100051, tried in the game); `UnassignedXP`, `SkillPoints`), XP spent read as unsigned (Decal's are Int32; the top costs pass
  two billion). Costs are the client's own experience tables, each read once through ExperienceSystem's statics
  (cdecl): `ExperienceToAttributeLevel(rank)` `0x5C9330`, `ExperienceToAttribute2ndLevel(rank)` `0x5C9370`,
  `ExperienceToSkillLevel(advancement class, rank)` `0x5C93B0` (2 trained, 3 specialized), each the total XP for that
  many raises (-1 past the end), up to `GetMaxAttributeLevel` `0x5C95B0`, `GetMaxAttribute2ndLevel` `0x5C95E0`,
  `GetMaxTrainedSkillLevel` `0x5C9580` and `GetMaxSpecializedSkillLevel` `0x5C9550`; a raise costs the next step's total
  less what's spent (`XpTable`). (UtilityBelt.Common's portal dat has the same table as `XpTable`.) The raise is the
  client's own `CM_Train` message, static cdecl: `Event_TrainAttribute(STypeAttribute 1-6, xp)` `0x6A8E90`,
  `Event_TrainAttribute2nd(1 max health, 3 max stamina, 5 max mana; Decal's vitals are 2, 4, 6, xp)` `0x6A8FA0`,
  `Event_TrainSkill(STypeSkill, Decal's numbers too, xp)` `0x6A90B0`, and training an untrained skill
  `Event_TrainSkillAdvancementClass(skill, credits)` `0x6A91C0` with the skill table's `TrainedCost` (Daralet's crafts
  cost 1000, so can't be trained that way); specializing is left to the game. A row's buttons wait for the server
  (what's spent to change, or two seconds) so a second click can't spend the same XP twice. Decal's own
  `Actions.AddAttributeExperience` and the like were passed over: Decal's actions have crashed the client before.
  `[game-panels]` and `[training]` lines in log.txt. The first build was tried in the game (an admin at the top level:
  every raise "As high as it goes", as it should); this layout is untested in the game.
  **Spellbook** (`Ui/SpellbookPanel*.cs`, `Ui/SpellPayload.cs`, `Ui/GamePanelArt.cs`, `Feed/SpellbookOptions.cs`,
  `Feed/SpellLevels.cs`, `SettingsData.Spellbook`): in place of the game's Spell Management panel (panel 0xD, layout
  `0x21000034`: spellbook page `0x100002AC`, layout `0x21000032`, a list over a filter box `06002722` of school and level
  checkboxes; components page `0x100002AD`, `0x21000033`), by `GamePanelInterceptor` as Attributes; sizes by the player
  and can share the shared window (`spellbook`). Spells are `CharacterFilter.SpellBook`, each the portal dat's spell
  table (`Spells.Info`, now with `Power`, `Duration` and `Components`, the formula's component ids); a spell's level is
  the client's rule (`SpellLevels`, daralet-ACE's `SpellFormula.Level`): the formula's first component, a scarab (Lead I
  to Platinum and Dark VII; Mana VIII held to 7), else retail's power steps (1, 50... 300), which the client's table
  keeps; the server's own power rule (10, 150... 400) left Acid Bane I with none and called IV a II. The list leaves
  room for what was under it last frame (`bottomHeight`), so Cast never needs scrolling to; school names are Daralet's skill names (Item Enchantment is Portal Magic). Search, school
  and level filters, the list by name as the game's, or by level then name with By level (`SpellbookOptions.ByLevel`,
  `Order`); a double-click or Cast casts (`Spells.Cast`); right-click or Add to spell bar puts it on a spell bar
  tab (`ClientSpellBar.Add`); a spell drags (`SpellPayload`, an ImGui payload) onto the Spell bar window or a hotbar slot.
  **Appraise** in the right-click menu opens the **Spell Appraisal** window (`Ui/SpellAppraisal.cs`, `spell-appraisal`,
  hidden to start; the user's call): icon, name, school and level, the description, then the game's own spell lines in
  its words and order, read from acclient's spell text (`0x4B6A60` on): "School: ", "Mana: %d" with " + %d per target"
  (the table's `ManaMod`), "Duration: %u sec." under a minute and "%u min." from one (none for -1 or 0), and "Range: %.1f
  yds.", the client's sum (`0x4AD7D0`): `BaseRangeMod` times your skill in the spell's school (the school-to-skill table
  at `0x597984`: War 34, Life 33, Item 32, Creature 31, Void 43; your best of them for a spell with none) plus
  `BaseRangeConstant`, held to 75 meters (`0x7AFF58`), over 0.9144 (`0x7AFE88`), and left out when 0; then the
  components with their icons, and Cast. All from the portal dat's spell table: the game asks the server nothing for
  this either. Untested in the game when written.
  Components lists what you carry (ObjectClass SpellComponent, stacks added up) and what the picked spell takes, named by
  `UBService.PortalDat.SpellComponentsTable`. **Buy to** (the user's call): a box beside each held count sets how many
  the game's own /fillcomps (`ClientCommunicationSystem::DoFillComponents`, `0x570B20`, a client command, not the
  server's) buys up to at a vendor, as the game's components page does (its handler near `0x48A7C3`):
  `CM_Character::Event_SetDesiredComponentLevel(wcid, amount)` (`0x6A3170`, static cdecl; the server's
  `HandleSetDesiredComponentLevel`, saved as the character's fill components and sent back at login), then
  `PlayerModule::SetDesiredCompLevel(wcid, amount)` (`0x5D5A90`) on the client's copy, read with `GetDesiredCompLevel`
  (`0x5D5850`); the PlayerModule is `CPlayerSystem::GetPlayerSystem()` (`0x55E1D0`) `+0x34` (its `playerModule` at
  `+0x30`). Levels go by weenie class id: the portal dat's dual DID mapper `0x27000002` turns a component table id
  into one (daralet-ACE's `Spell.GetComponentWCID`), and you carry what Decal has of that `Type`. A box sends once it's
  let go of (`ClientComponents`, `ComponentRow.Build`; All components lists every one, else those carried, set or
  needed). `[components]` lines in log.txt. `GamePanelArt` holds the frame's tabs,
  separator and line helpers both windows share. Its tabs are each as wide as their label; when the row is wider than
  the window, the room past each label goes first, the tabs with most to spare first, then all narrow alike, a label
  drawn smaller (to 70%) then clipped (`TabWidths`: first builds ran Config and Titles off the edge, then squeezed
  Gameplay Options while short tabs kept room); the shared window's own tab buttons wrap to another row instead
  (`Form.Flow`). Untested in the game when written.
  **Options** (`Ui/OptionsPanel*.cs`, `Ui/ClientOptions.cs`, `Ui/ClientMenu.cs`, `Feed/GameOptions.cs`,
  `Feed/GameOption.cs`): the game's Options panel (panel 0xA, layout `0x2100002B`) in a window of its own, by
  `GamePanelInterceptor`; can share the shared window (`options`). Its tabs are the game's (tab panel `0x10000208`):
  **Gameplay Options** (`0x1000020D`, page `0x10000212`, gmGameplayOptionsUI, layout `0x2100002A`), the "main menu" (the
  user's words): seven 240x32 blue buttons (template `0x10000618`, ends 32 wide: `060074A9`/`AC`/`AF`, pressed
  `A8`/`AB`/`AE`, disabled `A7`/`AA`/`AD`), whose actions are gmGameplayOptionsUI::ListenToElementMessage's (`0x49E390`):
  Exit to Character Selection (`0x10000203`) `CM_UI::SendNotice_EndCharacterSession(1)`; Exit Game (`0x10000617`) and
  Use Mouse Turning Settings (`0x100005CC`) `UIElementManager::BroadcastGlobalMessage` (`0x45B4C0`) with (1,
  `0x10000027`) and (0xC, 0); Urgent Assistance (`0x10000206`) and Report Abuse (`0x10000207`) ShellExecute
  `support.turbine.com`, gone, so the plugin opens the game's own panels 0xE and 1 instead; Configure Keyboard
  (`0x10000204`) and In-Game Help Files (`0x10000205`) aren't handled there: a button's property 0x12 is an input
  action it fires as a key would (`0x472210`: if the object at `0x837FF4` says yes, its vtable `+0x44`, an input event
  made by `0x430EE0`, the action at `+0`, 3 at `+0x1C`, 1 at `+0x20`, handed to the UIElementManager's first virtual
  function), theirs `0x1000001F`, which opens and closes the keyboard window (layout `0x21000009`, root element
  `0x1000001F`, gmKeyboardUI `0x10000020`, element type `0x1000000E`), and `0x7B`, the help. `ClientMenu.InputAction`
  does the same; if it can't, they open the game's panel at this tab. `[options]` lines in log.txt. The exits ask
  first.
  **Character** (`0x1000020E`, page `0x10000211`, gmCharacterSettingsUI, element type `0x10000027`, layout
  `0x21000028`): the game's six groups in its order (gmCharacterSettingsUI::InitOptions, `0x4A05B0`:
  PlayerOptionPage::AddHeader `0x4F3510`, AddToggleOption `0x4F3660` with each PlayerOption, AddSeperator), headed in
  yellow as the game shows them (User Interface Behavior, User Interface Display, Grouping, Other Players, Character
  Behavior, Chat), with the gold bar `060012C5` between groups; labels and tooltips from client_local_English.dat's
  string table `0x23000003` (`GameOptions`); option 52, past retail's enum, is Daralet's PK death messages. Each option
  is a light (template `0x1000032B`: 13x13, `06004D15` off, `16` off pressed, `17` on, `18` on pressed, `19` disabled;
  `GamePanelArt.Light`). As in the game a change waits for **Apply** (Apply, Reset, Defaults: 80x32 from template
  `0x100002CD`, the Cast button's art); Reset drops the waiting changes, Defaults sets each to
  `PlayerModule::GetDefaultOptionValue(option)` (`0x5D3A30`). Read with `PlayerModule::GetOption(option)` (`0x5D4AF0`),
  set with `PlayerModule::SetOption(option, on)` (`0x5D4F20`) on the client's PlayerModule
  (`ClientComponents.PlayerModule`), whose change raises `CPlayerModule::OnChanged` (`0x59B8F0`): the game's UI hears
  `CM_UI::SendNotice_PlayerOptionChanged` and the server gets `CM_Character::Event_PlayerOptionChangedEvent` (auto-saved
  options at once, the rest with the character's options), so the game's own panel agrees, unlike the Social tab's
  options, which send the event alone. **Config** (`0x1000020F`, page `0x10000213`, gmConfigUI `0x10000028`;
  `OptionsPanel.Config.cs`, `ClientPreferences`, `Feed/ConfigOptions.cs`): the game's six groups (Sound, Camera,
  Graphics, Rendering Quality, Input, UI) as its screenshots show them, each row a light, a slider (track `06001285`
  stretched, thumb `06001286`, template `0x1000036E`, the words for its ends under it), both, or a list. These are the
  client's own preferences (UIPreferences, items keyed by name in the table at `0x82514C`), registered at start by
  gmClient::InitUIPreferences (`0x4037B0`) with `UIPreferences::AttachPreference(&name, type, 0x10000003, label hash,
  help hash)` (`0x5DEE50`), the names held in globals `0x837564`-`0x837694` (the label's `ID_Sound_EffectVolume`... string
  ids beside each), type 4 bool, 3 float, 2 list. The plugin passes the global's address as the name (statics, cdecl):
  bool `InqPreferenceValue` `0x5DEC90` / `ModifyPreference` `0x5DEB90` (the item's vtable `+4`/`+8`), float `0x5DED50` /
  `0x5DEC50` (`+0x30`/`+0x34`), a float's range `InqPreferenceRange(name, &min, &max)` `0x5DE9E0` (`+0x38`); lists'
  `+0x1C`/`+0x20`, their choices `+0x24` (`InqEnumChoices` `0x5DEAC0`) aren't decoded yet, so each list row and Defaults
  open the game's own Config page. The sound lights are the client's Disable... preferences, so they're lit while off
  (`ConfigOption.Inverted`). Changes wait for Apply, as the game's do, but a slider changes its preference as it's
  dragged, as the game's Field of View does (`sliderOriginals`), and goes back on Reset, once the page isn't drawn
  (another tab, the window closed) or at shutdown, unless applied. Every change that took is followed by
  `CM_UI::SendNotice_PreferenceChanged(name)` (`0x47A740`, cdecl), as the game's preference controls do (`0x485795`,
  `0x4871F7`, `0x48457E`): it's what has the camera, sound and the rest pick it up. **Chat** (`0x1000050B`, page `0x1000050C`,
  gmChatOptionsUI, element type `0x10000042`, layout `0x2100005C`: one checkbox set per chat window,
  `AddCheckboxBitfield64Option` `0x49F020`) keeps its settings in the PlayerModule's gameplay option collection
  (`m_colGameplayOptions`, `+0x9C`), changed with `PlayerModule::SetChatWindowOption(window, BaseProperty*)` (`0x5D66C0`),
  which takes the client's property objects, not yet built from the plugin: it opens the game's panel at that tab
  (`LetOpen`, then `ClientElements.OpenTab` once it's showing). Untested in the game when written.
  **Buffs and Debuffs** (`Ui/EffectsPanel.cs`, one class made twice, keys `buffs` and `debuffs`): the game's Positive and
  Negative Magic panels (panels 4 and 5, gmEffectsUI, layout `0x2100001B`, 300x362, property `0x1000000C` 1 or 2), by
  `GamePanelInterceptor`. The spells on you from Decal's `CharacterFilter.Enchantments`, beneficial or harmful by the
  portal dat's flag as the indicators count them (cooldowns aren't in the spell table; the vitae, spell 666, has its own
  window), by name then time left (`TimeRemaining`; no time for a `Duration` of 0 or less). As the game's: the list
  `0x10000123` of 300x32 rows (template `0x10000128`: `effect-row` `06001396`, picked `effect-row-selected` `06001397`, a
  32x32 icon, the name at 37, the time at 225), and the picked spell's name and description in the box under it
  (`0x10000125`, `effect-description` `06001395`, 300x88). **Vitae** (`Ui/VitaePanel.cs`, `Feed/VitaeInfo.cs`): panel 0xF,
  gmVitaeUI, layout `0x21000020` (its words at 8,25 in 284x337 on the 48x48 stone `vitae-background` `06004CC2`, tiled).
  The game's own words (string table `0x23000001`: "Your Vitae, or life force, is at full strength." or "Due to your
  recent death, you have temporarily lost X% ... You will regain 1% of your Vitae once you earn Y more experience."):
  X from the vitae enchantment's `Adjustment` (its StatModValue, 0.95 for 5%), Y daralet-ACE's
  `Player.VitaeCPPoolThreshold`, min((DeathLevel² × 5 + 20) × vitae⁵ + 0.5, 12500), less `VitaeCpPool`: PropertyInts 139 and
  129, both SendOnLogin and sent again in private int updates (`0x02CD`, `VitaeInfo.ReadPrivateInt`), which the window
  follows in case Decal doesn't. All three can be the shared window's (ticked for new settings), and the Indicators
  window opens them in place of the game's panels while they Replace them. Untested in the game when written.
  **Vendor** (`Ui/VendorPanel*.cs`, `Ui/ClientVendor.cs`, `Feed/VendorPrices.cs`, `Ui/PrivateInts.cs`): the game's vendor
  window (gmVendorUI, element type `0x10000017`, layout `0x21000012`, 800x110 in ENVP) with its three tabs, starting on
  Items each time a vendor opens (by Decal's `VendorId` changing, not `ApproachVendor`, which comes after every
  purchase too), flatter than the panels' as the game's are (92x20: `GamePanelArt.Tabs`' `tabHeight`, `TabHeight`),
  on the brown strip along the top of the game's window (`vendor-tab-strip`, `06005F10`, 64x20, repeated: element
  `0x1000008D`, 800x20), the vendor's name and Close right of them in the same row (`GamePanelArt.Tabs`' `maxWidth` leaves
  them room), and beside the kind menu the picked item's name in bold bright white, with its material as the game
  writes it (the server's description's name, `PickedName`; Decal's has none), and its price under it. The bold is a
  label (`ItemIcons.DrawLabel`, GDI+ Segoe UI bold at 85% of the text's size, off-white, on whole pixels,
  `BoldCentred`; at the full size, an em, it came out bigger and heavier than the text round it): ImGui's text drawn
  twice a pixel apart, at the fractional place centring gave, came out smeared. The name takes exactly a text line,
  as "Pick an item..." does in its place, and neither has the frame's padding, or the items under them moved a few
  pixels as an item was picked and unpicked. The slots wrap
  to the width, or, with the window too short for two rows, go in one row scrolling sideways as the game's do
  (`VendorPanel.Cells.cs`, which draws only those in sight either way). It can't be made shorter than that one row and
  the buttons beside it (`HoldOneRow`, measured as it draws, into `FloatingWindow.MinHeight`, which overrides the least
  height a window was made with, and so follows the Text size and the title bar). **Loot highlights** (the user's
  name for it; `MarksLabel`)
  (`SettingsData.VendorLootMarks`, off to start, a box in the tab row before the vendor's name; `VendorPanel.Marks.cs`)
  marks each slot in sight as the Inventory's Loot highlights do: the server's description (yours, and the stock with
  `vendordescribe`) through the active profile (`Loot.Decide`), the deciding rule's mark by `ItemIcons.DrawMark`, kept
  per item until new descriptions come, the profile changes or two seconds pass. **The Selling tab opens while** a
  sellable item (`VendorSale.Buys`), a pack or the main pack is held over the window (`VendorPanel.Drag.cs`), from the
  plugin's lists (the ImGui payload) or the game's pack (`GameDrag.Dragging`, the selection; ImGui counts no window
  hovered then, so the mouse against the window's box), as the game's window does (`gmVendorUI::UpdateDragOver`), not
  only once it's let go; and the sell list's slot under it, empty or not, has the green ring, or the red circle for what
  it won't buy (`DragToSell`, `DrawSellDropMark`, `ItemIcons.DrawDropArt` as the Inventory window's slots): **Items**
  (page `0x100000BC`: a kind menu `0x100000BF`, here the kinds the vendor has by ItemType bit, `VendorPrices.Kinds`; the
  items `0x100000BD`, here wrapping; the picked one's name and "costs 11,787p (you have 9,474p)"; Buy `0x100000C2` and Add
  to List `0x100000C3`, with a count for one sold by the stack, the game's stack bar's, set first with
  `StackSplit.SetAmount`, back to 1 whenever another item is picked or another vendor opens, `amountFor`; the first
  build kept the last), **Buying** and **Selling** (pages `0x100000C4`, `0x100000CD`: Clear Item and Clear List, small
  buttons `060012BA`/`B9`/`B8` and on Selling `06001927`/`28`; the list with empty slots after; "Buying 3 items worth
  ...p" and "You have ...p"; Buy or Sell Item and All, red buttons `06004C4C`/`4D`/`4E`, cut into three for
  `GamePanelArt.Button`) on the stone (`GamePanelArt.Stone`). The lists are gmVendorUI's own (AcClient's layout: the buy
  list a `PackableList<ItemProfile>` at `+0x614`, the sell list at `+0x624`, head `+4`, each node's amount `+4`, item
  `+8`, next `+0x10`), so Decal, other plugins and the game agree, and every button is the game's:
  `gmVendorUI::HandleButtonClicks(button id)` (`0x4C5CC0`, ids `0x100000C2` to `0x100000D6`, the close button
  last), which acts on the client's selection (`0x871E54`), so the item is picked (selected) first. The gmVendorUI is
  found from its tab panel `0x100000B8` up the parents, by DynamicCast (its root `0x100000B7` is never made under that
  id). Prices are daralet-ACE's `Vendor.GetSellCost` and `GetBuyCost` with Decal's `Vendor.SellRate` and `BuyRate`
  (alternate currencies aren't known yet); pyreals are CoinValue, PropertyInt 20, through `PrivateInts` (Decal's, then
  the server's private updates), which the Vitae window uses too. Items let go over the window go on the sell list.
  **Decal's WorldFilter hasn't got a vendor's items** (`WorldFilter[id]` is null for each): they're only in its
  `Vendor` (`WorldFilter.OpenVendor`), whose WorldObjects don't outlive it. So `VendorStock` reads them once, as values
  (`VendorStockItem`: id, name, kind, value, burden, stack, icon layers, sold by the stack, where it's worn; never the
  WorldObject itself), when Decal's `WorldFilter.ApproachVendor` says the list came (on opening and after every
  purchase) or another vendor opens, and everything finds them there by id: the Items tab, the buy list (the vendor's
  items too, `VendorStockItem.Of`), the tooltips and the Toolbar window's selection name (which showed the hex id).
  Only the rows in the list's sight are drawn, the rest a `Dummy`: reading ten or so of Decal's values an item every
  frame hung the game at a vendor with thousands. Decal's Vendor gives the items in an order of its own (a Trade Note
  M before the I, armor jumbled), and the game's window shows them in the server's order (grouped by kind: weight class,
  then slot...), so they're put in that order: gmVendorUI's `shopItemProfileList` (a pointer at `+0x610` to a
  PackableList&lt;ItemProfile&gt;, nodes as the buy list's), which `VendorItemsUI::UpdateItemsList` (`0x4C2A90`) walks to
  fill the game's list (`ClientVendor.ShopOrder`). Not the game's on-screen list (`0x100000BD`), the first try: it holds
  only the kind its menu shows, so every other kind stayed in Decal's order. Looked at again every half second until
  the game's list has come. It's also what the vendor sells now, so an item Decal still has that isn't on it is left
  out (logged): a gear vendor's random uniques rotate away (`RotUniques` in daralet-ACE), and one still listed could be
  added to the buy list, which the server turned down as stale ("That item is no longer available.", the vendor saying
  it refreshed its stock, `HandleStaleVendorPurchase`), so Buy All never bought. `[vendor]` lines say how many were read
  and how long it took.
  The slot under the mouse has the selection's frame at 40% (`HoverTint`, `ItemIcons.DrawArtBox`'s tint; the user's
  call). Every slot has the item tooltip (`VendorPanel.Tooltip.cs`), ending with what it costs or pays, every item described by
  the server: yours (the sell list's) as in your packs, the vendor's stock where the server has the `vendordescribe`
  feature (`@playerui describe` takes what the vendor you last opened sells, `Vendor.TryGetItemForSale`, as the game's
  appraisal does). On an older server the stock shows what the client has (value, burden, stack). A double-click on
  the stock buys (the game's Buy); a right-click has the slot's menu (`VendorPanel.Menu.cs`): Buy and Add to List, or
  Buy/Sell Item and Clear Item on a list, and Appraise (`Appraisal.MenuItems` with the item's `EquipableSlots`, for the
  compare).
  **Replace** can't hide the game's window: `gmVendorUI::OnVisibilityChanged` (`0x4C4410`) closes every tab and resets
  the shop when it goes. So ENVP is moved off the left of the screen while a vendor is open (`VendorPanel.Hide.cs`,
  `ClientElements.Place`), checked once a second, and put back when the vendor closes, Replace goes off or the plugin
  stops; meanwhile WorldDrop's drops on the vendor panel can't happen (it's off the screen), so let go over the plugin's
  window instead. Like the Bank and Corpse windows it's only ever shown with a vendor open (`alwaysOnlyInUse`, by the
  vendor's id, through `GamePanel`'s constructor), and its close button closes the vendor (the game's close button)
  instead of switching the window off: the first build's X switched it off for good, and with Replace moving the game's
  window away, vendors then showed nothing at all. While Replace has the game's window off the screen, a window found
  switched off is switched on again. With anything on a list the game's close button asks first, in a Yes/No dialog
  (`HandleButtonClicks`' close case builds it, `0x478830`, with `CloseVendorDialogCallback`, `0x4C3C70`), and answering it
  crashed the game (why isn't known: the window was off the screen); so the plugin's close empties both lists first with
  the game's own Clear List buttons, and the game closes without asking. Add to List is the game's selection
  (`[0x871E54]`, looked up with `0x5583F0`) handed to `VendorItemsUI::AddToBuyList(object, amount)` (`0x4C49B0`, the stack
  bar's count for a stack), which links a node onto `gmVendorUI+0x614` (head `+0x618`, tail `+0x61C`, count `+0x620`)
  and takes it off the shop's view (`RemoveFromShop`, `0x4C48D0`), which deselects it; the window stays on Items (the
  user's call). **Buy All and Sell All send the game's page, not the list**: their case in `HandleButtonClicks` calls
  `gmVendorUI::RecordContents` (`0x4C1F00`), which empties the list and makes it again from the Buying or Selling
  page's item slots (`m_buyUI` `+0x638`, `m_sellUI` `+0x63C`, each's slot list at `+8`, its count at `+0x610`), then
  `SendShopEvent` (`0x4C0EA0`) with the vendor's id (`+0x608`). The page only catches up with the list when the game's
  tab opens, which with Replace's window off the screen it never did: Buy All sent nothing, or items from before that
  the vendor no longer had ("That item is no longer available."), and emptied the list. So the plugin opens the game's
  tab first (`ClientElements.OpenTab`, `VendorBuyingTab` `0x100000BA` or `VendorSellingTab`) and presses the button
  two frames later (`PressAll`, `StepPressAll`), logging the page's slots and the list's count. Each click logs both lists'
  counts, and each change of a list as the plugin reads it (`[vendor] buy list as read`); an entry the plugin can't name
  shows a "?". `[vendor]` lines in log.txt. Untested in the game when written.
  **Creature Appraisal** (`Ui/CreatureAppraisalPanel.cs`, `Ui/ClientExamine.cs`, `Feed/CreatureAppraisal.cs`,
  `Feed/CreatureSheet.cs`, `Feed/CreatureStats.cs`, `Feed/CreatureTypes.cs`; key `creature-appraisal`, its settings under
  Client UI Override's Game panels): the game's examination panel for a creature, NPC or player. That panel is the
  Examination floaty (Decal's `UIElementType` Examination, `0x100005F7`, layout `0x2100006B`, 310x400; its creature
  page `0x10000140`: name and kind `0x1000014D`-`52` left of the divider `060012C6`, the level `0x1000014A`-`4C` on the
  right, the list `0x10000148` under a separator `0600612C`, the description `0x10000335`), not in the panel frame, so
  it isn't hidden: the appraisal is taken before it starts. Every way the game appraises (the key, the magnifier, its
  menus) goes through `ClientUISystem::ExamineObject` (`0x5657B0`), which calls `CM_Examine::SendNotice_ExamineObject`
  (`0x6B0510`, static cdecl) once, at `0x5657BC`; that call is hooked (`ClientExamine`, AcClient.Hook as SpellDrag), and
  with Replace on a Monster, Player, Npc or Vendor (Decal's ObjectClass) is never passed on: Decal's `RequestId` asks
  instead, which the client doesn't show. Decal doesn't pass on a creature's profile, so the server's own message is read
  (game event `0x00C9`, SetAppraiseInfo, in daralet-ACE's `AppraiseInfo.Write` order: flags, success, the property
  tables, spells, armor profile, then `CreatureProfile`: health, and with ShowAttributes (0x8) the six attributes,
  stamina, mana and their maxima; with 0x1 which a spell raised or lowered, `AttributeMask` bits; then resistances'
  highlights and, for a player or a creature that can't be attacked, armor by body part). The window shows the name
  (in bold, `GamePanelArt.BoldCentred`, the Vendor window's GDI+ label made shared) and under it the game's lines
  (`CreatureSheet.HeaderLines`): "Male Gharu'ndim", from Gender (PropertyInt 113) and HeritageGroup (188), the
  numbers the server sends for players and NPCs alike (the Sex and HeritageGroup strings the first build waited for
  aren't AssessmentProperties, so never came, and a vendor showed as "Human", its CreatureType), the heritage's name
  the portal dat's (`CharGen.HeritageGroups`, Daralet's own; retail's when it can't be read); a creature's
  CreatureType (PropertyInt 2, spaced as the game writes it) for one with no heritage; the Template (string 5:
  "Spellsword", "Humble Servant"); and player killer or not, for one with a heritage. They're centred down the top
  box (by last frame's height, at least the game's 60), the level and "Level" centred in theirs. Then attributes and
  vitals, as the game lists them (green raised, red lowered), the ratings `AppraiseInfo.AddRatings` adds, armor three to a line as the
  game writes it ("Head/Chest/Groin" and "AL: 90/95/95", then "Bicep/Wrist/Hand", "Thigh/Shin/Foot"), allegiance
  (AllegianceName 47, AllegianceRank 30, followers, Monarch's and Patron's titles 21 and 35, Fellowship 10), character
  (Title, birth, age, deaths, titles, chess rank,
  fishing) and LongDesc. A failed appraisal has health alone. A later appraisal of the one shown (Appraise again, or
  another plugin's) updates it. Items still go to the game's panel. `[appraisal]` lines in log.txt. Untested in the
  game when written. **The model** (`Ui/CreatureModelView.cs`, `SettingsData.CreatureAppraisalModel`, Model in the
  window's settings and menu, on to start; the user's call, November 2026) is the game's own: its examination panel
  draws a creature's model in a 3D view, `0x10000148` (a `UIElement_Viewport` with a `CreatureMode`; 300x265 at 0,68
  of the creature page `0x10000140`, over the stone `0x10000141`, under the stats list `0x10000149`). So with Model on
  the game is let appraise a creature itself again (TakeOver says no, after noting it, `CreatureModelView.Expect`),
  and its panel is cut down to the model: every other piece of the floaty (frame, title, close, other pages) and of
  the page (name, level, stats, description) hidden, the floaty sized to the view and the page moved up so the view
  sits at its corner (a parent clips its children), the stone and view sized to match, and the floaty put under the
  window's body, all with UIElement's own MoveTo, ResizeTo and SetVisible, found under the floaty
  (`ClientElements.Under.cs`: its pieces' ids are used in other layouts too), none of which the game saves. The game
  draws its UI under ImGui's, so the window leaves the body see-through (`FloatingWindow.SetHole`,
  `FloatingWindow.Hole.cs`: no ImGui background, its own drawn round the hole; the Retail stone drawn round it too,
  on a draw-list channel under the header, being known only after it), and the stats draw over the model as in the
  game. Anything ImGui draws is in front of the model, so over it the parts are set apart by gaps, as in the game's
  own list, and the one bar, before the description, is the game's own (`0x10000461`, 300x5, drawn behind the model),
  placed at that gap each frame (`CreatureModelView.PlaceBar`; where it was and whether it showed put back after; the
  user asked for the model in front of the bars). It's laid out again only when the place changes or the game has moved it back. Put back (places, and the
  pieces that were showing) when the game appraises anything else (an item, for its own panel), and put back and
  hidden once the window stops placing it (250ms: closed, hidden, another appraisal window up) and when the plugin
  stops. Tried in the game: the model shows, at another size than 300x265 too, but the floaty kept to a least size
  (310x400, while the hole was 294x335) and stood out of the window: its limits (attributes `0x3C`-`0x3F`, which
  ResizeTo holds it to; `0x3E` the least height, the least width the smaller of `0x3D` and `0x3F`) are lowered while
  it's cut down and put back after (`LowerLeastSize`, logged), untested. `[appraisal]` lines in log.txt say
  where it was put. Untested in the game when written.
  **The appraisal windows share one window** (`Ui/AppraisalGroup.cs`, the user's call, November
  2026): the item Appraisal window (the first; Appraise in new window's stay apart), Creature Appraisal and Spell
  Appraisal take one place, size, lock, Size and background (`FloatingWindow.SharedLook`, the placement `appraisals`,
  starting where the item window was), and the one that came up last shows, the others closed (`Dismiss`; the item
  window forgets its item), going by `FloatingWindow.Shown`, since the item window is always Visible and shows when it
  has an item. Each has a close button (`FloatingWindow.CloseButton`, `FloatingWindow.Close.cs`: the game's gold X, the
  Indicators' log out picture, in the top right corner over the contents, worked by hand; `EdgeDrag` leaves its corner
  alone). Untested in the game when written.
- **Escape closes windows** (`Ui/EscapeCloses.cs`, the Config tab's Escape key section, `SettingsData.EscapeClosesWindows`,
  on to start): the /dap window and the Inventory, Bank (a Use of the bank, as its X), Character and Quests windows, all at
  once; Target, Fellowship, Loot, hotbars, map and the rest stay (the user's call). Read from Windows on Decal's
  RenderFrame like Hide game UI's key, and acted on a frame later, once an ImGui frame has run: what uses Escape itself (a
  tool up, the hotbars' key capture, Bind keys and Move, Hide game UI's key capture) reads it in that frame, in whatever
  order the windows draw, and calls `EscapeCloses.Taken`. A press with the chat bar, a text box or a popup menu active on
  that frame or the one before does nothing, as Escape's job there is leaving it.
- **Toolkit button and key** (`Ui/Launcher.cs`, the Config tab's section of that name): a button that opens and closes
  the /dap window, like UtilityBelt's bar button, in a `FloatingWindow` of its own (`launcher`, hidden to start) that can
  go anywhere and be any Size, and a key for the same (`SettingsData.ToolkitKey`, none to start), read from Windows on
  Decal's RenderFrame like Hide game UI's.
- **Borderless window** (`Ui/BorderlessWindow.cs`, `Feed/BorderlessPlacement.cs`, the Config tab's Game window section,
  `SettingsData.BorderlessWindow`, off to start): takes `WS_CAPTION` and `WS_THICKFRAME` off the game's window and resizes
  it to its client area, so the client area doesn't change size and the game has nothing to rebuild. A window as big as its
  monitor, or within 64 pixels of it each way (`BorderlessPlacement.ScreenSlack`), is made exactly the monitor's size
  and put over it: Windows keeps a bordered window from being taller than the screen, so a 2560x1440 game's picture
  was 2560x1431, and the first build placed it at 0,9, a strip empty at the top and the picture squeezed. Any other
  stays where its picture was, its size kept, on screen. It waits for login (the character's
  profile), looks again once a second in case the game puts the border back, and puts the border back when turned off or
  at shutdown. A true fullscreen game has no caption, so nothing happens. Centre and Top left move it, having no title bar
  to drag. `[borderless]` lines in log.txt. Untested in the game when written.

- **Chat windows** (`Ui/ChatWindows*.cs`, `Ui/ChatView*.cs`, `Ui/ChatTab.cs`, `Ui/ChatFonts.cs`, `Feed/Chat*.cs`,
  `SettingsData.Chat`, off to start): the main window (`chat`, titled Chat) and four others (`chat-1` to `chat-4`,
  Tells, Group, Channels and Combat to start), which are tabs of the main one until popped out (`ChatWindowSettings.
  PoppedOut`; closing a popped-out one puts it back). Their hud names are "Daralet Chat..." and their titles set apart
  (`FloatingWindow.Title`: UtilityBelt begins a window as `Title###Name`), so a renamed window keeps its place.
  Every line comes from Decal's `ChatBoxMessage`, and is added on the next RenderFrame (`ChatWindows.AddHeard`), when
  every handler has had it: Decal makes one event for all of them (`SafeFireEvent`), so its `Eat` by then says whether
  anyone ate it, other plugins whose handlers run after this one included (Daralet Admin's `DAA2` replies filled the
  chat windows until this). So a line eaten by anyone (the feed's replies) isn't shown, and one LfgChatLinks adds again
  is, link and all (`ChatWindows` is made after the Combat tab, and `LfgChatLinks` before the hud). The exception is
  combat the Combat log's Hide in chat eats (`CombatWatch.LastHidden`): that's kept out of the game's chat, but shows in a
  chat window that shows Combat or Magic, always counted as one of those. One history (`ChatSettings.History` lines) holds them;
  each window shows those of its categories (`ChatCategory`), which are the game's own chat options: `ChatDisplayMask`
  in daralet-ACE's `libs/entity/Enum`, whose bit n is chat type n (Decal's `e.Color`), as `ChatTypeInfo` has them, with
  Society and Olthoi of the plugin's own, and a channel told by the line's start ("[Trade]") too (`ChatCategoryInfo.Of`).
  The main window always shows Help and admin (the game's Mandatory). Colours are per chat type, and per channel for a
  channel's lines, near the game's to start (`ChatTypeInfo`, `ChatSettings.Colors`). **Drawing:** each line is parsed
  once (`ChatMarkup`: the game's and plugins' links, `<Tell:IIDString:id:text>shown<\Tell>` or `</Tell>`, and linked
  items), wrapped once per width and font (`ChatLayout`, measured with the font's own advances read from ImGui's
  `IndexAdvanceX` into a table, `ChatFonts.Advance`, so no ImGui call per letter), and only the lines in sight drawn,
  onto the draw list (`ChatView.Lines.cs`), the content's height a `Dummy`; the lines' child is `NoMove`, or a drag
  over them moved the window. Hovering a line lights it and shows a Copy button drawn by hand (an ImGui button can't sit
  over text drawn straight onto the window). **Fonts** (`ChatFonts`): ImGui stretches one font picture for any other
  size, which blurs, so each window's text is a font made at exactly its pixel size (`ChatWindowSettings.FontSize` times
  its window's Size; the Config tab's Text size isn't in it), any family Windows or UtilityBelt has, through
  UtilityBelt.Service's `UBService.Huds.FontManager.Create`, which adds it to the font picture and has UtilityBelt
  rebuild it (and every texture) before its next frame: only allowed between ImGui's frames, so a font asked for while
  drawing is made on Decal's next RenderFrame (`MakeWanted`), and pushed only once it's built (`ContainerAtlas` set);
  until then UtilityBelt's font is stretched. `ChatView.BeginFont` pushes it and sets the window's scale so the text is
  exactly that size. **Typing** (`ChatView.Input.cs`): the talk focus (`ChatTalk`: say, a tell to your selection, reply
  to your last tell, fellowship `/f`, allegiance `/a`, patron `/p`, vassals `/v`, covassals `/c`, monarch `/m`, the
  channels `/general`, `/trade`, `/lfg`, `/roleplay`, `/society`, `/olthoi`, or your own) puts its command in front,
  and a line starting with / or @ goes as typed, through `Actions.InvokeChatParser` (so other plugins' commands work).
  Up and Down go through what you've sent. Text put in while the box is typed in goes through ImGui's callback
  (`CallbackAlways`), since ImGui keeps its own copy of an active box's text. A tell that can't go says why in that
  window alone (`ChatView.Note`). **Hide game chat** (`ChatSettings.HideGameChat`, also Client UI Override's Chat;
  `ChatWindows.GameChat.cs`): the game's chat windows, Decal's `UIElementType` Chat `0x10000601` (which has the chat
  bar) and FloatChat1-4 (`0x10000505`, `0x1000050E`-`10`), hidden with `ClientElements.SetVisible`, looked at again once a
  second, and those that were showing shown again when it's off or the plugin stops; nothing saved. Enter still opens
  the hidden chat bar, so the moment Decal's `ChatState` goes true it's cleared (`ClientElements.ClearFocus`,
  `UIElementManager::SetFocusElement(null)`) and the plugin's box focused instead (with Enter types here too). But
  `ChatState` is true for any of the game's text boxes, and the stack bar's number box lost every key typed in it, so
  only a focus inside one of the chat windows is taken (`ClientElements.FocusIsIn`, climbing `UIRegion.m_parent`).
  **Links:** a click on one is handed to Decal's `ChatNameClicked` as the game's chat would (CoreManager's private
  `myHooks_ChatClickIntercept`, by reflection), so every plugin's links work here, the plugin's own included; a name no
  plugin takes starts a tell. **Item links** (`ChatWindows.Links.cs`, `Feed/ChatItemLink.cs`,
  `Feed/SharedItemRequests.cs`): Link in chat, in an item's right-click menu (inventory lists, hotbar slots), sends
  `@playerui shareitem <guid>`; the server keeps the item's description (`PlayerUiCorpseData.Describe`) for a day under
  who linked it and its name, one item to a name, the latest (`PlayerUiItemShares`, `SharedItemStore` in daralet-ACE:
  5,000 at most, a player's newest 50), and answers with the name; `[Name]` goes in the box (or on the clipboard, with the windows off), so
  players without the plugin read just that. The line carries nothing else: a `#code` in it, the first way, was ugly in
  the game's chat. Readers' plugins take each `[...]` after the first quote of a line someone said
  (`ChatMarkup.Speaker`, `MarkItems`) and ask `@playerui shared <key>...` (`ChatItemLink.Key`: speaker and name in base64,
  10 a request or 400 letters, `SharedItemRequests`). Only the speaker's own links are found under their name, so a
  typed `[Sword]` can't pass an item off as anything, and finds nothing unless they linked one; until the server says
  it's an item, it's drawn as plain text. A new line asks afresh (`RefreshSharedItem`, in case they've linked another
  of that name), and a line keeps the item it was answered with (`ChatSegment.Shared`). Link it again is `@playerui
  shareagain <key>`, keeping the same share under your name too. Hovering shows `ItemTooltip`, clicking opens the
  Appraisal window, which compares it with what you wear by the slots the server sent
  (`AppraisalWindow.givenSlots`). **Unread:** a tab counts lines come while it
  wasn't on screen and flashes (gold, red for one with your name or a highlight word, `ChatHighlights`). Saved chat
  (`ChatSettings.Save`) goes in `Chat Logs` through `CombatLogFile`. `[chat]` lines in log.txt. Untested in the game
  when written.
- **Combat Text** (the Combat tab's sub tab, with the Combat log window, whose lines show a Copy button while hovered: `Ui/CombatLogTab.cs`, `Ui/CombatWatch.cs`, `Ui/CombatText.cs`,
  `Feed/CombatLines.cs`, `CombatKindInfo.cs`, `CombatFloats.cs`, `CombatLogFile.cs`): everything is read from
  `ChatBoxMessage` by `CombatLines.Read`, since chat is the one place all combat comes through: the client writes its own
  line for `GameEventAttackerNotification` (`You %s %s for %d point%s of %sdamage!` in `acclient.exe`; the line for being hit
  isn't in the exe as plain text, so its pattern takes either "you" or "your <part>"), and the server writes spells,
  glancing blows and adrenaline itself, with short sentences in front ("Critical hit! Overpower! You..."). A combat line the
  patterns miss, mentioning points or damage, goes in log.txt (`[combat]`, the first 20 a session): add its wording to
  `CombatLines.Patterns` and a row to `CombatLinesTests`. Kill and death wording is too varied (`Strings.cs` in daralet-ACE),
  so those are told by their game events, 0x01AD and 0x01AC, which come just before the client writes the same text.
  Tells, speech, channels and anything with a quote in it are never combat, so nobody can fake a number with a tell.
  Each kind (`CombatKind`, a row in `CombatKindInfo.All`, whose `Key` is what settings.json has) has its own `CombatKindOptions`:
  Floating, Log, Save, Hide in chat (`e.Eat`) and a colour. Heals are three kinds, health (`healing`, the old key),
  stamina and mana (`CombatLines.HealedVital`, by the vital the server names straight after the number); the last two
  start as health's options were set, in their own colours (`SettingsData.CombatOptions`). `CombatKindInfo.All` is in
  the Combat tab's order, not the enum's (`For` looks a kind up). **Floating text** (`CombatText`) rises from two fixed places on
  screen, one for what you do and one for what's done to you, in percent of the screen. It's drawn on ImGui's background list from a hud of its own with `NoInputs`, so
  it never takes a click, using `ItemIcons.DrawLabel`; so many numbers fill the label cache, which now frees a full cache's
  textures a frame later (`RetireLabels`), since this frame's draw lists still point at them. **Over their head**
  (`SettingsData.CombatTextOverHead`, off to start, `Ui/CombatHeadText.cs`) puts them over whoever they landed on instead:
  the game leaves its camera on the Direct3D device (`UBService.Huds.D3Ddevice`, `Transform.View` and `Projection`, and
  `Viewport`, read from a hud's OnRender), drawing in landblock meters as (X, Z, Y) of `LocationX/Y/Z`, so a place in the
  world can be worked out on screen (`Feed/CombatProjection.cs`), and the numbers are drawn there like the rest, on top of
  everything, centred and stacked upward per target (`stackTops`) rather than in the fixed places' lanes. While some effects draw (a streak spell in flight, for seconds at a time) the device holds the same camera with its
  Y and Z swapped, which put numbers off screen (`CombatProjection.IsSwapped`, `Unswap`). Any other camera that isn't near
  you is passed over for the last good one (`MostFromCamera`). Decal doesn't say how tall anything is: `HeadMeters` is a person's, plus the Height
  slider. Worked out indoors; outdoors, a creature in the next landblock is taken to be 192 meters on
  (`LandblockOffset`), untested when written. Decal's own 3D text (`D3DService`, `D3DObj`) was tried first and dropped:
  the world's lighting darkened it, its font and size were its own, `Set2DText` failed with `0x8876086C`
  (D3DERR_INVALIDCALL) from a render callback (it starts a scene with `D3DXCreateRenderToSurface`), and `Color` refused a
  see-through colour. Chat only gives a name (`CombatLine.Target`, a pattern's `target` group), so
  `CombatTargets.Pick` takes the one you hit last, then your selection, then the nearest of that name, and lines of one
  kind and name within 100ms (a cleave) each take the next nearest (`NearestNotUsed`), a guess at which number is whose; what can't be
  placed, or with no camera (`CombatHeadText.HasCamera`, `[combat]` lines), uses the fixed places. **Saved logs**
  (`SettingsData.CombatLogSave`, off to start) are text files in `Combat Logs`, `<character> yyyy-MM-dd.txt`, written about
  once a second; nothing deletes them. Untested in the game when written.

- **Arena and Leaderboards tabs** (`Ui/ArenaTab.cs`, `Ui/LeaderboardsTab.cs`, `Feed/ArenaCommands.cs`,
  `Feed/Leaderboards.cs`): the leaderboards are the website's (`leaderboards.js`), read from `/api/leaderboards/...` with
  one `LeaderboardRow` for every board. A new board is a row in `Leaderboards.All`. One `MarketApi` (one `HttpClient`) is
  shared by the Market, Arena and Leaderboards tabs and made by `PlayerHud`.

- **Market tab** (`Ui/MarketTab.cs`, `Feed/MarketSearch.cs`, `Feed/MarketApi.cs`): My market, and Search over HTTP. The
  search filters are a table (`MarketSearch.Filters`) copied from the website's `market.js`: when the website gains a filter,
  add a row. Percent filters send a fraction (15 → 0.15), "percent of one" ones a multiplier (15 → 1.15), tiers go up by one
  (the API counts from 1), and Salvage has no tier. `ApiRequest<T>` runs a request off the game thread; the newest one wins.
- **The website's icons** are at `https://play.daralet.ac/images/<iconHex>.png` (13,000 of them, `daralet-website/images`),
  named by the portal.dat icon id, which is what the market listings' `iconHex` and Decal's `LongValueKey.Icon` give.

- **Inventory tab** (`Ui/InventoryTab.cs`, `Ui/InventoryBrowser*.cs`, `Ui/BankCommandBar.cs`, `Feed/BankCommands.cs`): sub tabs Carried
  (your packs, anywhere) and Bank (the bank's packs, while it's open). The `/bank` buttons are a `BankCommandBar` above the
  items in each, and in the Bank window: one bar, shared, so its "what" box carries over. Deposit with the box empty
  is Deposit packs (your "deposit"-tagged packs), with words in it a deposit of those. A bank pack, or a side pack you
  carry (`Holder.CarriedPack`, in the Retail view too), has a **Tags**
  drop-down of every `/bank` word (first **Deposit** and **Keep**, `BankCommands.DepositTag` / `KeepTag`, what the pack is
  for rather than what goes in it, each always a tag of its own: deposit packs are what `/bank deposit packs` empties
  and withdrawals never fill, keep packs are left alone by deposits of a kind and `/sort`), ticked when the pack has it (`BankCommands.HasTag` / `ToggleTag`, comma-separated
  groups), each tick sending `/bank inscribe` at once (the plugin keeps what it sent until the next bank reply). A carried
  pack has no number, so the command names it by its id (`/bank inscribe #8000ABCD weapons`, `BankCommand.FindPackById`,
  on a server with the `packid` feature). An older server only takes the pack last examined, so there the plugin
  appraises it (`Actions.RequestId`) first; but anything appraised in between (another add-on, or this list's own
  appraisal of the pack changed before) took the change instead: a player's "Weapons, Armor" landed on their "Salvage"
  pack while the list showed each where it was meant to be, and `/sort` filled the salvage pack with weapons. Decal only
  learns an inscription from an appraisal, and the server sends none when it changes, so the pack is appraised again a
  second later (`InventoryBrowser.RefreshCarried`), and a second after that the list shows the tags it has, not the
  ones it sent (`adoptPack`). But an appraisal of a pack with no inscription leaves Decal's old one in place, so Clear
  all tags never showed: the pack kept its old tags, and the next tick sent them back. So the `bank` reply carries the
  packs you carry too (`carried`, bank open or not, `BankInfo.Carried`), and their tags are the server's when it sends
  them (`InventoryBrowser.CarriedInscription`), asked for again straight after each change (`PlayerFeed.RefreshBankSoon`). Groups are
  OR (a comma) and the words in one AND (a space): the list's **OR** makes each tick its own group (`ToggleTag`), its
  **AND** adds the word to a picked group, or a new one (`ToggleInGroup`), and ticks then show that group. And an
  **Icon** drop-down of every style and color `/bank icon` takes (`BankCommands.PackIcons`, copied from daralet-ACE's
  `Entity/PackIcons.cs`: pouches have no white), plus the pack's own icon back; the bank's views add Withdraw, Sort
  and Search, and Carried has its own Sort, the server's `/sort` of your packs (`Commands/PlayerCommands/Sort.cs`, no bank
  needed, no words); the rest are under More. The last command's first reply
  line shows under the buttons, and hovering it shows the whole reply. Carried and Bank are each a `InventoryBrowser`
  (`carried: true` for yours), with their own selected pack and search. **Show my packs** on the Bank view
  (`SettingsData.BankShowCarried`) lists your packs there too, above the bank's, to drag between them. **Bank window**
  (`SettingsData.BankWindow`, switched under the Config tab's Client UI Override) hides the game's bank window (see
  above) and opens the plugin's own, a `FloatingWindow` you
  size yourself (`defaultSize`, its size saved like its position), with its own `InventoryBrowser`. It opens and closes with the
  bank, which `InventoryTab` follows every frame, so it works with the main window closed. The **Inventory window** (controls
  on the Carried tab, window id `carried`, hidden by default) is Carried in a `FloatingWindow` with its own `InventoryBrowser`
  (`retailAllowed: true`). Its **Retail** box (`SettingsData.CarriedRetail`, on by default) lays it out like the game's inventory
  (`InventoryBrowser.RenderRetail`): the paperdoll at the top left (shown whatever Worn says) and the packs beside it on the
  right, the two as tall as each other so their bottoms line up: the row is the pack column's height (from last frame, `packsHeight`), and the paperdoll closes up the space under its trinket and sigils' row to fit (`RenderPaperdoll(sigilGap)`, the empty row 1 and its gaps in the game's layout), and
  the chosen pack's items as the grid across the whole width below. The packs are icons only, each with a fill bar down its right side, over the icon, filling from the bottom (`LongValueKey.ItemSlots`; 102 for the main
  pack when the client isn't told) and its name on hover, then an empty square for each side pack slot left (`LongValueKey.PackSlots`, 7 when the client isn't told; drop a pack on one to put it there), with a burden bar down its right (`CharacterFilter.Burden`; hover it for the percent and `BurdenUnits`, full at 300%, the most you can carry, and yellow past 100%, with faint lines at 100% and 200%). The pop-out's **Reset width** makes it just wide enough, in Retail, for the paperdoll (with a little room) and the packs beside it, leaving the grid a little over seven columns (`RetailWidthShort`); in the plain view, for six columns of items (`InventoryBrowser.RetailColumns`, `GridWidthShort`). `FloatingWindow.SetWidth` sizes the window. No `/bank` bar in Retail: its top line has Loot highlights and Sort (`/sort`) instead, and the chosen pack is framed with the selection frame and a light brown arrow on its left edge, pointing in (`DrawChosenPack`). Packs (not the main pack) are dragged onto another pack, in any pack list, to take its place (`MoveItem` to that pack's container and slot, `Holder.Parent`/`Slot`): packs can't go inside packs, so that's how they're reordered, or moved between yours and the bank's at a place.
  `PlayerFeed.SendCommand` keeps the System and Help chat (types 5 and 15, Decal's `e.Color`) that comes back within three
  seconds as the command's reply. The items show as a list or, with **Grid** (`SettingsData.InventoryGrid`), as icons only, as
  many to a row as fit, each with the list's hover, drag and right-click; a stack shows its size in the corner
  (`StackText.Short`: from 10,000 in thousands to the hundred below, 50.1k, or whole thousands where that doesn't fit).
  Stack sizes are read through `StackText.FromDecal`, which reads a negative one as the unsigned 16-bit number the game
  sends: a 50,000 pyreal stack showed no count, likely read as -15,536. A drag shows
  only the item's icon, centred on the mouse and drawn on the foreground draw list (no preview tooltip, which ImGui fades).
  Dropped on another item, it goes to that item's container and slot (`InventoryBrowser.MoveTo`, `MoveItem` without stacking,
  unless it's a stack of the same thing), which is how items are reordered within a pack. **Hotbar highlights** (`SettingsData.InventoryHotbarMarks`, on to start, beside Loot highlights) marks what's on a hotbar you have showing as the game's pack marks its shortcuts: the icon's top left corner dithered in pale gold, every other pixel, and over it a cream square with the key that uses it in black (`ItemIcons.DrawMarks`, its text a label without the outline, `DrawLabel(..., outlined: false)`; empty when no key does or the item's on a page not showing, since a place's key uses the page showing), the gold under a loot mark's frame and symbol, which share the corner, and the square over them, so the key always shows; "(hotbar 1)" in a Compact list. `HotbarItems.KeyOnShownBar` works it out once a frame, a used-up stack's slot counting as the stack it would take next (`HotbarItems.Pick`, what `Resolve` uses without changing the slot); hidden bars and spells don't count. **Splitting stacks**
  (`Ui/StackSplit.cs`, `InventoryBrowser.Split.cs`): a click selects the item, so the game's stack bar shows, and a drag
  carries what it says (`Actions.SelectedStackCount`, while the dragged item is `CurrentSelection`), shown on the drag
  icon. Less than the whole stack, every drop (a pack, an item, an empty slot, the toolbar backpack, the ground, someone)
  is sent as the client's own split, through AcClient's `CM_Inventory`: `Event_StackableSplitToContainer`,
  `Event_StackableMerge` (onto a stack like it), `Event_StackableSplitTo3D`, `Event_GiveObjectRequest` with the amount;
  Decal's `MoveItem` always moves the whole stack, which is why a split on the bar did nothing before. `WorldDrop` takes
  the amount before its click clears the selection. Right-click **Split...** opens a slider (a plain popup, not a modal,
  which greys out the whole screen; opened next frame from `Render`, its slider centred where Split... was picked): Split puts that many at the end of the same pack, Drag them sets the game's stack bar for the next drag.
  The server puts the new stack at the place sent, but Decal gives a new item no place of its own, so the lists showed it
  by the old stack or first: once it shows up it's moved there again with `MoveItem` (`StackSplit.Step`), which Decal
  follows. The menu line goes by `StackCount` alone: Decal doesn't always know `StackMax` without an appraisal.
  A number typed in the stack bar's box (`0x100001A3`) only counts once the box loses focus (gmToolbarUI reads it on
  message 0x2F into the split size at `0x81D7EC`, which the slider sets straight away), and a click on an ImGui window never
  takes focus from it, so a drag (and Drag them) first does what a click in the game would: `ClientElements.Unfocus`,
  `UIElementManager::SetFocusElement(null)` (`0x45B970`) while the box is `m_focusElement` (`+0x328`).
  `[split]` lines in log.txt. **Onto the game's shortcut bar** (`Ui/GameShortcuts.cs`): let go over one of its 18 slots
  (`0x100001A7`-`AF`, `0x100006B7`-`BF`, the order `gmToolbarUI::InitShortcutArray` lists them), an item goes there, as
  `gmToolbarUI::HandleDropRelease` does it: `RemoveShortcutInSlotNum`, `CreateShortcutToItem(item, slot, 1, 0)`, and what
  was there to `GetFirstEmptyShortcutToTheRightOf` with `AddShortcut`, called on the gmToolbarUI (vtable `0x7B5650`),
  found by climbing from the slot's element through its parents (`UIRegion.m_parent`, `+0xAC`): the TBAR element itself
  isn't it, which left the first build's drops unanswered. Before this, `WorldDrop`'s click used the shortcut there and the item went on the
  ground. The slot gets the green ring while it's held there. `[shortcut]` lines in log.txt. **Worn**
  (`SettingsData.InventoryShowWorn`) slides out a paperdoll of what you're wearing on the right (`InventoryBrowser.RenderWorn`), laid
  out as the game's equipment panel by `EquipSlots` (the game's EquipMask bits, one square per slot; an item covering
  several shows in each; Pants leave out AbdomenWear, which shirts have too, and anything worn on the feet
  (`EquipSlot.Except`): boots are worn on the lower legs as well, and showed as pants while none were on; an empty one shows the game's own picture of it, `EquipSlot.EmptyIcon`, found in portal.dat by the
  bevelled blue frame they share and drawn as it is, `ItemIcons.DrawPlain`). A sigil slot you haven't unlocked isn't
  drawn at all (`AetheriaSlots`, `Feed/AetheriaFlags.cs`): the player's `AetheriaBitfield`, PropertyInt 322 (blue 1,
  yellow 2, red 4), which the server checks before wielding a sigil, sends at login (`SendOnLogin`, read with
  `CharacterFilter.GetCharProperty`) and sends again when it changes (a private int update, `0x02CD`, read from the
  message itself in case Decal doesn't follow it; `/enable-aetheria [flags]` sets it for a developer). `[aetheria]` lines
  in log.txt. The window grows by the panel's width when
  Worn is turned on and shrinks when it's off, once per window (`wornShownAs`, by host), and the Worn checkbox is above the
  packs, where it can't be squeezed out. Its items have the list's hover and right-click; dragging one onto a pack or onto
  the items takes it off into that pack, and dropping an item on a square wears it (`Actions.UseItem`, the game's own
  use).

- **Use** (`Ui/ItemUser.cs`, `Feed/UseRules.cs`): double-clicking an item you carry or wear in the Inventory tab's lists,
  or Use in its right-click menu, does what a double-click in the game's inventory does. A double-click is every
  second click of a quick run, as Windows counts them (`Widgets.DoubleClicked`): ImGui's `IsMouseDoubleClicked` is only
  the second, so clicking away at the top of a pack put on only the first item where the game's pack puts on each. Gear goes on with the game's own
  auto-wield (`Actions.AutoWield`), or comes off into the main pack. The server won't put anything over a taken slot
  ("You must remove your..."), and a plugin's AutoWield doesn't take anything off first the way the client's double-click
  does, so `ItemUser.Wield` takes off what's in the way (`UseRules.ToTakeOff`: armor and clothing sharing a layer of its
  coverage, Decal's `Coverage`, the server's ClothingPriority, since a shirt and pants share AbdomenWear without
  clashing; the left ring or bracelet when both are full, the main hand, the off hand for a two-hander, caster or bow, after the server's
  `CheckWeaponCollision`) one item at a time, 250ms apart, then wields; dropping onto a Worn square does the same. A ring
  or bracelet is wielded into one named place (`UseRules.PlaceFor`: the square dropped on, else a free one, left
  first), sent as the client's own `AcClient.CM_Inventory.Event_GetAndWieldItem(item, place)`: neither Decal's plain
  `AutoWield(id)` nor its explicit `AutoWield(id, place, 1, 0)` ever put one on the left wrist. A tool
  whose use flags have target bits (ACE's `Usable`, high 16 bits) and whose source bits (low 16) allow where it is (a
  caster's `SourceWieldedTargetRemote` only once wielded, so a caster in a pack is wielded) turns the mouse into a reticle (drawn on the foreground draw list: a plugin can't change the
  game's cursor), and the next item clicked in any list gets it (`Actions.ApplyItem`). Anything else is `UseItem`. A target
  must be in a place the flags allow (worn = Wielded, packs = Contained, the open bank = Viewed, the tool itself = ObjSelf)
  and of a kind its TargetType has (`TargetType & ItemType`, the server's own check in `HandleActionUseWithTarget`); the
  bar above the list adds "On yourself" (Self) and "On <selection>" (Remote or Viewed). A click outside every plugin
  window picks a target in the world (a door or chest for a key or lockpick: `ItemUser.World.cs`): the click reaches the
  game, which selects what's under it, and that's the target. The selection is cleared as the button goes down (unless
  the press already changed it), so a click on what's already selected still counts and a click on nothing leaves the
  tool up; a drag puts the old selection back. No click is posted, as `WorldDrop` does: one straight after the player's
  own would be a double-click, which opens the door or chest. A world target is checked against the TargetType only when
  the client knows both kinds (`MayUseOn`); otherwise the server decides. Escape or right-click puts the
  tool down. Decal's names for these keys come from the network header flags: `LongValueKey.Unknown10` is the use flags
  (flag 0x10), `UsageMask` the TargetType, `Category` the ItemType. Each use logs them to log.txt (`[use]` lines).

- **Selling and salvaging from the Inventory windows.** **Marked for sale** in an item's right-click menu (carried, not
  worn; a green $ on its icon, `ItemIcons.DrawSaleMark`) keeps its guid in `SettingsData.SaleItems` by character; the moment
  a vendor opens (its window's tabs, `ClientElements.VendorTabs`, coming into sight, half a second later, `VendorSale.Step`:
  Decal's `VendorId` needn't go back to 0 on closing, and the server resends the vendor's list after every purchase) every marked item you carry that
  it buys goes on its sell list (`VendorAddSellList`): nothing's sold until you press Sell. Marks of items gone from Decal
  (sold, given) are dropped then. A **pack let go over the vendor**, the main pack too (a drag type of its own, `MainPackDragType`, which no drop spot takes, since it's your character and not an item; its side packs' items don't go with it), puts on its items the vendor buys (an empty pack, the
  pack itself: a vendor only buys one empty). What a vendor buys is `ItemChecks.VendorBuys` (`Feed/ItemChecks.cs`, from
  daralet-ACE's `Player.VerifySellItems`: the vendor's ItemType mask, Decal's `Vendor.Categories`; a value; not retained;
  not attuned, since the server's IsSellable, which the client isn't sent, is off for those). **Trade and the Ust**
  (`Ui/PanelDrop.cs`): while a trade is open (Decal's `WorldFilter.EnterTrade`/`EndTrade`) any item let go in the game,
  but on the shortcut bar or the vendor, goes in the trade (`Actions.TradeAdd`) rather than on the ground. The salvage
  panel is taken to be what the panel along the bottom (ENVP) shows while no vendor, trade or container is open and you
  carry an Ust: an item let go there goes on it (`Actions.SalvagePanelAdd`), a pack its salvageable items of one material
  (`ItemChecks.NextMaterial`), the next material each time it's let go there again (what's been added is forgotten when
  ENVP goes). Untested in the game when written (the trade and salvage panels' own element ids weren't looked up):
  `[drop]` lines in log.txt say what was decided. **Salvage** (`Ui/RuleSalvage.cs`), beside Sort on your packs' views,
  describes every carried non-stacking item, then lists those the active loot profile's first matching rule says to
  Salvage (`Loot.Decide`, so a Keep rule above wins) and the server calls salvageable, leaving out items marked for sale,
  in a popup; its Salvage sends `@playerui salvage` 20 at a time, 700ms apart (the server drops a request of the same kind
  sooner than 400ms after the last). The server's `HandleSalvaging` already tops up bags of the same material and
  workmanship and puts new bags in the salvage crate (wcids 1055060, 1055070, 1055080) if you carry one; with Combine like
  bags afterwards, if two carried bags still match three seconds later, `/salvage combine` is sent (the server asks Yes/No).
  `[salvage]` lines in log.txt. Sort and Salvage start their rows (Retail's top line, the carried `BankCommandBar`), so a
  narrow window doesn't push them out of sight.
- **Dragging into the world** (`Ui/WorldDrop.cs`): an item (or pack) let go outside every ImGui window is given to what it was let go over, or dropped on the ground, as the game's own pack does. A plugin can't ask what's under the mouse, so the selection is cleared and the game is posted a left click there (`PostMessage` to `Decal.Hwnd`): whatever it selects within 300ms gets the item (`Actions.GiveItem`); nothing, and it's dropped (`Actions.DropItem`). Yourself does nothing; the bank's items are left alone. `[drop]` lines in log.txt. The game's own panels aren't ImGui windows, so letting go over one counts as the world, except the vendor window: while a vendor is open (`Actions.VendorId`), an item let go over the panel along the bottom it's shown in (ENVP, `ClientElements.Environment`, which also shows chests and corpses) goes on the sell list (`Actions.VendorAddSellList`) instead of on the ground, and while the game shows an open bank
  (Bank window off) or chest there (`Actions.OpenedContainer`, not a corpse), it goes in that container
  (`WorldDrop.OverOpenContainer`, `MoveItem`, or the split for part of a stack): before, it went on the ground. Which
  pack is the game's window's (gmExternalContainerUI, `ClientElements.Containers.cs`): its PostInit (`0x4CC9D0`) finds
  three item lists, the container's own icon `0x10000064`, its packs' slots `0x10000067` and the open pack's items
  `0x1000006A` (found under ENVP with `UIElement::GetChildRecursive`, `0x463C00`, and cast with its DynamicCast to
  `UIElement_ItemList`, `0x10000031`); a slot under the mouse (`UIElement_ListBox::GetItem`, `0x46DC50`, by place, its count at `+0x610`, each cast to `UIElement_UIItem`, `0x10000032`,
  whose `itemID` is at `+0x5FC`) takes it, else the items list's `parentContainerID` (`+0x66C`), the pack framed in
  green, at the items row's slot under the mouse (its place in the list, as the game's own drop puts it)
  (`WorldDrop.OpenContainerTarget`). The slot it'd go in has the green ring while it's held there. Only ever the open
  container or a pack in it, and a pack always goes in the container itself. While it's held there (`WorldDrop.OverVendor` decides both), `VendorDrop` does what the game does for one from its own pack: the window turns to its Selling tab and the sell list's slot under the mouse has the green ring. The game's own `gmVendorUI::UpdateDragOver` opens that tab only while it's dragging something of its own (`UIElementManager`'s drag element at `+0x31C`), with `gmVendorUI::OpenTab(0x100000BB)`, which hands the tab's id to the window's panel of tabs (`UIElement_Panel::OpenTab`, `0x46C110`); the plugin calls that on the panel itself (`ClientElements.OpenTab`), found by id, rather than reach the vendor window through an unchecked offset. A click posted on the tab instead reached ImGui too, which took its button-up for the item being let go, selling it the moment the tab switched. The vendor window's parts are its panel of tabs `0x100000B8` (element type 8, `UIElement_Panel`), the tabs Items, Buying and Selling (`0x100000B9`, `0x100000BA`, `0x100000BB`), their pages (`0x100000BC`, `0x100000C4`, `0x100000CD`: Selling is `0x100000CD`, which `UpdateDragOver` checks for in the panel's current page at `+0x6D8`), and each page's single row of 32-pixel slots (`0x100000BD`, `0x100000C5`, `0x100000CE`), all three rows in the same place. The client's functions were read with a throwaway disassembler (the Iced NuGet package) over `acclient.exe`, at the addresses AcClient's `Entrypoint` names. The vendor window's own layout is `0x21000012` (found by searching the layouts for gmVendorUI's element type `0x10000017`, which `gmVendorUI::GetUIElementType` returns), but the game never makes its root element, `0x100000B7`: `GetElement` finds nothing by that id.
- **Dragging in from the game** (`Ui/GameDrag.cs`, first version): a plugin can't see the game's drag, so it's inferred: the left button went down outside every ImGui window (ImGui counts no window hovered while a button held from outside is down), moved, and came up over one of ours, and the dragged thing is the game's selection. What can be dragged in is anything on the ground, or in the bank, chest or corpse the game has open or one of its packs (`GameDraggedItem`, `GameDrag.InOpenContainer`; before, one from the game's bank window showed red), part of a stack when its stack bar says so; the game is kept from hearing the release of one from its open container too, as for a carried item, so it stays put: let go over a spot (`InventoryBrowser.GameDropTarget`), a pack goes in a pack slot (or takes a pack's place), anything else in a pack or at a grid slot, by `MoveItem`; elsewhere it shows red. While it's held over our windows ImGui hovers nothing, so the drag is drawn by hand: the pack's icon under the mouse over an inventory window (`GameDrag.DrawIcon`; the game's own drag icon isn't drawn over ImGui windows), and the green ring or red circle over the spot it's over (`GameDrag.OverLastItem`, the mouse against the spot's square). Each drop spot's verdict is logged when it changes (`[drop]` lines, `LogDropMark`). `[drag-in]` lines in log.txt say what was seen.
- **Item icons** (`Ui/ItemIcons.cs`, `Feed/IconLayers.cs`): every item list (bank, Loot tab, Corpse window, Market) draws
  an item's underlay, icon and overlay before its name. UtilityBelt.Service's only icon loader is meant for its scripts
  (`ScriptHudManager.GetIconTexture`, which reads portal.dat), but a `ScriptHudManager` made with `new` works: its
  constructor only hooks the UB bar, and its `Dispose` frees every texture it made. At most 8 new textures a frame, so
  opening a full bank doesn't stall the game. Items the client has come from Decal (`LongValueKey.Icon`, `IconOverlay`,
  `IconUnderlay`); listings from the Market API's hex ids, and your own listings, sales and expired items from the
  `icon` `@playerui market` sends (read from each listing's snapshot by the Market API's `Describe`). The game picks an
  item's underlay tile from portal.dat's table `0x25000008`, by ItemType's bit number plus one (melee `060011CB`, missile
  and thrown `060011D2`, casters the plain `060011D4`). The Market API names these `MeleeWeapon`, `Weapon` (the missile
  tile, under its old name) and `Caster`; before that it called them all `Weapon`, so for an older server or API
  `IconLayers.WeaponKind` corrects it from the listing's class. Underlay tiles by kind are the website's
  (`IconLayers.Underlays`, from market.js). The outline is the client's own (`IconData::RenderIcons`, `0x58DFB0`, read
  with `acclient.pdb`'s names, though that pdb is an older build and its addresses are off; UtilityBelt's `AcClient` has
  the right ones): the overlay goes on the icon, then every pure white pixel (opaque `FFFFFFFF` exactly, `0x441750`), edge
  or not, takes the colour at the same spot in a 32x32 picture, a gradient for each effect, from portal.dat's table
  `0x25000009` (`IconLayers.EffectImages`, `ItemIcons.Painted`). The table goes by UiEffects' lowest bit set plus one
  (`IconLayers.EffectOf`, `0x685460`), so magic beats an element; its Default, for no effect, is black. Sigil trinkets'
  one colour each is the website's (`SigilOutlines`). Market listings only have the Market API's few effect names, from
  damage type and spells. Hovering an icon shows the item's tooltip. **Compact** on the
  window's top line (`SettingsData.CompactLists`) hides them; a loot mark still shows, on its own.

- **World tab** (`Ui/WorldTab.cs`, `Map/`): the map (Daralet Admin's `dereth-map.png`), the places in
  `Resources/locations.xml` (a `locations.xml` next to the dll wins, then a downloaded one: see World data), you, your
  fellows, and a destination arrow window. The map image is one `MapImage` both maps share, loaded from login
  (`WorldTab.Core_RenderFrame`) whether or not a map is open: decoded and cut into 16 tiles off the game's thread, one
  tile a frame made a texture on it, since one 2048-pixel texture stalled the game.
  The file also has 43 `_Unknown` entries (shops, taverns, halls): every type needs a row in `Locations.Types`, which a
  test checks.
  **Names never overlap** (`Map/LabelPlacer.cs`): each frame the names are offered in priority order and each takes a box
  (right of its marker, else left) only if it's wholly in view and clear of every box taken before it, fellows' names
  first (always written), then the searched-for place, then by kind (`Map/LabelPriority.cs`, `SettingsData.MapLabelOrder`,
  reordered with the arrows in Show...; null for `LabelPriority.DefaultOrder`), ties in file order. So zoomed out only the
  first few fit, and zooming in spreads them out for more; non-town names start at a lower zoom than before (6 pixels a map
  unit) for that reason. Kept out of `Locations.cs`, which Daralet Admin has a copy of.

- **Appraisal** (`Ui/Appraisal.cs`, `Ui/AppraisalWindow.cs`, `Feed/ItemCompare.cs`): Appraise and Appraise in new window
  in an item's right-click menu in the Corpse window or the bank (`Appraisal.MenuItems`; nothing happens until one is
  picked). Appraise reuses the first window, a new window opens another (up to 6, titles numbered, since UtilityBelt knows
  a window by its title). Each compares the item (only one with `EquipableSlots`: the section is left out for anything
  else) with what you're wearing where it goes (`AppraisalWindow.Equipped`:
  `EquipableSlots` against worn `EquippedSlots`, any weapon for a weapon), with `ItemCompare` saying which way each stat is
  better; ` describe` covers equipped items for this. A
  plugin can't open the game's appraisal panel: Decal's `Actions.RequestId` only sends the request, and the client shows
  only appraisals it started itself. So with **Plugin appraisal** (`SettingsData.AppraiseInPlugin`, on by default, on the
  window's top line) the plugin's Appraisal window shows it: the icon a size up with the loot rule's mark, the name, kind
  and tier, what your loot rules make of it, and `ItemLines` with a bar for each roll's quality. A corpse item uses the
  corpse's description; a carried or banked one is described on request. An item you can't wear or wield, or a trinket
  or sigil, also shows its Use text and long description, here and in the bank's and the Loot tab's tooltips
  (`Ui/ItemUse.cs`: `StringValueKey.UsageInstructions`, `FullDescription`). The server's description doesn't carry them:
  they reach Decal only with an appraisal, so the first look asks for one (`Actions.RequestId`, once an item, unless
  `HasIdData`), and the game's panel stays shut. Market search tooltips have none: those items aren't in Decal. The
  Appraisal window shows gear's too (`ItemUse.AppraisalLines`; the tooltips leave gear's out, as too long): the game's
  appraisal explains each property in a "Property Descriptions:" block atop the long description (`~ Name: text`
  lines, from AppraiseInfo's `_additionalPropertiesLongDescriptionsText`), which `Feed/PropertyDescriptions.cs` splits
  off, so hovering a line that names a property ("Biting Strike: +3%", "Mighty Thews II, Fire Rending") shows its
  description; any no line names stay in the Description. Off, right-click just selects the item, for the
  game's appraise key.

- **Dungeon window** (`Ui/DungeonWindow.cs`, `Map/Dungeon*.cs`, `dungeon`, hidden to start, its switch beside the
  World tab's Show... and its settings under the tab's Settings): a map of the landblock you're in while indoors, drawn
  from the dats, with no help from the server. A landblock's `LandblockInfo` (`0xXXYYFFFE` in the cell dat) says how many
  cells it has; each `EnvCell` (`0xXXYY0100` on) names its piece (an `Environment`, `0x0D000000 |` its id, in the portal
  dat, and which `CellStruct`) and where it goes (`Position`, in landblock meters, the same frame as Decal's
  `LocationX/Y/Z`); the piece has the polygons (`PhysicsPolygons`, or the drawn ones when it has none). `DungeonDat`
  reads them; `DungeonLayout` keeps the walkable ones (normal Z at least 0.66, the game's FloorZ; under 0.97 a ramp), and
  the walls are the floors' outline, edges no other floor shares (matched to 5cm, height included), so doorways stay
  open: the game's wall polygons were never needed. `DungeonLoader` reads 60 cells a frame on the game's thread (the dat
  readers aren't safe on another, and UtilityBelt uses them on this one; 750 cells take about 0.1 seconds) and keeps the
  last six landblocks. It uses `UBService.CellDat` and `PortalDat`, opening the cell dat itself off the game's thread if
  UtilityBelt hasn't (about a second and 55 MB) and handing it over, as `EnsurePortalDat` does. `DungeonView` draws each
  floor from yours (`DungeonGeometry.FloorFrom`, 6 meters a floor, from your `LocationZ`): four below darker the deeper,
  one above as a faint fill and outline, yours last and brightest, with anti-aliased fill off for the floors (seams).
  **Turn with me** turns it by your heading, with a compass needle; **Follow me**, dragging turning it off, as the maps.
  The window's Only when needed shows it only indoors. Inside a building it maps every building's rooms in the landblock.
  **In the Map window** (`SettingsData.MapEmbedDungeon`, off to start, the Map window's Dungeon map checkbox, which
  shows only while it's one of the shared window's, as does its **Pop out** button, `FloatingWindow.PopOut`, the
  shared window's Separate, then made square in the middle of the screen (as big as its longer side, at most 60% of
  the screen's shorter side) and brought to the front, `SharedWindow.PopOut`: left exactly where it was, the first build's looked as if nothing had happened; both
  wrap onto a line of their own, `Form.Flow`, the first build having run them off the
  window's right edge, unseen; the user's call, November 2026): indoors, the Map window draws the dungeon map at its top
  (`DungeonWindow.RenderEmbedded`, the same drawing, in a child; `WorldTab.RenderEmbeddedDungeon`), a divider to drag
  (`Panes`, `map-dungeon`) and the world map under it; the Dungeon window is hidden meanwhile
  (`FloatingWindow.Suppressed`, while the Map window shares and shows). Untested in the game when written.
  **Map opacity** (`SettingsData.DungeonMapOpacity`, under the window's settings beside Background) fades the map's own
  dark ground, floors and walls (`DungeonScene.Opacity`); markers, you and the route stay solid.
  **Rooms you've been in** are grey (`DungeonVisits`, in settings.json as `dungeonVisits`: by character name, then
  landblock as four hex digits, then cell numbers), noted from every frame's `Landcell` whether the window's open or not,
  in a colour of your own under Show... (`DungeonVisitedColor`; ramps lean 30% towards the ramp colour, lower floors
  darker).
  **Markers** (`DungeonMarker`, `DungeonView.MarkerKinds`, each kind shown or hidden by name in `DungeonMarkers`) are
  Decal's `WorldFilter.GetLandscape()`, four times a second, by `ObjectClass` (your corpse by its name, "Corpse of" you):
  where each is is `RawCoordinates()`, landblock meters like `LocationX/Y/Z`, and its cell `LongValueKey.Landblock`, or
  the floor under it (`DungeonLayout.CellAt`) when Decal doesn't say. Only those on your floor, in your room or one
  you've been in, are kept (`DungeonMarker.MayShow`): the server sends what's in every room in sight of yours, through
  walls and floors, and the map is meant to show what you'd have seen, not be a radar (the user's call, October 2026).
  Way out and the route only use those too. Anything truly secret needs the server's own hiding, `PropertyBool.Visibility`
  with `ResistPerception` and a `Tier`: such an object is sent only to a player who has passed a Perception roll for it
  (`WorldObject_Awareness.cs` in daralet-ACE), so no plugin sees it before then. The first five of each dungeon go in
  log.txt. Clicking one selects it. **The way** (`DungeonRoute`, Dijkstra over the rooms, each entered at the doorway it was reached
  by) goes room to room through the doorways, which are each `EnvCell`'s `CellPortals` (the portal polygon's middle, at
  its foot; `OtherCellId` 0xFFFF leads outside and is left out). Way out is the nearest portal by the route: the client
  can't tell which portal leads out. Right-click for the way to a marker (followed as it moves) or a spot. It's worked
  out again in each new room, or twice a second. While the game runs, a dat it has open can't be opened by anyone else
  (a sharing violation), which is likely what leaves UtilityBelt without a dat in ShadowLauncher's folders: the
  plugin's own try then fails the same way, and says so.
  UtilityBelt's own dungeon maps paste one of 614 hand-drawn 10-meter tiles per piece instead, so pieces without a tile
  (Daralet's own) and cells off its grid go missing; this draws whatever the dats have.
- **Floating windows** (`Ui/FloatingWindow.cs`): small auto-sized UB huds that stay on screen (fellowship vitals, target,
  tracked target). Locked means no dragging by the top edge and no sizing, and with Ctrl held
  any window, locked or not, is dragged from anywhere on it
  (`FloatingWindow.CtrlDrag`, by hand: ImGui only moves a window from its empty background, and a hotbar is all slots),
  its contents disabled meanwhile (`BeginDisabled`, `DisabledAlpha` 1 so nothing greys out) so a slot isn't used on the
  press; a window held to the toolbar (`Anchor`) isn't moved. A window the player sizes with `CtrlResize` set (the Vendor
  window) can be resized by its edges too while Ctrl is held: NoResize is lifted then, and an edge taken hold of
  (ImGui's grips being the only active items while the contents are disabled, `TrackCtrlSizing`) goes on resizing till
  the button's let go, and isn't also moved. `CtrlResize` is on for every window now (the user's call, November 2026:
  nearly every window adjustable by its edges), not only the Vendor's. **A window that sizes itself** (no
  defaultSize: the target, fellowship, hotbars, toolbar, quest tracker and the like) is sized by its edges too
  (`FloatingWindow.EdgeScale.cs`): dragging its left, right or bottom edge or a bottom corner changes its Size, the
  scale its contents are drawn at, by as much as the edge has gone (snapped as a resized edge is), the top edge still
  being the move; unlocked as it is, locked with Ctrl held; the strip is the window's padding (at least 3 pixels), so
  a hotbar's slots keep their clicks; worked by hand like SideGrips, which a window with its own (the bars) has
  instead. Untested in the game when written. Ctrl+click in a locked Corpse grid moves it instead of
  ticking. While Ctrl is held each such window gets a faint wash and frame in the theme's accent
  (`DrawCtrlMoveHighlight`), brighter on the one being dragged. Right-click one to lock, unlock or hide it. Two things decide
  whether one is drawn, worked out every frame (`Core_RenderFrame`): `Visible`, the saved setting its tab's checkbox and
  Hide change, and `inUse`, what it's showing now (null for nothing). A window with **Only when needed**
  (`WindowPlacement.OnlyInUse`) is drawn only while in use; the Corpse and Bank windows always are (`alwaysOnlyInUse`).
  Those two also have `onClose`: their close button (X), Close in the right-click menu (a locked window has no X) and the Bank window's Close bank close
  the corpse or bank (a Use of it), and the window stays away until it shows something else (`closedFor`), without touching
  the setting. Nothing sets `Visible` behind the player's back any more, so Hide holds; Track and a new destination still
  switch their windows on, as asked for. **Background** (`WindowPlacement.Background`, null for the old 0.55 locked / 0.85
  unlocked) fades each window's box; at 0% it's `NoBackground`, and the style's `WindowBorderSize` is zeroed for that
  window's Begin and put back in `Hud_OnRender` (or the next `Core_RenderFrame`), since UB begins the window between the two.
  **No title bars** (the user's call, November 2026; `Ui/FloatingWindow.Edge.cs`): locked or not, a window has none
  (`NoTitleBar | NoMove` always). Unlocked, it's moved by its top edge, as the game's panels are (`EdgeDrag`): a band
  from 4 pixels above the window (ImGui's own top-edge resize reaches that far) to 6 inside, wherever no item is under
  the mouse, takes hold on a press and moves `Look`, snapped, like a Ctrl drag (`RememberPosition` skips while it's on:
  it runs after, and saving the window's old place undid the move every frame, the first build); ImGui's resizing is off while the mouse
  is on it or a drag is on (`edgeHover`, `edgeGrab`), or that edge would resize the window instead. Its other edges and
  corners still size it. The mouse shows the game's move cursor there (`WantsMoveCursor`, through `CursorKeeper`). So
  locking no longer moves anything; before, it took the title bar away and moved the window down by its height (and
  shrank one the player sizes by it), so what was in it stayed put. Settings from before (`WindowPlacement.NoTitleBar`
  false) are moved once the same way for an unlocked window (`UpgradeFromTitleBar`: down by the height ImGui gave the
  title bar, a player-sized one that much shorter). With no title bar there's no close button: the right-click menu's
  Hide and Close do that, but a window with `CloseButton` set (the appraisal windows) has the game's gold X in its top
  right corner (`FloatingWindow.Close.cs`). Untested in the game when written. **Snapping** (`Ui/FloatingWindow.Snap.cs`,
  `Feed/WindowSnap.cs`, the Config tab's Window snapping: `SettingsData.SnapWindows`, on to start, `SnapDistance` 10,
  `SnapGrid` off, `SnapGridSize` 20, `SnapGridShown`): a window being dragged snaps to the screen's edges, to the edges
  of the plugin's other windows showing (each notes its box every frame it's drawn, `NoteBox`; an edge only lines up
  with a window beside it the other way), and with the grid on to the grid where no edge is near; Shift places it
  freely. ImGui moves an unlocked window itself, so one is taken to be dragged while the button's down, it's focused and
  it moved without changing size (`SnapWhileDragged`), and its snapped place is put on it (`SetWindowPos`) before
  `RememberPosition` saves it; ImGui works the place out from the mouse every frame, so nothing builds up. A locked
  one's Ctrl drag snaps in `CtrlDrag`. **Resizing snaps too** (`SettingsData.SnapResize`, on to start;
  `FloatingWindow.SnapSize`, `WindowSnap.SnapEdge`): a window the player sizes has the edges it's being resized by
  snapped to the screen's, other windows' beside it and the grid, as it's dragged, through ImGui's size callback
  (`FloatingWindow.SetSizeConstraints`, cimgui's `igSetNextWindowSizeConstraints` called directly with a cdecl
  delegate kept in a field: see the gotcha on ImGui.NET's callbacks): ImGui works a
  resize out from the mouse inside Begin, every frame, and asks the callback before it uses it. The edge moving is the
  one nearer the mouse (ImGui keeps the other put, moving the window for a left or top edge). The first build snapped
  after Begin, which ImGui undid the next frame, so it only showed once let go. The grid is drawn faintly on the
  background list while one's dragged or resized. **Guide lines** (the user's call, November 2026; `WindowSnap.Guides`):
  with the grid on, the screen's middle (3 pixels) and its thirds and quarters (2) are drawn thicker over it, a
  window's middle snaps to them as it's moved and the edge being dragged as it's resized, as near as any edge, and the
  guide the middle (or that edge) is on is drawn lit, in gold (`DrawGrid`'s `litX`, `litY`, `WindowSnap.OnGuide`): the
  grid is drawn once the snapped place is known (`ShowGrid`). **Snap resizing to the grid** (`SnapResizeGrid`, on to
  start, the user's call, November 2026) is a setting of its own: resizing snaps to the grid and its guide lines with
  it on, whether or not moving does (`ResizeGridSpacing`, where moving has `GridSpacing` and Snap to a grid), and so
  do the width grips (`GamePanel.WidthGrip`, the XP bar's end), which change a window that sizes itself. ImGui asks
  the size callback of windows that size themselves too, whose wanted size can differ from their own on any click,
  so with that on by default the grid showed for every press: `SnapSize` now only acts for a window the player sizes
  and a press begun on its edge (`TrackResizePress`: 4 pixels outside to half a line inside, more at the corners).
  Untested in the game when written. A window doesn't snap to those held to it (`Follows`), or its edge
  against theirs held it in place and it moved in jumps. The main /dap window isn't a FloatingWindow and doesn't snap. Untested in the game when written. `RememberPosition` skips a frame
  whose new position is still on its way (`positioned` false), or it would save the old one back. **Only a drag moves a
  window**: every frame the left button isn't down, `SetNextPlacement` puts it where it was saved (`ImGuiCond.Always`),
  and `RememberPosition` only saves while the button is down. ImGui pulls a window back inside a screen smaller than where
  it is, which the game's is for a moment around login: the hotbars came back in the middle of the screen every login,
  and that place was saved over theirs.
  These windows have `NoFocusOnAppearing`; `BringToFront` focuses one once (Appraisal uses it, so a new appraisal isn't
  hidden behind the main window). `menuItems` adds to a window's right-click menu, which never opens over an item, so whatever draws the item opens it
  (`FloatingWindow.OpenMenu`, at the window's own ID scope): the fellowship window's is the vitals and Pin of
  the fellow right-clicked (`SettingsData.FellowVitals`, by name, each vital null to follow the Stamina/Mana checkboxes).
  Vital and threat bars are never shorter than the font (`Widgets.Bar`), nor narrower than their numbers
  (`Widgets.VitalBarWidth`): a big font wrote each bar's numbers over the next. Clicking a threat row selects that
  creature or player. The Target and Tracked Target windows share their vitals' settings, as the fellowship window has
  them: Health, Stamina and Mana each shown or not, Side by side and Bar width (`TargetShowHealth`..., `TargetSideBySide`,
  `TargetBarWidth`, 220 to start), with spells and threat as wide as the row of bars; the Combat tab's Vitals shows all three.
  They're in both windows' right-click menus too (`TargetTab.RenderVitalsMenu`), which open from anywhere on the window
  (`OpenMenuOnRightClick`), since it's nearly all bars and the menu doesn't open over items.
  The **Map window** is the World tab's map in a `FloatingWindow` with its own `MapView` (its own pan,
  zoom and Follow me) and the tab's **Show...** (kinds of places, names, fellows, the names' Text size, and the Arrow
  and Dungeon windows, the last three added to the popup for it); the arrow window's **Details** box leaves the arrow
  alone.
- **The game's UI pictures** (`Resources/ui`, embedded as `DaraletPlayer.Resources.ui.<name>.png`, drawn by `ItemIcons.DrawArt`): taken from the client by hand, since they aren't item icons: the main pack (`main-pack`), the green behind each pack in the Retail column (`pack-background`, with the pack's icon on it without its usual tile), the ring over the slot an item is held over in a drag (`drop-ring`, 17x16, scaled to the slot's width; a frame over a list line), and the red crossed-out circle (`drop-invalid`, not the reticle's) where it can't go, letting go there doing nothing (`Accepted(allowed)`: a pack only in a pack slot, on a pack, the main pack or the bank's own slots, never among a pack's items; an item not in a full pack; a paperdoll square only for what its `EquipableSlots` include), the empty slot (`empty-slot`: a pack's room left in the grids, which show a pack as its every slot, `InventoryBrowser.Slots`, and the side pack slots left in the Retail column), the selection frame (`selected`: the selected item, as `Actions.CurrentSelection` says, in the grids, the paperdoll and the Retail pack column, a click in a list selecting it as the game's pack does), and the target reticles (`reticle`, `reticle-invalid`). The Retail chosen pack's arrow is still drawn with shapes: the game's own hasn't been found. The grids have no gap between squares, as the game's pack has none.
  **Retail bars** (`SettingsData.RetailVitalBars`, off to start, ticked in the fellowship window's or the Combat tab's Vitals'
  settings): `Widgets.Bar` draws any bar in a vital's colour (`Colors.Health`, `Stamina`, `Mana`) with the game's vitals
  panel art instead of ImGui's progress bar (`ItemIcons.DrawVitalBar`, `ItemIcons.Bars.cs`), its numbers outlined on
  top (bold, `SettingsData.BoldVitalText`, drawn twice a pixel apart: ImGui has no bold font; plain bars too, their
  numbers then drawn by `Widgets.Bar` in the middle). The panel is layout `0x2100006C`, its meters `0x100000E6` (health), `0x100000EC` (stamina) and `0x100000EE`
  (mana), each a dark empty layer (`0600747E`-`80`, `06007484`-`86`, `0600748A`-`8C`) under a coloured full one
  (`06007481`-`83`, `06007487`-`89`, `0600748D`-`8F`) cut off at the fraction: a 10x16 left cap, a 100x16 middle
  stretched (130 wide in the game) and a 10x16 right cap (`vital-<vital>-<empty|full>-<left|middle|right>.png`). Each
  meter also has an emblem (heart, sword, serpent: `06007490`-`95`) for a state the plugin doesn't use.
- **The installer** (`installer/DaraletToolkit.iss`, Inno Setup 6, built and released by `.github/workflows/release.yml` on a
  `v*` tag that must match the csproj's `<Version>`): copies `DaraletToolkit.dll` and writes Decal's entry itself, the values
  UtilityBelt's plugin template installer writes (`(Default)` name, `Enabled`, `Object`, `Assembly`, `Path`, and
  `Surrogate` = `{71A69713-6593-47EC-0002-0000000DECA1}`, Decal's .NET plugin host), as 32-bit `HKLM\SOFTWARE\Decal\...`
  (WOW6432Node). Its default folder is the `Path` of an entry already there, so it takes over a dll added by hand. It
  stops while `acclient.exe` or `DenAgent.exe` runs, warns (Yes goes on anyway) when .NET 4.8 or Decal 2.9.8 isn't found
  (Decal's version from `DenAgent.exe` in the `Agent` key's `AgentPath`), and runs `UtilityBelt.Service.Installer.exe`,
  taken from the UtilityBelt.Service NuGet package's `content/`, when Decal has no Services entry for its guid
  (`{8adc5729-db1a-4e28-9475-c4eafae1e6e7}`). Uninstalling removes the entry and offers to delete the data folder. Keep
  its `AppId`: it's how a new installer upgrades the old install. The build references Managed DirectX from `%WINDIR%`,
  which runners lack, so the workflow installs the DirectX June 2010 runtime first.
- **New versions** (`Feed/UpdateCheck.cs`): when the plugin starts it asks GitHub's API, once and off the game's thread,
  for `daralet-ac/daralet-toolkit`'s latest release (`releases/latest`, which never means a draft or a pre-release,
  so not `world-data`), and compares its tag with the assembly's version (both made four-part first: `Version` puts 0.5.0
  before 0.5.0.0). A newer one gets a line in chat once the character is logged in (`PluginCore.AnnounceUpdate`; a line
  added before login never shows), with a link to the release page, and an **Update** button on the window's top line that
  opens the same page. That line always shows the version you have, after the character's name, with the check's result
  as its tooltip (`PlayerHud.RenderVersion`). It only tells: the player runs the new installer. Unauthenticated, GitHub allows 60 requests an
  hour from one address. Chat links are UtilityBelt's kind: `<Tell:IIDString:id:openurl|url>text</Tell>` in
  `AddChatText`, whose click comes back as `ChatNameClicked` with that id (`PluginCore.ChatLinkId`, a fixed number).
  Anyone can put a link with that id in a tell, so a click only opens this plugin's release pages
  (`UpdateCheck.IsReleasePage`, which takes the repo's old name, `daralet-player-plugin`, too). The repo was renamed
  with the plugin, after v0.6.0: older plugins still ask the old address, which GitHub redirects (the API too), and
  their Update link falls back to their own `LatestPageUrl`, since a release page under the new name isn't one of theirs. No Decal plugin known (October 2026) downloads and installs its own updates; UtilityBelt's
  is a notice like this one, and the loader updating the plugin itself was left for later.
- **Everything the plugin writes** goes in `Documents\Decal Plugins\Daralet Toolkit` (`DataFolder`): `settings.json`,
  `profiles.json`, `Settings Profiles`, `Settings Exports`, `log.txt`, `Loot Profiles`, `Hotbar Layouts`, `Combat Logs`, `Chat Logs`, `world-data`. It was `Decal Plugins\Daralet` before the rename: `PluginCore.Startup` moves
  that folder over first thing (`DataFolder.MoveOldFolder`, `FolderMove.Once`, copying instead if a file's held open),
  unless the new one is already there. Only startup does it, so a test that asks `DataFolder` for a path doesn't move
  the developer's real folder. Only files a player drops in by hand (`dereth-map.png`, `locations.xml`) are looked for
  next to the dll (`PluginCore.AssemblyDirectory`).
- **Settings** (`Settings.cs`, what the file holds in `SettingsData.cs`, each window's in `WindowPlacement.cs`) live in
  `settings.json` there: window positions and visibility, and display options. A DataContract reader skips field initialisers, so new settings
  get their defaults in `SetDefaults`, which runs before a file is read as well. Saved two seconds after the last change.
  A renamed setting keeps its old `DataMember` name, or players lose it: the Inventory tab was the Bank tab, so
  `InventoryGrid`, `InventoryShowWorn` and `InventoryLootMarks` are still `bankGrid`, `bankShowWorn` and `bankLootMarks`
  in the file.
- **Text size** (`SettingsData.TextScale`, the Config tab's Text section): `SetWindowFontScale` on the main window and
  every `FloatingWindow` (times the window's own Size, `FloatingWindow.FontScale`). Popups and menus inherit their
  window's scale, but a tooltip is a window of its own drawn at the font's size, so tooltips open with
  `Widgets.BeginTooltip`, which scales them to the window they're shown from: never call `ImGui.BeginTooltip` or
  `SetTooltip` directly. Going through it is also what makes **Tooltips only while holding** a key work
  (`SettingsData.TooltipsNeedAlt`, on to start, its name from when the key was always Alt, and `TooltipKey`, a
  Windows virtual key code, Alt to start: the drop-down beside it has Alt, Ctrl and Shift, and Other key... takes the
  next key pressed, Alt, Ctrl and Shift on their own included (`ConfigTab.Modifiers`: `Hotkeys.Pressed` reads the
  hotbars' keys, which leave them out, so the first build ignored them); the Config tab's Text section; the user's call, November 2026): without the key held, read from
  Windows (`Hotkeys.IsDown`, through `Widgets.TooltipsHidden`, which `PlayerHud` sets), the tooltip is begun far off
  the screen (`SetNextWindowPos`), since every caller draws into it and ends it itself; the setting's own tooltips
  pass `always`. ImGui sizes a child window's text by its own scale times its direct parent's only, so a child
  inside a child (the Quests list's scrolling table in its `quests-list` child) drew at 100% whatever the Text size:
  child windows open with `Widgets.BeginChild`, and tables that scroll (ScrollX/ScrollY, a child of their own) with
  `Widgets.BeginTable`, which match the parent's font size; never call `ImGui.BeginChild` directly. Sizes picked in
  pixels grow with the text too: fixed table columns are `Widgets.Scaled(px)` (px at 100% text), and split panes
  (**Panes**, `Ui/Panes.cs`: Loot's rule list, the Inventory's pack list, the Hotbars page's bar list, Leaderboards'
  board list, and the Quests list above its details) are sized in lines of text, `SettingsData.PaneSizes` by name, set
  by dragging the divider between the two (double-click for the usual size). The slider only applies a size once let go, since it grows with the text under the mouse.
  Text drawn inside a square of a fixed size (stack counts, `ItemIcons.OutlinedText`; every hotbar slot label,
  `SlotPainter`) leaves the Text size out (`Widgets.SquareFontSize`, `SquareTextSize`, from `Widgets.TextScale`, which
  `PlayerHud` and `FloatingWindow` set as they apply it), or it spills out of the square; a window's own Size still counts.
  Item details have a size of their own on top (`SettingsData.ItemTextScale`, the Loot tab's Settings): item tooltips
  (`ItemTooltip.Scale`, passed to `Widgets.BeginTooltip`) and the Appraisal windows (`FloatingWindow`'s `textScale`).
- **Themes** (`Feed/UiThemes.cs`, `Feed/UiTheme.cs`, `Ui/UiThemeStyle.cs`, the Config tab's Theme section,
  `SettingsData.Theme`, null for Daralet, the default): ready-made looks for the plugin's windows. A theme is a few base colours
  (window, frame, title, accent, text; optionally solid `Button`, `Selection` and `Edge` colours of their own, as
  Daralet has, sampled from the game's keyboard and options panels) and a corner rounding; `UiTheme.Colors` works out every ImGuiCol from them, by
  name, so a colour an ImGui version renamed (`TabActive`/`TabSelected`) is set under whichever it has. The UtilityBelt
  theme (`Unstyled`) changes nothing: its own look. One ImGui serves every plugin, so `UiThemeStyle.Apply` changes the style in a hud's
  OnPreRender (Begin draws the background, title and border) and `Restore` puts it back at the end of its OnRender (so
  its popups and tooltips are themed too) and, for a window that began collapsed, on the next `Core_RenderFrame`. Not
  PushStyleColor: a push before Begin popped before End trips ImGui's stack check. All are dark with light text, since
  `Colors` is made for a dark window (a test checks). A new theme is a row in `UiThemes.All`.
- **Settings profiles** (`Settings.Profiles.cs`, `ProfileChoices.cs`, `Ui/ConfigTab.cs`, the **Config** tab, first):
  a profile is a whole `SettingsData`. Default is `settings.json` itself (so a plugin from before profiles still finds
  its settings), the rest `Settings Profiles\<name>.json`, named by file (`Settings.CleanName`), and which character uses
  which is `profiles.json` (`characters`, case-blind; one not there uses Default). The plugin starts on Default and
  swaps to the character's profile once logged in (`PluginCore.UseCharacterProfile`, also at a hot reload's start).
  A swap saves the old profile, replaces `Settings.Data` and raises `Settings.Swapped`: everything reads
  `settings.Data` afresh each frame, so only what keeps something from it listens: `FloatingWindow` (placed again from
  the new profile's placement, made hidden if it starts so), `Hotbars` (a window per bar, made again), `PlayerHud`
  (`InventoryBrowser.ForgetWornSizes`, `LootTab.SettingsSwapped`). Anything new that holds a piece of `Data` across
  frames must do the same. The rooms you've been in (`DungeonVisits`) aren't a setting: a swap adds them to the new
  profile's (`DungeonVisits.Merge`), and To file (`Settings.Export`, into `Settings Exports`) leaves them out. From
  file (`Settings.Import`) adds any settings file as a new profile named after the file.

`@playerui` only ever sends a player data about themselves, their fellowship and what they're fighting (for a monster
their client can see: its vitals, every buff and debuff on it, and its threat table). The one exception is `lfg`, whose lists
name the players and fellowships looking for group, with only what their own lines already told the LFG channel (the
user's call, October 2026). And `shared`: an item someone linked in chat, by their name and the item's, with their name, as they
chose to show it (the user's call, October 2026); `shareitem` only takes the player's own things (`PlayerUiCorpseData.
FindOwnItem`: carried, worn, or in the bank they have open). The server is the
only thing that can be trusted: never let the plugin's request decide what it's allowed to see. Anything that changes the
game (banking, listing, queueing) goes through the existing player commands, which check everything again; the plugin's own
checks (greying out market buttons away from a broker, say) are only there to be helpful.

## Conventions

- **License:** GPL-3.0 or later (`LICENSE`), with `NOTICE` adding a section 7 permission to combine it with Decal,
  UtilityBelt.Service, Managed DirectX and the client, and listing what isn't ours. Code copied in from elsewhere needs a
  GPL-compatible license (MIT, BSD, Apache 2.0, (A)GPL; daralet-ACE is AGPL-3.0, like ACE); anything else not written
  here goes on NOTICE's list. The installer installs `LICENSE`, `NOTICE` and UtilityBelt.Service's MIT notice.
- Braces on every `if`, even one-line bodies.
- One type per file. A big class is split into partial files by topic, named `Class.Topic.cs` (`InventoryBrowser.Retail.cs`,
  `LootTab.Corpse.cs`), so a file names what's in it; look for a class's other parts before adding to its main file.
  Fields go at the top of the class, and lines stay under about 125 characters: long tooltips are joined with `+`.
- Small shared pieces live in one place: `Widgets` (text, tips, `Button`, `OutlineIfHovered`, bars), `Colors`,
  `PluginCore.Chat` for a line in chat, `Words.Count` for "3 rules".
- **Sliders** are `Sliders.SliderInt` and `SliderFloat` (`Ui/Sliders.cs`), never ImGui's directly: ImGui's own slider
  (dragging, Ctrl+click or double-click to type) drawn see-through under the game's option slider art (track `06001285`,
  thumb `06001286`, `option-slider*.png`), the thumb where ImGui's grab is, the number outlined on top
  (`Feed/SliderText.cs` reads ImGui's printf formats), the label redrawn past the frame (the user's call, November
  2026). While a number is typed it's ImGui's plain box.
- **Drop-down menus** are `Combos.Begin` / `Combos.End` (`Ui/Combos.cs`), never `ImGui.BeginCombo` directly, and a row that's
  just a name is `Combos.Item`: the game's look (the user's call, November 2026), from the vendor window's kind menu
  (template `0x1000034B` in layout `0x21000043`: the 100x18 field `060012B3`, dark green in a grey bevel, cut into 4-pixel
  ends and a middle, `combo-left`...; the arrow `060012B1`, open `060012B2`, 17x19 at its right; rows `0x10000352`,
  `060012B3`, the picked one and the one under the mouse `060012B4`, `combo-row-selected-*`). ImGui still makes the combo:
  the art goes on the window's draw list before `BeginCombo` (once open, `GetWindowDrawList` is the popup's), under a
  see-through frame with `NoArrowButton`, the arrow over its right end after; the list's background, border and
  highlight are the art's colours, pushed till `End`. The open list is exactly as wide as the field
  (`SetNextWindowSizeConstraints` before `BeginCombo`, which takes them over and clears them when it doesn't open; as
  tall as ImGui's row count for the height flag, in rows of the field's height, `ListHeight`), its scroll bar inside
  rather than out past the arrow; the icon grids pass `listAsWide: false`, being laid out by their own widths. Rows that aren't a plain name (the icon grids, tick boxes) keep
  ImGui's own items on those colours. Plain ImGui before the art can be drawn. Untested in the game when written.
- **Scroll bars** are the game's (`Ui/Scrollbars.cs`, the user's call, November 2026): layout `0x2100003E`'s vertical
  `0x10000367` (16 wide: chain track `06004C5F`, thumb top/middle/bottom `06004C60`/`63`/`66`, arrows up `06004C6C` and
  down `06004C69`: the template's decrement button, property 0x77, is `0x10000072`, though `0x10000071` sits at 0,0,
  and the first build had them the wrong way up) and horizontal `0x1000036D` (16 high: chain `06004C7F`, thumb
  `06004C80`/`83`/`86`, arrows left `06004C8C`, its 0x77, and right `06004C89`), each part's next two pictures its
  hover (light blue) and pressed states
  (`scrollbar-*.png`, `scrollbar-h-*.png`). ImGui still makes the bar (its room, the wheel; `ScrollbarSize` 16 in
  the themed windows, `UiThemeStyle`), and the game's is drawn over it, opaque, at the box ImGui uses
  (`igGetWindowScrollbarRect`, and `igGetCurrentWindow`, `igGetActiveID`, `igClearActiveID`: ImGui's internal
  functions, which UtilityBelt's `cimgui.dll` exports and its ImGui.NET doesn't wrap, P/Invoked; the window's flags
  read at `+8` of ImGuiWindow, ImGui 1.89.7). The game's clicks are its own: the arrows step (repeating while held),
  the track pages, the thumb drags, along the travel between the arrows; ImGui's bar took the press as the window
  began, so its hold is let go and the scroll it jumped to replaced (a frame of the jump can show). Drawn from the end
  of each window that scrolls: `FloatingWindow`, the main window, `Widgets.EndChild` (every child begun with
  `Widgets.BeginChild` ends there, never `ImGui.EndChild`), `Widgets.EndScrollingTable` (a table with ScrollY) and
  `Combos.End` (a drop-down's long list, drawn before `EndCombo` while its popup is the current window);
  a scrolling area ended any other way keeps ImGui's own bar. A horizontal bar only where a window asks for one
  (`HorizontalScrollbar`). **Only a bar ImGui really made is looked up**: `igGetWindowScrollbarRect` asserts that it's
  there ("scrollbar_size > 0.0f"), and the first build, going by room to scroll, stopped the game at login (a window
  that sizes itself has room and no bar). So the bar's size is read from the content region (`ShownBars`: ImGui takes
  it off the region's right or bottom, past the padding the region's left edge shows, the scroll added back), and a
  horizontal bar of less than three quarters of `ScrollbarSize` counts as none, the bottom padding being a guess; a
  vertical one of 4 pixels or more counts, the sides being padded alike (a drop-down's list, padded by the frame's
  padding, was missed at three quarters, `[combo]` in log.txt giving its sizes once). A child's bar is cut to where its parent could
  draw as the child began (`Widgets.BeginChild` and a scrolling `BeginTable` note it, `EndChild` and
  `EndScrollingTable` hand it over), as ImGui's own is: drawn with a clip of its own, one out past the window showed.
  A window that scrolls but gets no game bar (none found, too small, or clipped away) is logged once with its sizes
  (`[scrollbars]` in log.txt, `Scrollbars.LogSkip`): the Spellbook's list was reported with ImGui's bar though its
  child ends in `Widgets.EndChild`, and why wasn't found from the code. Untested in the game when written.
- **Settings pages** are built like the Hotbars page, from `Form` (`Ui/Form.cs`): a `Section` heading per group, forms
  of labels and lined-up controls (`BeginForm`, `Label`, `ControlWidth`), and rows of checkboxes and buttons that wrap
  (`Flow`, `FlowCheck`, `FlowSmallButton`). A tab whose content comes first keeps its settings folded under a
  **Settings** header (`Form.SettingsHeader`, closed to start), with a section per floating window
  (`FloatingWindow.RenderControls`: Enabled, Only when needed, Locked, Reset position, then Size and Background). A popup
  opened from inside a form must be drawn inside it too: a table adds to the ID stack. **What's under an open
  header** (every `ImGui.CollapsingHeader`, Settings included) is set in, with a line in the header colour down its
  left side to where it ends (`Form.Folded()`, `Ui/Fold.cs`, the user's call, November 2026), so it's plain what's
  open: call it as `using var fold = Form.Folded();` straight after a header that said it's open, so the rest of the
  block or method is the section. The line comes down from the point of the header's arrow (the header's item box,
  plus the frame padding and half the font size, where ImGui draws it), the contents start half a font size past it,
  and they're in a group: ImGui's separators run from the window's left edge, across the line, except in a group of
  the window's, where they start at the indent. Tree nodes (Custom colours, loot folders, the tracker's quests) indent
  as ImGui's own.
  Sections go two abreast with `Form.Columns` (the World, Target and Inventory tabs' Settings) when the tab is wide enough, one under another
  when not; `Flow` and `ControlWidth` keep to the column they're in.
- Code comments explain why, in plain words; tooltips (`Widgets.Tip`) on every control say what it does and which command it
  sends.
- The user commits and pushes with GitHub Desktop: leave changes staged (`git add`), don't commit. New branches with
  `git switch -c <name> --no-track origin/main`.
