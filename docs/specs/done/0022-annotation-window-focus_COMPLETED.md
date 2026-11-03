# 0022 — Open the annotation window focused after a ⌘⇧6 selection

> Source: user request ("after selecting the area, make sure the annotate window pops into focus"); docs/PRD.md §5 (focus-return bullet after "After a selection, the overlay is removed…"), §8.1 (the "opens in front as the key window" bullet), §12 (the "After a ⌘⇧6 selection…" criterion)
> Depends on: 0007-selection-overlay-rectangle, 0017-annotation-window-canvas (both done). Independent of 0021-annotation-copy-and-close.

## Context

After a ⌘⇧6 selection, the annotation window often opens **behind** the app the user was in (or opens without becoming key), so the user has to click it before tool keys and ⌘ shortcuts work.

Root cause:

- `SelectionOverlayController.present()` records the frontmost app (`Snapper/Capture/SelectionOverlayController.swift:34-35`) and activates Snapper (`:44`).
- When a selection finishes, `finish(with:)` (`:150-162`) calls `close()` (`:164-178`). Unconditionally, `close()` reactivates the previous app (`:176`: `previousApplication.activate()`). This makes Snapper resign active status before the capture even starts.
- `AppCoordinator.beginSelection` (`Snapper/App/AppCoordinator.swift:60-79`) then captures, and `takeScreenshot` (`:207-224`) calls `AnnotationWindowManager.open` (`Snapper/Annotate/AnnotationWindowManager.swift:14-31`). That method calls `NSApp.activate()` (`:28`) and `makeKeyAndOrderFront` (`:29`).
- On macOS 14+, `NSApp.activate()` is *cooperative*: an inactive app's request can be refused because the user never interacted with it after the previous app took focus. So the window shows up behind or without key status, depending on timing.

Handing focus back is correct for ⌘⇧4 (nothing of ours appears afterwards), for ⌘⇧5 (the pill is a non-activating panel), and for every cancellation. It is wrong for ⌘⇧6, where our own window is about to appear.

PRD behavior delivered (updated in this change, see below):

- §5: "When the overlay closes, focus returns to the app that was active before the shortcut, except after a ⌘⇧6 selection: Snapper then keeps focus so the annotation window can open in front (section 8.1). Esc and display-change cancellations always return focus."
- §8.1: the annotation window "opens in front as the key window, so keyboard shortcuts and tool keys work without clicking it first. Closing it does not change focus beyond default macOS behavior."
- §12: "After a ⌘⇧6 selection, the annotation window opens focused in front of the previously active app."

## User story

As a Snapper user, after I select an area with ⌘⇧6, the annotation window appears in front and focused, so I can start drawing or press tool keys and ⌘S/⌘C right away without clicking it first.

## Scope & changes

### Pure logic (unit-tested)

- **New `Snapper/Capture/SelectionFocusPolicy.swift`**: a caseless `enum SelectionFocusPolicy`.
  - `enum Outcome: Equatable, Sendable { case selected, cancelled }`
  - `static func restoresPreviousApplication(purpose: CapturePurpose, outcome: Outcome) -> Bool`
    - `.cancelled` → `true` for every purpose (Esc, display-configuration change, no panels).
    - `.selected` → `true` for `.screenshot` and `.record`, `false` for `.annotate`.
  - Use an exhaustive `switch` over `CapturePurpose` with no `default:`, so a future purpose has to make an explicit choice.

### Platform glue (manually verified)

- **`Snapper/Capture/SelectionOverlayController.swift`**:
  - Change `selectRegion()` to `selectRegion(for purpose: CapturePurpose) async -> SelectionResult?` and store the purpose for the session (clear it in `close()` together with `previousApplication`).
  - Give `close()` an `outcome: SelectionFocusPolicy.Outcome` parameter. `cancel()` passes `.cancelled`, and `finish(with:)` passes `.selected`. Line 176 then reactivates `previousApplication` only when `SelectionFocusPolicy.restoresPreviousApplication(purpose:outcome:)` returns `true`. In every case, `previousApplication` is still set to `nil`.
  - Keep everything else unchanged: panels ordered out, `CATransaction.flush()`, the dismissal delay, and the cursor reset.
- **`Snapper/App/AppCoordinator.swift:67`**: call `overlay.selectRegion(for: purpose)`.
- **`Snapper/Annotate/AnnotationWindowManager.swift:27-30`**: keep `NSApp.activate()` → `makeKeyAndOrderFront` → `makeFirstResponder(canvas)`. As a fallback in case activation is still refused, also call `controller.window?.orderFrontRegardless()` right after `makeKeyAndOrderFront`, so the window is at least visible on top. Don't use the deprecated `activate(ignoringOtherApps:)`.
- If the annotate capture **fails**, the previous app stays inactive. The onboarding window or `ErrorAlerts` activates Snapper anyway (`Snapper/Support/ErrorAlerts.swift:10,20`), so no extra handling is needed.
- No `project.yml` change is needed: XcodeGen picks up new files under `Snapper/` by folder. Still run `xcodegen generate` so the new file joins the generated project.

### PRD

- docs/PRD.md §5, §8.1, and §12 were updated in this change (the bullets quoted above). "Last updated" is already 2026-11-03.

## Acceptance criteria

- [ ] After a ⌘⇧6 rectangle or window selection, the annotation window appears in front of the previously active app, as the key window, with the canvas as first responder. Pressing a tool key or ⌘Z works without clicking.
- [ ] This works the same when ⌘⇧6 is pressed while another app (for example Safari or Finder) is frontmost, and when an annotation window is already open (the new window comes in front of the old one and becomes key).
- [ ] After ⌘⇧4, focus still returns to the previously active app (unchanged).
- [ ] After ⌘⇧5 starts recording, focus still returns to the previously active app (unchanged).
- [ ] Esc during a ⌘⇧6 selection returns focus to the previously active app, and no annotation window opens.
- [ ] Closing the annotation window doesn't do anything new about focus.
- [ ] `SelectionFocusPolicy` exists with the unit tests below. Build and the full test suite pass.

## Edge cases

- **Previous app was Snapper itself** (for example, the Settings window was frontmost): `previousApplication` is already `nil` (`:35`), so nothing changes.
- **Previous app quit during the selection**: already guarded by `isTerminated`. It's irrelevant for annotate, which no longer reactivates.
- **Display configuration change during a ⌘⇧6 selection**: this is a cancellation, so focus returns to the previous app.
- **Annotate capture fails** (permission revoked or another error): the onboarding window or alert comes forward because both activate Snapper. The previous app is not reactivated afterwards (accepted).
- **Annotation window on another display**: the window opens on the capture display (existing `open` logic) and still becomes key there.
- **Multiple annotation windows**: the new window becomes key, and the older ones stay open behind it.

## Test plan

- **Unit tests** (`SnapperTests/SelectionFocusPolicyTests.swift`, Swift Testing, like `SnapperTests/CapturePurposeTests.swift`):
  - `cancelledAlwaysRestores`: for every `CapturePurpose.allCases`, `restoresPreviousApplication(purpose:, outcome: .cancelled) == true`.
  - `screenshotSelectionRestores`: `.screenshot`, `.selected` → `true`.
  - `recordSelectionRestores`: `.record`, `.selected` → `true`.
  - `annotateSelectionKeepsFocus`: `.annotate`, `.selected` → `false`.
- **Build check:** `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, `xcodebuild -scheme Snapper test`. All succeed.
- **Manual verification** (needs a real GUI session with Screen Recording granted; can't be automated):
  1. Click in a Finder window so Finder is frontmost. Press ⌘⇧6 and drag a rectangle. **Expected:** the annotation window appears in front of Finder with an active (non-dimmed) title bar. The menu bar shows Snapper's menus.
  2. Without clicking, press `R` (or any tool key), then drag on the canvas. **Expected:** the tool switches and the shape is drawn. Press ⌘Z. **Expected:** the shape is undone.
  3. Repeat step 1 with Safari frontmost, using window mode (Space, then click a window). **Expected:** same result as step 1.
  4. With one annotation window open, click Safari, then press ⌘⇧6 and select. **Expected:** the new annotation window is in front and key.
  5. With Finder frontmost, press ⌘⇧4 and select. **Expected:** Finder is frontmost and active again after the capture.
  6. With Finder frontmost, press ⌘⇧5, select, and let the recording start. **Expected:** Finder is frontmost and active again while recording. Stop the recording.
  7. With Finder frontmost, press ⌘⇧6 and then Esc. **Expected:** no annotation window, and Finder is active again.
  8. Repeat step 1 ten times in a row. **Expected:** the window is focused every time (the bug was timing-dependent).

## Out of scope

- Restoring focus to the previous app when the last annotation window closes (decided: keep default macOS behavior).
- Copy & Close (0021-annotation-copy-and-close).
- Any change to the selection overlay's own activation in `present()`.

## Verification status

Automated verification on 2026-11-03: `xcodegen generate`, `xcodebuild -scheme Snapper -configuration Debug build`, and `xcodebuild -scheme Snapper test` all passed. The full suite passed **368 tests in 67 suites**, including four new selection-focus policy tests. PRD §5, §8.1, and §12 already describe this behavior; no PRD or AGENTS.md changes were needed.

GUI behavior has not been verified. With Screen Recording permission granted:

- [ ] not yet run by user: From Finder, press ⌘⇧6 and select a rectangle; confirm the editor is in front, has an active title bar, and Snapper owns the menu bar.
- [ ] not yet run by user: Without clicking first, use a tool key, draw a shape, and undo with ⌘Z.
- [ ] not yet run by user: From Safari, use ⌘⇧6 window mode and confirm the new editor is in front and key.
- [ ] not yet run by user: With an editor already open, capture another annotation from Safari; confirm the new editor becomes key above the old one.
- [ ] not yet run by user: From Finder, take a ⌘⇧4 screenshot and confirm focus returns to Finder.
- [ ] not yet run by user: From Finder, start a ⌘⇧5 recording and confirm Finder regains focus; stop the recording.
- [ ] not yet run by user: From Finder, cancel a ⌘⇧6 selection with Esc; confirm Finder regains focus and no editor opens.
- [ ] not yet run by user: Repeat the Finder annotation capture ten times and confirm focus succeeds every time.

Rebuilding with a different signature can reset Screen Recording permission.
