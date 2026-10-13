<p align="center"><img src="docs/icon.png" width="96" alt="Stagecraft icon"></p>

# Stagecraft

**Presentations that look designed. Without the effort.**

Stagecraft is a free presentation app for Windows. Pick a theme, type your talk, and every slide comes out well designed: balanced layouts, real typography, soft colour, and smooth Morph transitions. When it's time to present, the presenter view shows your notes, the next slide and a timer while the audience sees only the slides.

![The Stagecraft editor](docs/screenshots/editor-light.png)

| | |
|---|---|
| ![Start screen with the ten themes](docs/screenshots/start-light.png) | ![Design panel in dark mode](docs/screenshots/design-dark.png) |
| ![Slide sorter](docs/screenshots/sorter-light.png) | ![Presenter view](docs/screenshots/presenter-dark.png) |

## What's in 1.0

**Beautiful by default**
- 10 designer themes (Aurora, Editorial, Midnight, Pastel, Corporate, Mono, Gradient, Swiss, Botanical, Slate), each with three colour variants, its own font pairing, backgrounds and decorations. Switching theme re-lays every slide and keeps your words.
- 16 smart layouts: Title, Section, Title + content, Two columns, Title only, Big number, Quote, Image left, Image right, Full-bleed image, Agenda, Timeline, Comparison, Team, Thank you and Blank. Change a slide's layout at any time; its title, text and pictures move along.
- Text that fits: long text shrinks to fit its box (it can be turned off per box), and titles wrap into balanced lines instead of leaving one word on its own.
- Smart guides while you move and resize: edges, centres, the slide's centre and margins, and equal spacing between neighbours. **Tidy up** (Ctrl+Alt+T) turns a messy selection into a neat row, column or grid.
- Bundled open-source fonts (Inter, Plus Jakarta Sans, DM Sans, Space Grotesk, Playfair Display, DM Serif Display, Source Serif 4, JetBrains Mono), so decks look the same on every PC. They're used by Stagecraft only and never installed into Windows.

**Fast editing**
- Command palette (Ctrl+K): every command, layout, theme, transition and slide, searchable.
- Text boxes with rich text: bold, italic, underline, size, colour, alignment, bullet and numbered lists with indents, line spacing.
- Shapes: rectangle, rounded rectangle, ellipse, triangle, diamond, line, arrow, callout and connectors that stay attached when you move things.
- Pictures: insert, drag and drop from Explorer or a browser, or paste. Crop, fill / fit, circle and rounded masks, focus point, alt text. Drop a picture on an image placeholder to fill it.
- Charts (bar, line, pie, donut) from a small data grid; paste rows straight from a spreadsheet.
- Tables with header row, banding and a bold first column.
- 1,111 icons (Fluent UI System Icons), searchable, in the theme's colours, with optional circle or rounded-square backgrounds.
- Undo and redo for everything, multi-select, group / ungroup, align and distribute, bring forward / send backward, lock, duplicate (Ctrl+D), copy and paste between slides and between decks, copy style / paste style (Ctrl+Alt+C / Ctrl+Alt+V), nudging with the arrow keys (Shift = bigger steps).
- Slide rail with drag-to-reorder and sections; a slide sorter (Ctrl+2) with sections, hide, duplicate and drag-and-drop; an outline view (Ctrl+3) to type a whole talk as titles and points.
- Speaker notes under every slide; zoom, fit and pan.
- 16:9 (default), 4:3 or a custom slide size.

**Smooth animation**
- Transitions: Fade, Push, Slide over, Zoom and **Morph**. Morph matches the same object on two slides (a duplicated slide keeps each object's identity; otherwise matching names, same content or the same layout slot) and glides position, size, rotation, opacity, colour and corner radius; text that changes cross-fades.
- Builds: appear, fade, rise, scale and wipe, with order and delay, previewable in the editor.
- Transitions and builds are eased and drawn by the GPU at the display's frame rate.

**Presenting**
- With two displays: the slides on the second screen, the presenter view on yours (current and next slide, notes with adjustable size, elapsed time and clock, slide grid).
- Laser pointer, pen and highlighter (ink is kept per slide), black and white screens (B / W), jump to a slide by typing its number and Enter, arrow keys, Page Up / Page Down, Space and presentation clickers. Esc ends.
- Rehearse timings: records how long you spend on each slide.
- The display stays on while you present.

**Safe with your work**
- Saving writes a complete new file and checks it before replacing the old one, so a crash or a full disk can't leave a half-written presentation.
- A recovery copy of unsaved work every few minutes (Settings); if Stagecraft closes unexpectedly, the start screen offers it back.
- Start screen with themes, three sample decks (also in [`samples/`](samples)) and your recent presentations with thumbnails.

## The .stagecraft format
A `.stagecraft` file is a ZIP archive:
- `mimetype` (stored first): `application/vnd.stagecraft.deck+zip`
- `deck.json`: the whole deck as readable JSON, with a `schemaVersion` (1). Older versions are upgraded when opened; files from a newer Stagecraft are refused with a clear message instead of being misread.
- `media/`: the pictures, each stored once and named by the SHA-256 of its bytes; only pictures the deck uses are saved.
- `thumbs/`: `cover.png` (shown on the start screen) and a small JPEG per slide.

## Import and export
| | What you get | Limits |
|---|---|---|
| **PDF** | One page per slide at the slide's size. Vector pages: text, shapes and gradients stay sharp at any zoom, pictures are embedded, and the text can be selected and searched (the words are drawn as outlines with an invisible text layer on top). Or "Pictures": each page is a high-resolution image. | Soft glows and see-through gradients are embedded as high-resolution pictures inside the vector page. |
| **PNG / JPG** | The current slide or every slide (into a folder) at 1920, 2560 or 3840 pixels wide. | |
| **PowerPoint export (.pptx)** | Text boxes, shapes, lines, pictures and tables as native, editable PowerPoint objects; slide size, speaker notes, hidden slides and transitions (Fade, Push, Slide, Zoom). Validated against the Office Open XML schema. | Charts, icons and the theme's background artwork become pictures. Morph becomes a fade. Builds aren't exported. Fonts that aren't installed on the other PC are replaced by PowerPoint. |
| **PowerPoint import (.pptx)** | Slide size, text with basic formatting (sizes, bold, italic, underline, colours, fonts, alignment, bullets), shapes (rectangles, rounded rectangles, ellipses, triangles, diamonds, callouts; others as rectangles), lines and connectors with arrowheads, pictures with cropping, groups, tables, bar / line / pie / doughnut charts (from their saved data), slide backgrounds, speaker notes, hidden slides and transitions. Opening a .pptx imports a copy; the original isn't changed. | SmartArt, other chart types, video and sound, ink, embedded objects and animations are left out (Stagecraft tells you what it skipped). Master and layout inheritance is only followed for positions, text styles and backgrounds, so some decks need touching up. EMF / WMF pictures aren't shown. |

The command line can export without opening a window: `Stagecraft.exe --export-pdf deck.stagecraft deck.pdf`.

## Download
| Windows version | Installer | Size |
|---|---|---|
| Windows 10 / 11 (64-bit) | [Stagecraft-Setup-1.0.0-x64.exe](release/Stagecraft-Setup-1.0.0-x64.exe) | 45.8 MB |
| Windows 10 / 11 on ARM | [Stagecraft-Setup-1.0.0-arm64.exe](release/Stagecraft-Setup-1.0.0-arm64.exe) | 40.6 MB |
| Windows 10 (32-bit) | [Stagecraft-Setup-1.0.0-x86.exe](release/Stagecraft-Setup-1.0.0-x86.exe) | 42.1 MB |
| Windows 7 SP1 / 8.1 | [Stagecraft-Setup-1.0.0-win7-8.exe](release/Stagecraft-Setup-1.0.0-win7-8.exe) | 5.6 MB |

SHA-256 checksums are in [release/SHA256SUMS.txt](release/SHA256SUMS.txt). The Windows 10 / 11 editions include everything they need. The Windows 7 / 8.1 edition needs Microsoft .NET Framework 4.8 (the installer checks and links to Microsoft's download).

Setup installs for all users by default; choose "Install for me only" if you don't have administrator rights. It can open `.stagecraft` files with Stagecraft and add Stagecraft to "Open with" for PowerPoint files.

**Windows SmartScreen:** the installers aren't code-signed yet, so Windows may show "Windows protected your PC" the first time. Click **More info**, then **Run anyway**. You can check the download against `SHA256SUMS.txt` first.

**Not tested yet:** ARM64 hardware (the ARM64 edition is built but hasn't run on a real ARM PC), real Windows 7 / 8.1 and 32-bit Windows 10 PCs (those editions were tested on 64-bit Windows 11), and a slide show on a real second display (it was tested in the windowed test mode).

## Updates
Stagecraft checks for a new version about 10 seconds after it starts and then at most once a day. When there is one, it offers **Update now**, **Later** or **Skip this version**. Update now downloads the installer for your edition, checks its size and SHA-256 checksum, installs it quietly and starts Stagecraft again. You can turn the check off in Settings.

## Privacy
Stagecraft works offline. The only network request it makes is the update check: once a day it downloads a small file (`https://www.nomanshafiq.com/updates/stagecraft.json`) to see whether there is a new version. Nothing about you or your presentations is sent, and there is no telemetry. Settings and recent files are stored in `%APPDATA%\Stagecraft`.

## Keyboard
| Keys | |
|---|---|
| Ctrl+K | Command palette |
| Ctrl+M | New slide |
| Ctrl+D / Ctrl+Shift+D | Duplicate the selection / the slide |
| Ctrl+Alt+T | Tidy up |
| Ctrl+G / Ctrl+Shift+G | Group / ungroup |
| Ctrl+1 / 2 / 3 | Editor / slide sorter / outline |
| Ctrl+Shift+E | Export |
| F5 / Shift+F5 / Alt+F5 | Present from the start / from this slide / presenter view on this screen |
| In the show | → Space PageDown next, ← PageUp previous, number + Enter jump, B / W black / white, Ctrl+L laser, Ctrl+P pen, Ctrl+I highlighter, E clear ink, Esc end |

## Building
Requirements: Windows, the [.NET 10 SDK](https://dot.net) and [Inno Setup 6](https://jrsoftware.org/isdl.php) for the installers.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1                 # tests, every edition, installers
powershell -ExecutionPolicy Bypass -File build.ps1 -Editions x64   # one edition
dotnet test tests\Stagecraft.Tests                                  # the engine tests (.NET 10 and .NET Framework 4.8)
```

Output: `dist\Stagecraft-Setup-<version>-<edition>.exe` and `dist\SHA256SUMS.txt` (copied to `release\` when every edition is built).

The code is one C#/WPF codebase for .NET 10 (Windows 10 / 11, self-contained) and .NET Framework 4.8 (Windows 7 / 8.1):
- `src/Stagecraft.Core`: the deck model, the .stagecraft format, themes and layouts, the slide renderer, snapping / tidy / Morph maths, and PDF, picture and PowerPoint import / export.
- `src/Stagecraft`: the app (editor, slide show, presenter view, dialogs).
- `tests/Stagecraft.Tests`: xUnit tests of the engine.
- `tools/`: scripts that download and verify the fonts (`get-fonts.ps1`), generate the icon geometry from Fluent UI System Icons (`make-icons.mjs`) and render the app icons (`make-icon.ps1`).

## Credits
- Fonts: Inter, Plus Jakarta Sans, DM Sans, Space Grotesk, Playfair Display, DM Serif Display, Source Serif 4 and JetBrains Mono, under the SIL Open Font License 1.1.
- Icons: [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) by Microsoft (MIT).
- [Open XML SDK](https://github.com/dotnet/Open-XML-SDK) (MIT) for PowerPoint files and [PDFsharp](https://www.pdfsharp.com) (MIT) for PDF export.

Full licence texts are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and in Stagecraft's About box.
