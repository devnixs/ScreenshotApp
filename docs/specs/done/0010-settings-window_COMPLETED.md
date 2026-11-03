# 0010 — Build the Settings window with General, Shortcuts, and Recording tabs

> Source: docs/PRD.md §10 (lines 164–177), §3 (line 43, `SMAppService.mainApp`), §4 (line 59, right-click opens Settings), §11 (lines 184 and 187), §12 (lines 201 and 203)
> Depends on: 0001-xcodegen-app-shell, 0002-file-naming-save-folder, 0003-app-settings, 0005-global-shortcuts-app-mode, 0006-permissions-onboarding, 0008-screenshot-rectangle-capture (only for the shared "Couldn't save to <folder>" alert in `ErrorAlerts`)

## Context

No app code exists yet. By the time this spec is built, the lower-numbered specs provide:

- **0001**: `StatusItemController`, where right-click and the menu's "Settings…" item both call `AppCoordinator.openSettings()`. It also provides a placeholder `SettingsWindowController` hosting a placeholder SwiftUI `SettingsView`, and `NSApp.mainMenu` with an Edit menu. This spec **replaces** the placeholder controller and view.
- **0002**: `SaveFolder` (resolves the default `~/Documents/Screenshots`, creates it, checks writability, `SaveFolderError`).
- **0003**: `AppSettings` (typed, injected `UserDefaults`) with the keys `launchAtLogin`, `saveFolderURL`, `screenshotDestination`, `playShutterSound`, and `recordMicrophone`, plus `ScreenshotDestination` (`.file` / `.clipboard` / `.both`).
- **0005**: `ShortcutNames.swift`, defining `KeyboardShortcuts.Name.screenshot` / `.record` / `.annotate` (defaults ⌘⇧4/5/6), and `CapturePurpose`. 0005's menu items already show the live bindings.
- **0006**: `SystemScreenshotShortcuts` and the pure `SymbolicHotKeysParser`. Together they read which macOS screenshot shortcuts are enabled and define the collision check between those and a Snapper binding. 0006 also provides `SystemSettingsLinks` (Screen Recording pane, Keyboard Shortcuts pane) and the pure `OnboardingDecision`.
- **0008**: `ErrorAlerts`, which includes the "Couldn't save to <folder>" alert with **Choose Folder…**.

PRD §10 requires:

- **General:** Launch at login (default **off**); Save folder (folder picker, default `~/Documents/Screenshots`); Screenshot destination: Save to file / Copy to clipboard / Both (default **Both**); Play shutter sound (default **on**).
- **Shortcuts:** one recorder each for Screenshot, Record, and Annotate. A warning appears when a binding conflicts with an enabled macOS screenshot shortcut, with a button that opens System Settings.
- **Recording:** Record microphone (default **off**). Turning it on requests microphone permission. If permission is denied, the toggle switches back off and a note appears with an **Open System Settings** button.

§11 says microphone permission is requested only when *Record microphone* is turned on. §12 requires that rebinding takes effect immediately and that launch at login works.

### Researched library behavior (KeyboardShortcuts 3.1.0, Sources/KeyboardShortcuts/RecorderCocoa.swift and ConflictPolicy.swift)

- The recorder checks every recorded shortcut in this order:
  1. a main-menu key-equivalent conflict (`ConflictPolicy.menuItem`, default `.block`);
  2. `isDisallowed` (blocked);
  3. `isTakenBySystem`, which covers **every** enabled symbolic hotkey, including the macOS screenshot ones (`ConflictPolicy.systemShortcut`, default `.warn`). This shows the alert "This keyboard shortcut cannot be used as it's already a system-wide keyboard shortcut." with **OK** and a "use anyway" button;
  4. an optional custom `.shortcutValidation { … }` closure, which returns `.allow` or `.disallow(reason:)` and, for `.disallow`, shows the reason in a blocking alert.
- **There is no built-in check for duplicates across a single app's own `Name`s.** If two Snapper names get the same binding, both handlers fire. This spec prevents that with `.shortcutValidation` (see Open questions).
- When a recorder in `.name` mode saves, it calls `KeyboardShortcuts.setShortcut(_:for:)`. That re-registers the global hotkey immediately, and then the recorder's `onChange` runs.
- The SwiftUI API is `KeyboardShortcuts.Recorder(_ title: String, name:onChange:)`, `.shortcutValidation(_:)`, and `.keyboardShortcutsConflictPolicy(_:)`.

If the KeyboardShortcuts version pinned by 0001 is older and lacks `ConflictPolicy` or `shortcutValidation`, use the closest equivalent API in that version and note the difference in the commit. Don't bump the dependency without asking.

## User story

As a Snapper user, I right-click the menu bar icon (or choose **Settings…**) and get one Settings window. There I can turn on launch at login, pick where screenshots go and whether they play a sound, rebind the three shortcuts with a clear warning when they clash with macOS's own screenshot shortcuts, and turn on microphone recording, with an obvious way out if I denied the permission.

## Scope & changes

### Pure logic (unit-tested, no AppKit / SwiftUI / ServiceManagement / AVFoundation imports)

- **`Snapper/Settings/ShortcutConflictDetector.swift`** (pure):
  - `struct ShortcutConflict: Equatable, Sendable { let purpose: CapturePurpose; let systemShortcutTitle: String }`.
  - `enum ShortcutConflictDetector`:
    - `static func conflicts(bindings: [CapturePurpose: <0006 key-combo type>?], enabledSystemShortcuts: [<0006 system-screenshot-shortcut type>]) -> [ShortcutConflict]` returns one entry per (Snapper binding, enabled macOS screenshot shortcut) pair that collides. The entries are ordered by purpose (screenshot, record, annotate) and then by system shortcut in 0006's order. A `nil` binding never conflicts, and disabled system shortcuts are never passed in (0006's reader filters them out).
    - **It must reuse 0006's collision predicate** (the same one `OnboardingDecision` uses for step 2), so Settings and onboarding can never disagree. If 0006 keeps that predicate private inside `OnboardingDecision`, move it into a shared `static func` next to `SymbolicHotKeysParser` (for example `SystemScreenshotShortcut.collides(with:)`) and call it from both places. Don't duplicate it.
    - `static func warningText(for conflict: ShortcutConflict) -> String` returns `Conflicts with the macOS shortcut “<title>”.`
    - `static func duplicateOwner(of candidate: <key-combo>, recordingFor purpose: CapturePurpose, bindings: [CapturePurpose: <key-combo>?]) -> CapturePurpose?` returns the *other* Snapper purpose that already uses `candidate`, or `nil`. The purpose being recorded never counts as a duplicate of itself.
    - `static func duplicateReason(owner: CapturePurpose) -> String` returns `Already used by Snapper's “<Screenshot|Record|Annotate>” shortcut.`
  - The system shortcut titles are the System Settings labels. Use 0006's names if it defines them. Otherwise add this table to 0006's type, in one place: 28 "Save picture of screen as a file" (⌘⇧3), 29 "Copy picture of screen to the clipboard" (⌃⌘⇧3), 30 "Save picture of selected area as a file" (⌘⇧4), 31 "Copy picture of selected area to the clipboard" (⌃⌘⇧4), 184 "Screenshot and recording options" (⌘⇧5).
  - Add a glue adapter that converts `KeyboardShortcuts.Shortcut` (`carbonKeyCode`, `carbonModifiers`) into 0006's key-combo type. Put it in the glue file, not the pure file, unless 0006 already provides one.

- **`Snapper/Settings/MicrophoneToggleDecision.swift`** (pure):
  - `enum MicrophoneAuthorization: Sendable { case notDetermined, authorized, denied, restricted }`. This is our own mirror of `AVAuthorizationStatus`, so tests don't need AVFoundation.
  - `struct MicrophoneToggleOutcome: Equatable { var storedValue: Bool; var showsDeniedNote: Bool; var shouldRequestAccess: Bool }`.
  - `enum MicrophoneToggleDecision`:
    - `static func userToggled(to requested: Bool, status: MicrophoneAuthorization) -> MicrophoneToggleOutcome`:
      | requested | status | storedValue | showsDeniedNote | shouldRequestAccess |
      |---|---|---|---|---|
      | false | any | false | false | false |
      | true | authorized | true | false | false |
      | true | notDetermined | false | false | **true** |
      | true | denied | false | true | false |
      | true | restricted | false | true | false |
    - `static func accessRequestFinished(granted: Bool) -> MicrophoneToggleOutcome` returns `granted ? (true, false, false) : (false, true, false)`.
    - `static func reconcileOnAppear(stored: Bool, status: MicrophoneAuthorization) -> MicrophoneToggleOutcome`: if `stored` is true and the status is denied or restricted, it returns `(false, true, false)`, which applies PRD §10's "switches back off + note" rule when permission was revoked in System Settings after it was granted. If `stored` is true and the status is notDetermined (for example after `tccutil reset`), it returns `(false, false, false)` so that the next toggle-on asks again. Otherwise it returns `(stored, false, false)`. It **never** requests access, because §11 says to request only when the user turns the toggle on.
  - The note is shown only while it applies. Once the toggle is turned on successfully, or the user turns it off, `showsDeniedNote` becomes false.

- **`Snapper/Settings/LaunchAtLoginState.swift`** (pure):
  - `enum LaunchAtLoginStatus: Sendable { case notRegistered, enabled, requiresApproval, notFound }` mirrors `SMAppService.Status`. The glue maps `@unknown default` to `.notRegistered`.
  - `struct LaunchAtLoginState: Equatable { var isOn: Bool; var note: Note? ; enum Note: Equatable { case requiresApproval, unavailable, failed(String) } }`.
  - `static func state(for status: LaunchAtLoginStatus, error: String? = nil) -> LaunchAtLoginState`:
    - `.enabled` → `isOn: true`, no note.
    - `.requiresApproval` → `isOn: true`, note `.requiresApproval`. The item is registered but the user has to allow it in Login Items.
    - `.notRegistered` → `isOn: false`, no note.
    - `.notFound` → `isOn: false`, note `.unavailable`.
    - If `error` is non-nil, the note is `.failed(error)` regardless of status, and `isOn` still comes from the status.
  - The note text (glue renders it):
    - `.requiresApproval`: "Allow Snapper in System Settings to launch it at login.", with the button **Open Login Items**.
    - `.unavailable`: "Launch at login isn't available for this copy of Snapper."
    - `.failed(msg)`: "Couldn't change launch at login: <msg>"

### Platform glue (manually verified)

- **`Snapper/Support/LaunchAtLogin.swift`**: `@MainActor enum LaunchAtLogin` wrapping `SMAppService.mainApp`:
  - `static var status: LaunchAtLoginStatus` maps `SMAppService.mainApp.status`.
  - `static func setEnabled(_ on: Bool) -> String?` calls `register()` or `unregister()`, catches any thrown error, and returns `error.localizedDescription` (or `nil` on success).
  - `static func openLoginItemsSettings()` calls `SMAppService.openSystemSettingsLoginItems()`.
  - **Source of truth:** `SMAppService.mainApp.status` is authoritative. The user can remove the login item in System Settings at any time, so the UI always reflects `status` when the window appears and after every toggle. `AppSettings.launchAtLogin` (0003) is only a **mirror**: after every status read, write `settings.launchAtLogin = state.isOn`. Nothing at app launch registers or unregisters based on the stored bool, because that would fight a removal the user made in System Settings. The default is off, and nothing registers until the user flips the toggle. Don't remove the 0003 key.

- **`Snapper/Settings/SettingsModel.swift`**: `@MainActor @Observable final class SettingsModel`. It is created once by `AppCoordinator` with the shared `AppSettings` and owns the view state:
  - `launchAtLogin: LaunchAtLoginState`, with `setLaunchAtLogin(_:)` (calls `LaunchAtLogin.setEnabled`, re-reads `status`, maps through `LaunchAtLoginState.state(for:error:)`, and mirrors the result into `AppSettings`).
  - `saveFolderURL`, `screenshotDestination`, and `playShutterSound`, which read and write straight through `AppSettings`.
  - `conflicts: [ShortcutConflict]` and `refreshConflicts()`, which reads 0006's `SystemScreenshotShortcuts` (fresh on every call; no caching) and the current bindings via `KeyboardShortcuts.getShortcut(for:)`, then calls `ShortcutConflictDetector.conflicts`.
  - `recordMicrophone: Bool` and `showsMicrophoneDeniedNote: Bool`, with `setRecordMicrophone(_:)`. This reads `AVCaptureDevice.authorizationStatus(for: .audio)`, maps it to `MicrophoneAuthorization`, and applies `MicrophoneToggleDecision.userToggled`. If `shouldRequestAccess`, it calls `AVCaptureDevice.requestAccess(for: .audio)`, **hops back to `@MainActor`** before applying `accessRequestFinished(granted:)`, and writes `storedValue` to `AppSettings.recordMicrophone`. While a request is pending, the toggle shows off.
  - `refreshOnAppear()`: re-reads the launch-at-login status, applies `MicrophoneToggleDecision.reconcileOnAppear`, and calls `refreshConflicts()`.
  - It observes `NSApplication.didBecomeActiveNotification` and calls `refreshOnAppear()`, so when the user comes back from System Settings, the conflict warnings, the mic note, and the launch-at-login state all update. Remove the observer in `deinit` (or use a task that is cancelled there).
  - Views never call `SMAppService`, `AVCaptureDevice`, or `UserDefaults` directly. They only call the model.

- **`Snapper/Settings/SettingsView.swift`**: replaces the 0001 placeholder. It is a SwiftUI `TabView` with three tabs, each using `Form { … }.formStyle(.grouped)` at a fixed width of about 460 pt:
  - **General** (`Label("General", systemImage: "gearshape")`):
    - `Toggle("Launch at login", …)` bound to the model. Below it, when a note is present, show the note as secondary text, plus the **Open Login Items** button for `.requiresApproval`.
    - A **Save folder** row: the folder's path, abbreviated with a tilde (`(path as NSString).abbreviatingWithTildeInPath`), truncated in the middle, with the full path as the help tooltip, followed by two buttons:
      - **Choose…** opens an `NSOpenPanel` (`canChooseDirectories = true`, `canChooseFiles = false`, `canCreateDirectories = true`, `allowsMultipleSelection = false`, `directoryURL` = current folder, `prompt = "Choose"`). Show it as a sheet on the Settings window. On OK, it stores the URL through `AppSettings.saveFolderURL`. If 0008 already added a folder-picking helper for its **Choose Folder…** alert, reuse it instead of writing a second one. The writability check isn't done here; per §9, an unwritable folder surfaces at save time.
      - **Show in Finder** first ensures that the folder exists, using 0002's `SaveFolder` create logic (the default folder may not exist yet), and then opens it with `NSWorkspace.shared.open(url)`. If creation fails, show 0008's "Couldn't save to <folder>" alert with **Choose Folder…**.
    - `Picker("Screenshot destination", …)` with `.pickerStyle(.radioGroup)` and the options "Save to file" (`.file`), "Copy to clipboard" (`.clipboard`), and "Both" (`.both`).
    - `Toggle("Play shutter sound", …)`.
  - **Shortcuts** (`Label("Shortcuts", systemImage: "keyboard")`):
    - Three rows, each `KeyboardShortcuts.Recorder("Screenshot:" / "Record:" / "Annotate:", name: .screenshot / .record / .annotate, onChange: { _ in model.refreshConflicts() })`.
    - Each recorder gets `.shortcutValidation { candidate in … }`, which returns `.disallow(reason: ShortcutConflictDetector.duplicateReason(owner:))` when `duplicateOwner` is non-nil, and `.allow` otherwise.
    - Keep the library's **default** `ConflictPolicy`. Main-menu conflicts are blocked (this protects ⌘Z/⌘C/⌘S/⌘Q from 0001's main menu), and system-shortcut conflicts get the library's "use anyway" dialog. Don't override the policy. PRD §10's inline warning below is the persistent indicator, and the library dialog is the momentary one.
    - Under each recorder that has conflicts, show one warning line per `ShortcutConflict`, using `Label(warningText, systemImage: "exclamationmark.triangle.fill")` with a yellow/orange symbol and secondary text.
    - If `conflicts` is non-empty, show a single **Open Keyboard Shortcuts** button below the rows. It uses 0006's `SystemSettingsLinks` link for Keyboard → Keyboard Shortcuts (→ Screenshots, if 0006's link targets it).
  - **Recording** (`Label("Recording", systemImage: "video")`):
    - `Toggle("Record microphone", …)` whose setter calls `model.setRecordMicrophone(_:)`.
    - When `showsMicrophoneDeniedNote` is true, show the secondary text "Snapper doesn't have access to the microphone." with an **Open System Settings** button that opens Privacy & Security → Microphone (`x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone`).
    - Add the link as a `microphone` case or member to 0006's `SystemSettingsLinks`, rather than hard-coding the URL in the view.
  - `.onAppear { model.refreshOnAppear() }` on the root.

- **`Snapper/Settings/SettingsWindowController.swift`**: replaces the 0001 placeholder. It is a `@MainActor final class` with **one instance**, owned by `AppCoordinator`:
  - It lazily creates an `NSWindow` (`styleMask: [.titled, .closable]`, not resizable or miniaturizable, title "Snapper Settings", `isReleasedWhenClosed = false`). The window's `contentViewController` is `NSHostingController(rootView: SettingsView(model:))` with `sizingOptions = [.preferredContentSize]`, so the window fits each tab's content.
  - `show()` calls `NSApp.activate()` (the app is `LSUIElement`, so without this the window opens behind other apps), centers the window on first show only, then calls `makeKeyAndOrderFront(nil)`.
  - Calling `show()` again while the window is open brings the existing window to the front. It never creates a second window, and it keeps the selected tab.
  - Closing the window only orders it out, and the model stays alive.

- **`Snapper/App/AppCoordinator.swift`**: `openSettings()` calls `settingsWindowController.show()`. Right-click and **Settings…** already route here (0001). Don't change 0005's ignore rules. Whether Settings can open during a recording or overlay is decided by 0005/0011 (a click during a recording stops it, per §4).

### project.yml (then run `xcodegen generate`)

- Add `NSMicrophoneUsageDescription` = "Snapper records your microphone when “Record microphone” is turned on." to the app's Info.plist properties, if 0001 didn't add it. `requestAccess` crashes without it. 0014 relies on it.
- If the target enables the hardened runtime (`ENABLE_HARDENED_RUNTIME = YES`), add the entitlement `com.apple.security.device.audio-input = true` to the app's entitlements, creating the entitlements file via `project.yml` if it doesn't exist yet. Without it, microphone access is silently denied under the hardened runtime.
- New source files are picked up by the `Snapper/` source glob, and new test files by `SnapperTests/`. Run `xcodegen generate`.

### PRD

- No PRD change is needed for the decided behavior. If the user answers the open question below differently from the recommendation, update docs/PRD.md §10 in the same change.

## Acceptance criteria

- [ ] Right-clicking the menu bar icon and choosing **Settings…** both open the same single Settings window in front of other apps. Opening it again brings the existing window forward.
- [ ] The window has three tabs: General, Shortcuts, and Recording.
- [ ] General: Launch at login defaults to off. Turning it on registers `SMAppService.mainApp`, so Snapper appears under System Settings → General → Login Items & Extensions → Open at Login. Turning it off removes it. After the item is removed in System Settings, the toggle shows off the next time Settings opens or the app becomes active.
- [ ] General: when the status is `.requiresApproval`, a note and an **Open Login Items** button appear.
- [ ] General: the save folder shows `~/Documents/Screenshots` by default. **Choose…** picks a directory (and can create one), and the new path persists across relaunch. **Show in Finder** opens the folder, creating the default one if it is missing.
- [ ] General: Screenshot destination (default **Both**) and Play shutter sound (default **on**) persist across relaunch and are read by the screenshot flow (0008).
- [ ] Shortcuts: three recorders show the current bindings (⌘⇧4/⌘⇧5/⌘⇧6 by default). A new binding takes effect immediately for the global hotkey and the menu item labels, with no relaunch (§12).
- [ ] Shortcuts: assigning a binding that another Snapper action already uses is rejected with "Already used by Snapper's “<Action>” shortcut."
- [ ] Shortcuts: while a Snapper binding collides with an enabled macOS screenshot shortcut, a warning such as "Conflicts with the macOS shortcut “Save picture of selected area as a file”." appears under that recorder, together with an **Open Keyboard Shortcuts** button. The warning disappears after rebinding, or after the macOS shortcut is disabled and Snapper becomes active again.
- [ ] Recording: Record microphone defaults to off. Turning it on with an undetermined permission shows the macOS microphone prompt. If the user grants it, the toggle stays on. If the user denies it, the toggle switches back off and the note with **Open System Settings** appears. Turning it on with a previously denied permission switches it back off and shows the note immediately, without a prompt.
- [ ] Microphone permission is never requested at launch or when Settings opens (§11).

## Edge cases

- **Two Snapper actions with the same binding:** blocked by `shortcutValidation`. A `nil` (cleared) binding never conflicts or counts as a duplicate. Clearing a binding is allowed; the menu item then shows no shortcut (0005).
- **The binding equals a system screenshot shortcut that is disabled in macOS:** no warning appears. KeyboardShortcuts' `isTakenBySystem` only reports enabled hotkeys, so no library dialog appears either.
- **The binding collides with a non-screenshot system shortcut** (for example ⌘Space for Spotlight): the library shows its "use anyway" dialog. There is no inline warning, because §10 only concerns screenshot shortcuts.
- **The binding collides with a main-menu item** (⌘Z, ⌘C, ⌘S, ⌘Q from 0001's main menu): blocked by the library's default policy.
- **Several bindings conflict at once** (for example the defaults, where ⌘⇧4 and ⌘⇧5 are both enabled in macOS): one warning line per (binding, system shortcut) pair, each under its own recorder, and a single **Open Keyboard Shortcuts** button.
- **Re-recording the default ⌘⇧4 while macOS ⌘⇧4 is enabled:** the library dialog appears, and if the user chooses to use it anyway, the inline warning shows. This is consistent with onboarding (0006).
- **Symbolic hotkeys changed in System Settings while Settings is open:** refreshed on `didBecomeActiveNotification`. Read fresh from 0006's reader every time, without caching.
- **Mic permission revoked in System Settings after it was granted:** on the next appear or activation, the toggle switches off and the note shows (`reconcileOnAppear`).
- **Mic permission reset with `tccutil reset Microphone`:** the stored true becomes false when Settings appears. The next toggle-on prompts again.
- **Mic `.restricted` (MDM):** treated like denied.
- **The `requestAccess` completion runs off the main thread:** it must hop back to `@MainActor` before touching the model.
- **The user toggles mic on, then off, while the request prompt is still pending:** when the result arrives, apply it only if the user's latest intent is still "on". Otherwise ignore the result, and keep the stored value false and the note hidden.
- **Launch at login: `register()` throws** (for example an unsigned build or a translocated app): the toggle reflects the real status (off) and the `.failed(msg)` note shows.
- **Launch at login: running from Xcode's DerivedData:** `SMAppService.mainApp` registers that bundle path. For the manual check, use a copy in `/Applications`.
- **Save folder: the user picks a read-only folder:** stored as is. The save flow (0008) shows "Couldn't save to <folder>" per §9.
- **Save folder: the stored folder was deleted later:** the row still shows the stored path. **Show in Finder** calls the same `SaveFolder` create logic as the save flow (0002), so it behaves exactly as saving would. If creation fails, it shows 0008's "Couldn't save to <folder>" alert.
- **Settings opened while the onboarding window is visible:** both windows can be open. Rebinding away from ⌘⇧4 satisfies onboarding step 2 (§11), and 0006's onboarding refresh picks that up. This spec doesn't change the onboarding window.

## Test plan

- **Unit tests** (`SnapperTests/ShortcutConflictDetectorTests.swift`):
  1. Default bindings (⌘⇧4/⌘⇧5/⌘⇧6) and enabled system shortcuts {28 ⌘⇧3, 29 ⌃⌘⇧3, 30 ⌘⇧4, 31 ⌃⌘⇧4, 184 ⌘⇧5} → exactly two conflicts: (`.screenshot`, "Save picture of selected area as a file") and (`.record`, "Screenshot and recording options"), in that order.
  2. The same bindings with only 28 and 29 enabled → no conflicts.
  3. No system shortcuts enabled → no conflicts.
  4. Screenshot rebound to ⌥⌘4, with 30 and 184 enabled → only the `.record` conflict.
  5. A `nil` binding for `.annotate`, with every system shortcut enabled → no `.annotate` entries.
  6. Same key but different modifiers (⌃⌘⇧4 binding vs 30 ⌘⇧4 enabled, 31 disabled) → no conflict. With 31 enabled → a conflict titled "Copy picture of selected area to the clipboard".
  7. `warningText` for a conflict with "Save picture of selected area as a file" equals `Conflicts with the macOS shortcut “Save picture of selected area as a file”.`
  8. `duplicateOwner(of: ⌘⇧5, recordingFor: .screenshot, bindings: defaults)` == `.record`.
  9. `duplicateOwner(of: ⌘⇧4, recordingFor: .screenshot, bindings: defaults)` == `nil`, because the purpose being recorded never counts against itself.
  10. `duplicateOwner(of: ⌥⌘9, recordingFor: .annotate, bindings: defaults)` == `nil`.
  11. `duplicateOwner` with another purpose's binding `nil` → `nil`.
  12. `duplicateReason(owner: .record)` == `Already used by Snapper's “Record” shortcut.`
  13. Consistency with onboarding: for a set of the inputs above, `conflicts(...).isEmpty` matches 0006's `OnboardingDecision` "step 2 done" result.
- **Unit tests** (`SnapperTests/MicrophoneToggleDecisionTests.swift`):
  1. All 8 rows of the `userToggled(to:status:)` table (requested ∈ {false, true} × status ∈ {notDetermined, authorized, denied, restricted}), each asserting the full `MicrophoneToggleOutcome`.
  2. `accessRequestFinished(granted: true)` == (true, false, false). `accessRequestFinished(granted: false)` == (false, true, false).
  3. `reconcileOnAppear`:
     - (stored true, denied) → (false, true, false)
     - (true, restricted) → (false, true, false)
     - (true, notDetermined) → (false, false, false)
     - (true, authorized) → (true, false, false)
     - (false, any status) → (false, false, false)
     - `shouldRequestAccess` is false in every case.
- **Unit tests** (`SnapperTests/LaunchAtLoginStateTests.swift`):
  1. `.enabled` → isOn true, note nil.
  2. `.requiresApproval` → isOn true, note `.requiresApproval`.
  3. `.notRegistered` → isOn false, note nil.
  4. `.notFound` → isOn false, note `.unavailable`.
  5. `.notRegistered` with `error: "x"` → isOn false, note `.failed("x")`.
  6. `.enabled` with `error: "x"` → isOn true, note `.failed("x")`.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test` both succeed, including all earlier specs' tests.
- **Manual verification** (needs a real GUI session; can't be automated; use a build copied to `/Applications` for the launch-at-login checks):
  1. Open Settings by right-clicking the menu bar icon. Expected: the "Snapper Settings" window is in front, on the General tab. Switch to another app, then choose **Settings…** from the left-click menu. Expected: the same window comes back to the front, and no second window appears.
  2. General defaults on a fresh install (`defaults delete com.thera-engineering.snapper`): Launch at login is off, the folder is `~/Documents/Screenshots`, the destination is Both, and the shutter sound is on.
  3. Change every General control (pick a new folder with **Choose…**, including creating a new folder in the panel; set the destination to "Copy to clipboard"; turn off the shutter sound). Quit Snapper and relaunch it. Expected: all values persist. Take a ⌘⇧4 screenshot. Expected: it goes to the clipboard only, with no sound. Reset to Both and confirm that the file lands in the new folder.
  4. **Show in Finder** with the default folder deleted (`rm -r ~/Documents/Screenshots` while it is set). Expected: the folder is recreated and opened in Finder.
  5. Turn Launch at login on. Expected: Snapper is listed in System Settings → General → Login Items & Extensions → Open at Login. Log out and back in. Expected: the Snapper menu bar icon appears without a manual launch. Turn the toggle off. Expected: Snapper is removed from the list.
  6. With Launch at login on, remove or disable Snapper in System Settings → Login Items, then click back into Snapper's Settings window. Expected: the toggle shows the real state (off, or on with the "Allow Snapper…" note and **Open Login Items**, if macOS reports `.requiresApproval`). **Open Login Items** opens that pane.
  7. Shortcuts: rebind Screenshot to ⌥⌘4. Without relaunching, press ⌥⌘4. Expected: the selection overlay opens. Open the menu. Expected: Screenshot shows ⌥⌘4. ⌘⇧4 no longer triggers Snapper.
  8. Try to bind Record to ⌥⌘4 (now Screenshot's). Expected: an alert says "Already used by Snapper's “Screenshot” shortcut." and Record keeps its previous binding.
  9. With macOS's "Save picture of selected area as a file" (⌘⇧4) **enabled** in System Settings → Keyboard → Keyboard Shortcuts → Screenshots, set Snapper's Screenshot back to ⌘⇧4 (accept the library's "use anyway" dialog). Expected: the warning "Conflicts with the macOS shortcut “Save picture of selected area as a file”." appears under Screenshot, together with **Open Keyboard Shortcuts**. Click it. Expected: System Settings opens on Keyboard Shortcuts. Disable that macOS shortcut and switch back to Snapper. Expected: the warning disappears.
  10. Microphone, first time (`tccutil reset Microphone com.thera-engineering.snapper`, then relaunch): turn on Record microphone. Expected: the macOS microphone prompt appears. Allow it. Expected: the toggle stays on and persists across relaunch.
  11. Microphone denied: `tccutil reset Microphone com.thera-engineering.snapper`, relaunch, open Settings. Expected: the toggle shows off. Turn it on and choose **Don't Allow**. Expected: the toggle switches back off, and "Snapper doesn't have access to the microphone." appears with **Open System Settings**, which opens Privacy & Security → Microphone. Turn the toggle on again. Expected: no prompt; it switches back off immediately, and the note stays visible.
  12. Microphone revoked: grant access, keep the toggle on, then disable Snapper in Privacy & Security → Microphone and switch back to Snapper's Settings. Expected: the toggle is off and the note is shown.
  13. Launch Snapper fresh with Record microphone off. Expected: no microphone prompt appears at launch or when Settings opens.

## Open questions

None. User confirmed on 2026-11-03: Reject bindings already assigned to another Snapper action, using the specified validation alert. Recorded in docs/PRD.md.

## Out of scope

- Reading `com.apple.symbolichotkeys`, the collision predicate itself, the onboarding window, and the Screen Recording / Keyboard Shortcuts deep links: **0006** (this spec only reuses them and adds the Microphone link).
- Registering hotkeys, the app-mode ignore rules, and the menu's live shortcut labels: **0005**.
- Using `playShutterSound`, `screenshotDestination`, and `saveFolderURL` during capture, plus the "Couldn't save to <folder>" alert itself: **0008**. Folder resolution and creation: **0002**.
- Actually recording a microphone track: **0014** (this spec only owns the toggle, the permission flow, and the Info.plist key and entitlement).
- A "Reset to defaults" button, a save-folder writability check at pick time, and any settings not listed in PRD §10.


## Implementation notes

- Settings and onboarding share `ShortcutCollision.collisions`; cleared bindings are represented by absent dictionary entries, matching 0006's existing adapter.
- Added a shared KeyboardShortcuts-to-`KeyChord` adapter, using the library's Cocoa modifier flags to match symbolic-hotkey data.
- General controls write through the existing observable `AppSettings`. Login state mirrors ServiceManagement rather than registering from the stored preference.
- The existing Microphone System Settings link, usage description, and audio-input entitlement already satisfy this spec, so no signing or project settings changed. Regenerated the Xcode project for the new files.
- Extracted microphone request intent/pending state so late completion after toggle-off and repeated toggle-on are unit-tested without invoking TCC.
- Reused the existing save-folder alert and extracted its folder-panel configuration for the Settings sheet.
- PRD already records the approved duplicate rejection. AGENTS.md now lists the pure Settings decisions among tested logic.

## Verification status

- Date: 2026-11-03 (Europe/Paris).
- `xcodegen generate`: passed.
- `xcodebuild -scheme Snapper -configuration Debug build`: **BUILD SUCCEEDED**.
- `xcodebuild -scheme Snapper test`: **TEST SUCCEEDED** — 192 tests in 33 suites, zero failures.
- No GUI, TCC permission, global-hotkey, or login-registration checks were performed. The app was not launched for manual verification.
- Manual checks below are pending. Use an installed copy in `/Applications` for login-item checks. Preserve existing captures before testing missing-folder behavior; use an empty folder rather than deleting captures. Changing the app signature can reset Screen Recording permission.

- [ ] not yet run by user — Open Settings via right-click and via Settings…; confirm one frontmost window, three tabs, and preserved tab selection when reopened.
- [ ] not yet run by user — On a fresh preferences domain, confirm General defaults: login off, default Screenshots folder, Both, shutter sound on; microphone off.
- [ ] not yet run by user — Choose/create a save folder in the sheet, change destination and sound, relaunch, and confirm persistence and screenshot output behavior.
- [ ] not yet run by user — With an empty configured folder missing, Show in Finder recreates and opens it. A folder that cannot be created shows the shared error and Choose Folder… recovery.
- [ ] not yet run by user — Enable launch at login from an installed copy; verify Login Items and relaunch after logout/login, then disable and verify removal.
- [ ] not yet run by user — Change login registration externally; returning to Snapper updates the toggle, approval note, and Open Login Items link as appropriate.
- [ ] not yet run by user — Rebind Screenshot and verify the global shortcut and live menu label immediately; clear a shortcut and verify its menu equivalent disappears.
- [ ] not yet run by user — Assign another Snapper action's binding and verify rejection with the correct owner and unchanged prior binding; main-menu conflicts remain blocked.
- [ ] not yet run by user — Accept a macOS screenshot conflict and verify inline warning and Open Keyboard Shortcuts; disable it externally and verify warning disappears on return. Check multiple conflicts and disabled/non-screenshot system shortcuts.
- [ ] not yet run by user — From undetermined microphone permission, enable and allow access; verify on state and persistence. Verify late result does not override a later off intent.
- [ ] not yet run by user — Deny microphone access; verify off state, denied note, correct Privacy pane, and no repeat prompt on another enable attempt.
- [ ] not yet run by user — Revoke granted microphone permission externally; returning to Snapper switches off and shows the note. Reset permission and verify Settings opens without requesting, with the next enable asking again.
- [ ] not yet run by user — Launch and open Settings with microphone recording off; verify neither action prompts for microphone permission.
