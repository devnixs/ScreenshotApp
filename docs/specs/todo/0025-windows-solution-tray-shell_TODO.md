# 0025 — Create the Windows solution and the tray app shell

> Source: docs/PRD.md §13.1 (platform, tray-only app, single instance, DPI, dependencies), §13.2 (term mapping), §13.3 (tray icon, replaces §4), §4 (menu structure), §13.11 (first, second, and seventh criteria); AGENTS.md *Windows app (`windows/`)*: *Stack*, *Layout*, *Commands*, *Architecture rules (Windows)*
> Depends on: none (first spec of the Windows batch; the macOS specs 0021–0024 don't touch `windows/`)

## Context
`windows/` doesn't exist yet. This spec creates it and lays the foundation that every later Windows spec
(0026–0047) builds on. It is the Windows counterpart of the macOS foundation spec
`docs/specs/done/0001-xcodegen-app-shell_COMPLETED.md`, adapted to PRD §13:

- a `.slnx` solution with three projects: `Snapper.Core` (pure logic, `net10.0`), `Snapper.Windows`
  (WPF + WinForms tray + CsWin32, `net10.0-windows10.0.22621.0`, `WinExe`), and
  `Snapper.Core.Tests` (xUnit, `net10.0`). Shared settings and central package versions live in
  `windows/Directory.Build.props` and `windows/Directory.Packages.props`;
- an `app.manifest` declaring Per-Monitor V2 DPI awareness and Windows 10/11 support (PRD §13.1);
- a tray-only app (no main window, no taskbar button) with a tray icon drawn in code, which is a
  camera viewfinder in white on a dark taskbar and black on a light one, or a red record dot while
  recording or converting (PRD §13.3);
- a menu opened by **left- or right-click** with Screenshot / Record / Annotate / — / Settings… /
  Exit Snapper (PRD §13.3). The capture actions are stubs here. *Exit Snapper* quits;
- single instance: a second launch makes the running instance open Settings, then exits
  (PRD §13.1);
- an `AppCoordinator` with stub entry points that later specs fill in.

macOS reference to port (behavior, not code):
- `Snapper/App/StatusMenuLayout.swift:1-29`: entry enum, order, titles. Ported as
  `TrayMenuLayout`. Differences: `quit` becomes `Exit` with the title "Exit Snapper" (PRD §13.3),
  and `capturePurpose` isn't ported yet because `CapturePurpose` is created by 0029, which adds that
  mapping.
- `SnapperTests/StatusMenuLayoutTests.swift:4-20`: order and title tests, ported 1:1 (with the
  Windows title).
- `Snapper/App/StatusItemController.swift:4-90`: builds the menu once and keeps it, exposes
  `item(for:)` so later specs can set shortcut text, `setRecording(_:)` swaps icon and tooltip, and
  routes clicks to the coordinator. Ported as `TrayIconController`.
- `Snapper/App/StatusItemClickRouter.swift` and `SnapperTests/StatusItemClickRouterTests.swift`:
  **don't apply to Windows.** On Windows both buttons open the menu and there is no Control-click
  or right-click → Settings (PRD §13.3 replaces §4). There's nothing to normalize, so no router is
  ported.
- `Snapper/App/AppEnvironment.swift` (unit-test host detection): **doesn't apply.** The xUnit tests
  only load `Snapper.Core`; they never launch the WPF app.
- `Snapper/App/MainMenuBuilder.swift` / `MainMenuLayout.swift` (the macOS main menu that routes
  ⌘ key equivalents): **doesn't apply.** Windows windows handle their own keyboard input (0040–0044
  add the annotation shortcuts).
- macOS facts that don't apply: `LSUIElement`, the Dock, template images, XcodeGen, signing/TCC
  (there's no Screen Recording permission on Windows, PRD §13.10), and the onboarding window
  (PRD §13.10: there is none).

### Facts verified on the author's Mac (macOS, .NET SDK 10.0.102)
- A `net10.0-windows10.0.22621.0` WPF project with `UseWindowsForms`, `EnableWindowsTargeting=true`,
  and `Microsoft.Windows.CsWin32` builds with `dotnet build` on macOS **once the implicit global
  usings `System.Windows.Forms` and `System.Drawing` are removed** (`<Using Remove="…"/>`).
  Otherwise `Application` (and others) are ambiguous between WPF and WinForms.
- Package versions in the local NuGet cache at the time of writing (known good, used as minimums):
  `Microsoft.Windows.CsWin32` 0.3.367, `xunit.v3` 3.2.2, `xunit.runner.visualstudio` 3.1.5,
  `Microsoft.NET.Test.Sdk` 18.0.1. **The implementer pins the current stable version of each**
  (check nuget.org; don't use prerelease). CsWin32 is still 0.x, so take the latest 0.3.x stable.

## User story
As a Windows Snapper user, I start `Snapper.exe` and see a viewfinder icon in the notification area
that matches my taskbar theme, and no window or taskbar button. Left- or right-clicking it shows the
Snapper menu, *Settings…* opens a (placeholder) Settings window, *Exit Snapper* quits, and
starting Snapper again just brings up Settings in the copy that's already running.

## Scope & changes

### Repository and solution (build config)
- **`.gitignore`** (repo root, existing; append, don't rewrite):
  ```
  # Windows app
  windows/**/bin/
  windows/**/obj/
  windows/**/*.user
  windows/.vs/
  ```
- **`windows/Snapper.slnx`** (new): XML solution format, listing the three projects:
  `src/Snapper.Core/Snapper.Core.csproj`, `src/Snapper.Windows/Snapper.Windows.csproj`,
  `tests/Snapper.Core.Tests/Snapper.Core.Tests.csproj` (use `/src/` and `/tests/` solution folders).
  Create it with `dotnet new sln --format slnx` + `dotnet sln add`, or by hand.
- **`windows/Directory.Build.props`** (new), applied to all three projects:
  - `LangVersion` `latest`, `Nullable` `enable`, `ImplicitUsings` `enable`,
    `TreatWarningsAsErrors` `true`, `Deterministic` `true`.
  - `WarningsNotAsErrors` `NU1901;NU1902;NU1903;NU1904`, so a newly published package advisory
    shows as a warning instead of breaking an unrelated build. Everything else stays an error.
  - `Company` `Thera Engineering`, `Product` `Snapper`, `Copyright` `© 2026 Thera Engineering`
    (version info and the icon are 0047).
- **`windows/Directory.Packages.props`** (new): `ManagePackageVersionsCentrally=true` and one
  `PackageVersion` each for `Microsoft.Windows.CsWin32`, `xunit.v3`, `xunit.runner.visualstudio`,
  and `Microsoft.NET.Test.Sdk`, pinned exactly (see "Facts verified"). No other packages
  (AGENTS.md). The projects' `PackageReference`s have no `Version`.
- Both props files sit in `windows/`, and MSBuild finds them by walking up from each project, so
  `dotnet build windows/Snapper.slnx` works from the repo root. Don't add a `global.json`. The
  SDK lookup would start from the current directory (the repo root), so a `global.json` in
  `windows/` wouldn't take effect.

### `windows/src/Snapper.Core/Snapper.Core.csproj` (new, pure)
- `TargetFramework` `net10.0`, `RootNamespace` `Snapper.Core`. No package references, no
  `UseWPF`/`UseWindowsForms`, and no Windows TFM (AGENTS.md: Core references no WPF, WinForms,
  WinRT, or interop types).
- `InternalsVisibleTo` `Snapper.Core.Tests` (via `<InternalsVisibleTo Include="Snapper.Core.Tests" />`).

### `windows/tests/Snapper.Core.Tests/Snapper.Core.Tests.csproj` (new)
- `TargetFramework` `net10.0`, `OutputType` `Exe` (required by xunit.v3), `IsPackable` `false`,
  `IsTestProject` `true`, `RootNamespace` `Snapper.Core.Tests`.
- `ProjectReference` to `Snapper.Core`. `PackageReference`s to `xunit.v3`,
  `xunit.runner.visualstudio` (`PrivateAssets=all`), and `Microsoft.NET.Test.Sdk`, so
  `dotnet test windows/Snapper.slnx` discovers and runs the tests (VSTest mode).
- `<Using Include="Xunit" />`. Test files are flat in the project folder, named `<Name>Tests.cs`
  (shared brief).

### `windows/src/Snapper.Windows/Snapper.Windows.csproj` (new, platform glue)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.22000.0</SupportedOSPlatformVersion>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>   <!-- required by CsWin32 -->
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>Snapper</AssemblyName>          <!-- produces Snapper.exe -->
    <RootNamespace>Snapper.Windows</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Using Remove="System.Windows.Forms" />
    <Using Remove="System.Drawing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Snapper.Core\Snapper.Core.csproj" />
    <PackageReference Include="Microsoft.Windows.CsWin32" PrivateAssets="all" />
  </ItemGroup>
</Project>
```
- **Namespace pitfalls the implementer must respect** (they will hit every later spec):
  1. The WPF application class **must not be named `App`**, because the namespace
     `Snapper.Windows.App` (the `App/` folder) would clash with a class `Snapper.Windows.App` (CS0101).
     Name it **`SnapperApplication`** (`x:Class="Snapper.Windows.SnapperApplication"`).
  2. Inside `namespace Snapper.Windows…`, an unqualified `Windows.X` resolves to `Snapper.Windows.X`.
     Import CsWin32 and WinRT namespaces with `using` directives at the top of the file
     (`using Windows.Win32;`), which resolve from the global namespace, or write
     `global::Windows.…`. Never write `Windows.Win32.PInvoke…` inline inside the namespace.
  3. Files that need WinForms or System.Drawing use aliases (`using Forms = System.Windows.Forms;`,
     `using Drawing = System.Drawing;`) rather than importing whole namespaces next to WPF ones.
- **`windows/src/Snapper.Windows/app.manifest`** (new):
  - `<compatibility>` with the Windows 10/11 `supportedOS` GUID `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`.
  - `<application><windowsSettings>`: `<dpiAware>true/pm</dpiAware>` and
    `<dpiAwareness>PerMonitorV2, PerMonitor</dpiAwareness>` (the SMI 2005/2016 namespaces), and
    `<longPathAware>true</longPathAware>`.
  - A `<dependency>` on `Microsoft.Windows.Common-Controls` version `6.0.0.0` (processorArchitecture
    `*`, publicKeyToken `6595b64144ccf1df`), so the WinForms `TaskDialog` used by 0031's error alerts
    works and menus get themed controls.
  - `requestedExecutionLevel` `asInvoker`.
- **`windows/src/Snapper.Windows/NativeMethods.txt`** (new). This spec's entries, one per line:
  `SetForegroundWindow`, `AllowSetForegroundWindow`, `ASFW_ANY`, `DestroyIcon`, `MonitorFromPoint`,
  `GetDpiForMonitor`. Later specs append their own.
  If CsWin32 needs a `NativeMethods.json`, add one with `"public": false` (the default) and nothing
  else.

### App entry and lifecycle (platform glue, WPF dispatcher thread)
- **`windows/src/Snapper.Windows/App.xaml`** + **`App.xaml.cs`** (new), class
  `Snapper.Windows.SnapperApplication : System.Windows.Application`:
  - `ShutdownMode="OnExplicitShutdown"`, no `StartupUri`. There is no main window, and closing the
    Settings (or later annotation) window never quits (PRD §13.1).
  - `OnStartup`:
    1. `var instance = SingleInstance.TryAcquire();`. If this isn't the primary instance, call
       `SingleInstance.SignalPrimary()` and then `Shutdown(0)`, and return. No tray icon is created.
    2. `Forms.Application.EnableVisualStyles()` and
       `Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2)` (ignore the return value, since
       the manifest already set the mode) before any WinForms object is created, so the tray menu
       scales with the monitor DPI.
    3. Create the `AppCoordinator`, call `Start()`, and start the `SingleInstance` listener with a callback
       that marshals to the dispatcher and calls `coordinator.OpenSettings(SettingsTab.General)`.
  - `OnExit`: dispose the coordinator (removes the tray icon) and the `SingleInstance` (unregisters the
    wait, releases the mutex).
  - No `DispatcherUnhandledException` handler in this spec (unchanged default behavior).
- **`windows/src/Snapper.Windows/App/SingleInstance.cs`** (new): `sealed class SingleInstance : IDisposable`.
  - Names (constants): mutex `Local\Snapper-8C1E2B7A-instance` and event
    `Local\Snapper-8C1E2B7A-open-settings`. `Local\` scopes them to the user's session, so another
    user's or another session's Snapper is independent. The GUID-ish suffix avoids collisions with other
    apps. Debug and Release builds share the names on purpose.
  - `static SingleInstance? TryAcquire()`: first creates or opens the event
    (`new EventWaitHandle(false, EventResetMode.AutoReset, EventName)`), then
    `new Mutex(initiallyOwned: true, MutexName, out bool createdNew)`. If `createdNew`, returns an instance
    that holds both for the app's lifetime (fields keep them alive so the GC doesn't release them).
    Otherwise it disposes both and returns `null`.
  - `static void SignalPrimary()`: `PInvoke.AllowSetForegroundWindow(ASFW_ANY)`, because the second
    process was just launched by the user, so it may grant the foreground to the primary, which
    otherwise can't take focus. Then it opens or creates the event and `Set()`s it. It never throws:
    on failure, it does nothing and the process still exits.
  - `void Listen(Action onSignal)`: `ThreadPool.RegisterWaitForSingleObject(event, …,
    Timeout.Infinite, executeOnlyOnce: false)`. The callback runs on a pool thread and calls
    `onSignal`, and the caller marshals with `Dispatcher.BeginInvoke`. An auto-reset event that was
    set before `Listen` stays signaled, so a second launch during the primary's startup isn't lost.
  - `Dispose`: unregister the wait, `ReleaseMutex()`, and dispose the handles. Disposing twice is a
    no-op.
  - An abandoned mutex (the primary crashed): the OS deletes the mutex when its last handle closes,
    so the next launch gets `createdNew == true`. If the constructor throws
    `AbandonedMutexException`, treat it as acquired.

### Coordinator (platform glue)
- **`windows/src/Snapper.Windows/App/AppCoordinator.cs`** (new): `sealed class AppCoordinator : IDisposable`,
  used only on the dispatcher thread.
  - Owns the `TrayIconController` and the placeholder Settings window.
  - `void Start()`: creates the tray icon.
  - Stub entry points that later specs implement: `StartScreenshot()`, `StartRecording()`,
    `StartAnnotate()`. In this spec each one only writes
    `System.Diagnostics.Debug.WriteLine("Snapper: StartScreenshot() not implemented yet")` (or the
    matching name). That goes through `OutputDebugString` in Debug builds, which you can see in Visual
    Studio or Sysinternals DebugView. No dialog, no sound.
  - `void TrayClicked()`: called for every left or right click on the icon. In this spec it shows the
    menu (`trayIconController.ShowMenu()`) when `TrayMenuClickGate` allows it. 0029 routes this
    through `AppModeMachine` (menu / stop recording / ignore while converting), mirroring macOS
    `AppCoordinator.send(.statusItemClicked)` (`Snapper/App/AppCoordinator.swift:41-58`).
  - `void OpenSettings(SettingsTab tab = SettingsTab.General)`: **decision: shows a placeholder
    window**, so the Settings menu item and the single-instance signal have something visible to verify.
    It creates (once) a code-built WPF `Window` with the title "Snapper Settings", about 420 × 200 DIPs,
    `WindowStartupLocation.CenterScreen`, `ResizeMode.NoResize`, `ShowInTaskbar = true`, and a centered
    `TextBlock` "Settings will appear here.". It ignores `tab`. If the window is already open, it restores
    it when it's minimized (`WindowState.Normal`) and calls `Activate()`. It never creates a second
    one. Closing it sets the field back to `null` and doesn't quit the app. Keep this window as a private
    nested class or a private method in `AppCoordinator`, not in `Settings/`: 0033 replaces it with
    `Settings/SettingsWindow` and deletes the placeholder.
  - `void Exit()`: the single exit path. It disposes the tray icon first, so it disappears immediately
    without a ghost icon, then calls `System.Windows.Application.Current.Shutdown()`. Later specs hook
    into this method: 0034 saves an active recording and 0043 adds the unsaved-annotations confirmation.
    So nothing may call `Shutdown()` directly.
  - `Dispose()`: disposes the tray controller (idempotent).
- **`windows/src/Snapper.Core/Settings/SettingsTab.cs`** (new, pure): `public enum SettingsTab { General, Shortcuts, Recording }`.
  This spec owns it (the ownership table doesn't assign it) because `OpenSettings(tab)` needs it.
  0029's hotkey failure notification opens `Shortcuts`, and 0033 selects the tab.

### Tray layout and style (pure, `Snapper.Core`, namespace `Snapper.Core.App`)
- **`windows/src/Snapper.Core/App/TrayMenuLayout.cs`** (new):
  - `public enum TrayMenuEntry { Screenshot, Record, Annotate, Separator, Settings, Exit }`
  - `public static class TrayMenuLayout`:
    - `public static IReadOnlyList<TrayMenuEntry> Entries` =
      `[Screenshot, Record, Annotate, Separator, Settings, Exit]`
    - `public static string? Title(TrayMenuEntry entry)` → `"Screenshot"`, `"Record"`, `"Annotate"`,
      `null` (Separator), `"Settings…"` (real ellipsis U+2026), `"Exit Snapper"`.
  - **Decision (same as macOS 0001): no shortcut text in this spec.** Hotkeys don't exist until 0029,
    which adds the `CapturePurpose` mapping and the right-aligned `Ctrl+Shift+4` text from the live
    bindings (PRD §13.3). Showing static text now would advertise shortcuts that do nothing.
- **`windows/src/Snapper.Core/App/TrayMenuClickGate.cs`** (new):
  `public static bool ShouldOpenMenu(bool menuVisible, long? millisecondsSinceMenuClosed, int doubleClickTimeMs)`.
  - `menuVisible` → `false`.
  - `millisecondsSinceMenuClosed` is not null and `< doubleClickTimeMs` → `false`. A click on the icon
    while the menu is open first closes the menu (outside click) and then delivers `MouseUp`, and
    Windows convention is that this click only closes the menu. It also absorbs the second `MouseUp`
    of a double-click.
  - Otherwise `true` (null = never opened).
- **`windows/src/Snapper.Core/App/TrayIconStyle.cs`** (new):
  - `public enum TrayIconState { Idle, Recording, Converting }` (0034/0045 set the last two).
  - `public enum TrayGlyph { Viewfinder, RecordDot }`
  - `public readonly record struct TrayIconAppearance(TrayGlyph Glyph, uint Argb, string Tooltip)`
  - `public static class TrayIconStyle`:
    - `public const uint DarkTaskbarForeground = 0xFFFFFFFF;` (white),
      `public const uint LightTaskbarForeground = 0xFF000000;` (black),
      `public const uint RecordingRed = 0xFFE81123;` (Windows' standard red, used on both themes).
    - `public static TrayIconAppearance Resolve(TrayIconState state, bool taskbarIsLight)`:
      `Idle` → `(Viewfinder, taskbarIsLight ? black : white, "Snapper")`. `Recording` and `Converting`
      → `(RecordDot, RecordingRed, "Snapper — Recording")` (em dash U+2014, PRD §13.3), regardless
      of the theme.
    - `public static bool TaskbarIsLight(int? systemUsesLightTheme)`: the raw
      `SystemUsesLightTheme` registry DWORD. `null` (missing) → `false` (dark, the conservative
      default), `0` → `false`, any non-zero → `true`. The taskbar follows the **system** mode
      (`SystemUsesLightTheme`), not the app mode (`AppsUseLightTheme`).
    - `public static int IconPixelSize(int dpi)`: the small-icon size at that DPI,
      `round(16 × dpi / 96)` with `MidpointRounding.AwayFromZero`. `dpi <= 0` → `16`. This matches
      `SM_CXSMICON` at the standard scale steps (96→16, 120→20, 144→24, 192→32), so the tray never
      rescales our bitmap.
- **`windows/src/Snapper.Core/App/TrayGlyphGeometry.cs`** (new). The icon drawing is pure geometry,
  and `TrayIconRenderer` only strokes or fills what it returns, so the shapes are tested without GDI:
  - `public readonly record struct GlyphLine(double X1, double Y1, double X2, double Y2)`
  - `public readonly record struct GlyphCircle(double CenterX, double CenterY, double Radius, bool Filled)`
  - `public sealed record TrayGlyphDrawing(double StrokeWidth, IReadOnlyList<GlyphLine> Lines, IReadOnlyList<GlyphCircle> Circles)`
  - `public static TrayGlyphDrawing Viewfinder(int size)`:
    - `stroke = max(1, round(size / 16.0, AwayFromZero))` (16→1, 20→1, 24→2, 32→2, 48→3).
    - Margin `m = 1` px. The bracket strokes' centre lines are at `c = m + stroke / 2.0` and
      `size − c`. The outer edge of each stroke is therefore exactly 1 px inside the icon edge. Line
      coordinates land on pixel centres for odd strokes (`n + 0.5`) and on integers for even strokes,
      so 1-px lines are crisp.
    - Four corner brackets, two lines each (8 lines). The top-left bracket is `(c, c)–(m + L, c)` and
      `(c, c)–(c, m + L)` with `L = round(size × 0.3, AwayFromZero)`. The other three are its mirror
      images.
    - One lens circle, not filled, centered at `(size / 2, size / 2)` with radius
      `round(size × 0.18)` (its stroke stays clear of the brackets).
    - The shape is mirror-symmetric horizontally and vertically.
  - `public static TrayGlyphDrawing RecordDot(int size)`: no lines, and one filled circle centered at
    `(size / 2, size / 2)` with diameter `round(size × 0.625)` (10 px at 16), `StrokeWidth` 0.
  - **Decision:** the glyphs are vector shapes drawn in code, not a Segoe Fluent Icons font glyph.
    PRD §13.3 requires icons drawn in code with no assets, and plain geometry avoids depending on a
    font's metrics at 16 px. The §13.2 "SF Symbols → Segoe Fluent Icons" mapping applies to toolbar
    buttons (0040).

### Tray icon (platform glue)
- **`windows/src/Snapper.Windows/App/TrayIconRenderer.cs`** (new): `static class TrayIconRenderer`.
  - `static Drawing.Icon Render(TrayIconAppearance appearance, int sizePx)`: draws
    `TrayGlyphGeometry.Viewfinder` or `RecordDot(sizePx)` into a `Drawing.Bitmap(sizePx, sizePx,
    Format32bppArgb)` with a transparent background. It uses `SmoothingMode.AntiAlias` for the circles,
    `PixelOffsetMode.Half`, a `Pen` of `StrokeWidth` in `appearance.Argb` with flat caps, and the lines
    joined at the bracket corners (draw each bracket as one 3-point polyline, so the corners don't
    overlap and darken).
  - HICON ownership: `IntPtr h = bitmap.GetHicon(); using var tmp = Drawing.Icon.FromHandle(h); var
    icon = (Drawing.Icon)tmp.Clone(); PInvoke.DestroyIcon(h); return icon;`. The caller owns and
    disposes the returned icon. `Icon.FromHandle` doesn't own `h`, which is why it's destroyed
    explicitly.
- **`windows/src/Snapper.Windows/App/TrayIconController.cs`** (new): `sealed class TrayIconController : IDisposable`,
  created on the dispatcher thread.
  - Takes the `AppCoordinator` (no static state). Creates a `Forms.NotifyIcon` with
    `Visible = true`. **It doesn't assign `NotifyIcon.ContextMenuStrip`**, because WinForms would then
    open it on right-click only. Instead it builds one `Forms.ContextMenuStrip` once from
    `TrayMenuLayout.Entries` (`ToolStripMenuItem` with `Title(entry)`, and `ToolStripSeparator` for
    `Separator`) and keeps it. Clicks dispatch to `StartScreenshot` / `StartRecording` /
    `StartAnnotate` / `OpenSettings(General)` / `Exit`.
  - `Forms.ToolStripMenuItem? Item(TrayMenuEntry entry)`, so that 0029 can set
    `ShortcutKeyDisplayString` (rendered right-aligned) in place.
  - `NotifyIcon.MouseUp`: for `MouseButtons.Left` or `MouseButtons.Right`, it calls
    `coordinator.TrayClicked()`. Other buttons are ignored. The icon is shown with the WinForms
    default behavior. If keyboard selection of the icon (Win+B, then arrows/Enter) reaches WinForms
    as a click, it opens the menu too. That's best effort, not a requirement.
  - `void ShowMenu()`: if `TrayMenuClickGate.ShouldOpenMenu(menu.Visible, msSinceClosed,
    Forms.SystemInformation.DoubleClickTime)` allows it, it calls
    `PInvoke.SetForegroundWindow(menu.Handle)` and then `menu.Show(Forms.Cursor.Position,
    Forms.ToolStripDropDownDirection.AboveLeft)`. Taking the foreground first is required, or the menu
    won't close when the user clicks elsewhere. WinForms keeps the menu inside the working area. It
    records the close time in `menu.Closed` (`Environment.TickCount64`).
  - `void SetState(TrayIconState state)`: stores the state and re-renders. 0034/0045 call it. In this spec
    the state is always `Idle`.
  - Re-rendering (`Refresh()`): `appearance = TrayIconStyle.Resolve(state,
    TrayIconStyle.TaskbarIsLight(ReadSystemUsesLightTheme()))`, `size =
    TrayIconStyle.IconPixelSize(PrimaryMonitorDpi())`, `notifyIcon.Icon = TrayIconRenderer.Render(…)`,
    and `notifyIcon.Text = appearance.Tooltip`. It disposes the previous icon **after** assigning the
    new one.
    - `ReadSystemUsesLightTheme()`: `Microsoft.Win32.Registry.CurrentUser` →
      `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, value `SystemUsesLightTheme`
      as `int?`. A missing key or value, or one of the wrong type, gives `null`.
    - `PrimaryMonitorDpi()`: `MonitorFromPoint((0,0), MONITOR_DEFAULTTOPRIMARY)` +
      `GetDpiForMonitor(MDT_EFFECTIVE_DPI)`. The notification area is on the primary monitor's taskbar.
      On failure it returns 96. (0028 later adds `DisplayInfoProvider`. This lookup stays local because
      it only needs the primary DPI.)
  - Theme and DPI changes: it subscribes to `Microsoft.Win32.SystemEvents.UserPreferenceChanged`
    (categories `General` and `VisualStyle`, which a light/dark switch raises) and
    `SystemEvents.DisplaySettingsChanged` (scale or primary changes). Each handler marshals to the
    dispatcher (`Dispatcher.BeginInvoke(Refresh)`), because SystemEvents may raise on its own thread.
    It **unsubscribes in `Dispose`**, because these static events otherwise leak the controller.
  - Explorer restarts: WinForms `NotifyIcon` re-adds itself on `TaskbarCreated`. Nothing extra is
    needed, but the manual check covers it.
  - `Dispose`: unsubscribe, `notifyIcon.Visible = false`, and dispose the icon, menu, and NotifyIcon.
    It's idempotent.

### Tests (pure, `windows/tests/Snapper.Core.Tests/`)
Listed in the Test plan.

### PRD / AGENTS.md
- No PRD change. Everything here is already specified in PRD §13.1/§13.3 or is internal structure.
- AGENTS.md already describes this layout and these commands. If the implementation needs a
  different command (for example a `-p:` switch for `dotnet test`), update the AGENTS.md *Commands
  (they run on macOS too)* block in the same change. Also add one line to the AGENTS.md Windows
  *Architecture rules* about the two namespace pitfalls (the `SnapperApplication` class name,
  `using`/`global::` for `Windows.*` namespaces). That's an architecture rule every later spec
  relies on.

## Acceptance criteria
- [ ] `dotnet build windows/Snapper.slnx -c Debug` succeeds on macOS and on Windows with zero
      warnings (warnings are errors). `dotnet test windows/Snapper.slnx` runs and passes every test
      listed below.
- [ ] `git status` after building shows no `bin/` or `obj/` folders under `windows/`.
- [ ] `windows/Directory.Packages.props` pins exactly four packages (CsWin32, xunit.v3,
      xunit.runner.visualstudio, Microsoft.NET.Test.Sdk). No `PackageReference` has a version.
      `Snapper.Core` has no package references.
- [ ] The build output is `Snapper.exe`, and its embedded manifest declares PerMonitorV2 DPI
      awareness, Windows 10/11 support, Common Controls 6, and `asInvoker`.
- [ ] Launching Snapper shows a tray icon and no window, no taskbar button, and no Alt+Tab entry
      (PRD §13.1, §13.11).
- [ ] The idle icon is a viewfinder, white on a dark taskbar and black on a light one. It switches
      within a second or two when the Windows mode changes, without a restart (PRD §13.3). The tooltip is
      "Snapper".
- [ ] Left-click and right-click both open the same menu: Screenshot, Record, Annotate, separator,
      Settings…, Exit Snapper, with no shortcut text yet (PRD §13.3). Screenshot, Record, and Annotate
      do nothing visible (they write a debug line).
- [ ] Settings… opens the placeholder "Snapper Settings" window in front, with a taskbar button.
      Opening it again reuses the same window. Closing it doesn't quit Snapper.
- [ ] Exit Snapper removes the tray icon immediately and ends the process.
- [ ] Launching `Snapper.exe` while it's running opens the running instance's Settings
      (placeholder) in front. The new process exits, and there's still exactly one tray icon (PRD
      §13.1, §13.11).
- [ ] The recording appearance (red dot, "Snapper — Recording") is covered by unit tests. It
      becomes visible in 0034.

## Edge cases
- **Mixed DPI / scale changes:** the tray lives on the primary monitor's taskbar, so the icon is
  rendered at the primary monitor's DPI and re-rendered on `DisplaySettingsChanged`. Secondary
  monitors don't affect the icon size. A tray on a non-standard scale (e.g. 175% → 168 DPI → 28 px)
  still gets an exact-size bitmap.
- **Theme value missing** (older profiles, policy-managed machines): dark taskbar is assumed (white
  icon).
- **High contrast themes:** out of scope for v1. The icon keeps the light/dark rule.
- **Windows 11 hides new tray icons** in the overflow flyout by default. That isn't a bug; the
  manual steps say how to pin it.
- **Clicking the icon while the menu is open** closes the menu and doesn't reopen it
  (`TrayMenuClickGate`). Esc or a click elsewhere closes the menu.
- **Double-click** on the icon opens the menu once.
- **Second launch during the primary's startup** (event set before `Listen`): the auto-reset event
  stays signaled, so Settings opens as soon as the listener is registered.
- **Second launch while Settings is minimized or behind other windows:** the window is restored and
  activated. `AllowSetForegroundWindow(ASFW_ANY)` from the launching process lets the primary take
  the foreground.
- **Primary crashed** (abandoned mutex, or the handle was closed by the OS): the next launch becomes
  primary. `AbandonedMutexException` counts as acquired.
- **Different user, or the same user in another session (RDP):** `Local\` names are per session, so
  each session runs its own Snapper. That's intended.
- **Explorer restarts:** the icon comes back (WinForms handles `TaskbarCreated`).
- **Exit while the menu is open** (Exit is clicked from the menu): the menu closes, then the icon is
  removed and the app shuts down. No ghost icon remains.
- **Running on macOS:** `Snapper.Windows` builds but can't run there. Only the Core tests run on
  macOS.

## Test plan
- **Unit tests** (xUnit v3, `windows/tests/Snapper.Core.Tests/`):
  - `TrayMenuLayoutTests.cs` (ports `SnapperTests/StatusMenuLayoutTests.swift:5-19`)
    - `EntriesFollowProductOrder`: `Entries` equals
      `[Screenshot, Record, Annotate, Separator, Settings, Exit]`.
    - `TitlesMatchProductMenu`: `Entries.Select(Title)` equals
      `["Screenshot", "Record", "Annotate", null, "Settings…", "Exit Snapper"]`.
    - `SettingsTitleUsesRealEllipsis`: `Title(Settings)` ends with `'…'` and doesn't contain `"..."`.
    - `OnlySeparatorHasNoTitle`: `Title(e) == null` exactly for `Separator`.
  - `TrayMenuClickGateTests.cs`
    - menu visible → `false`.
    - never closed (`null`), not visible → `true`.
    - closed 100 ms ago, double-click time 500 → `false`.
    - closed 499 ms ago, 500 → `false`. Closed 500 ms ago, 500 → `true` (boundary).
    - closed 2000 ms ago → `true`.
  - `TrayIconStyleTests.cs`
    - `Resolve(Idle, taskbarIsLight: false)` → `(Viewfinder, 0xFFFFFFFF, "Snapper")`.
    - `Resolve(Idle, true)` → `(Viewfinder, 0xFF000000, "Snapper")`.
    - `Resolve(Recording, false)` and `Resolve(Recording, true)` → both
      `(RecordDot, 0xFFE81123, "Snapper — Recording")`. The tooltip contains U+2014.
    - `Resolve(Converting, x)` equals `Resolve(Recording, x)` for both themes.
    - `TaskbarIsLight(null)` → `false`, `(0)` → `false`, `(1)` → `true`, `(2)` → `true`.
    - `IconPixelSize`: 96→16, 120→20, 144→24 (150%), 168→28, 192→32 (200%), 288→48, 0→16, −96→16.
  - `TrayGlyphGeometryTests.cs` (sizes 16, 20, 24, 28, 32, 48 via `[Theory]`)
    - `StrokeWidth`: 16→1, 20→1, 24→2, 32→2, 48→3.
    - Every line endpoint ± `StrokeWidth/2`, and every circle's bounds ± `StrokeWidth/2`, lie within
      `[0, size]` (nothing is clipped).
    - `Viewfinder` has exactly 8 lines and 1 unfilled circle centered at `(size/2, size/2)`.
    - Mirror symmetry: the line set maps onto itself under `x → size − x` and under `y → size − y`
      (compare as unordered sets of segments with a 1e-9 tolerance).
    - Pixel alignment: with odd `StrokeWidth`, every horizontal line's `Y` and every vertical line's
      `X` has a fractional part of 0.5. With even `StrokeWidth`, it's 0.
    - Each line is axis-aligned and its length is `round(size × 0.3)` ± `StrokeWidth`.
    - The lens circle doesn't touch any bracket (distance from the center to each line > radius +
      `StrokeWidth`).
    - `RecordDot(16)`: no lines, one filled circle at (8, 8) with radius 5. `RecordDot(32)`: radius 10.
  - `SmokeTests.cs`
    - `CoreAssemblyLoads`: `typeof(TrayMenuLayout).Assembly.GetName().Name == "Snapper.Core"`.
    - `CoreReferencesNoPlatformAssemblies`: `typeof(TrayMenuLayout).Assembly.GetReferencedAssemblies()`
      contains none of `PresentationFramework`, `PresentationCore`, `WindowsBase`,
      `System.Windows.Forms`, `System.Drawing`, `System.Drawing.Common`, `Microsoft.Windows.SDK.NET`,
      `Microsoft.Windows.CsWin32` (this enforces AGENTS.md's Core rule from now on).
    - `SettingsTabHasThreeTabsInOrder`: `Enum.GetValues<SettingsTab>()` equals
      `[General, Shortcuts, Recording]`.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`
  both succeed on macOS (verify there before reporting), with zero warnings. If `dotnet test` with the
  `.slnx` needs a flag on SDK 10, record it in AGENTS.md *Commands*.
- **Manual verification** (Windows 11 machine, x64 or Arm64; can't be automated):
  1. `dotnet build windows/Snapper.slnx -c Debug`, then run
     `windows\src\Snapper.Windows\bin\Debug\net10.0-windows10.0.22621.0\Snapper.exe`. **Expected:** no
     window opens, nothing appears in the taskbar or Alt+Tab, and a Snapper icon appears in the
     notification area. If it isn't visible, open the "Show hidden icons" chevron. To pin it, go to
     Settings → Personalization → Taskbar → Other system tray icons → Snapper → On.
  2. Hover the icon. **Expected:** the tooltip "Snapper".
  3. Settings → Personalization → Colors → Choose your mode → **Custom**, and set *Default Windows
     mode* to Dark, then Light. **Expected:** the viewfinder is white on the dark taskbar and black on
     the light one. It updates within a couple of seconds, without restarting Snapper.
  4. Left-click the icon. **Expected:** a menu opens at the cursor, above the taskbar, with
     Screenshot, Record, Annotate, a separator, Settings…, and Exit Snapper, with no shortcut text. Press
     Esc. **Expected:** it closes. Right-click the icon. **Expected:** the same menu. Click elsewhere on
     the desktop. **Expected:** it closes.
  5. Open the menu, then click the tray icon again. **Expected:** the menu closes and doesn't reopen.
     Double-click the icon. **Expected:** the menu opens once.
  6. With Sysinternals DebugView running (Capture Win32), choose Screenshot, Record, and Annotate in
     turn. **Expected:** nothing visible happens, and DebugView shows one
     "Snapper: Start… not implemented yet" line per click.
  7. Choose Settings…. **Expected:** the "Snapper Settings" placeholder window opens in front with a
     taskbar button. Choose Settings… again. **Expected:** the same window comes to the front (still
     one taskbar button). Close it. **Expected:** the tray icon remains.
  8. Set the primary monitor's scale (Settings → System → Display → Scale) to 100%, 150%, then 200%.
     **Expected:** the tray icon stays crisp (not blurry) at each step. The menu text is scaled
     correctly at 150% and 200%.
  9. With Snapper running, launch `Snapper.exe` again (double-click it in Explorer). **Expected:** the
     placeholder Settings window comes to the front. After a second, Task Manager → Details shows exactly
     one `Snapper.exe`, and there's still one tray icon.
  10. Minimize the Settings window, put another app in front, and launch `Snapper.exe` again.
      **Expected:** Settings is restored and in front, with keyboard focus.
  11. Restart Windows Explorer (Task Manager → Windows Explorer → Restart). **Expected:** the Snapper
      tray icon reappears.
  12. Choose Exit Snapper. **Expected:** the icon disappears immediately (hovering the area shows no
      leftover icon) and the process ends. Launch Snapper again right away. **Expected:** it starts
      normally with a tray icon (the mutex was released).
  13. (If an Arm64 device is available) repeat steps 1, 4, and 12 on Arm64.

## Out of scope
- File naming, the save folder, and Explorer reveal: **0026**.
- The typed settings and JSON store (`AppSettings`, `KeyChord`): **0027**.
- Display geometry and the display provider: **0028**.
- Global hotkeys, `CapturePurpose`, `AppModeMachine`, the shortcut text in the tray menu, routing tray
  clicks by app mode (menu / stop recording / ignore while converting), and the hotkey failure
  notification: **0029**.
- The real Settings window (tabs, launch at login, shortcut recorder), which replaces the placeholder:
  **0033**.
- Setting the icon to Recording/Converting, stop on click while recording, and saving on Exit or
  shutdown while recording: **0034** (recording), **0045** (converting).
- The unsaved-annotations confirmation on Exit: **0043**.
- Publish profiles, the app icon (`.ico`) and version resources, and Windows build docs: **0047**.
- Dark-mode styling of the tray menu itself (it uses the standard WinForms renderer), and high-contrast
  icon variants. PRD §13.3 doesn't require them.
