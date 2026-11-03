# 0008 — Capture a rectangle selection as a PNG and save it, copy it, or both

> Source: docs/PRD.md §6 (Screenshot), §9 (Files and naming), §5 last bullets (overlay removed before capture), §11 (permission → onboarding), §12 (⌘⇧4 criterion; "default save folder is created when it is missing…")
> Depends on: 0002-file-naming-save-folder, 0003-app-settings, 0004-display-geometry, 0006-permissions-onboarding, 0007-selection-overlay-rectangle (and transitively 0001, 0005)

## Context

Implemented by this spec. After 0007, pressing ⌘⇧4 opens the selection overlay on every display and a
rectangle drag produces a `SelectionResult.rect(display:rect:)`, but `AppCoordinator` receives it. This spec turns that result into an actual screenshot.

PRD behavior delivered here:

- §6.2: "PNG at native pixel resolution (2× on Retina)."
- §6.3: output follows the **Screenshot destination** setting (*Save to file* / *Copy to clipboard* /
  *Both*, default **Both**).
- §6.4: shutter sound if enabled (default on): the file
  `/System/Library/Components/CoreAudio.component/Contents/SharedSupport/SystemSounds/system/Screen Capture.aif`,
  falling back to `NSSound(named: "Grab")`, then `NSSound(named: "Pop")`.
- §6.5: missing Screen Recording permission → onboarding window (§11); any other capture failure →
  short alert with the error.
- §9: file named at capture time (`Screenshot 2026-11-02 at 19.18.04.png`, collisions ` (2)`…);
  folder `~/Documents/Screenshots` by default, created on first use; if it can't be created or isn't
  writable → alert "Couldn't save to <folder>" with **Choose Folder…**; with *Both*, the image is
  still copied to the clipboard.
- §5: "After a selection, the overlay is removed before capture so it never appears in the output."

Window selections (`SelectionResult.window`, shadowless single-window capture) are 0009. This spec
builds the reusable capture primitive (`ScreenshotCapturer` → `CapturedImage`) that 0009 and 0017
(Annotate) build on.

Types from earlier specs used here (refer to them by these names; adapt to the exact signatures they
landed with):

- `FileNamer`, `CaptureKind.screenshot` (0002) — file name for a date, collision suffixes.
- `SaveFolder`, `SaveFolderError` (0002) — resolve/create the folder and check it is writable.
- `AppSettings` (0003) — `screenshotDestination: ScreenshotDestination`, `playShutterSound`,
  `saveFolderURL`.
- `DisplayInfo`, `DisplayGeometry` (0004) — AppKit-global ↔ display-local top-left conversion,
  points → pixels.
- `ScreenRecordingPermission`, `OnboardingWindowController` (0006) — show onboarding.
- `SelectionOverlayController`, `SelectionResult` (0007) — the overlay closes itself before it
  reports a result. **Assumption:** `SelectionResult.rect(display: DisplayInfo, rect: CGRect)`
  carries the rect in AppKit global points, already confined to `display`. If 0007 landed with a
  display-local rect instead, skip the global→local step below; nothing else changes.
- `AppCoordinator`, `AppModeMachine`, `CapturePurpose` (0001/0005).

## User story

As a Snapper user, I press ⌘⇧4, drag a rectangle, hear the shutter sound, and get a full-resolution
PNG of exactly that area in `~/Documents/Screenshots` and on my clipboard (or only one of them,
per my setting), with no trace of Snapper's overlay in the image.

## Scope & changes

### Pure logic (unit-tested, no AppKit / ScreenCaptureKit imports)

- **`CaptureRegion` comes from 0004, not this spec.** Use `DisplayGeometry.captureRegion(for:on:)`
  (0004-display-geometry), which clamps, snaps outward to the pixel grid, and returns
  `CaptureRegion { displayID, appKitRect, sourceRect (display-local, top-left points), pixelSize }`.
  Do **not** create a second `CaptureRegion` type or file. The capturer uses
  `region.sourceRect` for `SCStreamConfiguration.sourceRect`, `region.pixelSize.width/height` for
  `width`/`height`, and `display.backingScale` as `pixelsPerPoint`. Before calling it, reject an
  empty clamped rect (defensive; 0007 already rejects drags under 4×4 pt) by checking
  `region.pixelSize.width == 0 || region.pixelSize.height == 0` and treating that as "nothing to
  capture" (return to idle without an alert).

- **`Snapper/Capture/CapturedImage.swift`** — `struct CapturedImage: Sendable { let cgImage: CGImage;
  let pixelsPerPoint: CGFloat }` plus `var pointSize: CGSize` (pixels ÷ pixelsPerPoint). This is
  the hand-off type for 0009 (window capture) and 0017 (Annotate's `AnnotationDocument` takes its
  `pixelsPerPoint`). `CGImage` is immutable; if the SDK doesn't mark it `Sendable`, use
  `@unchecked Sendable` with a comment saying so.

- **`Snapper/Capture/CaptureFailure.swift`** — `enum CaptureFailure { case permissionDenied, other }`
  with `init(_ error: Error)`: `.permissionDenied` when the error is
  `SCStreamError.userDeclined` — test it as `NSError(domain: SCStreamErrorDomain, code: -3801)` so
  the classifier only needs the domain string and code (import `ScreenCaptureKit` only for the
  constants, or compare against the literal domain `"com.apple.ScreenCaptureKit.SCStreamErrorDomain"`;
  either is fine as long as the test builds an `NSError` without calling SCK). Everything else →
  `.other`. 0011 (recording) reuses this.

- **`Snapper/Support/PNGEncoder.swift`** — `enum PNGEncoder { static func data(for image: CGImage,
  pixelsPerPoint: CGFloat) throws -> Data }` using ImageIO only
  (`CGImageDestinationCreateWithData`, `UTType.png.identifier`). Sets
  `kCGImagePropertyDPIWidth`/`kCGImagePropertyDPIHeight` = `72 × pixelsPerPoint` (144 on Retina,
  72 on 1×). **Why:** this writes a `pHYs` chunk, so Preview, Pages, Keynote, Slack, etc. show and
  paste the image at its on-screen point size instead of doubled — the same thing macOS's own
  `screencapture` does. Pixel data is untouched, so "native pixel resolution" (§6.2) holds. Throws
  a small `PNGEncoderError` if the destination can't be created or finalized. 0020 (annotation
  export) reuses this.

- **`Snapper/Capture/ScreenshotOutputPlan.swift`** — the decision logic around outputs:
  - `init(destination: ScreenshotDestination)` → `let copiesToClipboard: Bool`,
    `let savesToFile: Bool` (`.file` → save only; `.clipboard` → copy only; `.both` → both).
  - **Order is fixed: copy first, then save.** With *Both*, the clipboard copy therefore happens
    regardless of whether the save later fails (§9), and a modal save alert never delays the
    clipboard.
  - `static func saveFailureAlert(for error: Error) -> SaveFailureAlert` where
    `enum SaveFailureAlert { case saveFolder, generic }`: `.saveFolder` for any `SaveFolderError`,
    and for write errors meaning "can't write here" — `CocoaError.fileWriteNoPermission`,
    `.fileWriteVolumeReadOnly`, `.fileNoSuchFile` (folder vanished), and POSIX `EACCES`, `EPERM`,
    `EROFS` (direct or as `NSUnderlyingErrorKey`). Everything else (e.g. `.fileWriteOutOfSpace`) →
    `.generic`.
  - `static func folderDisplayName(_ url: URL) -> String` — the tilde-abbreviated path used in
    "Couldn't save to <folder>" (e.g. `~/Documents/Screenshots`; a path outside home stays
    absolute). Use `NSString.abbreviatingWithTildeInPath` (Foundation only) — or take the home
    directory as a parameter so the test is deterministic.

- **`Snapper/Support/ShutterSound.swift`** (pure part) — `enum ShutterSoundSource: Equatable {
  case file(URL), named(String) }` and
  `struct ShutterSoundResolver { static let systemSoundURL = URL(fileURLWithPath: "/System/Library/Components/CoreAudio.component/Contents/SharedSupport/SystemSounds/system/Screen Capture.aif");
  static func resolve(fileExists: (URL) -> Bool, namedSoundExists: (String) -> Bool) -> ShutterSoundSource? }`:
  file if it exists → else `"Grab"` if available → else `"Pop"` if available → else `nil`
  (play nothing, log once). Exactly the §6.4 chain.

### Platform glue (manually verified)

- **`Snapper/Capture/ScreenshotCapturer.swift`** — `@MainActor final class ScreenshotCapturer`
  (or a struct with `@MainActor` methods) exposing
  `func capture(_ region: CaptureRegion, pixelsPerPoint: CGFloat) async throws -> CapturedImage`:
  1. `let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)`.
     `onScreenWindowsOnly: false` makes sure Snapper is listed in `content.applications` even when
     none of its windows is currently on screen.
  2. `display = content.displays.first { $0.displayID == region.displayID }`; if missing (display
     unplugged between selection and capture) throw `ScreenshotCaptureError.displayUnavailable`
     ("The display is no longer available.").
  3. `ourApp = content.applications.first { $0.processID == getpid() }` (fall back to matching
     `Bundle.main.bundleIdentifier`). Filter:
     `SCContentFilter(display: display, excludingApplications: ourApp.map { [$0] } ?? [], exceptingWindows: [])`.
     Excluding the whole application (not individual windows) is the AGENTS.md rule and also covers
     any Snapper window that appears later (pill, annotation windows, alerts).
  4. `SCStreamConfiguration`: `sourceRect = region.sourceRect`, `width = region.pixelSize.width`,
     `height = region.pixelSize.height` (native pixels — this is what makes the PNG 2× on Retina),
     `showsCursor = false`, `scalesToFit = false`,
     `colorSpaceName = CGColorSpace.sRGB`, `capturesAudio = false`.
     **Color space decision — sRGB:** screenshots are mostly pasted into chat, web, docs and
     tickets, where untagged-or-sRGB is assumed; sRGB gives identical-looking output on every
     display and in every consumer, and the clipboard/PNG carry a standard profile. The cost —
     clipping of wide-gamut (P3) colors that are outside sRGB — is negligible for UI screenshots.
     (Display P3 would preserve those colors but renders dull/oversaturated in apps that ignore
     color profiles.)
  5. `let cgImage = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)`.
     Because the method is `@MainActor`-isolated, execution resumes on the main actor after each
     `await`.
  6. Return `CapturedImage(cgImage: cgImage, pixelsPerPoint: pixelsPerPoint)` (the selected display’s backing scale).
  Errors propagate unchanged so `CaptureFailure(error)` can classify them.

- **`Snapper/Support/Clipboard.swift`** — `@MainActor enum Clipboard { @discardableResult
  static func copy(pngData: Data, image: CGImage) -> Bool }`: `NSPasteboard.general.clearContents()`,
  then one `NSPasteboardItem` carrying **`.png`** (the encoded data, so DPI metadata travels and
  modern apps get lossless PNG) **and `.tiff`** (`NSBitmapImageRep(cgImage:).tiffRepresentation`,
  for older/Carbon-era apps that only read TIFF — this matches what macOS's own ⌃⌘⇧4 puts on the
  pasteboard). Returns `writeObjects`' result. The PNG is encoded once and shared with the file save.

- **`Snapper/Support/ShutterSound.swift`** (glue part) — `@MainActor final class ShutterSound`
  with `func play()`; resolves lazily via `ShutterSoundResolver` using
  `FileManager.default.fileExists(atPath:)` and `NSSound(named:) != nil`, caches the `NSSound`
  (keep a strong reference — an `NSSound` that is deallocated mid-play stops). Calling `play()`
  while it is already playing restarts it (`stop()` then `play()`). The caller checks
  `AppSettings.playShutterSound`; `ShutterSound` itself has no settings dependency.

- **`Snapper/Support/ErrorAlerts.swift`** — `@MainActor enum ErrorAlerts`. Every alert calls
  `NSApp.activate()` first (the app is `LSUIElement`, so otherwise the alert opens behind the
  frontmost app) and uses `NSAlert.runModal()`.
  - `static func showCaptureFailure(_ error: Error)` — message "Couldn't take screenshot",
    informative text `error.localizedDescription`, single "OK" button. Style `.warning`.
  - `static func showError(title: String, error: Error)` — same shape, custom title (used for
    "Couldn't copy to clipboard" and "Couldn't save screenshot" generic save errors; reused by
    0011 for recording errors).
  - `static func showSaveFolderFailure(folder: URL, error: Error, settings: AppSettings) -> URL?` —
    message `"Couldn't save to \(ScreenshotOutputPlan.folderDisplayName(folder))"`, informative text
    `error.localizedDescription`, buttons **"Choose Folder…"** (default, first) and **"Cancel"**.
    Choose Folder… opens an `NSOpenPanel` (`canChooseDirectories = true`,
    `canChooseFiles = false`, `canCreateDirectories = true`, `allowsMultipleSelection = false`,
    `prompt = "Choose"`, `directoryURL` = the failing folder if it exists, otherwise its nearest
    existing parent). On OK it writes `settings.saveFolderURL = chosenURL` and returns the URL;
    otherwise returns `nil`. 0010's Settings folder picker may reuse the panel setup.

- **`Snapper/App/AppCoordinator.swift`** — wire the `.screenshot` purpose:
  1. `startScreenshot()` keeps its 0006 permission gate (no permission → onboarding, no overlay)
     and opens the overlay with purpose `.screenshot` (0007).
  2. On `SelectionResult.rect(display, rect)` with purpose `.screenshot`: the overlay is already
     ordered out (0007's contract; verify `orderOut` happened before calling the capturer — the
     app-level `SCContentFilter` exclusion is the second line of defense). Build
     `DisplayGeometry.captureRegion(for:on:)`, then run the pipeline in a `Task { @MainActor in … }`:
     - `let captureDate = Date()` taken immediately before capturing (§9: "capture time").
     - `try await capturer.capture(region, pixelsPerPoint: display.backingScale)`. On error: `CaptureFailure(error) == .permissionDenied`
       → show the onboarding window (0006) and stop; otherwise `ErrorAlerts.showCaptureFailure`
       and stop. No sound, no output on failure.
     - Success → if `settings.playShutterSound`, `shutterSound.play()`.
     - `let png = try PNGEncoder.data(for:pixelsPerPoint:)` — encode off the main actor (a
       `nonisolated` async helper / `Task.detached`) since a full 5K frame takes noticeable time;
       come back to the main actor for clipboard and alerts. Encoding failure → generic alert.
     - `let plan = ScreenshotOutputPlan(destination: settings.screenshotDestination)`.
     - If `plan.copiesToClipboard`: `Clipboard.copy(…)`; `false` →
       `ErrorAlerts.showError(title: "Couldn't copy to clipboard", …)` (continue to save).
     - If `plan.savesToFile`: resolve/create the folder with `SaveFolder` (0002) from
       `settings.saveFolderURL` (this is what creates `~/Documents/Screenshots` on first use), pick
       the URL with `FileNamer` for `CaptureKind.screenshot` + `captureDate` + a
       `FileManager.fileExists` predicate, write with `Data.write(to:options: [.withoutOverwriting])`
       (on `.fileWriteFileExists` from a race, ask `FileNamer` again once). The folder is **not**
       touched when the destination is clipboard-only.
     - Save failure → `ScreenshotOutputPlan.saveFailureAlert(for:)`: `.saveFolder` →
       `ErrorAlerts.showSaveFolderFailure`; if it returns a new folder, **retry the save once into
       that folder** with the same `captureDate` and PNG data (confirmed by the user); a second failure shows the same alert again with no further automatic retry
       loop beyond what the user drives. `.generic` →
       `ErrorAlerts.showError(title: "Couldn't save screenshot", …)`.
     - Log successes/failures with `os.Logger(subsystem: "com.thera-engineering.snapper",
       category: "screenshot")`.
  3. Mode: return `AppModeMachine` (0005) to idle exactly as 0005/0007 define for a finished
     selection; do not add new modes here. (A second ⌘⇧4 pressed during the ~100 ms capture is
     allowed to open a new overlay; captures are independent.)
  4. `.annotate` purpose with a rect result: keep the stub — log "Annotate not implemented yet"
     and do nothing. 0017 wires `ScreenshotCapturer` → annotation window.
  5. `SelectionResult.window` is not produced until 0009; if the switch needs a case, log and ignore.

- **Files / project:** new files under `Snapper/Capture/`, `Snapper/Support/`, and tests under
  `SnapperTests/`. No `project.yml` changes beyond what XcodeGen picks up from the source folders
  (ScreenCaptureKit, ImageIO, UniformTypeIdentifiers and AppKit are system frameworks — link
  implicitly via `import`; add them to `project.yml` `dependencies: - sdk: …` only if the build
  needs it). Run `xcodegen generate` after adding files.

- **PRD:** the user-approved cursor, folder retry, and alert decisions are already recorded in
  `docs/PRD.md` §6/§9. No additional behavior decision or AGENTS.md change was needed.
- **Implementation detail:** `ScreenshotFileWriter` shares `SaveFolder` preparation and `FileNamer`,
  guards writes with `.withoutOverwriting`, and retries a collision once. `SaveFolder.prepareForWriting`
  preserves folder-creation system error details for alerts; its older typed `prepare()` contract stays
  compatible. On failure of the single folder retry, Choose Folder… can update the next capture’s
  setting but does not retry the current screenshot again.

## Acceptance criteria

- [ ] ⌘⇧4 + rectangle drag on a Retina display produces a PNG whose pixel size is 2× the selected
      point size (±1 px from rounding), saved to `~/Documents/Screenshots` and copied to the
      clipboard with the default *Both* destination (§12).
- [ ] On a 1× display the PNG pixel size equals the point size.
- [ ] The PNG's DPI is 144 on 2× and 72 on 1×; opened in Preview, it shows at its on-screen size.
- [ ] The capture comes from the display where the drag started, including a secondary display
      positioned left of or below the main one (negative AppKit origin).
- [ ] The captured image contains no dimming, selection rectangle, size readout or other Snapper
      window.
- [ ] The cursor is not in the screenshot.
- [ ] *Save to file* only saves; *Copy to clipboard* only copies and never creates the save folder;
      *Both* does both.
- [ ] The file name is `Screenshot YYYY-MM-DD at HH.mm.ss.png` using the capture time, with
      ` (2)`, ` (3)` … on collision (two captures in the same second).
- [ ] The shutter sound plays on a successful capture when *Play shutter sound* is on, and doesn't
      when off. No sound when the capture fails.
- [ ] If `~/Documents/Screenshots` doesn't exist, it is created and the file saved into it (§12).
- [ ] If the save folder isn't writable, the alert "Couldn't save to <folder>" appears with
      **Choose Folder…**; with *Both*, the image is on the clipboard anyway. Choosing a folder
      updates the save folder setting and saves the pending screenshot there once.
- [ ] If Screen Recording permission is denied at capture time, the onboarding window opens and no
      alert is shown.
- [ ] Any other capture failure shows a short alert with the error.
- [ ] ⌘⇧6 still just logs (no crash, no output); ⌘⇧5 behavior unchanged.
- [ ] `xcodebuild` build and test succeed.

## Edge cases

- **Mixed scales:** Retina built-in + 1× external — `CaptureRegion` uses the drag display's
  `backingScale` only; never `NSScreen.main`.
- **Negative-origin displays** (left of / below main): conversion goes through `DisplayGeometry`;
  `sourceRect` is display-local so its origin is never negative.
- **Fractional points:** Retina mouse positions at .5 pt → snapped outward to whole pixels; pixel
  size is an integer and never 0.
- **Selection touching the display edge / full-display selection:** clamped; full display captures
  exactly `frame.size × scale` pixels.
- **Display disconnected between selection and capture:** 0007 cancels while the overlay is open;
  if it disappears after, `displayUnavailable` → generic alert.
- **Permission revoked while running:** SCK returns `userDeclined` → onboarding, not an alert.
  Other SCK errors (e.g. `-3802` failedToStart) → generic alert. If capture returns blank images,
  check permission/signature first (AGENTS.md).
- **Snapper not found in `SCShareableContent.applications`:** proceed with an empty exclusion list
  (the overlay is already ordered out) and log it.
- **Save folder missing:** created via `SaveFolder`. **Save folder is a file, on a read-only or
  ejected volume, or permissions removed:** `.saveFolder` alert. **Disk full:** generic
  "Couldn't save screenshot" alert.
- **User cancels Choose Folder…:** nothing more happens; the screenshot is lost unless it was
  copied (*Both*/*Clipboard*).
- **Two captures in the same second:** collision suffix; `.withoutOverwriting` guards against races.
- **Very large captures** (6K full display ≈ 6016×3384): PNG encoding is off the main actor;
  the UI stays responsive.
- **Clipboard write fails:** short alert; saving still proceeds.
- **Shutter sound file missing** (future macOS): falls back to Grab → Pop → silent.

## Test plan

- **Unit tests**
  - No `CaptureRegionTests` here: region math (clamping, top-left flip, pixel snapping, mixed
    scales, negative origins) is tested in 0004's `DisplayGeometryTests`. Add one case there only if
    a scenario below is missing from it: primary 2× `frame (0,0,1512,982)`, selection
    `(100,100,200,100)` → `sourceRect (100,782,200,100)`, pixels `400×200`.
  - `SnapperTests/ScreenshotOutputPlanTests.swift`
    - Table: `.file` → (save true, copy false); `.clipboard` → (false, true); `.both` → (true, true).
    - `saveFailureAlert`: a `SaveFolderError` value → `.saveFolder`; `CocoaError(.fileWriteNoPermission)`
      → `.saveFolder`; `CocoaError(.fileWriteVolumeReadOnly)` → `.saveFolder`;
      `NSError(NSPOSIXErrorDomain, EACCES)` → `.saveFolder`; `CocoaError` wrapping POSIX `EROFS` as
      underlying error → `.saveFolder`; `CocoaError(.fileWriteOutOfSpace)` → `.generic`; an
      arbitrary `NSError(domain: "x", code: 1)` → `.generic`.
    - `folderDisplayName`: `<home>/Documents/Screenshots` → `~/Documents/Screenshots`;
      `/Volumes/Data/Shots` → unchanged.
  - `SnapperTests/CaptureFailureTests.swift`
    - `NSError(domain: SCStreamErrorDomain, code: -3801)` → `.permissionDenied`; code `-3802` →
      `.other`; `CocoaError(.fileReadUnknown)` → `.other`.
  - `SnapperTests/ShutterSoundResolverTests.swift`
    - file exists → `.file(systemSoundURL)` (and the named predicate isn't needed).
    - file missing, Grab available → `.named("Grab")`.
    - file missing, Grab missing, Pop available → `.named("Pop")`.
    - nothing available → `nil`.
    - `systemSoundURL.path` equals the exact §6.4 path.
  - `SnapperTests/PNGEncoderTests.swift`
    - Synthetic 200×100 RGBA `CGImage` (drawn in a `CGContext`, sRGB) encoded with
      `pixelsPerPoint: 2` → decode with `CGImageSourceCreateWithData`: type is `public.png`, image
      is 200×100 pixels, `kCGImagePropertyDPIWidth`/`DPIHeight` == 144.
    - Same with `pixelsPerPoint: 1` → DPI 72.
    - Round-trip keeps a known pixel's color (e.g. top-left pixel red) — guards against a
      flipped/blank encode.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build`
  and `xcodebuild -scheme Snapper test` succeed.
- **Manual verification** (real GUI session, Screen Recording permission granted; can't be automated).
  Until 0010 supplies Settings, quit Snapper before each `defaults write` and relaunch afterward so
  typed settings reload. The keys are `screenshotDestination` (`file`, `clipboard`, `both`),
  `playShutterSound` (Boolean), and `saveFolderPath` (absolute folder path):
  1. Defaults (destination *Both*, sound on). On the Retina built-in display press ⌘⇧4, drag a
     ~300×200 pt rectangle. Expect: shutter sound; a new `Screenshot <date> at <time>.png` in
     `~/Documents/Screenshots`. Run `sips -g pixelWidth -g pixelHeight -g dpiWidth "<file>"` →
     ≈600×400, dpi 144. ⌘V into Preview (File ▸ New from Clipboard) shows the same image.
  2. Open the PNG in Preview: no dim overlay, no selection border, no W×H label, no cursor; the
     area matches what was selected.
  3. On a 1× external display (if available): same steps → pixel size equals point size, dpi 72.
  4. Arrange an external display **left of** (or below) the main one in System Settings ▸ Displays.
     Drag on it → the image shows that display's content at the selected spot.
  5. Settings destination *Save to file* (via `defaults write com.thera-engineering.snapper
     screenshotDestination -string file` until 0010 lands, using the key/raw value from 0003): capture →
     file saved; clipboard unchanged (copy some text first; it's still there).
  6. Destination *Copy to clipboard*: `mv ~/Documents/Screenshots ~/Documents/Screenshots.bak`,
     capture → clipboard has the image; `~/Documents/Screenshots` was **not** created. Restore.
  7. Turn the shutter sound off (`defaults write … playShutterSound -bool NO`), capture → no
     sound; back on → sound.
  8. Destination *Both*: move `~/Documents/Screenshots` aside without deleting existing captures,
     capture → folder recreated with the file in it.
  9. `chmod a-w ~/Documents/Screenshots`, capture with *Both* → alert "Couldn't save to
     ~/Documents/Screenshots" with **Choose Folder…**; ⌘V shows the image (clipboard copy
     happened). Click Choose Folder…, pick e.g. `~/Desktop` → screenshot saved there; next capture
     goes to `~/Desktop`. Repeat and click **Cancel** → nothing else happens. Restore with
     `chmod u+w ~/Documents/Screenshots` and reset the folder.
  10. Take two screenshots within the same second (drag quickly twice or reuse a script) → second
      file gets ` (2)`.
  11. Remove Snapper from System Settings ▸ Privacy & Security ▸ Screen & System Audio Recording
      while it is running (or `tccutil reset ScreenCapture com.thera-engineering.snapper`), press
      ⌘⇧4 → onboarding opens (either from the 0006 gate or from the `userDeclined` path), no error
      alert.
  12. Press ⌘⇧6, drag → nothing is saved or copied (stub; log line in Console).

## Open questions

None. User confirmed on 2026-11-03: Screenshots exclude the cursor. Choosing a folder updates the setting and retries the pending screenshot save once. Save alerts use a tilde-abbreviated path, Cancel, and system error details; generic capture failures use “Couldn't take screenshot”. Recorded in docs/PRD.md.

## Out of scope

- Window mode, window hit-testing and shadowless single-window capture — 0009.
- Settings UI for destination, sound and folder (this spec only reads `AppSettings`) — 0010.
- Recording, its error handling and Finder reveal — 0011–0014 (they may reuse `CaptureFailure`,
  `ErrorAlerts`, `SaveFolder`).
- Opening captures in the annotation editor (⌘⇧6) — 0017; full-resolution annotation export,
  ⌘S/⌘C there — 0020 (reuses `PNGEncoder`, `Clipboard`).
- The selection overlay itself (dim, readout, Esc, display-change cancel) — 0007.
- JPEG output, floating thumbnails, window shadows — PRD §2 non-goals.

## Verification status

- 2026-11-03: `xcodegen generate` succeeded.
- 2026-11-03: `xcodebuild -scheme Snapper -configuration Debug build` — **BUILD SUCCEEDED**.
- 2026-11-03: `xcodebuild -scheme Snapper test` — **TEST SUCCEEDED**, 156 tests in 25 suites passed.
  New suites cover output destinations, save-error classification and paths, permission classification,
  shutter sound fallback, PNG pixel/DPI/color round-trips, image point size, and file creation/collisions.
  Existing geometry coverage includes 1×/2× displays, mixed scales, negative origins, fractional points,
  display edges, and the exact Retina source-rect example in this spec.
- No GUI capture, hotkey, TCC, pasteboard, sound, or modal UI verification was performed.
- No PRD or AGENTS.md edits were needed: the approved decisions were already recorded.

Manual checks (use the detailed procedure in Test plan above):

- [ ] Not yet run by user: default Retina capture saves and copies, with native pixels and 144 DPI.
- [ ] Not yet run by user: Preview output matches selection, with no overlay, Snapper windows, or cursor.
- [ ] Not yet run by user: external 1× capture has native pixels and 72 DPI.
- [ ] Not yet run by user: capture on a display left of or below the main display uses the correct area.
- [ ] Not yet run by user: file-only destination preserves the previous clipboard contents.
- [ ] Not yet run by user: clipboard-only destination copies without creating the save folder.
- [ ] Not yet run by user: shutter sound respects its setting and capture failures remain silent.
- [ ] Not yet run by user: a missing save folder is created for file output.
- [ ] Not yet run by user: unwritable-folder alert includes its abbreviated path and error details;
  clipboard output survives; Choose Folder updates the setting and saves the pending screenshot once;
  Cancel stops; a failed retry shows an alert without another pending-image retry.
- [ ] Not yet run by user: two captures within one second preserve both files with a collision suffix.
- [ ] Not yet run by user: denied or revoked Screen Recording permission opens onboarding without a capture alert.
- [ ] Not yet run by user: Annotate still logs without output, and Record remains unchanged.

Rebuilding with a different signature can reset Screen Recording permission; check permission first if
capture returns blank images or errors.
