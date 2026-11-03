# Snapper for Windows

Snapper is a native Windows tray app for screenshots, screen recordings, and capture-then-annotate. It follows the shared [product requirements](../docs/PRD.md#13-windows-version) and [repository guidance](../AGENTS.md#windows-app-windows).

## Requirements

Build with the .NET 10 SDK on macOS or Windows. Run on Windows 11 build 22000 or later, x64 or Arm64. The published executable includes .NET; the destination PC needs no runtime installation. The Windows app compiles on macOS, but cannot run there.

## Build and test

From the repository root:

```sh
dotnet build windows/Snapper.slnx -c Debug
dotnet test windows/Snapper.slnx
```

## Publish

```sh
dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-x64
dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-arm64
```

The output is one file per architecture:

- `windows/artifacts/publish/win-x64/Snapper.exe`
- `windows/artifacts/publish/win-arm64/Snapper.exe`

Expect roughly 75–80 MB per executable. `dotnet publish windows/src/Snapper.Windows -c Release -r win-x64` (or `win-arm64`) uses the same single-file settings, with the SDK's default publish folder under `bin/Release/`. Debug builds remain framework-dependent. WPF and WinForms do not support trimming, so trimming and ReadyToRun stay off. There is no installer or signing in v1.

## Run

Copy the matching `Snapper.exe` anywhere, for example `%LOCALAPPDATA%\Programs\Snapper\`, and double-click it. An unsigned downloaded copy may show SmartScreen: choose **More info → Run anyway**, or use **Properties → Unblock** if available. The first start extracts bundled native libraries to `%TEMP%\.net\Snapper\<hash>\`.

Launch at login stores the executable's full path. Turn it off before moving the executable, or turn it on again afterwards so the path is rewritten.

Default shortcuts are Ctrl+Shift+4 (screenshot), Ctrl+Shift+5 (record), and Ctrl+Shift+6 (annotate). Change them in Settings. Snapper starts with only a tray icon; new icons may initially appear under the taskbar's hidden-icons chevron.

## Files and removal

- Settings: `%APPDATA%\Snapper\settings.json`.
- Captures by default: `%USERPROFILE%\Pictures\Screenshots` (the Pictures known folder).
- Launch at login: value `Snapper` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Temporary recordings: `%TEMP%\Snapper`; non-empty originals left after a crash are recovered to the save folder at the next start.

To uninstall, turn off **Launch at login** (or delete the Run value), choose **Exit Snapper**, then delete the executable, `%APPDATA%\Snapper`, and `%TEMP%\.net\Snapper`. Keep or remove your captures as you prefer.

## Regenerate the app icon

The executable icon is generated from the same viewfinder geometry as the runtime tray icon, with a blue tile for visibility in Explorer. Regenerate and commit the asset when its code changes:

```sh
dotnet run --project windows/tools/Snapper.IconGen -- windows/src/Snapper.Windows/Assets/Snapper.ico
```

## Manual acceptance

Build and Core tests do not verify Windows capture, encoding, audio, or GUI behavior. Run the published executable's final checklist in [spec 0047](../docs/specs/done/0047-windows-packaging-release_COMPLETED.md#verification-status) on clean Windows 11 x64 and Arm64 machines without .NET, with mixed-DPI monitors for the marked cases. All manual checks remain pending until recorded by the user.
