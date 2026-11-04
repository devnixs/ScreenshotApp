# 0049 — Keep Windows shortcut recorders idle when entering the tab

> Source: user bug report (2026-11-04); docs/PRD.md §10, §13.9, §13.10, §13.11
> Depends on: 0033-windows-settings-window (completed)

## Context

Selecting Shortcuts currently puts the Screenshot recorder into "Press Shortcut" mode without clicking it. This also suspends the app's global hotkeys.

`windows/src/Snapper.Windows/Settings/ShortcutRecorder.cs:30–32` maps mouse clicks to focus and every keyboard-focus gain to `ShortcutRecorderSession.FocusGained()`. The pure session in `windows/src/Snapper.Core/Settings/ShortcutRecorderSession.cs:20` starts recording and emits `SuspendHotkeys`. `windows/src/Snapper.Windows/Settings/SettingsWindow.xaml:30` places the first recorder in the selected tab's content. The observed behavior is consistent with automatic WPF tab-content focus; confirm the exact event ordering during implementation rather than treating this as a reproduced diagnosis.

`SettingsWindow.xaml.cs:26` sets initial logical focus and `:61` exposes `FocusTabs()`, but its tab-selection handler at `:29` only reconciles microphone state. `SettingsWindowController.cs:16` calls `FocusTabs()` after showing the window. These safeguards do not cover the reported tab-entry path. Completed spec 0033 already requires that the recorder never take initial focus and remains keyboard accessible. The macOS reference `Snapper/Settings/SettingsView.swift:54` embeds KeyboardShortcuts recorders; this fix concerns WPF focus routing only.

## User story

As a Windows Snapper user, I can inspect my shortcuts without being prompted to replace one until I deliberately enter its recorder.

## Scope & changes

- **WPF glue:** update `windows/src/Snapper.Windows/Settings/SettingsWindow.xaml` and `SettingsWindow.xaml.cs` so selecting Shortcuts leaves keyboard focus on the tab header or another neutral tab-navigation target, rather than automatically entering its content. Cover mouse selection, keyboard tab-header navigation, programmatic selection, and returning to previously visited content. Use the existing `FocusTabs()` mechanism where appropriate, adjusting its target if necessary to avoid WPF forwarding focus into content. Handle only the owning TabControl's selection changes; nested routed selection events must not steal focus.
- Prevent automatic content focus from starting a recording session in the first place. A delayed focus correction that briefly displays "Press Shortcut" or suspends/re-registers hotkeys is insufficient. Check WPF focus ordering and use focus/navigation configuration or an appropriately scoped preview handler before the recorder receives focus. Do not repeatedly force focus after layout or block intentional navigation into content.
- **WPF glue:** review `SettingsWindowController.Show` and recorder visibility/deactivation handling so reopening, notification entry directly to Shortcuts, and restored logical focus cannot restart an old recorder automatically. Leaving an active recorder through tab change, hide, close, or deactivation must still end its session and resume hotkeys exactly once. No recorder should regain recording state merely when the window reactivates.
- Preserve `ShortcutRecorder` mouse and Tab/Shift+Tab accessibility, current focus-driven recording, clearing, validation, commit, cancellation, and suspend/resume effects. Do not disable focusability or remove recorders from the tab order to hide the bug.
- **Pure logic:** retain the existing `ShortcutRecorderSession` semantics and `SettingsTabSelection` decisions. This is a WPF focus-routing correction, with no new platform-independent decision logic required.
- **Documentation:** docs/PRD.md §13.9 and §13.11 now explicitly state passive tab entry and deliberate recorder entry, reflecting the bug report and 0033's existing requirement. Last updated is 2026-11-04.
- No new dependencies, project targets, interop, settings schema, or macOS changes. No XcodeGen changes or commands are needed.

## Acceptance criteria

- [ ] Opening Shortcuts from General or Recording leaves all three fields idle, showing their bindings or "Record Shortcut"; no field shows "Press Shortcut" or a recording highlight.
- [ ] Opening Settings directly on Shortcuts, reopening with Shortcuts remembered, and returning after another tab or window activation have the same passive result.
- [ ] Passive entry does not call the session's start transition or suspend global hotkeys, even transiently; it does not alter saved bindings.
- [ ] Clicking any recorder starts that recorder. Deliberate Tab/Shift+Tab traversal into it also starts recording. Keyboard tab-header navigation alone does not.
- [ ] Leaving an active recorder resumes hotkeys, and returning to the tab does not restart it. Existing commit, Escape, clear, duplicate, and invalid-shortcut behavior remains intact.
- [ ] Windows build and full test suite pass; GUI checks are recorded separately from automated results.

## Edge cases

- First visit versus WPF restoring a formerly focused recorder after a tab switch.
- Show a hidden existing window, restore a minimized window, or enter through a shortcut-conflict notification.
- Change tabs while a recorder is active or while modifier keys are held: no stale modifier preview or unbalanced suspension.
- Click the already selected Shortcuts header: this must not start a recorder.
- Cleared Screenshot binding: idle "Record Shortcut" must not be mistaken for active "Press Shortcut".
- Forward and reverse traversal between recorder fields must not loop back to the same field because a neutral-focus helper ran during traversal.

## Test plan

- **Unit tests:** no new pure logic is prescribed: WPF's automatic focus transfer is outside the net10.0 Core test target. Do not add tests that pretend direct session calls reproduce WPF tab selection. Run existing `windows/tests/Snapper.Core.Tests/ShortcutRecorderSessionTests.cs`, especially `FocusStartsAndStopsExactlyOnce`, `IdleKeysHaveNoEffects`, `TabPassesThrough`, `SuspendResumePairingAcrossInvalidCancelCommitAndRepeatedLoss`, and `IdlePreviewShowsBindingOrPlaceholder`. These assert deliberate-focus effects, no idle key effects, traversal effects, balanced suspension, and idle preview respectively. Run `SettingsTabSelectionTests.cs` to preserve explicit-tab selection and remembered-tab behavior. If implementation adds a pure decision, add its behavioral tests in this project.
- **Build check:** `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` must pass.
- **Manual verification** (Windows 11 GUI; PRD §13.9–§13.11):
  1. Open tray Settings, then click Shortcuts without clicking a field. Expect three idle fields and no recording highlight. Press the current Screenshot global shortcut: expect capture selection, not shortcut editing. Cancel capture with Esc.
  2. Switch to General and Recording and back ten times; also click the selected Shortcuts header. Expect no automatic recording or changed bindings. During development, observe recorder/session start and hotkey suspension calls to confirm there is no transient start on tab entry.
  3. Navigate between tab headers using the keyboard. Expect passive Shortcuts entry. Then deliberately press Tab into the recorder fields and traverse with Tab/Shift+Tab. Expect only the deliberately reached field to record, with normal forward/reverse traversal.
  4. Click each recorder; expect "Press Shortcut". Cancel with Esc; expect the old binding and resumed global hotkeys. Set a valid replacement, verify it takes effect, then restore the original. Check invalid and duplicate chords still follow existing validation.
  5. While editing, click another tab and return; repeat after Alt+Tab, close/reopen Settings, and minimize/restore. Expect no restarted session, no stale modifiers, and working global hotkeys after editing ends.
  6. Reopen Settings while Shortcuts is remembered, and open it through a shortcut-unavailable notification when a conflict is available to reproduce. Expect the correct tab, conflict warning where applicable, and all recorders idle.
  7. Clear Screenshot with × and revisit the tab: expect "Record Shortcut" without active recording. Restore the default and verify the saved bindings survive an app restart.

## Out of scope

- Changing shortcut validation, reserved keys, registration-conflict notifications, or the settings persistence format.
- Changing macOS recorder behavior or redesigning the Settings window.
- Direct3D capture failure work in 0048; this spec has no dependency on it.

## Verification status

Implemented 2026-11-04. Build and automated tests are green; user GUI acceptance remains pending.

- `dotnet build windows/Snapper.slnx -c Debug -p:OutputPath=C:/Projects/CSharp/ScreenshotApp/windows/artifacts/validation-0049-bin/`: passed, 0 warnings and 0 errors. The separate output directory avoids replacing the executable locked by the user's running Snapper process; that process was not stopped.
- `dotnet test windows/Snapper.slnx`: passed, **1,379 passed, 0 failed, 4 skipped, 1,383 total**. This includes `ShortcutRecorderSessionTests` and `SettingsTabSelectionTests`. The four existing filesystem/platform skips remain skips.
- No new Core tests or pure decisions were added. The correction concerns WPF focus ordering, outside the platform-independent test target; recorder session and tab-selection semantics are unchanged.
- A temporary ignored WPF diagnostic harness instantiated the actual `SettingsWindow` with an in-memory settings store and counted injected suspend/resume effects. It used synthetic routed header mouse events, direct selection changes and WPF `MoveFocus`, rather than physical input or the running Snapper instance. Before the correction, clicking Shortcuts from a focused General checkbox produced `CheckBox -> TabItem -> ShortcutRecorder`, immediately suspending hotkeys. Programmatic tab entry and hide/show also started a recorder.
- After the correction, the same header event stopped at `SettingsTabItem`, with **zero recorder starts/suspensions** on passive entry. Direct selection before showing Shortcuts, programmatic selection, clicking the selected header, ten General/Recording/Shortcuts round trips, left/right header traversal, idle and active hide/show, and deactivation/reactivation retained neutral header focus without restarting a session. Nested selector changes did not steal recorder focus. Deliberate focus and native forward/reverse traversal entered recorders normally; the expanded run ended with **6 deliberate starts and 6 corresponding resumes**.
- Diagnosis agrees with the .NET WPF implementation: [`TabControl.OnSelectionChanged`](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/TabControl.cs) updates content/focus before raising the selection event, and [`TabItem.OnPreviewGotKeyboardFocus`](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/TabItem.cs) can forward header focus into content. `SettingsTabItem` selects a focused header without that forwarding; descendant focus and native content traversal keep their existing behavior. `FocusTabs()` now targets the selected header, and `Show()` replaces remembered content focus before showing/activating the window.
- PRD §13.9/§13.11 already contain the approved focus behavior, so no PRD change was needed. No architecture, dependencies, project targets, schema or macOS sources changed.

Manual verification (Windows 11, matching the seven steps above):

- [ ] not yet run by user: Open Settings → Shortcuts without entering a field; confirm idle bindings and a working Screenshot global shortcut, then cancel capture with Esc.
- [ ] not yet run by user: Switch among all tabs ten times and click the selected Shortcuts header; confirm no recording highlight, transient suspension or binding change.
- [ ] not yet run by user: Navigate tab headers with the keyboard, then deliberately traverse recorders with Tab/Shift+Tab; confirm passive headers and normal forward/reverse recorder entry.
- [ ] not yet run by user: Click every recorder, cancel with Esc, set and restore a valid binding, and exercise invalid/duplicate chords; confirm existing validation and resumed global shortcuts.
- [ ] not yet run by user: Leave an active recorder through tab change, Alt+Tab, close/reopen and minimize/restore, including with modifiers held; confirm no restarted recording or stale modifiers.
- [ ] not yet run by user: Reopen with Shortcuts remembered and enter through a shortcut-unavailable notification when available; confirm the selected tab/conflict warning and idle fields.
- [ ] not yet run by user: Clear Screenshot with ×, revisit to see idle "Record Shortcut", restore its binding and restart Snapper to check persistence.
