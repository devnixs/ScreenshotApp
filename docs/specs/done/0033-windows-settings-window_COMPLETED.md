# 0033 — Build the Windows Settings window with General and Shortcuts tabs

> Source: docs/PRD.md §10 (General + Shortcuts, lines 183–194), §13.1 (single instance, line 251; Settings appears in the taskbar, line 250), §13.2 (shortcut display, line 273), §13.8 (default save folder), §13.9 (lines 321–326: WPF window, launch at login via the `Run` key, shortcut rules, "Another app is using this shortcut."), §13.10 (notification opens the Shortcuts tab), §12 (lines 226, 228) and §13.11 (shortcut-conflict, launch-at-login, and second-launch criteria)
> Depends on: 0025-windows-solution-tray-shell, 0026-windows-file-naming-save-folder, 0027-windows-app-settings, 0029-windows-global-hotkeys-app-mode, 0031-windows-screenshot-rectangle-capture (only for the shared "Couldn't save to <folder>" alert and folder picker in `Support/ErrorAlerts`)

## Context

`windows/` doesn't exist in the repo yet. By the time this spec is built, the lower-numbered Windows specs provide:

- **0025**:
  - `App/AppCoordinator` with `OpenSettings(SettingsTab tab = SettingsTab.General)`. It shows a **placeholder** "Snapper Settings" window, which is a private nested class or method inside `AppCoordinator`, and `Exit()` is the single exit path.
  - `App/TrayIconController`: the menu's **Settings…** item calls `OpenSettings`.
  - `App/SingleInstance`: `SignalPrimary()` already calls `AllowSetForegroundWindow(ASFW_ANY)`. The listener in `App.OnStartup` calls `coordinator.OpenSettings(SettingsTab.General)`.
  - Core `Settings/SettingsTab` = `{ General, Shortcuts, Recording }`.
  - `NativeMethods.txt` already lists `SetForegroundWindow`, `AllowSetForegroundWindow`, and `ASFW_ANY`.
  - This spec **deletes the placeholder** and replaces it with the real window.
- **0026**:
  - Core `Support/SaveFolderPolicy`, with `Prepare(folderPath)`, which creates the folder and checks that it's writable, throwing `SaveFolderException`.
  - Windows `Support/SaveFolder`: `DefaultPath`, `CreatePolicy()`.
  - Windows `Support/ExplorerReveal.OpenFolder(string folderPath) → bool`.
- **0027**:
  - Core `Settings/AppSettings`:
    - `SaveFolder` is a `string?`, where `null` means the default folder;
    - `ScreenshotDestination` and `PlayShutterSound`;
    - `ScreenshotShortcut`, `RecordShortcut`, and `AnnotateShortcut` are `KeyChord?`. JSON `null` means **cleared**, and an absent key means the default.
  - `Settings/ISettingsStore` / `JsonSettingsStore`, with change notification.
  - `Support/KeyChord`:
    - `[Flags] KeyModifiers { Alt, Ctrl, Shift, Win }`;
    - `TryCreate`/`Create`, which reject modifier virtual keys;
    - `DisplayString` (`Ctrl+Shift+4`, with modifiers in the order Ctrl, Alt, Shift, Win);
    - `IsValidBinding` (Ctrl or Alt, no Win). This is the single definition of the §13.9 rule.
- **0029**:
  - Core `App/CapturePurpose` and `App/HotkeyBindings.From(AppSettings)`.
  - Core `App/HotkeyFailureMessage`, including `SettingsWarning = "Another app is using this shortcut."`.
  - Core `App/HotkeyRegistrationState`:
    - `HotkeyStatus { Unbound, Registered, InUseByAnotherApp, DuplicateBinding }`;
    - `Plan`/`Commit`, which notify only for changed chords and retry unchanged failures silently.
  - Windows `App/HotkeyManager`:
    - `Sync(AppSettings)`, `StatusOf(purpose)`;
    - the events `Pressed`, `RegistrationFailed`, and `StatusesChanged`.
  - The coordinator calls `Sync` on every settings change. Its `RegistrationFailed` balloon already calls `OpenSettings(SettingsTab.Shortcuts)` when clicked.
  - 0029's Out of scope asks this spec to add `Suspend()`/`Resume()` to `HotkeyManager`.
- **0031**: Windows `Support/ErrorAlerts` (WinForms `TaskDialog`). Its `string? ShowSaveFolderFailure(string folder, Exception error, AppSettings settings, ISettingsStore store)` shows "Couldn't save to <folder>" with **Choose Folder…**, and its `OpenFolderDialog` setup can be reused.

If a sibling spec, as built, named a type or member differently from what is written here, use the sibling's name. Don't duplicate the types.

### macOS reference (port behavior, don't redesign)

- `Snapper/Settings/SettingsWindowController.swift:23-27`: one window instance, created lazily. `show()` refreshes, activates the app, and brings the existing window to the front. Closing only hides it.
- `Snapper/Settings/SettingsView.swift:19-52` (General tab), `:54-79` (Shortcuts tab).
- `Snapper/Settings/SettingsModel.swift:43-50` (launch at login: the OS state is the source of truth, and the stored bool is only a mirror), `:62-65` (duplicate check), `:85-89` (refresh on appear/activation), `:91-98` (Choose… / Show in Finder, with the shared save-folder failure alert).
- `Snapper/Settings/ShortcutConflictDetector.swift:18-25`: `duplicateOwner` and `duplicateReason`. Tests: `SnapperTests/ShortcutConflictDetectorTests.swift:40-48`.
- `Snapper/Settings/LaunchAtLoginState.swift:13-22` and `Snapper/Support/LaunchAtLogin.swift:15-21`: pure state mapping plus a glue setter that returns an error string. Tests: `SnapperTests/LaunchAtLoginStateTests.swift:5-16`.
- The recorder behavior comes from KeyboardShortcuts' `RecorderCocoa.swift` (the version pinned for the macOS app):
  - Recording starts when the field gets focus, and the field never takes initial focus. All global hotkeys are paused while recording (`KeyboardShortcuts.isPaused = true`).
  - Without modifiers: Tab ends recording and moves focus on, Delete/Backspace clears the binding, and Esc ends recording.
  - An invalid chord beeps and keeps recording. A validation failure shows an alert. A valid chord is saved and ends recording.
  - Recording ends when the window resigns key or the user clicks outside the field. A clear (×) button is shown while a binding exists. The placeholders are "Record Shortcut" and "Press Shortcut".

**macOS facts that don't apply to Windows** (don't port them):
- The symbolic-hotkey conflict detection (`ShortcutConflictDetector.conflicts`, `ShortcutCollision`, `SymbolicHotKeysParser`) and the **Open Keyboard Shortcuts** button. They're replaced by the `RegisterHotKey`-failure warning (§13.9).
- `SMAppService` statuses `.requiresApproval` and `.notFound`, the Open Login Items button, and the "isn't available for this copy" note. They're replaced by the `Run` key (§13.9). See Open question 3 for the Task Manager "Startup apps" disable state.
- The tilde-abbreviated path (Windows shows the full path), `NSApp.activate`, the main-menu key-equivalent conflict policy, and the onboarding window (§13.10: there's none on Windows).

### Not in this spec

PRD §13.9 says the window has three tabs. This spec builds **General** and **Shortcuts** only:
- **0037** adds the **Recording** tab with *Record microphone*.
- **0045** adds the MP4 quality picker, and **0046** adds Format and GIF quality.

Until 0037 lands, the window has two tabs. Build the `TabControl` so that 0037 can add a third `TabItem` without restructuring it.

## User story

As a Snapper user on Windows, I choose **Settings…** from the tray menu, launch Snapper a second time, or click the "is used by another app" notification, and get one Settings window in front. There I can:
- turn on launch at login and pick where screenshots go and whether they play a sound;
- rebind the three shortcuts by pressing the new key combination;
- see which shortcut another app is holding.

## Scope & changes

### Pure logic (Snapper.Core, xUnit-tested, no WPF/WinForms/Win32/registry types)

- **`windows/src/Snapper.Core/Settings/ShortcutValidation.cs`** (`namespace Snapper.Core.Settings`):
  - `enum ShortcutRejection { None, MissingModifier, WindowsKey, Reserved }`.
  - `static ShortcutRejection Validate(KeyChord chord)`. The base rule is **0027's `KeyChord.IsValidBinding`**. Don't re-implement it; only classify why it failed:
    1. `!chord.IsValidBinding` and the chord has `KeyModifiers.Win` → `WindowsKey`. This check comes first, so Win+Ctrl+X is `WindowsKey`.
    2. `!chord.IsValidBinding` otherwise → `MissingModifier`, so Shift+X and plain X are rejected.
    3. The chord is on the reserved list → `Reserved` (see Open question 2 for the recommended list).
    4. Otherwise `None`. Shift is allowed in addition to Ctrl/Alt (§13.9).
  - `static string? RejectionHint(ShortcutRejection r)` returns the inline hint shown under the recorder:
    - `MissingModifier` → `Use Ctrl or Alt with another key.`
    - `WindowsKey` → `Shortcuts with the Windows key aren't supported.`
    - `Reserved` → `This shortcut is reserved.`
    - `None` → `null`.
  - `static CapturePurpose? DuplicateOwner(KeyChord candidate, CapturePurpose recordingFor, IReadOnlyDictionary<CapturePurpose, KeyChord?> bindings)` is a port of `ShortcutConflictDetector.swift:18-21`. It returns the first *other* purpose, in Screenshot/Record/Annotate order, whose binding equals `candidate`. The purpose being recorded never counts against itself, and a `null` (cleared) binding never matches.
  - `static string DuplicateReason(CapturePurpose owner)` returns `Already used by Snapper's “Screenshot”/“Record”/“Annotate” shortcut.` (curly quotes, same text as macOS `ShortcutConflictDetector.swift:23-25`).
  - The warning text isn't defined here: use 0029's `HotkeyFailureMessage.SettingsWarning`.
  - `static bool IsModifierKey(int virtualKey)` is true for VK 0x10/0x11/0x12 (Shift/Control/Menu), 0xA0–0xA5 (left/right variants), 0x5B/0x5C (LWin/RWin), and the lock keys 0x14/0x90/0x91. It is also true for 0xE5 (VK_PROCESSKEY) and 0xE7 (VK_PACKET), which are treated as "not a key". The recorder ignores these keys, apart from updating the held modifiers. This is a superset of the modifier keys that `KeyChord.TryCreate` rejects.

- **`windows/src/Snapper.Core/Settings/ShortcutRecorderSession.cs`**: a pure state machine for one recorder field. The WPF control only forwards input to it and executes the returned effects. This structure makes "every suspension is paired with exactly one resume" testable.
  - It is constructed with `CapturePurpose purpose` and a `Func<IReadOnlyDictionary<CapturePurpose, KeyChord?>> currentBindings`.
  - It has a `bool IsRecording` property, plus `KeyModifiers HeldModifiers`, which uses 0027's modifier flags type.
  - Its methods return `IReadOnlyList<RecorderEffect>`. `RecorderEffect` is a small closed hierarchy, or a record with a kind enum:
    - `SuspendHotkeys`, `ResumeHotkeys`
    - `Commit(KeyChord? chord)`, where `null` means cleared
    - `Beep`
    - `ShowHint(string? text)`, where `null` hides the hint
    - `ShowDuplicateAlert(string reason)`
    - `ReleaseFocus` (the control moves keyboard focus off itself)
    - `PassKeyThrough` (the control leaves the key event unhandled)
  - **`FocusGained()`**: when not recording → `IsRecording = true`, `[SuspendHotkeys, ShowHint(null)]`. Otherwise `[]`.
  - **`FocusLost()`** (also used for window deactivation, hide, or close): when recording → `IsRecording = false`, `HeldModifiers = none`, `[ShowHint(null), ResumeHotkeys]`. Otherwise `[]`. This is the **only** method that emits `ResumeHotkeys`, so suspend and resume always pair up.
  - **`KeyDown(int virtualKey, KeyModifiers modifiers)`**, only while recording (otherwise it returns `[]`, and the control does nothing). Every key is handled (the control sets `e.Handled = true`) except where `PassKeyThrough` is returned:
    1. A modifier key (`IsModifierKey`): update `HeldModifiers` → `[]`. The control redraws its preview.
    2. Esc (0x1B), with any modifiers → `[ReleaseFocus]` (cancel, binding unchanged).
    3. Tab (0x09) without Ctrl and Alt → `[ReleaseFocus, PassKeyThrough]`, so focus moves to the next or previous control.
    4. Backspace (0x08) or Delete (0x2E) with no modifiers → `[Commit(null), ReleaseFocus]` (clear the binding; Open question 1).
    5. Build the chord with `KeyChord.TryCreate(modifiers, virtualKey, out chord)`. If that fails (a virtual key outside 0x01–0xFE) → `[Beep]`. If `Validate` ≠ `None` → `[Beep, ShowHint(RejectionHint(...))]`, and recording continues.
    6. `DuplicateOwner` ≠ null → `[ShowDuplicateAlert(DuplicateReason(owner)), ReleaseFocus]`, with the binding unchanged. The alert takes focus anyway, so recording ends.
    7. Otherwise → `[Commit(chord), ReleaseFocus]`.
  - **`KeyUp(int virtualKey, KeyModifiers modifiers)`**: while recording, update `HeldModifiers` → `[]`.
  - **`ClearClicked()`** (the × button, available while not recording): `[Commit(null)]`. It never suspends hotkeys.
  - `static string PreviewText(bool isRecording, KeyModifiers held, KeyChord? binding)`:
    - not recording, with a binding → `binding.DisplayString` (for example `Ctrl+Shift+4`, from 0027);
    - not recording, no binding → `Record Shortcut`;
    - recording, no modifiers held → `Press Shortcut`;
    - recording, modifiers held → the modifier names in 0027's display order, each followed by `+`, then `…` (for example `Ctrl+Shift+…`).

- **`windows/src/Snapper.Core/Settings/LaunchAtLoginState.cs`**: the Windows replacement for the macOS status mapping.
  - `const string RunValueName = "Snapper";`
  - `static string RunValue(string exePath)` → `"\"" + exePath + "\""` (§13.9: the quoted path of the running exe).
  - `static string? CommandPath(string? runValue)`:
    - `null` or whitespace → `null`;
    - after trimming, if the value starts with `"` → the text up to the next `"` (or the rest, if there is no closing quote);
    - otherwise, the text up to and including the first `.exe` (case-insensitive), or the whole trimmed value if there is no `.exe`. This tolerates arguments that other tools may append.
  - `static bool PointsTo(string? runValue, string exePath)` compares `CommandPath(runValue)` with `exePath` using `StringComparison.OrdinalIgnoreCase`, after trimming both. Don't normalize with `Path` APIs: Core runs on macOS in tests.
  - `static bool IsStartupApprovedDisabled(byte[]? data)`: `null` or empty → `false`; otherwise `(data[0] & 0x01) == 1`. Windows writes 0x02/0x06 for enabled and 0x03/0x07 for disabled.
  - `record LaunchAtLoginState(bool IsOn, LaunchAtLoginNote? Note)` and `enum LaunchAtLoginNote { DisabledInStartupApps, Failed }`, plus `string? FailureMessage`. Alternatively, use a record per note case, whichever the implementer prefers, as long as the tests below can assert it.
  - `static LaunchAtLoginState From(string? runValue, string exePath, byte[]? startupApproved, string? error = null)`:
    - `IsOn = PointsTo(runValue, exePath)`;
    - the note is `Failed(error)` if `error` is non-null. Otherwise it is `DisabledInStartupApps` when `IsOn` and `IsStartupApprovedDisabled(startupApproved)`. Otherwise it is `null`;
    - this mirrors `LaunchAtLoginState.swift:13-22`, where an error overrides the note and `IsOn` always comes from the OS state.
  - `static string NoteText(...)`:
    - `DisabledInStartupApps` → `Snapper is turned off in Startup apps.`
    - `Failed(msg)` → `Couldn't change launch at login: <msg>` (same as macOS).

- **`windows/src/Snapper.Core/Settings/SettingsTabSelection.cs`**: reuse 0025's `SettingsTab { General, Shortcuts, Recording }` and don't redefine it.
  - `static SettingsTab Resolve(SettingsTab? requested, SettingsTab? current, IReadOnlyCollection<SettingsTab> available)`:
    1. Start from `requested ?? current ?? General`.
    2. If that tab isn't in `available`, return `General`.
  - A plain reopen keeps the tab the window last showed (as on macOS), and the notification forces `Shortcuts`.
  - Until 0037 adds the Recording tab, `available` = {General, Shortcuts}, so a `Recording` request falls back to `General`. 0037 adds `Recording` to the window's available set.

### Platform glue (Snapper.Windows, manually verified)

- **`windows/src/Snapper.Windows/Settings/LaunchAtLogin.cs`**: a `static class` using `Microsoft.Win32.Registry` (it is part of the Windows desktop framework, so no NuGet package is needed).
  - Key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Snapper`, type `REG_SZ`.
  - It reads the StartupApproved key `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`, value `Snapper` (`REG_BINARY`).
  - `ExePath` = `Environment.ProcessPath`. If that is null, launch at login is unavailable: show the `Failed` note with "Couldn't find Snapper's location." and keep the toggle off.
  - `static LaunchAtLoginState Read(string? error = null)` reads both values and returns `LaunchAtLoginState.From(...)`.
  - `static string? SetEnabled(bool on)`:
    - **on**: writes `RunValue(ExePath)`, overwriting any existing value (§13.9: "Turning it on rewrites the value"), and deletes the `Snapper` StartupApproved value if present (Open question 3).
    - **off**: deletes the `Run` value (no error if it is missing) and leaves the StartupApproved value alone.
    - It catches `UnauthorizedAccessException`, `SecurityException`, and `IOException`, and returns `ex.Message`. On success it returns `null`.
  - **Source of truth:** the registry. Nothing at app launch writes the registry from a stored bool, which mirrors `SettingsModel.swift:43-50`. After every read, write the state into 0027's `AppSettings.LaunchAtLogin`, as a mirror only (0027 schema note).
  - `static void OpenStartupApps()` opens `ms-settings:startupapps` via `Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })`.

- **`windows/src/Snapper.Windows/Settings/ShortcutRecorder.cs`** (+ a default style in `Settings/ShortcutRecorder.xaml`, or a code-built template): a focusable WPF `Control`, one per purpose, that owns a `ShortcutRecorderSession`.
  - **Appearance:** a text-box-like rounded border, about 160 DIP wide, with centered `PreviewText`. The border uses the accent color while recording. Show a small **×** clear button (Segoe Fluent Icons `\uE711`, tooltip "Clear") only when not recording and a binding exists. The button isn't a tab stop. `Focusable = true`, `IsTabStop = true`, and the cursor is the default arrow.
  - **Start:** `GotKeyboardFocus` → `session.FocusGained()`. A click focuses the control. The Settings window must not give a recorder initial focus. Set the window's initial focused element to the `TabControl`, so that opening on the Shortcuts tab doesn't start recording, as macOS's `preventBecomingKey` does.
  - **Keys:** handle `PreviewKeyDown` and `PreviewKeyUp`.
    - Resolve the real key:
      - `e.Key == Key.System` → `e.SystemKey`, so Alt combinations and F10 arrive;
      - `e.Key == Key.ImeProcessed` → `e.ImeProcessedKey`;
      - `e.Key == Key.DeadCharProcessed` → `e.DeadCharProcessedKey`.
    - Convert the key with `KeyInterop.VirtualKeyFromKey`, and read the modifiers from `Keyboard.Modifiers`. `ModifierKeys.Windows` maps to the Win flag.
    - Forward the key to the session and run the effects. Set `e.Handled = true` unless `PassKeyThrough` was returned, so Alt combinations don't trigger WPF access keys or the system menu.
  - **End:** call `session.FocusLost()` from:
    - `LostKeyboardFocus`;
    - the owning window's `Deactivated` event (the user Alt+Tabs away while recording; without this, global hotkeys would stay suspended);
    - `IsVisibleChanged` → false.
  - **Effects:**
    - `SuspendHotkeys` / `ResumeHotkeys` → `AppCoordinator.SuspendHotkeys()` / `ResumeHotkeys()` → `HotkeyManager.Suspend()` / `Resume(current)` (see below);
    - `Commit(chord)` → write the purpose's shortcut to `AppSettings` through `ISettingsStore`, which persists it and raises the change notification. Then 0029's `HotkeyManager` and the tray menu labels pick it up;
    - `Beep` → `System.Media.SystemSounds.Beep.Play()`;
    - `ShowHint` → set the row's hint text;
    - `ShowDuplicateAlert(reason)` → `MessageBox.Show(settingsWindow, reason, "Snapper", MessageBoxButton.OK, MessageBoxImage.Warning)`. Run `ReleaseFocus` first, so the session resumes the hotkeys before the modal loop starts;
    - `ReleaseFocus` → move keyboard focus to the window's `TabControl` (not `Keyboard.ClearFocus()`, which would leave the window without a focused element);
    - `PassKeyThrough` → leave `e.Handled = false`.
  - **Order:** when one key returns both `Commit` and `ReleaseFocus`, run `Commit` first. The `ResumeHotkeys` emitted by the resulting `FocusLost` then registers the **new** binding.

- **Hotkey suspension (this spec adds it to 0029's types):**
  - **Core `App/HotkeyRegistrationState`** (0029's file), extended:
    - Add `HotkeyStatus.Suspended`.
    - Add `public IReadOnlyList<CapturePurpose> Suspend()`. It returns the purposes that are currently `Registered`, in `CapturePurposes.All` order, and sets their status to `Suspended`. `ChordOf` is kept. `InUseByAnotherApp`, `DuplicateBinding`, and `Unbound` are left unchanged, so the Settings warning stays visible while recording.
    - `Plan` needs no rule change. `Suspended` isn't `Registered`, so it is never unregistered again and is always re-registered.
    - `Commit` needs no rule change. Re-registering an unchanged chord after a suspension isn't notified, even if it now fails. A chord that changed during the suspension *is* notified when it fails, which is PRD §13.10's "whenever a binding changes".
  - **Windows `App/HotkeyManager`** (0029's file), extended:
    - `public bool IsSuspended { get; }`.
    - `public void Suspend()`: a no-op if already suspended. Otherwise it sets `IsSuspended`, calls `UnregisterHotKey` for each purpose returned by `state.Suspend()`, and raises `StatusesChanged`.
    - `Sync(AppSettings)` while suspended returns immediately and touches neither the state nor the registrations. The coordinator still updates the tray menu labels on every settings change, so a recorded binding shows in the menu at once.
    - `public void Resume(AppSettings current)`: a no-op if not suspended. Otherwise it clears `IsSuspended` and runs the normal `Sync(current)`, which raises `RegistrationFailed` only for changed chords, and `StatusesChanged`.
    - `Dispose()` while suspended unregisters nothing extra and doesn't throw.
  - The recorder's `SuspendHotkeys`/`ResumeHotkeys` effects call these through the coordinator (`AppCoordinator.SuspendHotkeys()` / `ResumeHotkeys()`, which pass `settingsStore.Current` to `Resume`).
- **The settings warning** reads `hotkeyManager.StatusOf(purpose) == HotkeyStatus.InUseByAnotherApp` and updates on `StatusesChanged`. A cleared binding is `Unbound`, so it never shows the warning. A suspended one doesn't either.
- **The notification click** (0029) already calls `OpenSettings(SettingsTab.Shortcuts)`. It now reaches the real window. Nothing else changes.

- **`windows/src/Snapper.Windows/Settings/SettingsViewModel.cs`**: `sealed class SettingsViewModel : INotifyPropertyChanged`, created once by `AppCoordinator` with the shared `ISettingsStore`/`AppSettings`, the `HotkeyManager`, and the `SaveFolder` service. Views never touch the registry, `RegisterHotKey`, or the JSON file directly.
  - **Launch at login:**
    - `LaunchAtLogin` (the state) and `SetLaunchAtLogin(bool)`, which calls `LaunchAtLogin.SetEnabled`, then `Read(error)`, then raises the change;
    - `LaunchAtLoginNoteText`, plus `ShowsOpenStartupApps` (true for `DisabledInStartupApps`).
  - **General:** `SaveFolderPath`, which is `AppSettings.SaveFolder ?? SaveFolder.DefaultPath`, shown as a full path. Also `ScreenshotDestination` and `PlayShutterSound`. All three read and write straight through `AppSettings`/`ISettingsStore`. Raise property changes when the settings store reports an external change, for example the "Couldn't save to" alert's **Choose Folder…**.
  - **Shortcut rows:** per `CapturePurpose`, a row object with:
    - `Label` ("Screenshot:", "Record:", "Annotate:");
    - `Binding` (`KeyChord?`);
    - `HintText`;
    - `ShowsConflictWarning`, which is `HotkeyManager.StatusOf(purpose) == HotkeyStatus.InUseByAnotherApp`. When it is set, the row shows `HotkeyFailureMessage.SettingsWarning`. It updates on `StatusesChanged`, including while Settings is open.
  - **`ChooseSaveFolder()`**: use the same `Microsoft.Win32.OpenFolderDialog` setup as 0031's `ErrorAlerts`, owned by the Settings window:
    - `Title = "Choose Save Folder"`, `Multiselect = false`;
    - `InitialDirectory` = the nearest existing ancestor of the current folder (`ScreenshotOutputPlan.NearestExistingFolder`). If 0031 extracted a helper for this, call it.

    On OK, it stores the folder in `AppSettings.SaveFolder` and persists it. No writability check happens here: an unwritable folder surfaces at save time (§9), like macOS.
  - **`OpenSaveFolder()`** (the **Open folder** button), a port of `SettingsModel.swift:95-98`:
    1. Call `SaveFolder.CreatePolicy().Prepare(SaveFolderPath)`, the same create and check the save flow uses, then `ExplorerReveal.OpenFolder(path)` (0026).
    2. If `Prepare` throws, show `ErrorAlerts.ShowSaveFolderFailure(folder, error, settings, store)` (0031: "Couldn't save to <folder>" with **Choose Folder…**).
    3. If `OpenFolder` returns `false`, show `ErrorAlerts.ShowError("Couldn't open folder", SaveFolderPath)`.
  - **`RefreshOnActivate()`**: re-read the launch-at-login state. Call it when the window is shown and from `Window.Activated`, so a Run value removed in Task Manager or regedit is reflected when the user comes back. This ports `SettingsModel.swift:85-89`.

- **`windows/src/Snapper.Windows/Settings/SettingsWindow.xaml` + `.xaml.cs`**: a WPF `Window` with these properties:
  - `Title="Snapper Settings"`, `ResizeMode="NoResize"`, `SizeToContent="Height"`, `Width` about 480 DIP, `ShowInTaskbar="True"` (§13.1: Settings appears in the taskbar and Alt+Tab while open);
  - `WindowStartupLocation="CenterScreen"` on the first show only;
  - the font and colors use the WPF defaults (no custom theme).
  - Don't use `_` access-key mnemonics in any label, so Alt+letter combinations reach the recorder unambiguously.
  - It contains a `TabControl` with these tabs:
    - **General** (`TabItem Header="General"`):
      - `CheckBox` "Launch at login". When a note exists, show the note text below it as secondary (gray) text, plus a **Open Startup Apps** link button for `DisabledInStartupApps`.
      - A **Save folder** group:
        - the full folder path in a `TextBlock`, with `TextTrimming="CharacterEllipsis"` and the full path as the `ToolTip`;
        - the buttons **Choose…** and **Open folder**.
      - A **Screenshot destination** group with three `RadioButton`s: "Save to file", "Copy to clipboard", "Both".
      - `CheckBox` "Play shutter sound".
    - **Shortcuts** (`TabItem Header="Shortcuts"`): three rows (Screenshot, Record, Annotate). Each row has:
      - the label;
      - the `ShortcutRecorder`;
      - below it, when `ShowsConflictWarning` is set, a line with a warning glyph (Segoe Fluent Icons `\uE7BA`, in an amber color) and "Another app is using this shortcut.";
      - below that, when set, the hint text in secondary color.
      - There is no System Settings or "Open Keyboard Shortcuts" button (§13.9).
  - The `TabControl`'s tabs are tagged with their `SettingsTab` value. The window exposes `SelectedTab` and `AvailableTabs` (here {General, Shortcuts}).
  - **Closing:** handle `Closing` with `e.Cancel = true; Hide();`, so the view model, the selected tab, and the window stay alive, like macOS `isReleasedWhenClosed = false`. The exception is when `AllowClose` was set: `AppCoordinator.Exit()` sets it before `Application.Current.Shutdown()`, so exit is never blocked.

- **`windows/src/Snapper.Windows/Settings/SettingsWindowController.cs`** (or members on `AppCoordinator`; one owner, created once):
  - `Show(SettingsTab? tab)`:
    1. Lazily create the single `SettingsWindow`.
    2. Select `SettingsTabSelection.Resolve(tab, window.SelectedTab, window.AvailableTabs)`. On the first show, the current tab is `null`.
    3. Call `vm.RefreshOnActivate()`.
    4. If the window is minimized, set `WindowState = Normal`.
    5. `Show()`, `Activate()`, then `SetForegroundWindow(hwnd)` via CsWin32. Calling `Show` again never creates a second window.
  - **Foreground rights:**
    - From the tray menu, Snapper owns the foreground input, so `Activate` works.
    - On the second-launch path, 0025's `SignalPrimary()` already grants the foreground with `AllowSetForegroundWindow(ASFW_ANY)`, so `SetForegroundWindow` works.
- **`App/AppCoordinator` (0025)**:
  - **Delete 0025's placeholder Settings window** (the nested class or method and its field).
  - Change the signature to `OpenSettings(SettingsTab? tab = null)`, which calls `settingsWindowController.Show(tab)`.
  - **Settings…** in the tray menu and the single-instance callback in `App.OnStartup` pass `null` (change 0025's `SettingsTab.General` argument there), so a reopen keeps the current tab. The notification click passes `Shortcuts`.
  - Add `SuspendHotkeys()` / `ResumeHotkeys()` (see above). The recorders reach them through the view model.
  - `OpenSettings` opens in **every** app mode. While a selection overlay is up, the window opens behind the topmost overlays and is there once the selection ends. During a recording, the tray can't open Settings (a click stops the recording, §13.3), but a second launch can.
  - `Exit()` sets `AllowClose` on the window (if it was created) before shutting down.

- **`NativeMethods.txt`**: no additions (0025 already lists `SetForegroundWindow`, `AllowSetForegroundWindow`, and `ASFW_ANY`).

### PRD

- No PRD edit in this spec (batch rule: the orchestrator updates the PRD once the open questions are answered). When they are answered, §13.9 needs:
  - the clearing behavior (Q1);
  - the reserved-chord list (Q2);
  - the Startup-apps note (Q3).

## Acceptance criteria

- [ ] **Settings…** in the tray menu opens the "Snapper Settings" window in front, with keyboard focus. Choosing it again, or launching `Snapper.exe` a second time, brings the **same** window to the front with its current tab: no second window and no second tray icon (§13.1, §13.11).
- [ ] The window shows a taskbar button while open, and none after it is closed. Closing and reopening keeps the selected tab and values.
- [ ] The window has the tabs **General** and **Shortcuts**. The Recording tab arrives with 0037. 0025's placeholder window and its text "Settings will appear here." are gone.
- [ ] **General defaults** on a fresh `settings.json`:
  - Launch at login off;
  - save folder `%USERPROFILE%\Pictures\Screenshots`, shown as a full path;
  - destination **Both**;
  - Play shutter sound **on**.
- [ ] **General persistence:** every General control persists in `%APPDATA%\Snapper\settings.json` across restarts and is used by the screenshot flow (0031).
- [ ] **Launch at login:**
  - Turning it on writes `HKCU\…\Run\Snapper` = `"<full path of the running Snapper.exe>"`, and turning it off deletes the value.
  - The checkbox is checked only when the value exists and points to the running exe. A value pointing to another path shows unchecked, and checking it rewrites the value.
  - Snapper starts at the next sign-in (§13.11).
- [ ] **Choose…** stores a new folder. **Open folder** opens the folder, creating the default one if it's missing.
- [ ] **Shortcuts:** three recorders show `Ctrl+Shift+4`, `Ctrl+Shift+5`, `Ctrl+Shift+6` by default.
  - Clicking a recorder shows "Press Shortcut".
  - Pressing a valid chord (for example Ctrl+Alt+S) saves it. The global hotkey and the tray menu label change immediately, with no restart (§12).
  - Pressing the recorder's *current* binding while recording records it instead of starting a capture.
- [ ] **Shortcut validation:**
  - A chord without Ctrl or Alt (for example Shift+4 or plain 4) beeps, shows "Use Ctrl or Alt with another key.", and keeps recording.
  - A chord with the Windows key is rejected with "Shortcuts with the Windows key aren't supported."
  - A chord another Snapper shortcut uses shows the alert "Already used by Snapper's “Record” shortcut." and leaves the binding unchanged.
- [ ] Esc cancels recording without changing the binding. Tab leaves the recorder. Backspace/Delete clear the binding, and so does the × button: the tray menu item then shows no shortcut, and the hotkey is unregistered.
- [ ] While another app holds a binding, "Another app is using this shortcut." appears under that recorder, the other shortcuts keep working, and the tray notification appears (0029). Clicking the notification opens Settings on the **Shortcuts** tab (§13.10, §13.11).
- [ ] Leaving the window while recording (Alt+Tab, clicking elsewhere, closing it) ends recording and re-enables all global shortcuts.

## Edge cases

- **Pressing the current binding:** while a recorder records, every Snapper hotkey is unregistered (`HotkeyManager.Suspend`), so the key reaches the window. This matches macOS `KeyboardShortcuts.isPaused`. When recording ends for any reason, `Resume` re-registers.
- **Another app grabs a Snapper chord during the suspension window** (a few seconds): on `Resume`, that chord fails. The Settings warning appears, but there's no balloon, because the chord is unchanged. That's acceptable, since Settings is open and shows the warning.
- **Settings change from elsewhere while suspended** (for example the "Couldn't save to" alert changes the folder): `Sync` is skipped, and `Resume` applies whatever is current.
- **A binding held by another app:** that app's `RegisterHotKey` consumes the key, so the recorder never sees it and nothing is recorded. That's acceptable. The warning case arises when another app grabbed a binding that Snapper already had saved (at launch or after a change).
- **Window deactivated, hidden, or closed while recording:** `FocusLost` resumes the hotkeys. If the app exits while recording, the process ends, so no cleanup is needed.
- **Alt combinations:** WPF delivers them as `Key.System`. The recorder must read `SystemKey` and set `Handled`, or Alt+S gets lost and Alt alone focuses the system menu. F10 also arrives as `Key.System`, without Ctrl/Alt, so it's rejected (MissingModifier).
- **AltGr layouts:** AltGr arrives as Ctrl+Alt. Ctrl+Alt+<key> is accepted even if it types a character on the user's layout. No special rule, as on macOS.
- **Numpad vs top-row digits** have different virtual keys, so they are different chords. That's fine. Left and right modifiers aren't distinguished.
- **IME active:** use `ImeProcessedKey`. A chord with Ctrl/Alt normally bypasses the IME anyway.
- **Duplicate check uses the stored bindings**, read fresh from `AppSettings` at key time, not cached when recording starts.
- **Clearing a binding that had failed registration:** the warning disappears, because a cleared binding is never "failed".
- **Re-recording the same chord** the purpose already has: not a duplicate (it doesn't count against itself), so it's committed. It's a no-op change, and `Resume` re-registers it without a notification.
- **New binding fails to register:** the warning appears immediately after recording ends, and 0029's tray notification is shown because the binding changed.
- **Launch at login:**
  - A `Run` value written by an older copy in another folder → unchecked. Checking it rewrites the value to this exe.
  - Value with arguments or unquoted → `CommandPath` handles both.
  - Registry write denied (group policy) → the checkbox reflects the real state, and the `Failed` note shows the exception message.
  - Running from `bin\Debug` → it registers that path. For the sign-in check, use a published copy.
- **Disabled in Task Manager → Startup apps:** checked, with "Snapper is turned off in Startup apps." and **Open Startup Apps** (Q3). Unchecking deletes the `Run` value. Checking again re-enables it (it deletes the StartupApproved entry).
- **Save folder deleted after it was chosen:** the row still shows the path. **Open folder** recreates it like the save flow, or shows the "Couldn't save to" alert.
- **Settings opened during a selection overlay** (second launch): the window opens but stays behind the topmost overlays until the selection ends. Nothing crashes, and the selection isn't cancelled.
- **Settings opened during a recording** (second launch) and a recorder is focused: Snapper's hotkeys are suspended, so Ctrl+Shift+5 doesn't stop the recording until recording in the recorder ends. The pill's Stop still works (0035).
- **Mixed-DPI monitors:** the window is plain WPF under Per-Monitor V2 and needs no pixel geometry. Moving it between a 100% and a 150% monitor must re-scale it crisply, which WPF does automatically. No `SetWindowPos` positioning is needed.
- **Before 0037:** there is no Recording tab, and no code path requests one.

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/ShortcutValidationTests.cs`):
  1. `Validate(Ctrl+Shift+4)` == `None`. The same holds for `Alt+4`, `Ctrl+Alt+S`, `Alt+Shift+F5`, and `Ctrl+Delete`.
  2. `Validate(Shift+4)` == `MissingModifier`, and `Validate(4 with no modifiers)` == `MissingModifier`.
  3. `Validate(Win+Ctrl+4)` == `WindowsKey`, and `Validate(Win+4)` == `WindowsKey` (the Win check comes before the modifier check).
  4. Each reserved chord from Q2 (Alt+F4, Ctrl+F12, Alt+Space, Alt+Tab, Ctrl+Esc, Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+A, Ctrl+S, Ctrl+W, Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z, Ctrl+Enter) == `Reserved`, while `Ctrl+Shift+C` and `Ctrl+Alt+Z` == `None`.
  5. `RejectionHint` returns the exact three strings above and `null` for `None`.
  6. Port of `ShortcutConflictDetectorTests.swift:40-48`:
     - `DuplicateOwner(Ctrl+Shift+5, Screenshot, defaults)` == `Record`;
     - `DuplicateOwner(Ctrl+Shift+4, Screenshot, defaults)` == `null` (self);
     - `DuplicateOwner(Ctrl+Alt+9, Annotate, defaults)` == `null`;
     - with Record's binding `null`, `DuplicateOwner(Ctrl+Shift+5, Screenshot, …)` == `null`.
  7. `DuplicateReason(Record)` == `Already used by Snapper's “Record” shortcut.`, with the same pattern for Screenshot and Annotate.
  8. `Validate` agrees with `KeyChord.IsValidBinding` for every non-reserved chord in cases 1–3: `None` ⇔ `IsValidBinding`.
  9. `IsModifierKey` is true for 0x10, 0x11, 0x12, 0xA0–0xA5, 0x5B, 0x5C, 0x14, 0x90, 0x91, 0xE5, and 0xE7. It is false for 0x34 ('4'), 0x41 ('A'), 0x1B, 0x08, 0x2E, and 0x70 (F1).
- **Unit tests** (`windows/tests/Snapper.Core.Tests/ShortcutRecorderSessionTests.cs`), with a session for `Screenshot` and default bindings:
  1. `FocusGained` → `[SuspendHotkeys, ShowHint(null)]` and `IsRecording`. A second `FocusGained` → `[]`.
  2. `FocusLost` while recording → ends with `ResumeHotkeys`, and `IsRecording` is false. `FocusLost` while idle → `[]`.
  3. `KeyDown` while idle → `[]` for any key.
  4. Ctrl down (0x11, Ctrl) → `[]` and `HeldModifiers == Ctrl`. Then `PreviewText(true, Ctrl, …)` == `Ctrl+…`. Ctrl+Shift held → `Ctrl+Shift+…`. With no modifiers → `Press Shortcut`.
  5. Esc → `[ReleaseFocus]`, with no `Commit`. Ctrl+Esc → `[ReleaseFocus]` too.
  6. Tab with no modifiers → `[ReleaseFocus, PassKeyThrough]`. Shift+Tab → the same.
  7. Backspace with no modifiers → `[Commit(null), ReleaseFocus]`, and the same for Delete. Ctrl+Backspace is a normal chord → `[Commit(Ctrl+Backspace), ReleaseFocus]`.
  8. Shift+4 → `[Beep, ShowHint("Use Ctrl or Alt with another key.")]`, and still `IsRecording`. Win+Ctrl+4 → `[Beep, ShowHint(WindowsKey hint)]`.
  9. Ctrl+Shift+5 (Record's binding) → `[ShowDuplicateAlert("Already used by Snapper's “Record” shortcut."), ReleaseFocus]`, with no `Commit`.
  10. Ctrl+Shift+4 (its own binding) → `[Commit(Ctrl+Shift+4), ReleaseFocus]`.
  11. Ctrl+Alt+S → `[Commit(Ctrl+Alt+S), ReleaseFocus]`.
  12. Bindings are read at key time: change the `currentBindings` source between `FocusGained` and `KeyDown` so that Annotate becomes Ctrl+Alt+S. Ctrl+Alt+S → the duplicate alert naming Annotate.
  13. Pairing invariant: for a scripted sequence (gain, invalid key, Esc, lose, gain, valid chord, lose, lose), the count of `SuspendHotkeys` equals the count of `ResumeHotkeys`, and they alternate.
  14. `ClearClicked` while idle → `[Commit(null)]`, with no suspend or resume.
  15. `PreviewText(false, none, null)` == `Record Shortcut`, and `PreviewText(false, none, Ctrl+Shift+4)` == `Ctrl+Shift+4`.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/LaunchAtLoginStateTests.cs`), with `exe = C:\Apps\Snapper\Snapper.exe`:
  1. `RunValue(exe)` == `"C:\Apps\Snapper\Snapper.exe"` (with the quotes).
  2. `CommandPath`:
     - quoted → the inner path;
     - quoted with args `"C:\x\Snapper.exe" --tray` → `C:\x\Snapper.exe`;
     - unquoted with a space `C:\Program Files\Snapper\Snapper.exe --x` → `C:\Program Files\Snapper\Snapper.exe`;
     - unquoted without `.exe` → the trimmed value;
     - `null`, empty, or whitespace → `null`;
     - an opening quote with no closing quote → the rest.
  3. `From(null, exe, null)` → `IsOn` false, no note.
  4. `From(RunValue(exe), exe, null)` → `IsOn` true, no note. The same holds with different casing (`c:\apps\snapper\SNAPPER.EXE`).
  5. `From("\"D:\\Old\\Snapper.exe\"", exe, null)` → `IsOn` false (it points elsewhere).
  6. StartupApproved:
     - `From(RunValue(exe), exe, [0x03, 0, …])` → `IsOn` true, note `DisabledInStartupApps`. The same holds with `0x07`.
     - With `[0x02, …]` or `[0x06, …]` → no note.
     - An empty array → no note.
  7. `From(null, exe, [0x03])` → `IsOn` false, no note. The disabled note only applies when on.
  8. Error overrides the note but not `IsOn`:
     - `From(RunValue(exe), exe, [0x03], error: "x")` → `IsOn` true, note `Failed("x")`;
     - `From(null, exe, null, "x")` → `IsOn` false, note `Failed("x")`. This ports `LaunchAtLoginStateTests.swift:14`.
  9. `NoteText`: `Snapper is turned off in Startup apps.` and `Couldn't change launch at login: x`.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/SettingsTabSelectionTests.cs`), with `avail` = {General, Shortcuts}:
  - `Resolve(Shortcuts, General, avail)` == `Shortcuts`
  - `Resolve(null, Shortcuts, avail)` == `Shortcuts`
  - `Resolve(null, null, avail)` == `General`
  - `Resolve(Recording, Shortcuts, avail)` == `General`
  - with all three tabs available, `Resolve(Recording, null, all)` == `Recording`
- **Unit tests**, added to 0029's `windows/tests/Snapper.Core.Tests/HotkeyRegistrationStateTests.cs`:
  1. `SuspendReturnsOnlyRegistered`: after a sync with Screenshot `Registered`, Record `InUseByAnotherApp`, and Annotate `Unbound`:
     - `Suspend()` == [Screenshot];
     - Screenshot is `Suspended`, Record stays `InUseByAnotherApp`, and Annotate stays `Unbound`;
     - `ChordOf(Screenshot)` is unchanged.
  2. `SuspendTwiceReturnsEmpty`: a second `Suspend()` returns [].
  3. `ResumeUnchangedReRegistersSilently`: after case 1, `Plan(same desired)` → `ToUnregister` empty, and `ToRegister` contains Screenshot/C4 and Record/C5.
     - A `Commit` where both succeed returns [], and both become `Registered`.
     - A `Commit` where Screenshot fails also returns [], because the chord is unchanged.
  4. `ResumeChangedChordNotifiesFailure`: after suspending with Screenshot C4, `Plan(D(7,5,6))` has an empty `ToUnregister` (Screenshot is `Suspended`, not `Registered`) and `ToRegister` containing Screenshot/C7. A `Commit` that fails returns [C7].
  5. `ResumeClearedChordBecomesUnbound`: after suspending, a desired `null` for Screenshot → no unregister and no register. After `Commit`, Screenshot is `Unbound`.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` succeed, including all earlier specs' tests.
- **Manual verification** (on a Windows 11 machine; can't be automated):
  1. Start Snapper. Right-click the tray icon → **Settings…**. Expected: the real "Snapper Settings" window (not the 0025 placeholder) is in front with focus, on the General tab, with a taskbar button. Switch to another app, then left-click the tray → **Settings…**. Expected: the same window comes to the front, and no second window appears.
  2. Run `Snapper.exe` again (from Explorer). Expected: the existing Settings window comes to the front, not just a flashing taskbar button. There is still only one tray icon, and the second process has exited (Task Manager shows one Snapper).
  3. Close the window with the × button and with Alt+F4. Expected: the taskbar button disappears and Snapper keeps running. Reopen it. Expected: the last tab and the values are unchanged.
  4. Delete `%APPDATA%\Snapper\settings.json` and restart Snapper. Expected: launch at login off, `C:\Users\<you>\Pictures\Screenshots`, Both, and shutter sound on.
  5. Choose a new folder with **Choose…** (create a new folder in the dialog), set **Copy to clipboard**, and turn off the shutter sound. Exit Snapper from the tray and restart it. Expected: everything persists. Press Ctrl+Shift+4 and drag. Expected: the image is on the clipboard only, with no sound. Set **Both** and check that the file lands in the new folder.
  6. Point the save folder at an empty folder, delete that folder in Explorer, and click **Open folder**. Expected: the folder is recreated and opened.
  7. Launch at login, using a published `Snapper.exe` (0047) or the Debug exe:
     - Check **Launch at login**. Expected: `regedit` shows `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Snapper` = the quoted exe path.
     - Sign out and back in. Expected: the tray icon appears without a manual launch.
     - Uncheck it. Expected: the value is gone.
  8. With launch at login on, delete the `Run` value in regedit, then click back into Settings. Expected: the checkbox unchecks. Set the value to `"C:\Other\Snapper.exe"`. Expected: it stays unchecked, and checking it rewrites the value to the real path.
  9. With launch at login on, disable Snapper in Task Manager → Startup apps, then return to Settings. Expected: the box is checked, and "Snapper is turned off in Startup apps." appears with **Open Startup Apps**, which opens Settings → Apps → Startup. Uncheck and recheck the box. Expected: Task Manager shows Snapper enabled again.
  10. Shortcuts:
      - Click the Screenshot recorder. Expected: "Press Shortcut", accent border.
      - Hold Ctrl+Alt. Expected: "Ctrl+Alt+…".
      - Press S. Expected: the recorder shows `Ctrl+Alt+S`, and recording ends.
      - Open the tray menu. Expected: Screenshot shows `Ctrl+Alt+S`.
      - Press Ctrl+Alt+S anywhere. Expected: the overlay opens.
      - Press Ctrl+Shift+4. Expected: nothing.
  11. Click the Screenshot recorder and press its current binding (Ctrl+Alt+S). Expected: no overlay opens, and the binding stays `Ctrl+Alt+S`. Click the Record recorder and press Ctrl+Alt+S. Expected: the alert "Already used by Snapper's “Screenshot” shortcut.", and Record keeps `Ctrl+Shift+5`.
  12. In a recorder, press Shift+4, then 4, then Win+Ctrl+4 (if the shell lets it through). Expected: a beep and the matching hint each time, and recording continues. Press Alt+F4 while recording. Expected: "This shortcut is reserved." and the window stays open. Press Esc. Expected: recording ends, the binding is unchanged, and the hint disappears.
  13. Focus a recorder, then press Backspace. Expected: it shows "Record Shortcut", the tray item shows no shortcut, and the old chord no longer triggers. Set it back with the recorder. Clear another one with the × button: same result.
  14. Click a recorder, then Alt+Tab to another app and press Ctrl+Shift+5. Expected: recording starts, because the hotkeys were resumed on deactivation. Repeat by closing the window while recording.
  15. Shortcut held by another app: use a small tool or second program that calls `RegisterHotKey` for Ctrl+Shift+6, then restart Snapper.
      - Expected: the tray notification "Ctrl+Shift+6 is used by another app. Change it in Settings."
      - Click the notification. Expected: Settings opens on the **Shortcuts** tab, with "Another app is using this shortcut." under Annotate.
      - Expected: Ctrl+Shift+4/5 still work, and Annotate still works from the tray menu.
      - Rebind Annotate to Ctrl+Alt+6. Expected: the warning disappears.
  16. Move the Settings window between a 100% and a 150% monitor. Expected: the text stays crisp and the layout scales correctly.

## Resolved decisions

- All three recommendations accepted (clearing a binding, the reserved-chord list with "This shortcut is reserved.", and the Startup-apps note with **Open Startup Apps**). Recorded in PRD §13.9.

## Out of scope

- The **Recording** tab and *Record microphone* with the privacy check: **0037**. MP4 quality: **0045**. Format and GIF quality: **0046**.
- `RegisterHotKey` registration, the app-mode rules, the tray failure notification text and its trigger, and the tray menu's shortcut labels: **0029**. This spec only adds `Suspend`/`Resume` and the `Suspended` status.
- The single-instance mechanism itself (mutex, signal, `AllowSetForegroundWindow`): **0025**. This spec only connects it to the real window.
- The settings file format, defaults, and `KeyChord` display: **0027**. Folder resolution and creation: **0026**. The "Couldn't save to" alert: **0031**.
- The app icon on the Settings window: **0047**.
- A "Reset to defaults" button, a writability check at pick time, and anything not in PRD §10/§13.9 (same exclusions as macOS 0010). Onboarding: not on Windows (§13.10).

## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**. `dotnet test windows/Snapper.slnx` passed **685 tests**, including **102 new cases**, with zero failures or skips. The new cases cover shortcut validation and exact hints, recorder focus/effect pairing and current-binding lookup, launch command/StartupApproved state mapping, tab selection, and suspended hotkey registration/retry decisions.

The placeholder is replaced by a reusable General/Shortcuts window. Its view model uses the existing typed settings/store, and the registry remains the launch-at-login source of truth. Recorders release their hotkeys on focus loss, deactivation, hide, or close; WPF Tab traversal runs from the recorder's position after ending the pure session, avoiding a cycle back into the same field. PRD §13.9 already includes every resolved decision; no PRD or AGENTS.md edit was needed.

GUI behavior has **not** been verified. On Windows 11:

- [ ] not yet run by user: Open Settings from either tray menu and reopen it from another app; confirm one focused Settings window and one taskbar button.
- [ ] not yet run by user: Launch Snapper a second time; confirm the existing Settings window comes forward and only one process/tray icon remains.
- [ ] not yet run by user: Close Settings using × and Alt+F4, then reopen; confirm Snapper keeps running and the selected tab and values persist.
- [ ] not yet run by user: Restart with a fresh settings file; confirm General defaults (launch off, Pictures\Screenshots, Both, sound on).
- [ ] not yet run by user: Choose a folder, change destination and sound, restart, then capture; confirm persistence and screenshot behavior.
- [ ] not yet run by user: Delete the selected save folder and use Open folder; confirm recreation and Explorer opening.
- [ ] not yet run by user: Toggle launch at login, inspect the quoted Run value, sign out/in, and toggle off; confirm startup and value removal.
- [ ] not yet run by user: Delete or replace the Run value with another exe path, then reactivate Settings; confirm the checkbox reflects the registry and checking it restores this exe path.
- [ ] not yet run by user: Disable Snapper in Task Manager Startup apps; confirm the checked toggle, note and Open Startup Apps link, then uncheck/recheck to re-enable it.
- [ ] not yet run by user: Record Ctrl+Alt+S for Screenshot; confirm modifier preview, immediate tray-label change, new global shortcut, and removal of the old binding.
- [ ] not yet run by user: Re-record a current binding without launching capture, then try another Snapper binding; confirm the duplicate alert and unchanged binding.
- [ ] not yet run by user: Try plain/Shift-only, Windows-key and reserved chords (including Alt+F4); confirm hints, beep, continued recording, and Esc cancellation. Check Tab/Shift+Tab traversal.
- [ ] not yet run by user: Clear with Backspace/Delete and ×; confirm placeholders, no tray shortcut, and unregistration. Check that × does not start recording.
- [ ] not yet run by user: End recording by clicking blank space, Alt+Tab, hiding/closing, or switching tabs; confirm shortcuts resume and reopening does not start a recorder automatically.
- [ ] not yet run by user: Hold a binding in another app, restart Snapper and click its notification; confirm the Shortcuts tab, live warning, working other shortcuts/tray commands, and warning removal after rebinding.
- [ ] not yet run by user: Move Settings between 100% and 150% monitors; confirm crisp scaling and correct layout.
