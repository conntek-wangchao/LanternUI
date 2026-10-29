# Lantern UI

A WPF settings-UI framework taken out of the Lantern Engine launcher: theme tokens, pages built from
declarations, row objects that are also the search index, the window shell (two button families,
tabs or sidebar, activity log) and localization.

What it ships:

| piece | what it does |
|---|---|
| `SettingRow` + row builders | one row = one call; an icon, a title, a description, a hint and the control |
| `Segmented` / `ChoicePicker` | 2-6 short options as pills; long lists as a drop-down with a filter and type-to-jump |
| `SliderRow` | slider plus a typed value box that reports a value outside the range instead of clamping silently |
| `AdvancedSection` | a folded group; the search opens it and still indexes its rows |
| `Sectionizer` | group headers and the rows under them become cards, built once when the page loads |
| `SearchScorer` | ranks a page's rows; `Reveal` unfolds, centres, flashes and focuses the hit |
| `AccentTheme` | one accent colour mixed into the whole surface scale, as a dictionary merged last |
| `ApplyBackdrop` | a picture behind the Modern layout, blurred on request, under an accent-tinted shade |
| `PageScroll` | the wheel scrolls by its own delta, and inner lists/sliders/inputs keep theirs |
| `ContainerPage` | a tab that hosts pages: each section is shown by a switch and is its own place in the search |
| `ListPage` + `ListItem` | a virtualized list (thousands of rows, only the visible ones built) with a themed row template |
| `ShellWindow.ApplyLayout` | Modern ↔ Classic without a restart: the cached pages are rebuilt with the new layout |
| `WindowPlacement` | size, position and maximised state written back on every move, restored safely onto a real screen |
| `SingleInstance` | one window per user; a second start raises the first instead of opening another |
| `Prompt` | a modal dialog in the theme (tell / confirm / fields / up to three buttons), with an off-screen PNG mode |
| `Toast` | notification cards in the corner, queued and animated |
| `JsonSettingsStore` | settings.json that keeps unknown keys, with a debounced save |
| `JsonCatalog` / `Localizer` | English-source keys, fallback to the key, placeholder validation, the gate |

## Layout

```
src/LanternUI/                 the framework (WPF class library, net8.0-windows, no packages)
  Contracts/                   IAppState · ISettingsStore · IShell · ILocalizer
  Theming/                     Palette.Dark.xaml · Styles.xaml · AccentTheme (Mix + overlay)
  Rows/                        SettingRow · Segmented · AdvancedSection
  Layout/                      PageBase (builders, search index, Reveal) · Sectionizer
  Search/                      SearchScorer
  Storage/                     JsonSettingsStore
  Localization/                JsonCatalog · Localizer · RecordingLocalizer · CatalogReport
  Shell/                       ShellWindow (title bar, nav, log, search)
demos/LanternUI.Demo/          the template application (zero packages)
demos/LanternUI.Demo.WpfUi/    the same shell with WPF-UI 4.3.0 bridged onto the palette
tests/LanternUI.Tests/         xUnit: rows, search, localization, the string audit
LanternUI.sln                  all four projects (Visual Studio or dotnet build)
check.ps1                      build (warnings are errors) + tests + the gate, one command
check-i18n.ps1                 the localization gate on its own
```

## Build and run

```powershell
dotnet build demos/LanternUI.Demo/LanternUI.Demo.csproj -c Release
demos/LanternUI.Demo/bin/Release/net8.0-windows/LanternUI.Demo.exe
```

| switch | effect |
|---|---|
| `--lang <code>` | start in a language |
| `--layout classic` | the Classic layout |
| `--nav sidebar` | tabs or the sidebar |
| `--accent #RRGGBB` | the accent, recolouring the whole surface scale |
| `--config <path>` | the settings file (default `%APPDATA%\Lantern UI\settings.json`) |
| `--check-i18n` | build the UI offscreen, report translation gaps, exit non-zero on failure |
| `--search <text>`, `--log`, `--save` | open the search, expand the log, write the settings |
| `--no-log-file` | keep the run out of the log folder (a clean screenshot or gate run) |
| `--toast` | show a notification card |
| `--backdrop <png>` | use a picture behind the page |
| `--test-prompt <png>` | draw the restart dialog off screen to a PNG and exit (no waiting) |
| `--scroll <0..1>` | scroll the page before the screenshot (0 top, 1 bottom) |
| `--shot <png>` | render the window to a PNG and exit (the screenshot pipeline) |

## Localization

- **The English source text is the key.** English has no file; a missing key falls back to itself.
- One flat map per language: `demos/LanternUI.Demo/Localization/<code>.json`.
- Every language listed in `JsonCatalog.DefaultLanguages` is **required**: a listed language with a
  gap fails the gate. Narrow it while working with `--require zh-CN`.
- **Placeholders must be kept exactly**: `{0}`, `{0:HH:mm:ss}`, `{0,5}` — the same tokens, in the
  same order. A dropped `{0}` prints a raw value; an extra `{1}` throws at run time.
- A language chosen in the UI applies after a restart: strings are written into the controls when a
  page is built, which is what keeps the pages cheap.

### Adding a language

1. Add `new LanguageOption("xx", "Name")` to `JsonCatalog.DefaultLanguages`.
2. `.check-i18n.ps1` — its report lists every key the UI uses; copy the list into
   `Localization/xx.json` with the translations.
3. Run it again: green means complete and placeholder-safe.

## Checks

```powershell
.\check.ps1                    # everything: build -warnaserror, 16 tests, the gate
dotnet test LanternUI.sln      # just the tests
.\check-i18n.ps1               # just the gate
```

check.ps1 exits with the number of failed steps, so CI is one line. The build turns warnings into
errors there: a template that ships warnings teaches the wrong habit. Unused output is suppressed.
```

The tests run WPF on one STA thread with a themed Application (see `Sta.cs`), and cover four things:

| test | what it locks down |
|---|---|
| `ChoicePickerTests` | every entry listed, the filter (with case folded), selecting reports once |
| `PageTests` | a page indexes its rows — a folded section included — and the scorer ranks a title prefix first |
| `LocalizationTests` | every language file is complete, placeholders match, the framework's own message is translated |
| `StructuralTests` | a container exposes each section as a place; 5 000 list rows realize fewer than 500 containers; `ApplyLayout` rebuilds the page |
| `SnapshotTests` | the window rendered to a bitmap and compared with a committed baseline (0.5% allowance) |
| `StructureSnapshotTests` | the portable half: every page's row titles and six theme tokens as JSON |
| `StableKeyTests` | editing the English keeps the translation; a former spelling still resolves |
| `ThemeTests` | high contrast swaps the palette and an accent can no longer fight it |
| `LoggingTests` | the log file: format, size roll-over, retention, and the state's fan-out |
| `AutomationTests` | the real UIA tree of a shown window: a row is a named group, its control repeats the name, the window buttons are named, the picker is a combo box |
| `StringAuditTests` | **no user-visible English *or Chinese* in `src/LanternUI` outside `ILocalizer.T`** |

### The string audit

The i18n gate can only check the keys the interface actually asks for, so a hardcoded sentence is
invisible to it. This test reads the framework's sources and fails on any string literal that looks
like a sentence and is not passed to `T(` / `F(` / `Format(`. Genuine exceptions (a font family
name, say) go in `tests/LanternUI.Tests/string-baseline.txt`, one literal per line. A new entry
there should be rare and justified — a translated wrapper is almost always the right answer.

### Accessibility

The framework is drawn by hand, so every piece that was a `Border` had to say what it is:

| piece | what a screen reader gets |
|---|---|
| `SettingRow` | a `Group` named after the setting, described by its explanation text |
| the row's control | the same name, so "check box" is never read on its own |
| `Segmented` | real `RadioButton`s: keyboard, state and naming come from WPF |
| `ChoicePicker` | a `ComboBox`: name plus the picked value, `ExpandCollapsePattern`, read-only value |
| window buttons | drawn paths, named "Close" / "Minimize" / "Maximize" from the catalog |
| `Toast` | a UIA notification event, so it is announced, not just drawn |
| `ListItem` | its title (`ToString`), not the record's dump |

Known limit, measured rather than assumed: the entries inside the picker's popup are drawn, not
controls, so a client finds their labels as plain `Text` elements ("Item 2") but no list, no
`ListItem`, no selected state and nothing to invoke — readable, not operable. `PickerAutomationLimitTests`
pins that down. Converting the popup to a real `ListBox` (its item style already exists) closes it.

## Packaging

A package is one zip plus a manifest, and it carries its own .NET runtime — the target machine needs
nothing installed:

```powershell
.\tools\pack.ps1 -Version 1.2.0 -Notes "what changed"
```

| Mode | Contents | Size |
|---|---|---|
| `self-contained-single` (default) | one exe with the runtime inside, plus `Localization\` and `Assets\` | ~63 MB packed, ~68 MB exe |
| `self-contained-folder` | the same app as a folder of ~200 files | ~180 MB |
| `framework-dependent` | what a developer already has installed | ~0.6 MB |

```
release\LanternUI-<version>-<runtime>-<mode>.zip
release\release.json        version, beta, notes, package, sha256, size, published
```

The zip root is the install root: `UpdateApplier` maps every file in it onto the application folder
and skips the player's own (`settings.json`, `logs`, `user`, `mods`).

### Updating

The channel is a folder or an http(s) address holding `release.json`; set `ui_update_source` or pass
`--update-source <path>`. The About page has the rows, and two switches drive it without any UI:

```powershell
LanternUI.Demo.exe --config <file> --update-source <path> --check-updates    # 0 newer / 2 current / 1 none
LanternUI.Demo.exe --config <file> --update-source <path> --download-update # 0 staged
```

A download is refused unless its sha256 matches the manifest. The verified package is extracted to
`%AppData%\Lantern UI\updates\staging\<version>\` and a `pending.json` records it; the next start
applies it — files the process holds open are renamed to `.old` and deleted on the start after, every
new file is written as `.incoming` first and swapped in, and a failed swap rolls back and keeps the
pending marker so the next start retries.

Known gap: applying only adds and replaces files, it never deletes one that a new version dropped.
Switching an installed copy from framework-dependent to self-contained therefore leaves the old
`.dll` files behind.

## Localization keys

The code still says `T("Interface language")` — reading the code is still reading the screen — but the
files are keyed by a stable id from `Localization/keys.json`:

```json
{ "appearance.language": "Interface language" }
```

Editing that English text keeps the id, so the four translations keep working; adding a former
spelling to an array (`["Interface language", "Language"]`) keeps an old call site alive. A language
file keyed by English (no `keys.json`) is still read, so the migration is per-file, not big bang.

## High contrast, reading direction, touch

- **High contrast**: `Palette.HighContrast.xaml` repeats the same token names with `SystemColors`
  behind them, and `ThemeBoot` swaps it live when Windows changes (and drops the accent, which would
  fight the system highlight). Nothing else in the framework knows.
- **Reading direction**: `IShell.ApplyFlowDirection(true)` mirrors the window. The layout containers
  follow on their own; the title bar is pinned by hand, and a row uses a grid rather than a physical
  margin so its icon column moves with the direction. A pixel baseline (`appearance-rtl`) keeps it honest.
- **Touch / keyboard**: the pill group and the tab strip were `Focusable="False"` — a setting and the
  navigation nobody could reach without a mouse. Both take focus now, with a visible focus ring, a
  pressed state, and a 30 px minimum height.

## The gate

```powershell
.check-i18n.ps1                 # build, check, print the report, exit with the result
.check-i18n.ps1 -NoBuild        # check the current build
.check-i18n.ps1 -Require zh-CN  # require only one language for now
```

It writes `i18n-report.txt` (the table plus every placeholder problem) and `i18n-report.json`
(machine-readable) beside the settings file, and returns 1 when any language is incomplete or any
placeholder disagrees with its key — wire it into CI as a single step.