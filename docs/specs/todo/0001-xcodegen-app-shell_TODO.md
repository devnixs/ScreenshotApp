# 0001 — Create the XcodeGen project and the menu bar app shell

> Source: docs/PRD.md §1, §3, §4 (menu structure, icon, left/right click), §12 (first two criteria); AGENTS.md *Stack*, *Project layout*, *Commands*
> Depends on: none

## Context
The repo has no app code yet: only `AGENTS.md`, `CLAUDE.md`, `docs/PRD.md`, `docs/specs/`, and agent
skills. There is **no `.gitignore`** at the repo root, even though `AGENTS.md` says the
`.xcodeproj` is git-ignored. This spec creates it.

This spec lays the foundation every later spec builds on:
- an XcodeGen `project.yml` that produces a buildable app target and a unit-test target, with
  `KeyboardShortcuts` as the only SwiftPM dependency (PRD §3);
- an agent app (`LSUIElement = YES`, no Dock icon) with a menu bar item using the SF Symbol
  `camera.viewfinder` (PRD §4);
- a left-click menu `Screenshot / Record / Annotate / — / Settings… / Quit Snapper` and a right-click
  that opens Settings directly (PRD §4);
- an `AppCoordinator` with stub entry points that later specs fill in;
- a placeholder Settings window (real content arrives in 0010);
- a main menu (`NSApp.mainMenu`) so standard key equivalents (⌘Z, ⇧⌘Z, ⌘C, ⌘V, ⌘X, ⌘A, ⌘S, ⌘W, ⌘Q, ⌘,)
  reach our windows when they are key. Agent apps don't display a menu bar, but
  `NSApplication` still routes key equivalents through `mainMenu`, and without one ⌘C/⌘Z in the
  annotation window (0018–0020) and text fields in Settings (0010) silently do nothing.

## User story
As a Snapper user, I launch the app and see a viewfinder icon in the menu bar and no Dock icon.
Left-clicking it shows the Snapper menu, right-clicking opens Settings, and *Quit Snapper* quits.

## Scope & changes

### Repository / project (platform config)
- **`.gitignore`** (new, repo root) containing at least:
  ```
  *.xcodeproj/
  xcuserdata/
  DerivedData/
  build/
  .build/
  .DS_Store
  Config/Local.xcconfig
  ```
  Note: `Package.resolved` lives inside the ignored `.xcodeproj`, so reproducibility comes from the
  exact version pin below, not from a resolved file.
- **`project.yml`** (new, repo root), the source of truth for the Xcode project:
  - `name: Snapper`; `options`: `bundleIdPrefix: com.thera-engineering`,
    `deploymentTarget: { macOS: "15.0" }`, `createIntermediateGroups: true`.
  - `packages`: `KeyboardShortcuts` → `url: https://github.com/sindresorhus/KeyboardShortcuts`,
    `exactVersion: 3.1.0` (latest tag as of 2026-11-02; its `Package.swift` declares
    swift-tools 6.2 and macOS 10.15+, which Xcode 26 supports). Only this package. Nothing in this spec
    imports it yet; it is linked so that 0005 can use it without touching the package setup.
  - Target **`Snapper`**: `type: application`, `platform: macOS`, `sources: [Snapper]`,
    `dependencies: [{ package: KeyboardShortcuts }]`, `entitlements.path: Snapper/Snapper.entitlements`
    (see below), `configFiles` for Debug and Release → `Config/Signing.xcconfig`.
    Settings (`base`):
    - `PRODUCT_BUNDLE_IDENTIFIER: com.thera-engineering.snapper`, `PRODUCT_NAME: Snapper`
    - `MACOSX_DEPLOYMENT_TARGET: "15.0"`
    - `SWIFT_VERSION: "6.0"` (Swift 6 language mode, so strict concurrency is on). Do **not** set
      `SWIFT_DEFAULT_ACTOR_ISOLATION` (leave the nonisolated default) so pure model types stay usable
      off the main actor. UI types are marked `@MainActor` explicitly.
    - `GENERATE_INFOPLIST_FILE: YES` with the Info.plist keys set as build settings (no checked-in
      or generated Info.plist file):
      - `INFOPLIST_KEY_LSUIElement: YES`
      - `INFOPLIST_KEY_CFBundleDisplayName: Snapper`
      - `INFOPLIST_KEY_LSApplicationCategoryType: public.app-category.utilities`
      - `INFOPLIST_KEY_NSMicrophoneUsageDescription: "Snapper records your microphone when Record microphone is on."`
        (used by 0014; harmless now)
      - `INFOPLIST_KEY_NSHumanReadableCopyright: "© 2026 Thera Engineering"`
    - `MARKETING_VERSION: "1.0"`, `CURRENT_PROJECT_VERSION: "1"`
    - `ENABLE_HARDENED_RUNTIME`: `NO` for Debug, `YES` for Release (per-config settings). Debug stays
      off so the hosted unit-test bundle can be injected into an ad hoc signed app without
      library-validation failures. Release keeps it for direct distribution (PRD §3).
    - `CODE_SIGN_ENTITLEMENTS` comes from the `entitlements` key. No App Sandbox (AGENTS.md: not sandboxed).
    - Scheme: `scheme: { testTargets: [SnapperTests] }` so that `xcodebuild -scheme Snapper test`
      runs the tests.
  - Target **`SnapperTests`**: `type: bundle.unit-test`, `platform: macOS`, `sources: [SnapperTests]`,
    `dependencies: [{ target: Snapper }]`, same `SWIFT_VERSION` and deployment target and the same
    `configFiles`. XcodeGen sets `TEST_HOST`/`BUNDLE_LOADER` to the app automatically, so the tests are
    hosted and can `@testable import Snapper`.
- **`Snapper/Snapper.entitlements`** (new): a plist with `com.apple.security.device.audio-input = true`
  (needed for microphone capture under the Release hardened runtime, used by 0014) and nothing else.
  No `com.apple.security.app-sandbox`.
- **`Config/Signing.xcconfig`** (new, tracked). This is the local-development signing setup:
  ```
  CODE_SIGN_STYLE = Automatic
  CODE_SIGN_IDENTITY = -
  DEVELOPMENT_TEAM =
  #include? "Local.xcconfig"
  ```
  With no `Local.xcconfig`, the app is signed ad hoc ("Sign to Run Locally"), so it builds on any
  machine with no team. A developer who wants a stable signature creates the git-ignored
  `Config/Local.xcconfig` with `DEVELOPMENT_TEAM = <TEAMID>` and `CODE_SIGN_IDENTITY = Apple Development`.
  Add a comment block at the top of `Signing.xcconfig` explaining this, and that **ad hoc signatures
  change on every rebuild, which can reset the Screen Recording (TCC) permission. Use a real team
  identity when iterating on capture code.**
- Run `xcodegen generate`. Commit `project.yml`, `Config/Signing.xcconfig`, `.gitignore`, and the
  sources, but never the `.xcodeproj`.

### App entry and lifecycle (platform glue, `@MainActor`)
- **`Snapper/App/main.swift`** (new): AppKit entry point without a nib or SwiftUI `App`:
  create `NSApplication.shared`, create an `AppDelegate`, keep it in a top-level constant
  (`NSApplication.delegate` is weak, so it must be retained there), assign it, and call `app.run()`.
  Top-level code in `main.swift` is main-actor isolated in Swift 6. If the compiler disagrees, wrap
  it in `MainActor.assumeIsolated { … }` and add a comment explaining why.
- **`Snapper/App/AppDelegate.swift`** (new): `@MainActor final class AppDelegate: NSObject, NSApplicationDelegate`.
  - `applicationDidFinishLaunching`: sets `NSApp.setActivationPolicy(.accessory)` (belt and braces
    with `LSUIElement`), installs the main menu (`NSApp.mainMenu = MainMenuBuilder.makeMainMenu(…)`),
    creates and starts the `AppCoordinator`.
  - If `AppEnvironment.isRunningUnitTests(ProcessInfo.processInfo.environment)` is true, it installs
    nothing and returns early, so the hosted test run creates no status item and (in later specs)
    opens no onboarding window.
  - `applicationShouldTerminateAfterLastWindowClosed` returns `false`, so closing Settings never quits
    the app.
  - `applicationSupportsSecureRestorableState` returns `true` (silences the AppKit warning).
- **`Snapper/App/AppEnvironment.swift`** (new, **pure**): `enum AppEnvironment { static func isRunningUnitTests(_ environment: [String: String]) -> Bool }`.
  Returns true when `XCTestConfigurationFilePath` or `XCTestBundlePath` is present in the
  environment. This is the only place test detection lives.

### Coordinator (platform glue, `@MainActor`)
- **`Snapper/App/AppCoordinator.swift`** (new): `@MainActor final class AppCoordinator`.
  - Owns the `StatusItemController` and the `SettingsWindowController`.
  - `func start()`: creates the status item.
  - Stub entry points that later specs implement:
    `func startScreenshot()`, `func startRecording()`, `func startAnnotate()`. In this spec each one
    only logs through `os.Logger(subsystem: "com.thera-engineering.snapper", category: "app")`
    (e.g. `"startScreenshot() not implemented yet"`). No alert, no beep.
  - `func openSettings()` calls `settingsWindowController.show()`.
  - `func quit()` calls `NSApp.terminate(nil)`. Later, 0020 hooks the unsaved-annotations prompt into
    `applicationShouldTerminate`, so `quit()` must go through `terminate`, not `exit`.

### Status item (pure layout + routing; AppKit glue)
- **`Snapper/App/StatusMenuLayout.swift`** (new, **pure**, no AppKit import):
  - `enum StatusMenuEntry: Equatable { case screenshot, record, annotate, separator, settings, quit }`
  - `static let entries: [StatusMenuEntry] = [.screenshot, .record, .annotate, .separator, .settings, .quit]`
  - `var title: String?` → `"Screenshot"`, `"Record"`, `"Annotate"`, `nil` (separator), `"Settings…"`
    (with the real ellipsis character U+2026), `"Quit Snapper"`.
  - **Decision: no key equivalents are shown on the Screenshot/Record/Annotate items in this spec.**
    The global shortcuts don't exist until 0005, which adds the live bindings to these items
    (PRD §4: "The shortcuts shown always reflect the current bindings"). Showing static ⌘⇧4/5/6 now
    would advertise shortcuts that do nothing. *Settings…* and *Quit Snapper* also get no key
    equivalent in the status menu. ⌘, and ⌘Q are handled by the main menu instead.
- **`Snapper/App/StatusItemClickRouter.swift`** (new, **pure**, no AppKit import):
  - `enum StatusItemMouseButton { case left, right }`
  - `enum StatusItemClickAction: Equatable { case showMenu, openSettings }`
  - `static func action(button: StatusItemMouseButton?, controlKeyDown: Bool) -> StatusItemClickAction`:
    - `.right` → `.openSettings`
    - `.left` with Control held → `.openSettings` (Control-click is the macOS equivalent of a secondary
      click)
    - `.left` without Control → `.showMenu`
    - `nil` (no current event, e.g. activated through Accessibility/VoiceOver) → `.showMenu`
  - The "while recording, either button stops the recording" rule (PRD §4) is **not** added here.
    0011 extends this router with a recording input.
- **`Snapper/App/StatusItemController.swift`** (new): `@MainActor final class StatusItemController`.
  - Init takes the `AppCoordinator` actions as closures (or a weak coordinator reference). Avoid a
    retain cycle.
  - Creates `NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)`. Sets
    `button.image = NSImage(systemSymbolName: "camera.viewfinder", accessibilityDescription: "Snapper")`
    with `isTemplate = true` so it adapts to light and dark menu bars. Sets `button.toolTip = "Snapper"`.
  - The `statusItem.menu` property is **not** left assigned, because an assigned menu swallows
    right-clicks. Instead: `button.target = self`, `button.action = #selector(handleClick(_:))`,
    `button.sendAction(on: [.leftMouseDown, .rightMouseDown])`.
  - `handleClick`: maps `NSApp.currentEvent` to the router input (`.leftMouseDown` → `.left`,
    `.rightMouseDown` → `.right`, else `nil`; `controlKeyDown` from `modifierFlags.contains(.control)`),
    then:
    - `.showMenu`: shows the menu attached to the status item, using the standard pattern
      `statusItem.menu = menu; button.performClick(nil); statusItem.menu = nil`. This positions and
      highlights the menu natively. The menu is built from `StatusMenuLayout.entries`, with each item
      targeting the controller and dispatching to `startScreenshot()` / `startRecording()` /
      `startAnnotate()` / `openSettings()` / `quit()`. Build it once and keep it. 0005 updates the item
      key equivalents in place.
    - `.openSettings`: `coordinator.openSettings()`.
  - Expose the menu items by `StatusMenuEntry` (e.g. `func item(for: StatusMenuEntry) -> NSMenuItem?`)
    so that 0005 can set key equivalents and 0011 can swap the icon. The icon swap itself is 0011.

### Settings window placeholder (platform glue, `@MainActor`)
- **`Snapper/Settings/SettingsView.swift`** (new): placeholder SwiftUI view, e.g. a `VStack` with
  `Text("Snapper Settings")` and a secondary `Text("Settings will appear here.")`, padded, about
  420 × 200 pt. 0010 replaces the contents with the tabs.
- **`Snapper/Settings/SettingsWindowController.swift`** (new): `@MainActor final class SettingsWindowController`.
  - Lazily creates **one** `NSWindow` (`styleMask: [.titled, .closable]`, `title: "Settings"`,
    `contentViewController: NSHostingController(rootView: SettingsView())`,
    `isReleasedWhenClosed = false`, `collectionBehavior` includes `.moveToActiveSpace`) and centers it
    the first time it is created.
  - `func show()`: `NSApp.activate()` (macOS 14+ API), then `window.makeKeyAndOrderFront(nil)`. Calling
    it again while the window is open brings the same window to the front. It never creates a second
    one.

### Main menu (platform glue, `@MainActor`)
- **`Snapper/App/MainMenuBuilder.swift`** (new): `@MainActor enum MainMenuBuilder { static func makeMainMenu(settingsTarget: AnyObject, settingsAction: Selector) -> NSMenu }`.
  Items with a `nil` target go to the first responder, so later windows can implement them.
  - **App menu** ("Snapper"): *About Snapper* (`orderFrontStandardAboutPanel(_:)`), separator,
    *Settings…* ⌘, (targets the coordinator's open-settings action), separator, *Hide Snapper* ⌘H
    (`hide(_:)`), *Quit Snapper* ⌘Q (`terminate(_:)`).
  - **File**: *Close* ⌘W (`performClose(_:)`), *Save…* ⌘S (`saveDocument(_:)`, nil target. 0020's
    annotation window responds to it. With no responder, the item is disabled automatically).
  - **Edit**: *Undo* ⌘Z (`undo:`), *Redo* ⇧⌘Z (`redo:`), separator, *Cut* ⌘X (`cut:`), *Copy* ⌘C
    (`copy:`), *Paste* ⌘V (`paste:`), *Delete* (`delete:`, no key equivalent), *Select All* ⌘A
    (`selectAll:`).
  - **Window**: *Minimize* ⌘M (`performMiniaturize(_:)`), *Zoom* (`performZoom(_:)`). Set
    `NSApp.windowsMenu` to it.
  - The selectors and key equivalents are defined in a **pure** table, so they can be tested:
    **`Snapper/App/MainMenuLayout.swift`** (new, Foundation only): `struct MenuCommandSpec: Equatable { let title: String; let keyEquivalent: String; let modifiers: MenuModifiers; let action: String /* selector name */ }`
    (with `MenuModifiers` an `OptionSet` of command/shift/option/control), and
    `static let app/file/edit/window: [MenuCommandSpec?]` (`nil` = separator). `MainMenuBuilder` turns
    the specs into `NSMenuItem`s using `NSSelectorFromString`. The one exception is *Settings…*, whose
    target the builder sets explicitly.

### Tests
- **`SnapperTests/`** (new folder), using **Swift Testing** (`import Testing`, `@Test`, `#expect`)
  with `@testable import Snapper`. Test files are listed in the Test plan.

### PRD
- No PRD change. Everything here is already specified in PRD §3–§4, or is internal structure.

## Acceptance criteria
- [ ] `xcodegen generate` produces `Snapper.xcodeproj`. `xcodebuild -scheme Snapper -configuration Debug build`
      and `xcodebuild -scheme Snapper test` both succeed with no Swift 6 concurrency errors.
- [ ] `.gitignore` exists, and `git status` after generating and building shows no `.xcodeproj`,
      `xcuserdata`, or build products.
- [ ] `KeyboardShortcuts` resolves at exactly 3.1.0 and is the only package.
- [ ] The built `Snapper.app` Info.plist has `LSUIElement = true`, bundle id `com.thera-engineering.snapper`,
      `LSMinimumSystemVersion` 15.0, and an `NSMicrophoneUsageDescription`.
- [ ] Launching the app shows a `camera.viewfinder` template icon in the menu bar and **no Dock icon**
      (PRD §12).
- [ ] Left-click opens a menu with exactly: Screenshot, Record, Annotate, separator, Settings…, Quit
      Snapper (PRD §4). Screenshot/Record/Annotate do nothing visible yet (they log).
- [ ] Right-click (and Control-click) opens the Settings window directly, in front, with the app
      activated (PRD §4, §12).
- [ ] Settings… in the menu opens the same single Settings window. Opening it again doesn't create a
      second window. Closing it does not quit the app.
- [ ] Quit Snapper quits the app. ⌘Q while the Settings window is key also quits, and ⌘W closes the
      Settings window, showing that the main menu routes key equivalents.
- [ ] Running the unit tests does not leave a Snapper status item in the menu bar.

## Edge cases
- **No current event** when the status item action fires (Accessibility/VoiceOver press): treat it
  as a left click and show the menu.
- **Control-click** acts as a right click and opens Settings.
- **Settings already open** behind other apps or on another Space: `show()` activates the app and
  brings the existing window to the current Space (`.moveToActiveSpace`). It never creates a duplicate.
- **Last window closed** (Settings): the app keeps running (`applicationShouldTerminateAfterLastWindowClosed` → `false`).
- **Menu bar overflow / notch** hides the status item: nothing to handle in code, but mention it in
  the manual steps if the icon isn't visible.
- **Dark/light menu bar and tinted wallpapers**: the icon must be a template image.
- **Ad hoc signing**: each rebuild changes the signature. Later capture specs may see Screen Recording
  permission reset. This is documented in `Config/Signing.xcconfig`. It is not a bug in this spec.
- **Hosted tests**: the app launches as the test host. `AppEnvironment` must suppress all UI so tests
  never show windows or menu bar items.

## Test plan
- **Unit tests**
  - `SnapperTests/StatusItemClickRouterTests.swift`
    - `.left`, no Control → `.showMenu`
    - `.left`, Control held → `.openSettings`
    - `.right`, no Control → `.openSettings`
    - `.right`, Control held → `.openSettings`
    - `nil` button → `.showMenu`
  - `SnapperTests/StatusMenuLayoutTests.swift`
    - `entries == [.screenshot, .record, .annotate, .separator, .settings, .quit]`
    - titles are exactly `"Screenshot"`, `"Record"`, `"Annotate"`, `"Settings…"` (U+2026, not three
      dots), `"Quit Snapper"`, and `separator.title == nil`
  - `SnapperTests/MainMenuLayoutTests.swift`
    - the Edit menu contains `undo:` ⌘Z, `redo:` ⇧⌘Z, `copy:` ⌘C, `paste:` ⌘V, `cut:` ⌘X, `selectAll:` ⌘A
    - the File menu contains `saveDocument:` ⌘S and `performClose:` ⌘W
    - the App menu contains `terminate:` ⌘Q and *Settings…* ⌘,
    - no two specs across all menus share the same key equivalent + modifiers (no duplicate bindings)
  - `SnapperTests/AppEnvironmentTests.swift`
    - `[:]` → `false`
    - `["XCTestConfigurationFilePath": "/x"]` → `true`
    - `["XCTestBundlePath": "/x"]` → `true`
    - unrelated keys only → `false`
  - `SnapperTests/SmokeTests.swift`: the test bundle loads and `Bundle.main.bundleIdentifier ==
    "com.thera-engineering.snapper"` (the host app). This proves the target wiring and TEST_HOST.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build`
  and `xcodebuild -scheme Snapper test` both succeed. If `xcodebuild` complains about an ambiguous
  destination, add `-destination 'platform=macOS'` and note it in the report (and in AGENTS.md
  *Commands* if it turns out to be required).
- **Manual verification** (needs a real GUI session; can't be automated):
  1. Build, then run `Snapper.app` from DerivedData (or ⌘R in Xcode). **Expected:** a viewfinder icon
     appears in the menu bar. No Dock icon appears, and Snapper isn't in the ⌘Tab switcher. If the icon
     is missing on a notched Mac, quit some menu bar apps and check again.
  2. Switch macOS between Light and Dark appearance. **Expected:** the icon stays legible (template
     tinting).
  3. Left-click the icon. **Expected:** a menu opens under the icon with Screenshot, Record, Annotate, a
     separator, Settings…, Quit Snapper, and no shortcuts shown next to the items. Click Screenshot,
     Record, and Annotate in turn. **Expected:** nothing visible happens. In Console.app (filter
     subsystem `com.thera-engineering.snapper`), one "not implemented yet" log line appears per click.
  4. Right-click the icon while another app (e.g. Finder) is frontmost. **Expected:** the Settings window
     opens in front showing the placeholder text, and no menu appears.
  5. Control-click the icon. **Expected:** same as step 4.
  6. With Settings open, left-click the icon → Settings…. **Expected:** the same window comes to the
     front. There is still only one Settings window (check Mission Control).
  7. Move the Settings window to another Space or put it behind another app, then right-click the icon.
     **Expected:** it appears on the current Space, in front.
  8. With Settings key, press ⌘W. **Expected:** the window closes and the menu bar icon is still there
     (the app didn't quit).
  9. Reopen Settings and press ⌘Q. **Expected:** Snapper quits and the icon disappears.
  10. Relaunch, then left-click → Quit Snapper. **Expected:** Snapper quits.

## Out of scope
- Global shortcuts and live key equivalents on the menu items: **0005** (global-shortcuts-app-mode).
- The red `record.circle` icon and "click while recording stops and saves": **0011** (recording-core).
  0011 extends `StatusItemClickRouter`.
- The real Settings content (tabs, launch at login, shortcut recorders, microphone toggle): **0010**.
  Typed preferences: **0003** (`AppSettings`).
- Screen Recording permission and onboarding (including what happens at launch): **0006**.
- Save/copy behavior behind ⌘S/⌘C, and the quit prompt for unsaved annotations: **0020**. Undo/redo
  behavior: **0016**/**0018**. This spec only provides the menu routing.
- App icon artwork (`AppIcon` asset), notarization, and Release distribution packaging. They are not
  required for v1 development (PRD §3: direct distribution, signed with the developer's certificate).
