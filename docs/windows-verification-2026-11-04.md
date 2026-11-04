# Windows verification — 2026-11-04

Result: **not accepted as fully verified**. Build and packaging pass; two automated tests fail and passive Shortcuts tab entry fails. Most capture-dependent end-to-end acceptance checks remain unverified.

## Environment and scope

- Windows 11 Pro, 10.0.26200, x64; repository HEAD `0f7a181c4203fc1c5d9dd546e018d4a0a2a2432c`.
- Source of truth: PRD §§5–10, 12, 13. Existing working-tree PRD edits and todo specs 0048–0050 were present before testing and were preserved.
- Desktop automation used the Windows computer-use skill. No application code was changed and no specs were moved to done.
- A Snapper process was already running. Launching the Debug executable opened Settings; only one Snapper process was observed afterward. The running process was not restarted, so GUI observations do not establish that a freshly built process was exercised.
- Existing bindings are Ctrl+Alt+Num 4/5/6, rather than the defaults. They were preserved. Recording format was temporarily switched to GIF, then restored to MP4; original High MP4 and Medium GIF qualities remained selected.

## Automated results

| Command | Result |
|---|---|
| `dotnet build windows/Snapper.slnx -c Debug` | PASS, 0 warnings, 0 errors |
| `dotnet test windows/Snapper.slnx` | FAIL: 1,377 passed, 2 failed, 4 skipped; 1,383 total |
| `dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-x64` | PASS |
| `dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-arm64` | PASS |

Each publish directory contained one `Snapper.exe`: x64 77,933,076 bytes; Arm64 73,680,053 bytes. This verifies artifact creation, not clean-machine execution or native Arm64 runtime behavior.

Failures:

1. `AtomicFileWriterTests.FailedRenameCleansTemporaryAndPreservesExistingEntry`, line 20: expected `IOException`; Windows returned `UnauthorizedAccessException` when replacing a directory. Stack reaches `AtomicFileWriter.Replace`, line 15. The exception assertion prevents the subsequent preservation/cleanup assertions from running; those assertions are not verified by this test result.
2. `JsonSettingsStoreTests.SerializePinsSchemaAndCanonicalModifierOrder`, line 32: `Assert.DoesNotContain('\r', text)` fails because serialization contains CRLF on Windows. Earlier schema assertions passed; the final round-trip assertion in this test was not reached.

Skipped: `PhysicalSaveFolderFileSystemTests.DanglingSymlinkIsACollision`, `ReadOnlyParentThrowsCannotCreate`, `DirectorySymlinkIsAccepted`, and `ReadOnlyFolderThrowsNotWritable`. Skips are not passes.

## GUI observations

| Check | Result and evidence |
|---|---|
| Second launch opens Settings, one process | Observed; tray icon count was not inspected |
| General controls | Displayed save folder, Both destination, shutter enabled, login disabled |
| Passive Shortcuts entry | **FAIL**: clicking Shortcuts displayed `Press Shortcut` in Screenshot without deliberately entering its field; matches existing spec 0049 |
| Escape exits recorder | PASS: existing Ctrl+Alt+Num 4 binding returned |
| Recording controls | MP4 High displayed; switching to GIF displayed Medium and its size/frame-rate explanation |
| GIF microphone rule | PASS: checkbox disabled and `GIFs don't include sound.` displayed |
| Return to MP4 | PASS: High selected and microphone enabled as a control, still unchecked; settings JSON confirmed original values |
| Screenshot hotkey/selection | INCONCLUSIVE: injected configured numpad chord, but no targetable selection overlay was returned. Subsequent screenshot-backed targeting was inconsistent. No successful output or capture-error alert was established |

No capture or recording failure is claimed reproduced here. Existing 0048 and 0050 describe earlier reports, not newly established results from this run.

## Remaining end-to-end acceptance checks

All rows below are **not run to completion**. Use a fresh process from the published x64 executable, then repeat architecture-sensitive checks on native Arm64. PRD §§12 and 13.11 apply.

| Area | Required procedure and expected result |
|---|---|
| Tray | Check no main taskbar window; left/right click open current-shortcut menu; during recording either stops and saves |
| Rectangle screenshot | Invoke Screenshot, drag a known area; verify PNG pixels/content, clipboard PNG and DIB, no cursor/overlay, and each destination independently |
| Window screenshot | Space toggles window mode; select a partially covered window; entire window, no shadow or covering content |
| Selection boundaries | Esc cancels; tiny clicks keep overlay open; Space during drag resets drag; other capture shortcuts ignored during selection |
| Displays | Repeat 100%/150%/200%, negative origins, cross-monitor drags; verify start-monitor clamping/native pixel dimensions; display changes cancel selection |
| MP4 | Record changing content with cursor; verify immediate start, playable output, timing, fixed even dimensions, and no pill/border |
| Recording lifecycle | Exercise Stop button, recording hotkey, tray stop; Explorer selects output. Exercise Cancel, Esc, ignored screenshot/annotate hotkeys, and exit while recording |
| Window recording | Move, resize, cover, minimize/restore, move across scales, then close target; verify following, fixed dimensions/letterboxing, reachable pill, and stop/save on close |
| MP4 quality | Same changing content/size/duration at High/Medium/Low; confirm dimensions/timing and smaller Medium/Low files; audio unchanged when present |
| GIF | Each quality; validate actual frame timing, output sizes, looping, no audio/pill/border; conversion busy state and exit waiting |
| Microphone | MP4 with available default input, no device/denied state, and disconnect mid-recording; video continues as specified. Permission/security dialogs require user handling |
| Annotation entry | Rectangle/window captures open focused editor without premature file/clipboard output; multiple editors; initial fit and no upscaling |
| Annotation editing | Rectangle/square, arrow/45-degree snap, text/multiline/commit/re-edit/empty removal; Select/move/resize, color/stroke, undo/redo, delete, Esc and tool keys |
| Annotation export | Save, Copy, Copy & Close including during text editing; full image resolution at different zooms; confirm clipboard content and file pixels |
| Unsaved annotations | Modified close prompts; cancel retains editor; saved/copied state; exit prompts once across editors |
| Settings | All shortcut rebindings/clear/duplicates/reserved keys, immediate effect, intentional Tab entry, reopen and tab-return focus, persistence after restart |
| Shortcut conflict | Hold a binding in another app; notification/warning, unaffected other bindings, failed action usable from tray, notification opens Shortcuts |
| Save errors/naming | Missing default folder, collision suffixes, unwritable-folder alert, Both still copies, Choose Folder retries pending screenshot once |
| Login | Toggle Run entry on/off; actual next sign-in; Startup apps disabled note |
| Fault/recovery | Genuine write/conversion failure retains/reveals nonempty original and alerts; crash temporary-file recovery; device reset with active leases |
| Session end | Unsaved annotation shutdown block and recording/conversion shutdown behavior; requires controlled session-end testing |
| Distribution | Execute published x64 and Arm64 on clean matching Windows 11 machines without .NET installed |

Do not mark the release or specs fully verified from this report. Resolve the two automated failures and spec 0049, then finish the capture-dependent matrix with reliable GUI input and artifact inspection.
