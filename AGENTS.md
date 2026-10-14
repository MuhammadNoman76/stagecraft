# Stagecraft: notes for coding agents

Shared project memory for AI coding assistants (Codex, Claude Code, Gemini CLI, …). `CLAUDE.md` and `GEMINI.md` load this file. Keep it up to date when something important changes.

## What this is
Stagecraft is a free Windows presentation app ("like PowerPoint, but simpler and better-looking"), built in C#/WPF.
- Owner: Muhammad Noman (https://github.com/MuhammadNoman76). Repository: https://github.com/MuhammadNoman76/stagecraft (ask before pushing).
- Tagline: "Presentations that look designed. Without the effort."
- Look: Windows 11 Fluent (Mica on 22H2+, light / dark and the accent colour, Segoe UI Variable, vector Fluent System Icons so Windows 7 works too). No ribbon: a slim command bar, the slide rail on the left, the canvas, and a contextual properties panel on the right.
- Its own file format `.stagecraft`; imports and exports PowerPoint (.pptx), exports PDF and PNG / JPG.

## Layout
- `src/Stagecraft.Core/`: the engine (WPF drawing, no windows; the tests use it directly).
  - `Model/`: the deck model (`Deck`, `Slide`, `Element` and its kinds: text, shape (incl. action buttons), image, chart, table, icon, video, audio, online video; `RichText`, `Fill`, `Stroke`, `Shadow`, `Transition`, `DeckTheme`, `HeaderFooter`, `GuideLine`). `Animation.cs`: `AnimationEffect` (per slide, in `Slide.Animations`, in play order) and `EffectCatalog`. `Links.cs`: `Hyperlink` + `LinkSafety` (only http/https/mailto URIs; blocked executable extensions; file paths relative to the deck). `Media.cs`: `MediaClipElement` (video/audio), `MediaTypes`, `MediaLimits`. Colours are references (`accent1`…`accent5`, `text`, `text2`, `bg`, `bg2`, `#RRGGBB`, `#RRGGBB@50` for 50 % opacity) resolved per slide by `Rendering/SlideStyle`.
  - `IO/`: `DeckFile` (the .stagecraft ZIP: `mimetype`, `deck.json` with `schemaVersion`, `media/` named by SHA-256 (videos and sounds stored uncompressed and unpacked to the media cache on load), `thumbs/`; atomic save with a read-back check of every entry; linked media and file links rebased relative to the deck's folder on save; `Migrations` (v1 → v2 turns builds into animations); repair of damaged values), `MediaDuration` (MP4 / WAV header lengths), `DeckJson`, `DocumentLoader` (.stagecraft or .pptx), `RecoveryStore` (crash-recovery copies).
  - `Themes/ThemeCatalog.cs`: the 10 designer themes and their colour variants. `Layouts/LayoutEngine.cs`: the 16 smart layouts (placeholders are tagged; decorations have `Placeholder = "decor"`; `Reflow` re-lays a slide after a theme / size change and keeps text and user-moved items).
  - `Rendering/`: one renderer for everything (editor, thumbnails, show, PDF, pictures): `SlideRenderer`, `ElementRenderer`, `TextLayout` (FormattedText per paragraph, auto-fit by binary search, balanced wrapping for titles), `ChartRenderer`, `TableRenderer`, `DecorPainter` (theme backgrounds and decorations), `FontCatalog` (bundled fonts loaded privately from `<app>\Fonts`), `MediaStore` (pictures in memory; videos and sounds as files in `MediaCache`: `%TEMP%\Stagecraft-media\<pid>-<id>`, deleted at exit, folders of Stagecraft processes that are gone cleaned at start), `MediaProbe` (opens media with WPF's MediaPlayer for length, size and poster frames), `ShadowRenderer`, `LinkRegions` (clickable areas for the show, the editor and PDF), `IconCatalog`.
  - `Editing/`: `DeckSession` + `UndoStack` (snapshot undo: slide edits merge by key within 2 s; `EditStructure` also covers the footer and guides; `MarkSaved(path, top)` marks an automatic save's snapshot), `DeckOperations` (`RenewIds` keeps animation targets, triggers and connectors when slides are copied), `AnimationOps` (adding, reordering, painter, media start, group / ungroup keep effects), `FindReplace`, `Snapper` (smart guides: edges, centres, slide, margins, equal spacing; user guides; grid), `ScreenFit` (window placement maths), `Tidy`.
  - `Animation/`: `Easing`, `Morph` (planner: identity → name → content → placeholder; interpolator; `MorphFrame`, which can hide elements that animations keep hidden), `Timeline` (`SlideTimeline`: click groups, the automatic group, trigger sequences, paragraph clicks, text staggering), `Animator` (state of every element / text piece at a time), `AnimGeometry` (transforms and wipe / split / wheel clips), `PathMath` (motion paths), `AnimationRules` (targets, pruning, repair, copying).
  - `Export/`: `PdfExporter` (PDFsharp; walks the WPF drawing: vectors, embedded pictures, text as outlines plus an invisible Inter text layer so PDFs are searchable; rasterizes only soft effects), `ImageExporter`, `PptxExporter` (raw PresentationML written into Open XML SDK parts; links, media parts, footer text boxes) + `PptxTiming` (p:timing from the timeline; the mapping of effects is in `PptxTimingWriter.Mapping`). PDF link annotations in `PdfExporter.AddLinks`. `Import/PptxImporter.cs` + `PptxImporter.Animations.cs` (links, media, timing; counts what it changed or dropped for the notice after importing).
  - `Samples/`: the three demo decks and their procedural artwork (no stock photos). `Assets/SampleMedia/` holds their short video, poster and music, generated with ffmpeg's built-in generators by `tools\make-sample-media.ps1` (embedded resources).
  - `Fonts/`: the bundled SIL OFL fonts + their OFL.txt + SOURCE.txt (hashes, URLs). `tools\get-fonts.ps1` refreshes them from Google Fonts / GitHub and verifies each file.
  - `Assets/IconCatalog.txt`: the 1,112 slide icons (Fluent UI System Icons, MIT), generated by `tools\make-icons.mjs`.
- `src/Stagecraft/`: the WPF app.
  - `Program.cs` (single instance hand-over), `App.xaml.cs` (routes the command line).
  - `Services/`: `AppSettings` (JSON, mutex; in test mode changes stay in memory), `CommandLine`, `InstanceManager`, `Recovery` (wraps RecoveryStore), `Updater` + `UpdateService`, `CaptureRunner` (hidden `--capture`), `LinkLauncher` (opens links after the safety checks; file links ask first), `OnlineVideos` (oEmbed picture and title, optional), `Log`.
  - `Views/`: `MainWindow` (start ↔ editor), `StartView`, `EditorView` (+ partial files: Commands, Slides, Elements, Text, Clipboard, Files (incl. automatic save), Views (incl. rulers / grid menu), Show, Animations, Links, Media, Find), `Editor/` (SlideCanvas + partials: Input, Text, Animation (motion path editing, drawing), Crop, Media (hover playback bar), Find (highlights), ViewAids (rulers, guides); SelectionOverlay, SlideRail, PropertiesPanel (+ Element, Slide, Animations, Media), AnimationGallery, FindBar, TextContextMenu (spelling), CommandPalette, SorterView, OutlineView), `Show/` (SlideShowController, ShowSurface, ShowParts (per-element layers), MediaHosts, MediaControlBar, AudienceWindow, PresenterWindow, SlideShow), dialogs (LinkDialog, TrimDialog, HeaderFooterDialog, OnlineVideoDialog, …).
  - `Themes/`: `ThemeManager`, `Theme.xaml`, `Editor.xaml`, `Icons.xaml` (generated by `tools\make-icons.mjs`). `Assets/Logo.xaml` is the vector logo; `tools\make-icon.ps1` renders `Stagecraft.ico`, `StagecraftDeck.ico` and `docs\icon.png`.
- `src/Shared/`: `Compat.cs`, `LegacyPolyfills.cs`, `MathEx.cs`, compiled into both assemblies.
- `tests/Stagecraft.Tests/`: xUnit tests of the engine on both targets (WPF code runs on an STA thread via `Sta.Run`).
- `installer/Stagecraft.iss`: one Inno Setup script for every edition (`/DEdition=`, `/DAppVersion=`). UTF-8 with BOM.
- `build.ps1`: tests, builds every edition, writes `dist/` and `SHA256SUMS.txt`, copies them to `release/`.
- `publish/` and `dist/` are build output (git-ignored).

## Units and formats
- A 16:9 slide is 1920 × 1080 units; 1 unit = 1/144 inch = 6350 EMU = 0.5 pt. 4:3 is 1440 × 1080. Theme sizes are for a 1080-unit-high slide (`SlideStyle.Scale` = height / 1080).
- `deck.json` uses camelCase, enums as camelCase words, `"type"` as the element discriminator, nulls left out. Bump `Deck.CurrentSchemaVersion` and add a step to `Migrations.Upgrade` for any breaking change; files with a newer version are refused with a friendly message. Unknown element kinds are dropped with a warning instead of failing the whole file.
- Media are stored once, named by the SHA-256 of their bytes (first 10 bytes, hex); only media that are used are saved. `Deck.CurrentSchemaVersion` is 2 (1.1): slides have `animations`, elements and runs `link`, elements `shadow`, the deck `footer` and `guides`; v1 `build` fields become animations in `Migrations`.

## Editions (one codebase)
| Edition | Target | Installer |
|---|---|---|
| x64 | net10.0-windows, win-x64, self-contained | `Stagecraft-Setup-<ver>-x64.exe` (Windows 10/11) |
| arm64 | net10.0-windows, win-arm64, self-contained | `-arm64.exe` |
| x86 | net10.0-windows, win-x86, self-contained | `-x86.exe` (32-bit Windows 10) |
| win7-8 | net48 AnyCPU | `-win7-8.exe` (Windows 7 SP1 / 8.1; needs .NET Framework 4.8) |

- The edition is embedded at build time (AssemblyMetadata "Edition", `-p:StagecraftEdition=`) and the updater uses it.
- Keep the same Inno AppId `{B522CE93-BE8D-4A68-A206-8B1ED3ED7489}` for every edition (the updater's uninstall-key lookup uses it).
- Each installer must stay under 100 MB (GitHub's file limit).

## Compatibility rules (don't break)
- The code must compile for both net10.0-windows and net48 with 0 warnings. Use `Compat`, `MathEx.Clamp`, the polyfills and `FolderPicker` / `OpenFolderDialog` (net48).
- No `Math.Clamp`, `Environment.ProcessPath` or `File.Move(…, true)` directly.
- 32-bit: never import `GetWindowLongPtrW`/`SetWindowLongPtrW` directly; use the `IntPtr.Size` wrappers in `Interop/Native.cs`.
- Windows 7/8.1: Mica, dark title bars, rounded corners and per-monitor DPI are guarded (`Native.OsVersion`, `EntryPointNotFoundException`).
- WPF groups optical-size font statics under their Win32 family names ("DM Sans 14pt", "Source Serif 4 14pt"): `FontCatalog` maps the typographic names; check new fonts with the `BundledFonts_AreFound_ByTheirFamilyNames` test.

## Slide show
- `SlideShow.Start` picks the screens: audience full screen on the second display and the presenter view on the main one; with one display, the audience view only (Alt+F5 = presenter view on this screen).
- **Testing: never run a real full-screen show on the owner's PC.** Use `Stagecraft.exe --present-windowed <deck>` (or `editor.Present(windowed: true)`): both views open as ordinary small windows.
- Keys: → ↓ Space PageDown Enter N = next; ← ↑ PageUp Backspace P = previous; number + Enter jumps; B / . black, W / , white; Ctrl+P pen, Ctrl+I highlighter, Ctrl+L laser, Ctrl+E eraser, Ctrl+A arrow, E clears ink; Esc ends.
- "Next" plays the slide's next click group before moving on (`SlideShowController.Step` / `StepCount` come from `SlideTimeline`); "previous" steps back (later effects snap to their end state, media started later stop). Each element is its own layer (`ShowParts`): cached as a bitmap while it moves, text pieces get their own layers for by-paragraph / word / letter effects.
- Sound: only the audience surface plays it; the presenter view is muted unless it is the only window (presenter view on this screen). `MediaPlayback.ForceMute` mutes everything in test mode (`STAGECRAFT_TEST_OFFSCREEN=1` or `STAGECRAFT_MUTE=1`). Sounds set to play across slides are handed from slide to slide (`MediaPlayback.Background`).
- Links in the show go through `SlideShowController.LinkRequested` → `LinkLauncher` (suppressed in test mode).

## Animations, links and media (1.1)
- The animation list is per slide and ordered; `SlideTimeline.Build` turns it into click groups (an automatic group 0 when the first effects aren't "On click"), trigger sequences per element, and per-paragraph items. The editor, the show, the pane's timeline and the .pptx export all use it, so keep the timing rules there.
- Targets are element ids or group ids (a group animates as one). Deleting elements prunes their effects; ungrouping / merging groups moves a group's effects to its members (`AnimationOps.Group` / `Ungroup`); copies (duplicate, copy / paste, duplicate slide, paste slides) remap ids with `AnimationRules.CopyFor` / `DeckOperations.RenewIds`.
- Motion paths are stored relative to the element's starting position (slide units, first point 0,0); smooth paths are Catmull-Rom curves flattened by `PathMath`. PowerPoint paths are fractions of the slide.
- Media: `MediaStore.AddFile` hashes and copies a file into the media cache without loading it into memory; `DeckFile.Save` streams it into the ZIP (stored). The clipboard carries the cache file's path for videos and sounds (base64 only for pictures). Linked media: `LinkPath` (relative to the deck's folder, rebased on Save) + `LinkAbsolute`.
- Video plays through WPF `MediaElement` / `MediaPlayer` (Media Foundation): no codecs are bundled. With `LoadedBehavior = Manual` a file only opens after `Play()` or `Pause()`. Turn `ScrubbingEnabled` on only for video: with it on, sound-only MP4 / M4A files never open (no MediaOpened, no MediaFailed).
- PowerPoint: preset ids were checked against PowerPoint itself (entrance: Appear 1, Fly 2, Fade 10, Split 16, Swivel 19, Wheel 21, Wipe 22, Zoom 23 / Faded zoom 53, Bounce 26, Float 30 / 42 / 47, Grow & turn 31, Rise up 37; emphasis: Grow / shrink 6, Spin 8, Transparency 9, Bold flash 10, Pulse 26, Teeter 32, Wave 34; media: play 1, pause 2, stop 3). Only the effects listed as "the same effect" in the README are written with their own preset; their behaviours must match what PowerPoint expects for that preset. p14:trim `end` is the amount cut from the end, not the end time.
- `p:timing` comes after `p:transition` in a slide; `a:hlinkClick` goes inside `p:cNvPr` (first child) and inside `a:rPr` after the fonts. Slide jumps use `ppaction://hlinkshowjump?jump=…` with an empty `r:id`, or `ppaction://hlinksldjump` with a relationship to the target slide part, so every slide part is created before any slide is written.
- Spell checking uses WPF's `SpellCheck` on the slide text editor and the notes box with the keyboard's language; the right-click menu (`TextContextMenu`) adds the suggestions.
- Automatic save (`EditorView.AutoSaveFileTick`): only decks that have a file, 4 s after the last change, not while typing / dragging / cropping or with a dialog open; the deck is cloned on the UI thread and written on a background thread with `DeckFile.Save`; the snapshot's undo step is what's marked saved, and a manual save waits for a running automatic save.

## Build and release
1. Bump `<Version>` in `src/Stagecraft/Stagecraft.csproj` and `src/Stagecraft.Core/Stagecraft.Core.csproj` (and the defaults in `build.ps1` and `installer/Stagecraft.iss`).
2. Build: `powershell -ExecutionPolicy Bypass -File build.ps1 -Version X.Y.Z` (runs the tests, then all editions). The .NET SDK is at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`; Inno Setup 6 is at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`.
3. `release/` gets the new installers and `SHA256SUMS.txt`; update the README download table (sizes).
4. Commit and push this repo first, so the download links work (ask the owner before pushing).
5. In the website repo (`D:\Work\nomanshafiq-portfolio`), add `public/updates/stagecraft.json` (same shape as vibeplayer.json: `app: "Stagecraft"`, version, released, notes, page, `editions` x64 / arm64 / x86 / win7-8 with url, sha256, size) and a download page at `/stagecraft/`; run its checks, commit and push.

## Automatic updates
- Feed: `https://www.nomanshafiq.com/updates/stagecraft.json` (doesn't exist yet: checks fail silently and are only logged). `STAGECRAFT_UPDATE_URL` overrides it for testing.
- First check 10 s after start-up, then at most once every 24 h; Settings › Check now always answers; "Check for updates automatically" turns it off.
- Update now downloads this edition's installer to `%TEMP%\Stagecraft-Update`, checks size and SHA-256, runs it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1 /CURRENTUSER|/ALLUSERS /DIR=…` and exits; the installer removes the old program files (`[InstallDelete]`, only in a folder named Stagecraft holding Stagecraft.exe) and starts `Stagecraft.exe --updated`. Leftover installers are deleted on the next start.

## Command line
`Stagecraft.exe [file.stagecraft | file.pptx]…`, `--present <file>`, `--present-windowed <file>` (test mode), `--slide N`, `--export-pdf <file> <out.pdf>` (no window), `--settings`, `--solo` (don't hand over to a running Stagecraft), hidden `--capture <folder>` (screenshots of Stagecraft's own windows, off screen; `STAGECRAFT_CAPTURE_ONLY=start,editor,smoke,theme,ui,file,v11` and `STAGECRAFT_CAPTURE_FILE=<deck>`; `smoke` runs almost every editor command, then undoes and redoes them all, and writes `smoke.txt`; `v11` captures the animation pane and gallery, motion paths, links, video, trim, header and footer, find and replace, crop and rulers). Popups would be placed on screen by WPF, so captures show galleries inside the window instead (`AnimationGallery.Intercept`).

## Testing on the owner's PC (be careful)
- The owner uses VibePlayer, PawSip and Trove daily: never close or touch them, and don't change their file associations.
- Never open windows over a full-screen app (check `SHQueryUserNotificationState`; `--capture` waits by itself). Never run a full-screen slide show (see above).
- Don't send keystrokes to "whatever is in front" (SendKeys / keybd_event / SendInput): post messages only to windows of the Stagecraft process (check the PID) or use UI Automation patterns. Capture screenshots with PrintWindow on Stagecraft's own windows, never screen regions (notifications end up in screen captures).
- Back up `%APPDATA%\Stagecraft` (and `%LOCALAPPDATA%\Stagecraft` if it exists) before test runs and restore it afterwards, checking the hashes (if it didn't exist, delete it). In test mode (`STAGECRAFT_TEST_OFFSCREEN=1`) and in `--capture` runs, settings changes stay in memory, no recovery copies are written and the logs go to `%TEMP%\Stagecraft-test-logs`, but still back up and check.
- Check whether the owner's Stagecraft is running first: a test copy could hand files to it through the single-instance pipe (`--solo`, `--capture` and `--present-windowed` never hand over; `STAGECRAFT_TEST_OFFSCREEN` alone does not prevent it).
- Never play test audio out loud: test mode mutes all media.
- Installer tests: install per-user into a scratch folder (`/CURRENTUSER /DIR=… /TASKS="fileassoc"`), check the HKCU keys, run the association's command line directly with `STAGECRAFT_TEST_OFFSCREEN=1` (windows open off screen and never take the focus; the opened file is logged in `%TEMP%\Stagecraft-test-logs\info.log`), close it with WM_CLOSE to that process's own window, then uninstall silently and check that files, keys and shortcuts are gone.
- PowerPoint is installed on this PC and is useful to check exports (open a .pptx through COM with `WithWindow = false` and read the effects, media and links back). Only when POWERPNT isn't running and nothing is full screen, and quit it only if no other presentation is open.
- The engine tests write only under `%TEMP%\stagecraft-tests-*` (media cache included) and delete it.

## Git and writing rules
- Commit as `Muhammad Noman <muhammadnomanshafiq76@gmail.com>`.
- Never add AI attribution: no `Co-Authored-By` trailer and no "Generated with …" line.
- Don't mention AI in commit messages, README, release notes or repo descriptions.
- Ask before pushing unless the owner has asked for it in the current task.
- Read and write text files as UTF-8. PowerShell `Get-Content`/`Set-Content` without `-Encoding` corrupts characters such as · – ’ …
- Public claims (README, website) must match the code. Don't invent features or numbers, and say what is untested (ARM64 hardware, real Windows 7/8.1 machines, real two-monitor shows).
- No LICENSE file yet: the owner will choose one.
