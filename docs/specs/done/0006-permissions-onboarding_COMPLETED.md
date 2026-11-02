# 0006 — Add Screen Recording permission check, system-shortcut detection, and the onboarding window

> Source: docs/PRD.md §11 (First run and permissions), §6 step 5 and §7 step 3 (missing permission → onboarding), §10 Shortcuts (conflict warning, reuses this spec's collision check), §12 ("The onboarding window appears when permission is missing or the system shortcuts conflict.")
> Depends on: 0003-app-settings, 0005-global-shortcuts-app-mode (and transitively 0001-xcodegen-app-shell)

## Context

Greenfield: none of the files below exist yet. After 0001/0003/0005 the app has:

- `AppCoordinator` (`Snapper/App/AppCoordinator.swift`, `@MainActor final class`) with `startScreenshot()`, `startRecording()`, `startAnnotate()`, `openSettings()`, driven by `AppModeMachine` (`Snapper/App/AppModeMachine.swift`, 0005) from global shortcuts and menu items.
- `AppSettings` (`Snapper/Settings/AppSettings.swift`, 0003) with an `onboardingDismissed: Bool` key (default `false`).
- `KeyboardShortcuts.Name.screenshot/.record/.annotate` (`Snapper/App/ShortcutNames.swift`, 0005), defaults ⌘⇧4/⌘⇧5/⌘⇧6, and `CapturePurpose`.

Nothing checks Screen Recording permission or the macOS screenshot shortcuts yet. PRD §11 requires:

1. At launch, check Screen Recording permission and whether macOS screenshot shortcuts collide with Snapper's bindings. If either step is needed, show one **onboarding window** with two steps: (1) grant Screen Recording, with a button to System Settings → Privacy & Security → Screen & System Audio Recording and a note that macOS may require relaunching Snapper; (2) turn off the macOS screenshot shortcuts, with a button to System Settings → Keyboard → Keyboard Shortcuts → Screenshots.
2. Step 2 is done when **no enabled** macOS screenshot shortcut collides with a **current** Snapper binding (rebinding Snapper also satisfies it).
3. Shown at **each launch** until both steps are done or the user clicks **"Don't show again"**.
4. A shortcut (or menu item) that fires without Screen Recording permission opens the onboarding window **instead of** the overlay.

### Research findings (verified on this machine, macOS 26 / Darwin 25.6)

- The macOS screenshot hotkeys live in the `com.apple.symbolichotkeys` preferences domain under key `AppleSymbolicHotKeys`, a dictionary keyed by **string** IDs (`"28"`, …). Both `CFPreferencesCopyAppValue("AppleSymbolicHotKeys", "com.apple.symbolichotkeys")` and `UserDefaults(suiteName: "com.apple.symbolichotkeys")?.dictionary(forKey:)` return it from an unsandboxed process. Use **CFPreferences**, preceded by `CFPreferencesAppSynchronize` so a re-check after the user changes System Settings sees fresh values (and so we never accidentally write into that domain through a `UserDefaults` instance).
- Entry shape: `{ enabled = 0|1; value = { type = standard; parameters = (ascii, virtualKeyCode, modifierFlags) } }`. Observed values:

  | ID | macOS action | ascii | keyCode | modifiers | Chord |
  |---|---|---|---|---|---|
  | 28 | Save picture of screen as a file | 51 | 20 | 1179648 (0x120000) | ⌘⇧3 |
  | 29 | Copy picture of screen to the clipboard | 51 | 20 | 1441792 (0x160000) | ⌃⌘⇧3 |
  | 30 | Save picture of selected area as a file | 52 | 21 | 1179648 | ⌘⇧4 |
  | 31 | Copy picture of selected area to the clipboard | 52 | 21 | 1441792 | ⌃⌘⇧4 |
  | 184 | Screenshot and recording options | 53 | 23 | 1179648 | ⌘⇧5 |
  | 181 | Save picture of the Touch Bar as a file (Touch Bar Macs only) | 54 | 22 | 1179648 | ⌘⇧6 |
  | 182 | Copy picture of the Touch Bar to the clipboard (Touch Bar Macs only) | 54 | 22 | 1441792 | ⌃⌘⇧6 |

  Modifier flags use the `NSEvent.ModifierFlags` device-independent bits: shift `0x20000`, control `0x40000`, option `0x80000`, command `0x100000`. Some entries may also carry `0x800000` (function/numeric-pad bit), which must be masked off. Key codes are Carbon virtual key codes (`kVK_ANSI_3` = 20, `kVK_ANSI_4` = 21, `kVK_ANSI_6` = 22, `kVK_ANSI_5` = 23), the same space as `KeyboardShortcuts.Shortcut.carbonKeyCode`.
- An ID can be **absent** from the dictionary (fresh account, never touched). macOS then uses the built-in default, which for 28–31 and 184 is enabled with the binding above. 181/182 are absent on Macs without a Touch Bar (absent on this machine) and don't exist as active hotkeys there.
- `CGPreflightScreenCaptureAccess()` reports the current TCC state without UI. It is known to keep returning `false` for the running process after the user grants access, until relaunch. That's why the PRD's relaunch note exists. `CGRequestScreenCaptureAccess()` shows the system prompt the first time and adds the app to the Screen & System Audio Recording list (toggle off); after that it returns the state without prompting again.
- System Settings URLs (macOS 15 and 26):
  - Screen recording: `x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture`
  - Keyboard: `x-apple.systempreferences:com.apple.preference.keyboard?Shortcuts`. Since macOS 13, Keyboard Shortcuts is a sheet inside the Keyboard pane, and no public URL opens the *Screenshots* section directly. This URL opens the Keyboard pane, and the onboarding copy tells the user to click **Keyboard Shortcuts… → Screenshots**.
  - Microphone (for 0010 to reuse): `x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone`

## User story

As a Snapper user launching the app for the first time, I see one window that tells me what to grant and what to turn off, with buttons that take me to the right System Settings panes. It reflects my progress when I come back, and it stops appearing once I'm set up or tell it not to. If I press a Snapper shortcut before granting permission, I get this window instead of a broken overlay.

## Scope & changes

### Pure logic (unit-tested, no AppKit/CoreGraphics calls)

- **`Snapper/Support/KeyChord.swift`**: `struct KeyChord: Hashable, Sendable { let keyCode: Int; let modifiers: UInt }`.
  - `static let relevantModifierMask: UInt = 0x1E0000` (shift | control | option | command).
  - `init(keyCode:rawModifiers:)` masks `rawModifiers` with `relevantModifierMask`. Every `KeyChord` is stored masked.
  - A `displayString` (e.g. "⌃⇧⌘4") for the onboarding text. It covers key codes for digits 0–9 and falls back to `"Key \(keyCode)"`. Modifier order: ⌃⌥⇧⌘ (Apple's order).
- **`Snapper/Support/SymbolicHotKeysParser.swift`**:
  - `struct SystemShortcut: Equatable, Sendable { let id: Int; let chord: KeyChord; let isEnabled: Bool }`.
  - `enum SystemScreenshotHotKeyID` (or a static table) listing the IDs **28, 29, 30, 31, 184, 181, 182**, each with its default `KeyChord` from the table above and a flag `assumedPresentWhenMissing` (`true` for 28–31 and 184, `false` for 181/182).
  - `static func parse(_ root: [String: Any]?) -> [SystemShortcut]`, a pure function over the plist-typed dictionary (`AppleSymbolicHotKeys`). It returns entries **only for the screenshot IDs** and ignores everything else. Rules for each ID:
    - `root == nil` (domain or key missing): every ID with `assumedPresentWhenMissing` produces its default chord, enabled.
    - Entry missing: same as above for that ID. 181/182 produce **nothing** (see Edge cases for why).
    - `enabled`: accept `Bool` or `NSNumber`/`Int` (0/1). If missing or of the wrong type, it counts as **enabled**, which is the conservative choice and matches macOS's treatment of absent keys.
    - `value.parameters`: if it's an array of exactly 3 integers (`NSNumber`/`Int`), the chord is `KeyChord(keyCode: params[1], rawModifiers: UInt(params[2]))`. If it's missing or malformed (wrong count, non-numeric, negative keyCode), fall back to that ID's default chord. A keyCode of 65535 means "no key assigned", so produce nothing for that ID.
  - The result is sorted by ID for deterministic tests.
- **`Snapper/Support/ShortcutCollision.swift`**: the **shared collision check**. **0010's `ShortcutConflictDetector` must reuse it, not reimplement it.**
  - `struct ShortcutCollision: Equatable, Sendable { let purpose: CapturePurpose; let system: SystemShortcut }`.
  - `static func collisions(snapperBindings: [CapturePurpose: KeyChord], systemShortcuts: [SystemShortcut]) -> [ShortcutCollision]`. A collision means `system.isEnabled && system.chord == binding` (both masked). A purpose missing from the map (shortcut cleared by the user) never collides. Results are ordered by `CapturePurpose` order (screenshot, record, annotate), then by system ID.
- **`Snapper/Settings/OnboardingDecision.swift`**:
  - `struct OnboardingStatus: Equatable, Sendable { let permissionGranted: Bool; let collisions: [ShortcutCollision] }`, with `isPermissionStepDone` (= `permissionGranted`), `isShortcutStepDone` (= `collisions.isEmpty`), and `allDone`.
  - `enum OnboardingDecision` with `static func shouldShowAtLaunch(status: OnboardingStatus, dismissed: Bool) -> Bool`, which is `!dismissed && !status.allDone`.
  - `static func shouldGateCapture(permissionGranted: Bool) -> Bool`, which is `!permissionGranted`. It's trivial, but naming it keeps the coordinator rule explicit and testable.

### Platform glue (manually verified)

- **`Snapper/Support/ScreenRecordingPermission.swift`**: `@MainActor enum ScreenRecordingPermission`.
  - `static var isGranted: Bool` calls `CGPreflightScreenCaptureAccess()`.
  - `static func request()` calls `CGRequestScreenCaptureAccess()` and discards the result.
  - **When to request:** never at launch. `request()` is called **only when the user clicks step 1's "Open System Settings" button**, immediately before opening the pane URL. Why: the request call is what adds Snapper to the Screen & System Audio Recording list. Without it the user lands on a pane where Snapper is missing and has to add it with "+". Calling it at launch, though, would stack the system prompt on top of our onboarding window and show two competing UIs. On the first click macOS may show its own prompt, which also offers to open System Settings. That's fine, because we open the pane as well. Later clicks don't prompt again.
- **`Snapper/Support/SystemScreenshotShortcuts.swift`**: `enum SystemScreenshotShortcuts`.
  - `static func current() -> [SystemShortcut]` calls `CFPreferencesAppSynchronize("com.apple.symbolichotkeys" as CFString)`, then `CFPreferencesCopyAppValue("AppleSymbolicHotKeys" as CFString, "com.apple.symbolichotkeys" as CFString) as? [String: Any]`, and passes the result to `SymbolicHotKeysParser.parse`. It never writes to that domain.
- **`Snapper/Support/SystemSettingsLinks.swift`**: `enum SystemSettingsLinks` with `static let screenRecording`, `keyboardShortcuts`, and `microphone` (`URL`s above), plus `@MainActor static func open(_ url: URL)` calling `NSWorkspace.shared.open`. These are file-level constants, and their force-unwrap of `URL(string:)` gets a comment saying the literal is a known-valid constant.
- **Snapper bindings → `KeyChord`**: add a small helper (in `ShortcutNames.swift` or `SystemScreenshotShortcuts.swift`) `static func currentSnapperBindings() -> [CapturePurpose: KeyChord]`. It reads `KeyboardShortcuts.getShortcut(for:)` for `.screenshot/.record/.annotate` and maps each to `KeyChord(keyCode: shortcut.carbonKeyCode, rawModifiers: UInt(shortcut.modifiers.rawValue))`. Nil shortcuts are omitted.
- **`Snapper/Settings/OnboardingModel.swift`**: `@MainActor @Observable final class OnboardingModel`. It holds an `OnboardingStatus` and has `refresh()`, which recomputes it from `ScreenRecordingPermission.isGranted`, `SystemScreenshotShortcuts.current()`, and `currentSnapperBindings()`.
- **`Snapper/Settings/OnboardingView.swift`** (SwiftUI) shows these elements:
  - Title "Set up Snapper" and a one-line intro.
  - **Step 1, "Allow Screen Recording":** a status badge (green checkmark "Done" / orange "Needed"), the text "Snapper needs Screen Recording permission to capture your screen." and the button **"Open System Settings"**, which calls `ScreenRecordingPermission.request()` and then opens `SystemSettingsLinks.screenRecording`. A secondary note: "After granting access, macOS may ask you to quit and reopen Snapper." The note is always visible while the step is needed.
  - **Step 2, "Turn off macOS screenshot shortcuts":** a status badge and the text "In Keyboard Shortcuts… → Screenshots, turn off the shortcuts that Snapper uses:". While the step is needed, the text is followed by a list of the colliding chords and the macOS action names (e.g. "⇧⌘4: Save picture of selected area as a file"; the action names come from the `SystemScreenshotHotKeyID` table). It ends with an **"Open System Settings"** button that opens `SystemSettingsLinks.keyboardShortcuts`. When the step is done, the text reads "No conflicts with macOS shortcuts."
  - Footer: a **"Don't show again"** button that sets `AppSettings.onboardingDismissed = true` and closes the window, and a default **"Done"** button that only closes it.
  - The view never decides anything itself. It renders `OnboardingStatus`.
- **`Snapper/Settings/OnboardingWindowController.swift`**: `@MainActor final class OnboardingWindowController`.
  - It owns one `NSWindow` (titled, closable, not resizable, centered) hosting `OnboardingView` via `NSHostingController`. `show()` calls `model.refresh()`, creates the window if needed or reuses the existing one (never two windows), then `NSApp.activate()` and `window.makeKeyAndOrderFront(nil)`, because the app is `LSUIElement` and wouldn't otherwise come to front.
  - It observes `NSApplication.didBecomeActiveNotification` while the window is open and calls `model.refresh()`. Users return from System Settings by clicking our window, which re-activates the app. Remove the observer when the window closes.
  - Closing the window by any route doesn't change `onboardingDismissed`, except "Don't show again".
- **`AppCoordinator` changes** (`Snapper/App/AppCoordinator.swift`):
  - The coordinator owns an `OnboardingWindowController` and adds `showOnboarding()`.
  - **Launch check:** in `applicationDidFinishLaunching` (via the coordinator), build an `OnboardingStatus` and call `OnboardingDecision.shouldShowAtLaunch(status:dismissed: settings.onboardingDismissed)`. If it returns true, call `showOnboarding()`.
  - **Capture gate:** when an event would *start a selection* (`startScreenshot()`, `startRecording()` while idle, `startAnnotate()`, from shortcut or menu), check `OnboardingDecision.shouldGateCapture(permissionGranted: ScreenRecordingPermission.isGranted)` first. If gated, call `showOnboarding()` and leave the mode as `idle` (no overlay). The gate applies **regardless of** `onboardingDismissed`. **Stopping** an active recording (⌘⇧5 / menu icon while recording, 0011) is never gated.
  - Put the gate where 0005's `AppModeMachine` action is executed, i.e. right before entering `selecting(purpose)`, so the machine's ignore rules (shortcuts ignored while selecting, ⌘⇧4/⌘⇧6 ignored while recording) still apply first.
- **`project.yml`:** no new targets, keys, or entitlements (screen capture needs no Info.plist usage string). Run `xcodegen generate` because new source files were added.
- **PRD:** no change. The choices made here are implementation details within §11 (see Edge cases for the 181/182 interpretation; the caller is told about it).

## Acceptance criteria

- [ ] With Screen Recording not granted, launching Snapper shows the onboarding window in front, with step 1 "Needed".
- [ ] With macOS ⌘⇧4/⌘⇧5 shortcuts enabled and Snapper on its defaults, step 2 shows "Needed" and lists the colliding chords.
- [ ] Step 1's button adds Snapper to the Screen & System Audio Recording list and opens that pane. Step 2's button opens Keyboard settings.
- [ ] Returning to Snapper (activating the app) refreshes both steps' status without reopening the window.
- [ ] Step 2 turns "Done" when the colliding macOS shortcuts are turned off, **or** when Snapper is rebound away from them (e.g. Snapper Screenshot → ⌃⌥4), even if macOS shortcuts remain enabled.
- [ ] When both steps are done, the window isn't shown at the next launch.
- [ ] After "Don't show again", the window isn't shown at launch even if steps are still needed.
- [ ] Without permission, pressing ⌘⇧4 / ⌘⇧5 / ⌘⇧6 (or choosing a menu item) opens the onboarding window and **no overlay** appears. This happens even after "Don't show again".
- [ ] Only one onboarding window ever exists. Triggering it again brings the existing one to front.
- [ ] The app builds and all unit tests pass.

## Edge cases

- **Permission granted mid-session:** `CGPreflightScreenCaptureAccess()` may still return `false` until relaunch, so step 1 keeps showing "Needed" and the relaunch note explains why. Don't try to work around it by calling ScreenCaptureKit APIs to probe, since they can trigger additional system prompts. The capture gate keeps opening onboarding until relaunch. That's acceptable, and the note tells the user to relaunch.
- **Permission revoked while running:** the gate checks preflight on every trigger, so a revoked permission routes to onboarding. (Any SCK error mid-capture is handled by 0008/0011's alerts.)
- **Missing `AppleSymbolicHotKeys` domain or entries:** IDs 28–31 and 184 count as enabled with default bindings, so a fresh account shows step 2 as needed.
- **Touch Bar IDs 181/182 (⌘⇧6 / ⌃⌘⇧6):** these are checked **only when present** in the dictionary. Treating a missing 181 as "enabled ⌘⇧6" would make step 2 impossible to complete on every Mac without a Touch Bar, because macOS shows no toggle for it there. When present and enabled (Touch Bar Macs), it collides with Snapper's ⌘⇧6 as normal. This reading matches PRD §11's "(⌘⇧4/⌘⇧5)".
- **Modifier noise:** system entries may include the `0x800000` function bit, and KeyboardShortcuts modifiers may include caps-lock or other bits. `KeyChord` masks both sides to ⇧⌃⌥⌘, so ⌘⇧4 matches regardless.
- **Partial overlap is not a collision:** Snapper ⌘⇧4 vs. system ⌃⌘⇧4 (ID 31) is no collision, because the modifiers differ.
- **User rebinds a macOS screenshot shortcut** to a Snapper chord (e.g. ID 28 → ⌘⇧6): the parser reads the actual parameters, so this collides.
- **Snapper shortcut cleared** (recorder emptied in 0010): it never collides.
- **Malformed plist values** (strings, wrong array length, missing `value`): fall back to the defaults described above. Never crash.
- **Onboarding open while a shortcut fires with permission granted:** the normal overlay flow proceeds, and the onboarding window can stay open behind it. 0007's overlay is a higher window level.
- **Onboarding shown at launch while permission is missing and the user presses a shortcut:** the existing window comes to front. There's no duplicate.

## Test plan

- **Unit tests**
  - `SnapperTests/SymbolicHotKeysParserTests.swift`:
    - `nil` root → returns 28, 29, 30, 31, 184 enabled with default chords (assert keyCode and masked modifiers, e.g. ID 30 = keyCode 21, modifiers 0x120000). There's no 181/182.
    - Empty dictionary gives the same result as `nil`.
    - Fixture copied from the real plist (all five present with `enabled = 0`) → all present, `isEnabled == false`.
    - `enabled` given as `Bool true`, `NSNumber(1)`, `Int 1` → enabled. `NSNumber(0)` and `false` → disabled. `enabled` missing → enabled. `enabled = "yes"` (wrong type) → enabled.
    - Custom parameters: ID 28 with `[54, 22, 1179648]` → chord keyCode 22, 0x120000 (user rebound to ⌘⇧6).
    - Malformed parameters (2 elements; strings; `value` missing; `parameters` not an array) → default chord for that ID, with `enabled` still honored.
    - keyCode 65535 → no entry for that ID.
    - Modifiers with `0x800000` set → masked off.
    - 181 present and enabled with `[54, 22, 1179648]` → included. 181 absent → not included. 182 present but disabled → included with `isEnabled == false`.
    - Unrelated IDs (e.g. `"15"`, `"164"`) → ignored. Non-numeric string keys → ignored.
    - Output is sorted by ID.
  - `SnapperTests/ShortcutCollisionTests.swift`:
    - Defaults (Snapper ⌘⇧4/5/6, system 28–31 and 184 enabled) → collisions screenshot↔30 and record↔184 only, in that order.
    - Same with 30 and 184 disabled → none.
    - Snapper screenshot rebound to ⌃⌘⇧4 → collides with 31 only if 31 is enabled.
    - Snapper ⌘⇧6 with enabled 181 → collision. With 181 disabled or absent → none.
    - Snapper binding with extra caps-lock bit (`0x10000`) still collides (masking).
    - ⌘⇧4 vs ⌘⌥⇧4 → no collision.
    - Purpose missing from the bindings map → no collision.
  - `SnapperTests/KeyChordTests.swift`: masking in `init`, plus `displayString` for ⌘⇧4 ("⇧⌘4"), ⌃⌘⇧3 ("⌃⇧⌘3"), and an unknown key code fallback.
  - `SnapperTests/OnboardingDecisionTests.swift`: the full truth table of `shouldShowAtLaunch` over `permissionGranted × hasCollisions × dismissed` (8 rows; true only when `!dismissed` and at least one step needed). Also `isPermissionStepDone`/`isShortcutStepDone`/`allDone` for each combination, and `shouldGateCapture(true) == false`, `shouldGateCapture(false) == true`.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` both succeed.
- **Manual verification** (needs a real GUI session; these steps can't be automated):
  1. Quit Snapper. Run `tccutil reset ScreenCapture com.thera-engineering.snapper`. In System Settings → Keyboard → Keyboard Shortcuts… → Screenshots, make sure ⌘⇧4 and ⌘⇧5 are **enabled**. Launch Snapper. **Expected:** the onboarding window comes to front with both steps "Needed". Step 2 lists ⌘⇧4 and ⌘⇧5 with their macOS action names.
  2. Press ⌘⇧4 (macOS may grab it; if so, use the menu bar → Screenshot). **Expected:** no Snapper overlay; the onboarding window comes to front, and there's still only one.
  3. Click step 1's **Open System Settings**. **Expected:** the macOS prompt may appear once. System Settings opens Privacy & Security → Screen & System Audio Recording, and Snapper is listed. Click it again: no second system prompt.
  4. Enable Snapper there and accept "Quit & Reopen" if offered (or quit and relaunch manually). **Expected:** after relaunch, step 1 shows "Done" (the window still shows, because step 2 is needed).
  5. Click step 2's **Open System Settings**. **Expected:** Keyboard settings opens. Go to Keyboard Shortcuts… → Screenshots and turn off "Save picture of selected area as a file" and "Screenshot and recording options". Click back on the onboarding window. **Expected:** step 2 now shows "Done" without relaunch.
  6. Quit and relaunch. **Expected:** no onboarding window.
  7. Re-enable the macOS ⌘⇧4 shortcut, relaunch. **Expected:** onboarding shows, with step 2 needed. In Settings (once 0010 exists) or via `defaults`, rebind Snapper Screenshot to another chord, then re-activate the onboarding window. **Expected:** step 2 shows "Done". (Before 0010 exists, skip the rebind part.)
  8. With step 2 still needed, click **Don't show again**, then relaunch. **Expected:** no onboarding at launch. Then `tccutil reset ScreenCapture com.thera-engineering.snapper`, relaunch, and choose Screenshot from the menu. **Expected:** onboarding opens anyway (the gate ignores the dismissed flag) and no overlay appears.
  9. With permission granted, press ⌘⇧4. **Expected:** the normal flow (0007 overlay once that exists; before that, the 0005 stub action), not onboarding.
  10. Restore state afterwards: re-grant permission and set the macOS shortcuts back as you prefer.

## Out of scope

- Settings window, the Shortcuts tab's conflict warning, and microphone permission handling: **0010-settings-window**. 0010's `ShortcutConflictDetector` must reuse `ShortcutCollision.collisions` and `SystemScreenshotShortcuts.current()` from this spec, and `SystemSettingsLinks.keyboardShortcuts` / `.microphone` for its buttons.
- The selection overlay itself (**0007**), capture errors other than missing permission (**0008** `ErrorAlerts`), and recording start/stop (**0011**).
- Programmatically disabling macOS shortcuts (writing to `com.apple.symbolichotkeys`). Snapper only reads that domain.
- Automatic relaunch after granting permission. The PRD asks only for a note.
- Microphone permission (PRD §11 last bullet): **0010 / 0014**.

## Verification status

- **2026-11-02:** `xcodegen generate` succeeded; `xcodebuild -scheme Snapper -configuration Debug build` finished **BUILD SUCCEEDED**; `xcodebuild -scheme Snapper test` finished **TEST SUCCEEDED**, with **128 tests in 17 suites passing**, including 14 new tests for this spec.
- Implemented the pure chord/parser/collision/decision types, read-only system-preference adapter, Screen Recording preflight and explicit user-triggered request, settings links, observable onboarding state, reusable window with activation refresh, launch check, and capture gate.
- No PRD or AGENTS.md changes were needed: implementation follows the existing behavior and architecture. Missing Touch Bar IDs remain absent; standard screenshot IDs fall back to enabled defaults.
- GUI/TCC behavior was **not run**. No app launch or permission request was performed. The selection overlay remains the 0005 stub until 0007.

Manual verification (the numbered procedure above supplies the setup and expected results):

- [ ] not yet run by user — Reset ScreenCapture permission and enable macOS screenshot shortcuts; launch and verify both steps are Needed, with action names and colliding chords.
- [ ] not yet run by user — Trigger Screenshot without permission; verify onboarding comes forward, no overlay appears, and no duplicate window exists. Repeat for Record and Annotate using the menu if macOS intercepts the shortcut.
- [ ] not yet run by user — Open step 1's System Settings link; verify Snapper appears in Screen & System Audio Recording and the first-click system prompt does not repeat.
- [ ] not yet run by user — Grant permission and relaunch; verify step 1 is Done.
- [ ] not yet run by user — Open Keyboard settings, disable the conflicting screenshot shortcuts, and return to Snapper; verify step 2 refreshes to Done.
- [ ] not yet run by user — Quit and relaunch with both steps complete; verify onboarding stays closed.
- [ ] not yet run by user — Re-enable a conflict and relaunch; verify onboarding returns. Once 0010 is implemented, rebind Snapper away from it and verify activation refresh clears the conflict.
- [ ] not yet run by user — Choose Don't show again and relaunch; verify launch suppression. Revoke permission and trigger a capture; verify onboarding still opens and the mode returns to idle without an overlay.
- [ ] not yet run by user — With permission granted, verify captures use the normal selection flow (the existing stub before 0007) instead of onboarding.
- [ ] not yet run by user — Restore preferred Screen Recording permission and macOS shortcut settings.
