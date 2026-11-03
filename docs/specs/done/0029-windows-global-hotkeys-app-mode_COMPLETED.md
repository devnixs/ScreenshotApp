# 0029 — Register the Windows global hotkeys and route every trigger through the ported app-mode machine

> Source: docs/PRD.md §1, §4 (bindings shown in the menu; click while recording stops), §5 ("While the overlay is open, all Snapper shortcuts are ignored"), §7.4 (⌘⇧5 stops; ⌘⇧4/⌘⇧6 ignored while recording), §7.6 (converting state), §13.2 (Ctrl+Shift+4/5/6, display format), §13.3 (tray clicks), §13.9 (*Shortcuts*), §13.10 (registration and failure notification), §13.11 ("A shortcut that another app already holds produces the tray notification …; the other shortcuts keep working")
> Depends on: 0025-windows-solution-tray-shell, 0027-windows-app-settings

## Context

Not yet implemented on Windows. After 0025 the app has a tray icon (`windows/src/Snapper.Windows/App/TrayIconController.cs`)
with the menu Screenshot / Record / Annotate / — / Settings… / Exit Snapper (layout in Core
`App/TrayMenuLayout`), and an `AppCoordinator` (`windows/src/Snapper.Windows/App/AppCoordinator.cs`)
with stubs `StartScreenshot()`, `StartRecording()`, `StartAnnotate()`, `OpenSettings(SettingsTab)`,
and `Exit()`. After 0027 the shortcuts are stored in `AppSettings` as one `KeyChord` per purpose
(defaults Ctrl+Shift+4/5/6, display string `"Ctrl+Shift+4"`), persisted by `JsonSettingsStore`,
which raises a change notification when settings are saved. Nothing registers a global hotkey yet,
and the menu items show no shortcut.

**macOS reference implementation (port it, don't redesign it):**

- `Snapper/App/AppModeMachine.swift:1-71`: `AppMode`, `AppModeEvent`, `AppModeAction`,
  `AppModeMachine.handle(_:)`. Spec: `docs/specs/done/0005-global-shortcuts-app-mode_COMPLETED.md`
  (transition table and design notes).
- `Snapper/App/CapturePurpose.swift:1-4`, tests `SnapperTests/CapturePurposeTests.swift:4-13`.
- `SnapperTests/AppModeMachineTests.swift:12-114`: the literal 5 × 11 expectation table (lines
  12-61), the replay helper (63-73), `repeatedStopsRemainRecordingUntilSessionEnds` (89-102), and
  `cancellationAndCompletionAllowNewSelections` (104-114).
- `Snapper/App/AppCoordinator.swift:41-57`: `send(_:)` applies the transition **before**
  performing the action (re-entrant sends see the new mode) and logs event, old → new mode,
  and action.
- `Snapper/App/StatusItemClickRouter.swift` is **not ported**. Control-click as a secondary click
  is a macOS convention, and on Windows both buttons mean the same thing (PRD §13.3).
- `Snapper/App/ShortcutNames.swift` is **not ported**. On Windows the bindings live in `AppSettings`
  (0027, PRD §13.9), not in KeyboardShortcuts' storage. The macOS fact that `CapturePurpose` raw
  values are persisted identifiers doesn't apply on Windows.

**What changes for Windows (PRD §13.3, §13.10):**

- In idle, **both** tray buttons open the menu. macOS right-click → Settings doesn't apply, so the
  machine has no `OpenSettings` action. Settings opens from the Settings… menu item, the
  notification click, and the second-instance launch (0025), which call
  `AppCoordinator.OpenSettings(tab)` directly.
- While recording, either click stops and saves, and no menu opens.
- **Converting** (PRD §7.6 and §13.3, "While a conversion runs, clicks do nothing"): this is
  represented the same way as in macOS todo spec 0023 (lines 78-79). The app mode stays
  `Recording` until `RecordingEnded`, which the recording session sends only after the conversion
  finishes. A click or Ctrl+Shift+5 during conversion still yields `StopRecording`, and the
  recording lifecycle (0034 `RecordingLifecycle`, extended with the converting phase in 0045)
  ignores a stop request while converting. Ctrl+Shift+4/6 stay ignored because the mode is
  `Recording`. **No extra `AppMode` case is added, now or in 0045.** This spec documents that
  contract in the machine's doc comments, and 0034/0045 must keep `RecordingSession.Stop()`
  idempotent and a no-op while converting.
- Hotkeys use Win32 `RegisterHotKey` (PRD §13.1), not KeyboardShortcuts. Snapper must register
  the hotkeys itself, re-register them when a binding changes (PRD §12 "take effect immediately"),
  and report each failure (PRD §13.10).

## User story

As a Snapper user on Windows, I can press Ctrl+Shift+4 / 5 / 6 anywhere, or pick the matching tray
menu item, to start a screenshot, recording, or annotation selection. The tray menu shows my
current bindings. A shortcut pressed at the wrong moment (overlay open, or Ctrl+Shift+4/6 during a
recording) does nothing. If another app already holds one of my shortcuts, a tray notification
tells me so and takes me to Settings, and my other shortcuts keep working.

## Scope & changes

### Pure logic in `Snapper.Core` (unit-tested, no WPF/WinForms/interop types)

- **`windows/src/Snapper.Core/App/CapturePurpose.cs`** (new):
  `public enum CapturePurpose { Screenshot, Record, Annotate }`, declared in this order. The order
  is the canonical iteration order (menu order, registration order, duplicate resolution).
  Provide `public static IReadOnlyList<CapturePurpose> All { get; }` in a static class
  `CapturePurposes`, returning `[Screenshot, Record, Annotate]`.

- **`windows/src/Snapper.Core/App/AppModeMachine.cs`** (new): a faithful port of
  `Snapper/App/AppModeMachine.swift`. C# has no enums with associated values, so use closed record
  hierarchies (value equality, so the tests compare with `==`/`Assert.Equal`):
  ```csharp
  public abstract record AppMode {
      private AppMode() { }
      public sealed record Idle : AppMode;
      public sealed record Selecting(CapturePurpose Purpose) : AppMode;
      /// Includes recording startup, the recording itself, finalization, and conversion
      /// (MP4 Medium/Low re-encode, GIF; 0045/0046), until RecordingEnded.
      public sealed record Recording : AppMode;
  }
  public enum TrayButton { Left, Right }
  public abstract record AppModeEvent {
      private AppModeEvent() { }
      public sealed record Shortcut(CapturePurpose Purpose) : AppModeEvent;   // WM_HOTKEY
      public sealed record MenuItem(CapturePurpose Purpose) : AppModeEvent;   // tray menu Screenshot/Record/Annotate
      public sealed record TrayClicked(TrayButton Button) : AppModeEvent;
      public sealed record SelectionFinished : AppModeEvent;                  // sent by 0030+
      public sealed record SelectionCancelled : AppModeEvent;                 // Esc, display change, overlay failure
      public sealed record RecordingEnded : AppModeEvent;                     // saved/cancelled/failed, after any conversion (0034+)
  }
  public abstract record AppModeAction {
      private AppModeAction() { }
      public sealed record None : AppModeAction;
      public sealed record ShowOverlay(CapturePurpose Purpose) : AppModeAction;
      public sealed record CompleteSelection(CapturePurpose Purpose) : AppModeAction;
      /// The mode stays Recording until RecordingEnded; RecordingSession.Stop() must be idempotent
      /// and does nothing while converting. Pill Stop/Cancel (0035) call the session directly.
      public sealed record StopRecording : AppModeAction;
      public sealed record OpenMenu : AppModeAction;
  }
  public sealed class AppModeMachine {
      public AppMode Mode { get; private set; } = new AppMode.Idle();
      /// Applies the event, updates Mode, and returns the single action the caller must perform.
      public AppModeAction Handle(AppModeEvent e);
  }
  ```
  `Handle` switches on `Mode` and then on the event, like the Swift code. Type-pattern switches
  aren't exhaustive for the compiler, so end each switch with
  `_ => throw new UnreachableException()` (warnings are errors). Ignored events return `None` and
  leave `Mode` unchanged. Settings… and Exit Snapper don't go through the machine.

  **Transition table (normative; implement and test exactly this):**

  | Mode \ Event | `Shortcut(p)` / `MenuItem(p)` | `TrayClicked(Left)` | `TrayClicked(Right)` | `SelectionFinished` | `SelectionCancelled` | `RecordingEnded` |
  |---|---|---|---|---|---|---|
  | `Idle` | → `Selecting(p)`, `ShowOverlay(p)` | stay, `OpenMenu` | stay, `OpenMenu` (Windows change) | stay, `None` | stay, `None` | stay, `None` |
  | `Selecting(q)` | stay, `None` (PRD §5) | stay, `None` | stay, `None` | `q == Record`: → `Recording`, `CompleteSelection(Record)`; else → `Idle`, `CompleteSelection(q)` | → `Idle`, `None` | stay, `None` |
  | `Recording` | `p == Record`: stay, `StopRecording`; else stay, `None` (PRD §7.4) | stay, `StopRecording` | stay, `StopRecording` | stay, `None` | stay, `None` | → `Idle`, `None` |

  Port the design notes from 0005 into doc comments: `Recording` is entered as soon as a record
  selection finishes, before capture starts, and a failed start sends `RecordingEnded`.
  `StopRecording` doesn't leave `Recording`. Status clicks during selection are ignored
  defensively.

- **`windows/src/Snapper.Core/App/HotkeyRegistrationState.cs`** (new): the pure bookkeeping
  behind `RegisterHotKey`, so that re-registration and notification decisions are testable.
  ```csharp
  public enum HotkeyStatus { Unbound, Registered, InUseByAnotherApp, DuplicateBinding }
  public sealed record HotkeySyncPlan(
      IReadOnlyList<CapturePurpose> ToUnregister,                      // executed first
      IReadOnlyList<(CapturePurpose Purpose, KeyChord Chord)> ToRegister);
  public sealed class HotkeyRegistrationState {
      public static int IdOf(CapturePurpose p);          // Screenshot 1, Record 2, Annotate 3
      public static CapturePurpose? PurposeForId(int id); // inverse; null for any other id
      public HotkeyStatus StatusOf(CapturePurpose p);    // Unbound before the first sync
      public KeyChord? ChordOf(CapturePurpose p);        // last desired chord
      public HotkeySyncPlan Plan(IReadOnlyDictionary<CapturePurpose, KeyChord?> desired);
      /// Records the RegisterHotKey results for plan.ToRegister and returns the chords to name in a
      /// failure notification, in CapturePurpose order.
      public IReadOnlyList<KeyChord> Commit(HotkeySyncPlan plan,
          IReadOnlyDictionary<CapturePurpose, bool> registered,
          IReadOnlyDictionary<CapturePurpose, KeyChord?> desired);
  }
  ```
  Rules:
  - **Duplicates:** walking purposes in `CapturePurposes.All` order, a chord equal to an earlier
    purpose's chord gets `DuplicateBinding` and isn't registered. 0033 prevents this in the UI, but
    a hand-edited `settings.json` can still contain it. Duplicates are never notified.
  - `null` desired chord (if 0027 can represent an unbound shortcut) → `Unbound`, not registered.
  - **Unregister** a purpose that is currently `Registered` when its desired chord changed, became
    `null`, or became a duplicate.
  - **Register** a purpose whose desired chord is non-null and not a duplicate, when the chord
    changed or its current status isn't `Registered`. An unchanged chord that previously failed is
    retried on every sync, because the other app may have exited. An unchanged `Registered` chord
    is left alone, so it isn't released and possibly lost to another app.
  - Unregister everything first, then register, so swapping two chords between purposes works.
  - **Notification:** `Commit` returns a purpose's chord when its registration failed **and**
    either this is the first sync or its chord differs from the chord before this sync. A retry of
    an unchanged chord that fails again isn't re-notified, so changing an unrelated setting such
    as the shutter sound doesn't repeat the balloon. The Settings warning (0033) still reflects
    `InUseByAnotherApp`.
  - Purposes with no plan entry keep their status. Registered results set `Registered`, and failed
    results set `InUseByAnotherApp`.

- **`windows/src/Snapper.Core/App/HotkeyNativeChord.cs`** (new): the pure mapping from 0027's
  `KeyChord` to `RegisterHotKey` arguments as plain numbers (Core must not reference interop
  types). `public static (uint Modifiers, uint VirtualKey) ToRegisterHotKeyArgs(KeyChord chord)`:
  `MOD_ALT = 0x0001`, `MOD_CONTROL = 0x0002`, `MOD_SHIFT = 0x0004`, `MOD_WIN = 0x0008`, always OR
  `MOD_NOREPEAT = 0x4000`. The virtual key is passed through unchanged. Keep the constants private
  to this class, with a comment naming the Win32 header (`WinUser.h`).

- **`windows/src/Snapper.Core/App/HotkeyFailureMessage.cs`** (new): user-facing strings (PRD
  §13.10, §13.9):
  - `public const string Title = "Shortcut unavailable";` (the balloon title; the PRD only fixes the body)
  - `public static string Body(IReadOnlyList<KeyChord> failed)`, using 0027's display string:
    - 1 → `"Ctrl+Shift+4 is used by another app. Change it in Settings."` (PRD wording)
    - 2 → `"Ctrl+Shift+4 and Ctrl+Shift+5 are used by other apps. Change them in Settings."`
    - 3 → `"Ctrl+Shift+4, Ctrl+Shift+5, and Ctrl+Shift+6 are used by other apps. Change them in Settings."`
    - 0 → `ArgumentException` (callers never show an empty notification)
  - `public const string SettingsWarning = "Another app is using this shortcut.";` (for 0033, PRD §13.9)

- **`windows/src/Snapper.Core/App/HotkeyBindings.cs`** (new):
  `public static IReadOnlyDictionary<CapturePurpose, KeyChord?> From(AppSettings settings)` maps
  the three shortcut properties from 0027 to purposes. It is the single place that knows which
  settings property belongs to which purpose. The tray menu (shortcut text) and `HotkeyManager`
  both use it.

### Platform glue in `Snapper.Windows` (manually verified)

- **`windows/src/Snapper.Windows/NativeMethods.txt`**: add `RegisterHotKey`, `UnregisterHotKey`,
  `WM_HOTKEY`, `HOT_KEY_MODIFIERS` (if CsWin32 needs the enum for the signature, cast Core's
  `uint` to it), and `HWND_MESSAGE` if the message-only parent isn't already available.
  `RegisterHotKey` is generated with `SetLastError = true`. Read failures with
  `Marshal.GetLastWin32Error()` and log them (`ERROR_HOTKEY_ALREADY_REGISTERED` = 1409 is the
  expected one, but any failure counts as `InUseByAnotherApp`).

- **`windows/src/Snapper.Windows/App/HotkeyManager.cs`** (new, `sealed class : IDisposable`,
  created and used only on the WPF dispatcher thread, because hotkeys are thread-affine and
  `WM_HOTKEY` is posted to the owning window's thread):
  - Creates a **message-only window**:
    `new HwndSource(new HwndSourceParameters("SnapperHotkeys") { ParentWindow = HWND_MESSAGE (-3), WindowStyle = 0, Width = 0, Height = 0 })`
    with `AddHook(WndProc)`. It never appears on screen, in the taskbar, or in Alt+Tab, so it
    doesn't need capture exclusion.
  - `WndProc`: on `WM_HOTKEY`, `HotkeyRegistrationState.PurposeForId((int)wParam)` → raise
    `event Action<CapturePurpose> Pressed`, then `handled = true`. Ignore unknown ids.
  - `public void Sync(AppSettings settings)`: `desired = HotkeyBindings.From(settings)`,
    `plan = state.Plan(desired)`. For each `ToUnregister`, call `UnregisterHotKey(hwnd, IdOf(p))`.
    For each `ToRegister`, call `RegisterHotKey(hwnd, IdOf(p), mods, vk)` with
    `HotkeyNativeChord.ToRegisterHotKeyArgs`. Then `failed = state.Commit(...)`. If anything
    failed, raise `event Action<IReadOnlyList<KeyChord>> RegistrationFailed`. Always raise
    `event Action StatusesChanged` (for 0033's warning) and log each purpose's chord and status.
  - `public HotkeyStatus StatusOf(CapturePurpose p)` delegates to the state (0033 reads it).
  - `Dispose()`: unregister every `Registered` id, remove the hook, and dispose the `HwndSource`.
    It's idempotent.

- **`AppCoordinator`** (`windows/src/Snapper.Windows/App/AppCoordinator.cs`, from 0025), modified:
  - Owns `private readonly AppModeMachine modeMachine = new();` and exposes
    `public AppMode Mode => modeMachine.Mode;`.
  - `public void Send(AppModeEvent e)`: `old = Mode; action = modeMachine.Handle(e);` log
    `"[AppMode] {e}: {old} → {Mode}, action: {action}"`, then `Perform(action)`. The transition is
    applied before the action, so nested `Send` calls see the new mode (0005 edge case). Logging uses
    the facility 0025 established; if there is none, use
    `System.Diagnostics.Trace.WriteLine(message, "AppMode")`.
  - Replace the 0025 stubs `StartScreenshot/StartRecording/StartAnnotate` with
    `Send(new AppModeEvent.MenuItem(p))` from the menu, and `Send(new AppModeEvent.Shortcut(p))`
    from `HotkeyManager.Pressed`.
  - `Perform`:
    - `ShowOverlay(p)` → `BeginSelection(p)`. **Stub in this spec:** close the tray menu if it is
      open (`TrayIconController.CloseMenu()`, so a hotkey pressed while the menu is shown doesn't
      leave it on top of the overlay), log `"Overlay requested for {p} (not implemented until 0030)"`,
      and immediately `Send(new AppModeEvent.SelectionCancelled())`, so the app goes back to `Idle`
      and stays usable. 0030 replaces the stub. Unlike macOS, there's no permission gate.
    - `CompleteSelection(p)` → log only (unreachable here; 0031/0034/0040 fill it in).
    - `StopRecording` → log only (0034 calls `RecordingSession.Stop()`).
    - `OpenMenu` → `trayIconController.ShowMenu()`.
    - `None` → nothing.
  - **Startup** (after the tray icon is visible, because a balloon needs a visible `NotifyIcon`):
    create `HotkeyManager`, subscribe `Pressed`, `RegistrationFailed`, and `StatusesChanged`, then
    `Sync(settingsStore.Current)`.
  - **Settings change:** subscribe to 0027's change notification. On each change, marshal to the
    dispatcher if raised elsewhere, then call `hotkeyManager.Sync(newSettings)` and
    `trayIconController.UpdateShortcutTexts(...)`. Sync is cheap and a no-op for unchanged
    registered chords, so there's no need to diff which setting changed.
  - `RegistrationFailed(chords)` →
    `trayIconController.ShowWarningBalloon(HotkeyFailureMessage.Title, HotkeyFailureMessage.Body(chords), onClick: () => OpenSettings(SettingsTab.Shortcuts))`.
    `OpenSettings` stays the 0025 stub until 0033 builds the window.
  - `Exit()`: dispose `HotkeyManager` before the tray icon (0034 later adds the recording save
    before this).

- **`TrayIconController`** (`windows/src/Snapper.Windows/App/TrayIconController.cs`, from 0025),
  modified:
  - Mouse-up with the left or right button →
    `coordinator.Send(new AppModeEvent.TrayClicked(TrayButton.Left/Right))`. Ignore other buttons.
    The controller no longer opens the menu on its own: the `ContextMenuStrip` must **not** be
    assigned to `NotifyIcon.ContextMenuStrip`, because WinForms would then pop it up on
    right-click by itself, which would bypass the machine and open a menu while recording. Keep
    the menu in a field.
  - `public void ShowMenu()`: shows the stored menu at the cursor. Keep 0025's technique if it
    already brings the menu to the foreground (`SetForegroundWindow` on the menu's handle before
    `Show`, so it closes when you click elsewhere). Otherwise add it.
  - `public void CloseMenu()`: closes the menu if it is open.
  - Screenshot / Record / Annotate items → `coordinator.Send(new AppModeEvent.MenuItem(p))`.
    Settings… → `coordinator.OpenSettings(SettingsTab.General)`. Exit Snapper →
    `coordinator.Exit()`.
  - **Shortcut text:** each capture item shows its current binding right-aligned
    (`ToolStripMenuItem.ShortcutKeyDisplayString` = `KeyChord` display string, e.g.
    `"Ctrl+Shift+4"`; empty when unbound). Set it via
    `UpdateShortcutTexts(IReadOnlyDictionary<CapturePurpose, KeyChord?>)` at startup and on every
    settings change. Don't set `ShortcutKeys`: the global hotkey triggers the action, and a menu
    accelerator would double-fire. If 0025's `TrayMenuLayout` already models shortcut text per
    item, feed it from `HotkeyBindings.From` instead of setting the strings in the controller.
    A failed binding still shows its chord, and its menu item still works (PRD §13.10).
  - `public void ShowWarningBalloon(string title, string body, Action onClick)`:
    `notifyIcon.ShowBalloonTip(10_000, title, body, ToolTipIcon.Warning)`. Store `onClick` in a
    field that `BalloonTipClicked` invokes and then clears. `BalloonTipClosed` also clears it.

- No PRD change. Every rule here is already in docs/PRD.md §4, §5, §7.4, §7.6, §13.3, §13.9, and
  §13.10, apart from the wording decisions under **Open questions**.

## Acceptance criteria

- [ ] At launch, Ctrl+Shift+4 / 5 / 6 are registered with `RegisterHotKey` (with `MOD_NOREPEAT`)
      on a message-only window. Pressing each one anywhere logs a `Shortcut(...)` event and the
      `ShowOverlay` stub, then returns to `Idle`.
- [ ] Choosing Screenshot / Record / Annotate from the tray menu produces the same log with
      `MenuItem(...)`.
- [ ] The tray menu shows `Ctrl+Shift+4`, `Ctrl+Shift+5`, `Ctrl+Shift+6` right-aligned next to
      Screenshot, Record, Annotate. After a binding changes in `settings.json` (once 0033 exists, in
      Settings), the menu shows the new chord, and the new chord works immediately while the old
      one no longer does.
- [ ] Left-click and right-click on the tray icon both open the menu through the machine (log
      shows `TrayClicked(Left|Right)` → `OpenMenu`).
- [ ] If another app holds a Snapper binding at launch, a warning balloon says
      "Ctrl+Shift+4 is used by another app. Change it in Settings." (naming the chord), clicking it
      calls `OpenSettings(Shortcuts)`, the failed purpose still works from the menu, and the other
      shortcuts still work.
- [x] `AppModeMachine` implements the transition table exactly, and all 55 cells are covered by
      unit tests.
- [x] `HotkeyRegistrationState`, `HotkeyNativeChord`, `HotkeyFailureMessage`, `HotkeyBindings`,
      and `CapturePurpose` behave as specified, covered by unit tests.
- [x] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` are green.

## Edge cases

- **Key held down:** `MOD_NOREPEAT` suppresses auto-repeat `WM_HOTKEY`. Any extra press while
  `Selecting` is ignored by the machine anyway.
- **Hotkey pressed while the tray menu is open:** `ShowOverlay` closes the menu first. A menu item
  and a hotkey arriving together: the second event arrives in `Selecting` and is ignored. (With
  the stub, which returns to `Idle` synchronously, two log lines are acceptable.)
- **Double-click on the tray icon:** two mouse-ups. In idle, the menu opens, and the second click
  lands while it's open, which is harmless. While recording, two `StopRecording` actions are sent,
  which is fine because `Stop()` is idempotent (0034).
- **Click or Ctrl+Shift+5 while converting (0045/0046):** the mode is `Recording`, so the machine
  returns `StopRecording`, the lifecycle ignores it, and nothing visible happens (PRD §13.3).
  Ctrl+Shift+4/6 return `None`.
- **Binding held by another app:** `RegisterHotKey` fails → `InUseByAnotherApp`, a notification
  (once per chord change), and the menu item still works. If the other app exits later, the next
  sync (any settings save) registers it silently.
- **Two failures at once:** a single combined balloon. A second balloon would replace the first
  one.
- **Duplicate chords in a hand-edited `settings.json`:** the first purpose wins, the later ones get
  `DuplicateBinding`, there's no notification, and it's logged.
- **Swapping two bindings** (Screenshot ↔ Annotate): both are unregistered first, then both are
  registered, and both succeed.
- **Unrelated setting saved** (e.g. shutter sound): the plan is empty for registered chords.
  Previously failed chords are retried, and they aren't re-notified if they fail again.
- **Stale lifecycle events** (`SelectionFinished`/`SelectionCancelled` in `Idle`/`Recording`,
  `RecordingEnded` outside `Recording`): ignored.
- **Re-entrant `Send` from `Perform`** (the stub sends `SelectionCancelled` synchronously): the
  transition is applied before `Perform` runs.
- **Settings change event raised off the dispatcher thread:** marshal with
  `Dispatcher.BeginInvoke` before calling `Sync`, because `RegisterHotKey` must run on the thread
  that owns the message-only window.
- **Focus assist / Do Not Disturb:** Windows may suppress the balloon. The Settings warning (0033)
  still shows the failure, which is acceptable.
- **Exit:** every registered hotkey is unregistered, so the chords are free again right away.
- **Win-key chords:** 0027/0033 don't accept them (PRD §13.9). If one gets through from a
  hand-edited file, `RegisterHotKey` may fail, and it is reported like any other failure.

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/AppModeMachineTests.cs`), porting
  `SnapperTests/AppModeMachineTests.swift:12-114`:
  - Helper `MachineIn(AppMode)`, built by replaying events: `Idle` → fresh; `Selecting(p)` →
    `Shortcut(p)`; `Recording` → `Shortcut(Record)`, `SelectionFinished`.
  - Literal table: the 11 events in this order: `Shortcut(Screenshot|Record|Annotate)`,
    `MenuItem(Screenshot|Record|Annotate)`, `TrayClicked(Left)`, `TrayClicked(Right)`,
    `SelectionFinished`, `SelectionCancelled`, `RecordingEnded`. The 5 modes are `Idle`,
    `Selecting(Screenshot|Record|Annotate)`, `Recording`. The literal expected (action, mode) per
    cell matches the Swift table, except **`Idle` × `TrayClicked(Right)` → (`OpenMenu`, `Idle`)**.
    `LiteralTableCoversEveryCell`: 5 rows × 11 = 55. `[Theory]` with `MemberData`
    `TransitionMatchesProductRules`: the mode before equals the initial mode, `Handle` returns the
    expected action, and the mode after equals the expected mode.
  - `RepeatedStopsRemainRecordingUntilSessionEnds`: in `Recording`, `Shortcut(Record)` ×2,
    `TrayClicked(Left)` ×2, `TrayClicked(Right)`, `MenuItem(Record)` each → `StopRecording`, still
    `Recording`. Then `RecordingEnded` → `None`, `Idle`. Then `Shortcut(Screenshot)` →
    `ShowOverlay(Screenshot)`, `Selecting(Screenshot)`.
  - `ConvertingIsRecording` (documents the 0045 contract): in `Recording`, after `StopRecording`
    has been returned, a further `TrayClicked(Left)` still returns `StopRecording` (which the
    lifecycle ignores), and `Shortcut(Screenshot)`/`Shortcut(Annotate)` return `None`. The mode
    stays `Recording` until `RecordingEnded`.
  - `CancellationAndCompletionAllowNewSelections`: `Shortcut(Annotate)` → `ShowOverlay(Annotate)`;
    `SelectionCancelled` → `None`, `Idle`; `MenuItem(Screenshot)` → `ShowOverlay(Screenshot)`;
    `SelectionFinished` → `CompleteSelection(Screenshot)`, `Idle`.
  - `BothTrayButtonsOpenMenuWhenIdle`: Left and Right → `OpenMenu`, the mode stays `Idle`.
  - `RecordValueEquality`: `new AppMode.Selecting(Record) == new AppMode.Selecting(Record)`,
    `new AppMode.Idle() == new AppMode.Idle()`, and `Selecting(Record) != Selecting(Annotate)`
    (guards the record-based design).
- **Unit tests** (`windows/tests/Snapper.Core.Tests/CapturePurposeTests.cs`):
  `CapturePurposes.All` equals `[Screenshot, Record, Annotate]` (the persisted-identifier test
  from macOS doesn't apply).
- **Unit tests** (`windows/tests/Snapper.Core.Tests/HotkeyRegistrationStateTests.cs`). `C(n)` is
  a Ctrl+Shift+n chord built with 0027's `KeyChord` API, and `D(...)` builds the desired
  dictionary:
  - `IdsAreStable`: `IdOf` → 1, 2, 3; `PurposeForId(1..3)` round-trips; `PurposeForId(0)`,
    `PurposeForId(4)` → null.
  - `InitialStatusIsUnbound`: all three `Unbound`, `ChordOf` null.
  - `FirstSyncRegistersAll`: `Plan(D(4,5,6))` → `ToUnregister` empty, `ToRegister` =
    Screenshot/C4, Record/C5, Annotate/C6 in order. After `Commit` with all true, all are
    `Registered` and the returned list is empty.
  - `FirstSyncFailureIsNotified`: `Commit` with Screenshot false → it returns `[C4]`; Screenshot is
    `InUseByAnotherApp`, the others are `Registered`.
  - `TwoFailuresNotifiedInPurposeOrder`: Annotate and Screenshot fail → `[C4, C6]`.
  - `UnchangedRegisteredChordsProduceEmptyPlan`: after a successful sync, `Plan(D(4,5,6))` has both
    lists empty.
  - `ChangedChordUnregistersThenRegisters`: after sync, `Plan(D(7,5,6))` → `ToUnregister` =
    [Screenshot], `ToRegister` = [Screenshot/C7]. Commit success → Screenshot `Registered`,
    `ChordOf` = C7.
  - `ChangedChordFailureIsNotified`: same, but the commit fails → returns `[C7]`.
  - `UnchangedFailedChordIsRetriedSilently`: first sync with Screenshot failing; then
    `Plan(D(4,5,6))` → `ToRegister` = [Screenshot/C4] and `ToUnregister` empty (it was never
    registered). A failing commit returns empty and the status stays `InUseByAnotherApp`; a
    successful commit returns empty and the status becomes `Registered`.
  - `SwapUnregistersBothBeforeRegistering`: after sync, `Plan(D(6,5,4))` → `ToUnregister` =
    [Screenshot, Annotate], `ToRegister` = [Screenshot/C6, Annotate/C4].
  - `DuplicateLaterPurposeIsNotRegistered`: `Plan(D(4,4,6))` → `ToRegister` excludes Record. After
    commit, Record is `DuplicateBinding` and the returned list is empty even though Record isn't
    registered.
  - `BecomingDuplicateUnregisters`: after a normal sync, `Plan(D(4,4,6))` → `ToUnregister` = [Record].
  - `NullChordIsUnbound` (only if 0027 allows null): after a normal sync, `Plan` with Annotate null
    → `ToUnregister` = [Annotate], and the status after commit is `Unbound`.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/HotkeyNativeChordTests.cs`):
  - Ctrl+Shift+4 → (`0x4006`, `0x34`).
  - Ctrl+Alt+P → (`0x4003`, `0x50`).
  - Alt+F9 → (`0x4001`, `0x78`).
  - A chord with Win (only constructible if 0027's type allows it) → modifiers include `0x0008`.
  - `MOD_NOREPEAT` (`0x4000`) is set for every chord.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/HotkeyFailureMessageTests.cs`):
  - One chord → exactly `"Ctrl+Shift+4 is used by another app. Change it in Settings."`
  - Two → exactly `"Ctrl+Shift+4 and Ctrl+Shift+5 are used by other apps. Change them in Settings."`
  - Three → exactly `"Ctrl+Shift+4, Ctrl+Shift+5, and Ctrl+Shift+6 are used by other apps. Change them in Settings."`
  - Empty → `ArgumentException`.
  - `SettingsWarning == "Another app is using this shortcut."`; `Title == "Shortcut unavailable"`.
- **Unit tests** (`windows/tests/Snapper.Core.Tests/HotkeyBindingsTests.cs`): default
  `AppSettings` → Screenshot C4, Record C5, Annotate C6. Settings with a changed Record shortcut →
  only Record differs.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`
  succeed with warnings as errors.
- **Manual verification** (Windows 11, real desktop session; can't be automated). Run a Debug build
  from Visual Studio (Output window) or run `Snapper.exe` with Sysinternals DebugView capturing
  Win32 output, so the `AppMode` and hotkey log lines are visible:
  1. Launch Snapper. The log shows Screenshot/Record/Annotate `Registered` with
     Ctrl+Shift+4/5/6, and no balloon appears.
  2. Left-click the tray icon. The menu shows Screenshot `Ctrl+Shift+4`, Record `Ctrl+Shift+5`,
     Annotate `Ctrl+Shift+6` (right-aligned), a separator, Settings…, and Exit Snapper. The log shows
     `TrayClicked(Left)` → `OpenMenu`. Press Esc. Right-click the icon: the same menu opens, and the
     log shows `TrayClicked(Right)` → `OpenMenu`. Clicking elsewhere closes it.
  3. With Notepad focused, press Ctrl+Shift+4, then 5, then 6. Each one logs `Shortcut(<p>)`,
     `Idle → Selecting(<p>)`, the "Overlay requested … (not implemented until 0030)" line, then
     `SelectionCancelled` → `Idle`. Notepad receives no text, because the hotkey is consumed.
  4. Hold Ctrl+Shift+4 down for two seconds: only one `Shortcut(Screenshot)` line appears
     (`MOD_NOREPEAT`).
  5. Open the tray menu and, while it is open, press Ctrl+Shift+5: the menu closes and the stub log
     appears.
  6. Choose Screenshot, Record, and Annotate from the menu: the same log lines appear with
     `MenuItem(<p>)`.
  7. Conflict: exit Snapper. Start a helper that holds Ctrl+Shift+4, for example a PowerShell
     session that runs a small `Add-Type` C# snippet calling `RegisterHotKey(IntPtr.Zero, 1, 0x6, 0x34)`
     and then waits, or an app such as AutoHotkey with `^+4::return`. Launch Snapper: a warning
     balloon titled "Shortcut unavailable" says "Ctrl+Shift+4 is used by another app. Change it in
     Settings." Ctrl+Shift+5 and Ctrl+Shift+6 still log their events. Screenshot in the tray menu
     still logs `MenuItem(Screenshot)`. Click the balloon: the log shows `OpenSettings(Shortcuts)`
     (the stub until 0033).
  8. Live rebind (until 0033 adds the recorder, edit the file): exit Snapper, release the helper,
     and change the Screenshot shortcut in `%APPDATA%\Snapper\settings.json` to Ctrl+Shift+7 (in
     0027's JSON format). Launch Snapper: the menu shows Screenshot `Ctrl+Shift+7`, Ctrl+Shift+7
     logs `Shortcut(Screenshot)`, and Ctrl+Shift+4 logs nothing. The "takes effect immediately
     without restart" check through the Settings window is deferred to 0033.
  9. Click Exit Snapper. Pressing Ctrl+Shift+5 in Notepad now does nothing in Snapper (it is
     unregistered), and the helper from step 7 can register Ctrl+Shift+4 again.
  10. Restore `settings.json` (delete the file to get the defaults).
  - Not manually verifiable in this spec (covered by unit tests, verified for real later): shortcuts
    ignored while the overlay is open (0030), Ctrl+Shift+4/6 ignored and a click or Ctrl+Shift+5
    stopping during a recording (0034), clicks doing nothing during conversion (0045).

## Resolved decisions

- All three recommendations accepted (combined balloon, title "Shortcut unavailable", notify only at launch and on chord changes). Recorded in PRD §13.10.

## Out of scope

- The tray shell, menu layout, icon rendering (incl. red recording dot), single instance, and the
  `OpenSettings(tab)`/`Exit` stubs: **0025**. `AppSettings`, `KeyChord`, JSON persistence, and the
  change notification: **0027**.
- The selection overlay, Esc, display-change cancellation, and focus restore: **0030**. Screenshot,
  annotate, and recording pipelines on `CompleteSelection`: **0031**, **0040**, **0034**.
- Stopping the recording, the tray recording state, and quit/shutdown save: **0034**. Pill
  Stop/Cancel: **0035**. The converting phase in the recording lifecycle: **0045**/**0046**,
  bound by the `Recording`-includes-converting contract above.
- The Settings window, shortcut recorder, Ctrl/Alt validation, the in-UI duplicate rejection, and
  the "Another app is using this shortcut." warning UI (reading `HotkeyManager.StatusOf` and
  `StatusesChanged`): **0033**. Note for 0033: while the recorder field captures a new chord, it
  must temporarily unregister Snapper's hotkeys (or the recorder never sees a currently bound
  chord). Add a `Suspend()`/`Resume()` pair to `HotkeyManager` there.
- The onboarding window and permission gate: not applicable on Windows (PRD §13.10).

## Verification status

Automated verification on 2026-11-03: `dotnet build windows/Snapper.slnx -c Debug` passed with **0 warnings and 0 errors**. `dotnet test windows/Snapper.slnx` passed **398 tests**, including 96 new cases, with no failures or skips. The tests cover the literal 55-cell transition table, repeated stop/conversion contracts, registration changes/swaps/duplicates/unbound states, silent retries, native modifier mapping, exact failure messages, and settings bindings. Core's platform-dependency smoke test remains green. PRD and AGENTS.md already describe the implementation; neither needed edits.

Implementation note: the message-only HwndSource uses the documented WinUser.h `HWND_MESSAGE` pseudo-handle value `-3`; CsWin32 does not expose it on `PInvoke`. Registration, unregistration, constants and modifier enums are generated from `NativeMethods.txt`.

GUI behavior has not been verified. Run these checks on Windows 11 with DebugView capturing Win32 output or Visual Studio's Debug Output:

- [ ] not yet run by user: Launch Snapper; confirm Ctrl+Shift+4/5/6 log Registered, with no failure balloon.
- [ ] not yet run by user: Open the tray menu with each mouse button; confirm matching TrayClicked/OpenMenu logs, current shortcuts right-aligned, and dismissal by Esc or clicking elsewhere.
- [ ] not yet run by user: In Notepad, press Ctrl+Shift+4, 5, and 6; confirm each logs Shortcut, Selecting, the overlay stub, then SelectionCancelled/Idle, without entering text.
- [ ] not yet run by user: Hold Ctrl+Shift+4 for two seconds; confirm a single shortcut event because MOD_NOREPEAT is set.
- [ ] not yet run by user: Press Ctrl+Shift+5 while the tray menu is open; confirm it closes before the overlay-stub log.
- [ ] not yet run by user: Choose Screenshot, Record, and Annotate from the tray menu; confirm matching MenuItem events and the same routing.
- [ ] not yet run by user: Start a helper holding Ctrl+Shift+4, then launch Snapper; confirm the exact Shortcut unavailable balloon, the other bindings still work, Screenshot remains usable from the menu, and clicking the balloon opens Settings with a logged Shortcuts tab request.
- [ ] not yet run by user: Exit, release the helper, edit Screenshot to Ctrl+Shift+7 in settings.json, and relaunch; confirm the new displayed shortcut works and Ctrl+Shift+4 no longer triggers Snapper. Live rebinding through Settings is deferred to 0033.
- [ ] not yet run by user: Exit Snapper; confirm its shortcuts are released and the helper can register Ctrl+Shift+4 again.
- [ ] not yet run by user: Restore settings.json, deleting it if defaults are wanted.

Real overlay/recording/conversion guards remain pending the Windows GUI checks in 0030, 0034, and 0045; this spec verifies their routing policy through unit tests only.
