---
name: spec-implementer
description: >
  Work through the backlog of specs in docs/specs/todo/ and implement them one at a time, each in
  its own sub-agent, end to end: Swift code, unit tests, xcodegen + build + full test run with
  fixes, PRD/AGENTS.md updates, a manual-verification checklist, then commit on main and move the
  spec into docs/specs/done/. After the batch, bump the version and publish one GitHub release with
  the self-contained macOS and Windows binaries. Use this whenever the user wants the Snapper todo specs actually
  BUILT (not just planned): "implement the specs in docs/specs/todo", "work through the backlog",
  "build out the todo folder", "implement 0003 through 0007", "clear the spec queue". Trigger even
  when the user names specific spec files or a range instead of the whole folder. Do NOT trigger
  for authoring a new spec (that is the spec-writer skill). This skill assumes the specs already
  exist and implements them.
---

# Spec Implementer

This skill drives the **Snapper** implementation backlog. Each file in `docs/specs/todo/` is a
ready-to-build spec written by the spec-writer skill. Your job is to turn those specs into built,
tested, committed work, **one spec at a time, in order, each inside its own sub-agent.**

Running this skill counts as the user asking for commits. `AGENTS.md` says not to commit unless
asked, and here the user has asked, once per spec.

## Why one at a time, in order

Every spec ends with a full build and test run and a commit on `main`. Specs are numbered in
dependency order: later ones build on the types, files, and `project.yml` targets that earlier ones
create. Built in parallel, they would race on `project.yml`, the generated Xcode project,
DerivedData, and the same branch, and you couldn't tell which change broke what. Running them
strictly in order means each sub-agent starts from a clean, green `main` that already contains the
previous spec's work, so every failure can be traced to one spec and the history stays bisectable.
**Never parallelize.** Finish and check one spec before starting the next.

## Implementations take a long time, so be patient

One spec can keep a sub-agent busy for well over half an hour: Swift code, unit tests, xcodegen,
cold `xcodebuild` builds (the first one also resolves the KeyboardShortcuts package), and fixing
whatever the build turns up. That is normal, not a hang. Give each sub-agent the time it needs.
**Don't abandon it, interrupt it, or decide it has failed before at least 35 minutes of real
work.** A quiet sub-agent that is still building or testing is working, not stuck.

## What can't be verified here

Screen Recording permission, global hotkeys, overlays, capture, and recording need a real GUI
session with TCC permission granted. Neither you nor the sub-agents can check them. A spec is
"done" for this skill when it **builds, its unit tests pass, and its manual verification steps
are written down for the user**. It never counts as manually verified. Keep that distinction in
every report.

---

## Workflow

### 1. Preflight

Before building anything, check the environment once. Ask the user about anything that's missing;
these are their decisions, not yours:

- **Git:** the repo must be a git repository on `main` with a clean working tree. If it isn't a
  repo yet, ask whether to run `git init` (and make an initial commit of the existing files). If
  the tree is dirty, ask what to do with the changes. Never stash or discard them yourself.
- **Remote:** check for an upstream (`git rev-parse --abbrev-ref @{u}`). If there is one, each spec
  is pushed after it's committed. If there isn't, commits stay local, and you say so in the reports.
- **Release:** the batch ends with a GitHub release (step 6), so `gh auth status` must show a
  logged-in account with push access to `devnixs/ScreenshotApp`, and there must be an upstream.
  If either is missing, say so up front: the specs can still be built, but the release will be
  skipped.
- **Tooling:** for Windows specs (they touch `windows/`; titles say "Windows"), `dotnet` with the
  .NET 10 SDK must be on `PATH`. For macOS specs, `xcodegen` and `xcodebuild` must be on `PATH`. If `xcodegen` is missing, ask the
  user to install it (for example `brew install xcodegen`). Don't install tools on your own.

### 2. Determine the queue

List `docs/specs/todo/` and decide which specs to implement:

- If the user named specific specs or a range (for example "0003 through 0007"), do exactly those.
- Otherwise, do **every** `*_TODO.md` in the folder, in ascending filename order. The numbering is
  the dependency order.

For each spec, check its `Depends on:` line. If a dependency is neither in `docs/specs/done/` nor
earlier in this queue, flag it. A spec with a non-empty **Open questions** section isn't ready:
list those questions and get answers (update the spec and the PRD) before it runs, or leave it out
of the queue.

Show the user the ordered list and the count, then start. Once they've approved the queue, you
don't need approval for each spec. The point of this skill is to run the whole batch on its own.

### 3. For each spec, spawn one sub-agent and wait for it

Work through the queue top to bottom. For each spec, launch a **single** `general-purpose`
sub-agent with the brief below (filled in with the current spec's path), and **wait until it has
finished before you start the next one**. Never have two implementer sub-agents running at once.
Waiting for each one is what keeps the work in order and gives each sub-agent as much time as it
needs.

### 4. Check, repair, and continue

When a sub-agent returns, confirm it actually finished the job before you start the next spec:

- The spec file has moved out of `docs/specs/todo/` into `docs/specs/done/` as `_COMPLETED.md`, with
  a **Verification status** section.
- There is a new commit on `main` and `git status` is clean. If there's an upstream, it has been
  pushed (`git status -sb` shows the branch even with its upstream).
- The test run for the spec's platform is green: `xcodebuild -scheme Snapper test` for macOS specs,
  `dotnet build windows/Snapper.slnx -c Debug && dotnet test windows/Snapper.slnx` for Windows specs.
  Re-run it yourself; a cheap check is worth it.

If anything is missing or red, fix it yourself before starting the next spec. Don't ask the user:

1. Re-run the failed or incomplete command cleanly.
2. Diagnose and fix every failure, including failures that existed before this spec.
3. Re-run the affected tests, then the full build and test run.
4. Commit the fixes on `main` (and push them if there's an upstream).
5. Continue only when the spec has been moved, the tree is clean, and the build and tests are
   green.

For build-environment problems (stale DerivedData, a hung `xcodebuild`, a project that's out of
date with `project.yml`, a locked simulator or build database): kill leftover `xcodebuild`
processes, re-run `xcodegen generate`, and if necessary delete the project's DerivedData folder,
then run again.

**Stop and ask the user** only when the work can't continue without them:
- a product decision that `docs/PRD.md` and the spec don't settle (`AGENTS.md`: ask, don't guess);
- a new dependency, a signing/entitlement/certificate change, or another change `AGENTS.md` says to
  ask about;
- missing tools, credentials, or permissions.

When you halt, leave the blocked spec in `todo/` and stop the batch, because later specs probably
depend on it. Once the user answers, update the PRD and the spec, then resume the same spec (send
the answer to the same sub-agent if it's still there, or start a fresh one with the brief plus the
answer).

### 5. Report progress as you go

After each spec closes, post a one-line status, for example
`✅ 0004-rectangle-screenshot: built, 12 tests green, committed a1b2c3d (pushed), 5 manual checks pending`.
At the end, summarize: which specs shipped, any that were skipped or that stopped the batch, the
final state of the queue, and **one combined manual verification checklist** covering every spec in
the batch, grouped by spec, so the user can work through it in one GUI session. Remind them that
rebuilding with a different signature can reset the Screen Recording permission. Include the
release URL and version from step 6 (or why it was skipped).

### 6. Publish a release

Once the queue is finished, publish **one** GitHub release
(https://github.com/devnixs/ScreenshotApp/releases) that contains both the macOS and the Windows
binaries. Running this skill counts as the user asking for this release, its version-bump commit,
its tag, and the push.

**Only release when the batch ended cleanly:** at least one spec shipped in this session, no spec
stopped the batch, `main` is clean and pushed, and the step 4 checks are green. If the batch halted
on a blocker, or nothing was implemented, skip the release and say why.

1. **Pick the bump.** `minor` when any spec in the batch adds or changes user-facing behavior;
   `patch` when the batch only fixes bugs, refactors, or changes docs/tooling. Use `major` only
   when the user asks for it. The script reads the current version from
   `windows/Directory.Build.props` and the highest `vX.Y.Z` tag, so it never goes backwards.
2. **Write the release notes** to a file in your scratchpad (not in the repo), in plain user-facing
   English, following PRD §13 wording rules for Windows text:
   - one line on what Snapper is;
   - `## What's new`: one bullet per shipped spec, describing the change for users (not spec IDs
     or file names), with Windows-only or macOS-only items labeled;
   - `## Download`: `Snapper-macOS.dmg` (open it and drag Snapper to Applications),
     `Snapper-macOS.zip` (same app), both universal for Apple silicon and Intel, macOS 15 or
     later; `Snapper-win-x64.exe` (most Intel and AMD PCs) and `Snapper-win-arm64.exe` (Windows on
     Arm), Windows 11, self-contained single files with no .NET install or installer needed;
   - `## First launch`: the macOS build is ad hoc signed and not notarized (System Settings ›
     Privacy & Security › Open Anyway, or `xattr -dr com.apple.quarantine /Applications/Snapper.app`),
     and a new ad hoc build may need Screen Recording permission granted again; the Windows
     executables are unsigned, so SmartScreen may warn (More info › Run anyway). Exit any older
     running Snapper before launching the new one;
   - a note that GUI acceptance checks (capture, recording, annotation) for this batch have not
     been run yet, unless the user has recorded them.

   The script appends a `## Validation` section (test counts) and the source commit.
3. **Run the release script** from the repo root:

   ```sh
   .agents/skills/spec-implementer/scripts/release.sh --bump <patch|minor|major> --notes <notes-file>
   ```

   It refuses to run unless `main` is clean and not behind `origin/main`. It then: bumps
   `<Version>` in `windows/Directory.Build.props` and `MARKETING_VERSION` (and
   `CURRENT_PROJECT_VERSION` + 1) in `project.yml` and commits `Release vX.Y.Z`; runs the macOS
   and Windows tests; builds a **universal (arm64 + x86_64), ad hoc signed** macOS Release app and
   packages `Snapper-macOS.zip` and `Snapper-macOS.dmg`; publishes **self-contained, single-file**
   `Snapper-win-x64.exe` and `Snapper-win-arm64.exe` through the existing publish profiles; checks
   that the app links only system libraries or libraries embedded in the bundle, carries the new
   version, and that each Windows publish is one exe that bundles the .NET runtime; writes
   `SHA256SUMS.txt`; pushes `main`, tags `vX.Y.Z`, and creates the release marked as latest. Logs
   and assets are in `build/release/` (git-ignored). Expect 10–20 minutes. `--dry-run` does the
   build and checks without editing, committing, tagging, or publishing anything.
4. **If it fails**, fix the cause the same way as step 4 (commit fixes on `main` and push), then
   run the same command again. If the failed run already made the `Release vX.Y.Z` commit, the
   script notices the untagged release and finishes it without bumping again. Never delete or move
   an existing tag or release without asking. A signing, notarization, or certificate change is a
   blocker to raise with the user, not something to fix here.
5. **Confirm** with `gh release view vX.Y.Z --repo devnixs/ScreenshotApp` that all five assets are
   attached, and put the release URL in the final summary.

---

## The sub-agent brief

Give each sub-agent this brief, with `<SPEC_PATH>` replaced by the current spec. Keep all the
guardrails: sub-agents start with none of your context, so everything they need has to be in the
prompt.

```
You are implementing ONE spec for Snapper, a screenshot/recording/annotation app with a macOS
version (Swift 6, AppKit + SwiftUI, ScreenCaptureKit, at the repo root) and a Windows version
(C# .NET 10, WPF, Windows.Graphics.Capture, in windows/), end to end, on the `main` branch. Work in
the repository root.

IF THE SPEC IS A WINDOWS SPEC (it targets windows/): the "Windows app" section of AGENTS.md and
docs/PRD.md §13 replace the macOS-specific rules below. Use C#, put pure logic in
windows/src/Snapper.Core with xUnit tests in windows/tests/Snapper.Core.Tests, use the
Windows commands instead of xcodegen/xcodebuild in steps 2 and 4:
     dotnet build windows/Snapper.slnx -c Debug
     dotnet test windows/Snapper.slnx
(filter while iterating: --filter FullyQualifiedName~<TestClass>), and write manual verification
steps for a Windows 11 machine. The Swift sources and done specs are the reference implementation:
port their behavior and test cases. Never touch the macOS app in a Windows spec.

Spec to implement: <SPEC_PATH>

Read these first. They are binding:
- The spec itself, in full (Context, Scope & changes, Acceptance criteria, Edge cases, Test plan,
  Out of scope).
- AGENTS.md: stack, project layout, commands, architecture rules, conventions. Follow it exactly.
- docs/PRD.md: the SOURCE OF TRUTH for behavior. When the code, the spec, or your own assumptions
  disagree with the PRD, the PRD wins. If the PRD and the spec are both silent or ambiguous on a
  product decision you need, DO NOT GUESS: stop and report it as a blocker (see the end of this
  brief).

Key project rules (from AGENTS.md, repeated because they're easy to get wrong):
- project.yml is the source of truth for the Xcode project. Never edit project.pbxproj. Run
  `xcodegen generate` after adding or removing files or targets.
- Keep logic pure and testable, separate from AppKit: the annotation model, geometry helpers
  (AppKit bottom-left ↔ CG/SCK top-left, points ↔ pixels, display selection, pill placement), file
  naming, settings. Views only render the model and forward input to it.
- Annotation shapes are stored in image pixel coordinates. Export renders at full image
  resolution.
- All UI on @MainActor. ScreenCaptureKit callbacks hop back to the main actor before touching UI.
  Swift 6 strict concurrency: fix warnings properly, don't silence them with @unchecked Sendable or
  nonisolated(unsafe) unless there's a justified comment.
- Our own windows are excluded from capture via SCContentFilter. A selection is confined to one
  display.
- ScreenCaptureKit only (SCScreenshotManager, SCStream + SCRecordingOutput). No
  CGWindowListCreateImage or CGDisplayStream.
- The only dependency is KeyboardShortcuts. Don't add others, and don't change signing or
  entitlements beyond what the spec says.
- Preferences go behind the one typed settings type over UserDefaults.
- No force unwraps outside tests except truly invariant cases, each with a comment.
- Reuse and extend existing code before adding new code. Avoid duplication and hardcoded values.

Do the work in this order:

1. IMPLEMENT the spec's Scope & changes so every Acceptance criterion is met. Handle the listed
   Edge cases.

2. WRITE UNIT TESTS in SnapperTests/ for every piece of pure logic the spec adds or changes, as the
   spec's Test plan describes. Write them to match the PRD, not the current code. Coordinate
   conversions get explicit multi-display, non-zero-origin, and 1×/2× scale cases. If the spec is
   purely platform glue with nothing to extract, say so and explain why.

3. MAKE THE NEW TESTS PASS.

4. RUN THE FULL BUILD AND TESTS from the repo root, and fix every failure, whether this spec caused
   it or it was already there:
     xcodegen generate
     xcodebuild -scheme Snapper -configuration Debug build
     xcodebuild -scheme Snapper test
   (Filter while iterating: -only-testing:SnapperTests/<TestClass>. Use -quiet or tail to keep
   output readable, but read the real errors.) A green build and test run is required before you
   continue. Don't call it green unless it actually passed. If a run hangs or collides, kill
   leftover xcodebuild processes, re-run xcodegen, clear this project's DerivedData if needed,
   and run again.

5. YOU CAN'T VERIFY GUI BEHAVIOR. Permissions, hotkeys, overlays, capture, recording, the menu bar,
   and windows need a real GUI session with TCC permission. Don't launch the app expecting to test
   them, and never claim they're verified. Instead, finalize the spec's Manual verification steps
   so they match what you actually built.

6. UPDATE DOCS in the same change. If you settled a behavior detail that the PRD should record (only
   within what the spec already decided), update docs/PRD.md. If the project layout, commands, or
   architecture rules changed, update AGENTS.md. Edit the existing sections; don't add new docs.

7. MOVE THE SPEC to done, keeping its history and following the naming convention:
     git mv docs/specs/todo/<name>_TODO.md docs/specs/done/<name>_COMPLETED.md
   Then append a "## Verification status" section to it: the build and test result (date, test
   count, green), and each manual verification step as "- [ ] not yet run by user".

8. COMMIT on main. Don't create a branch or open a PR. Make one focused commit (or a few), with an
   imperative message that starts with the spec ID, for example
   "0004: capture a rectangle screenshot to file and clipboard". Include the spec move. Follow your
   harness's commit attribution instructions (co-author trailer). If `git rev-parse
   --abbrev-ref @{u}` shows an upstream, push. If not, leave the commit local and say so.

Report back: what you implemented, the files you created or changed, the unit tests you added, the
final build and test results (say plainly whether they're green, with the summary output), any
PRD/AGENTS.md edits, the commit hash and whether it was pushed, confirmation that the spec now lives
in docs/specs/done/, and the manual verification checklist the user still needs to run.

Don't stop for ordinary build or test failures: diagnose, fix, and re-run until green. Report a
BLOCKER and stop (without committing half-finished work, and without moving the spec) only for
things you mustn't decide alone: a product decision the PRD and the spec don't settle, a new
dependency, a signing/entitlement change, or missing tools or permissions. Explain exactly what
needs deciding, give your recommended answer, and say what state you left the working tree in.
```

---

## Notes and edge cases

- **Nothing to do.** If `docs/specs/todo/` has no `*_TODO.md` files, say so and stop.
- **A spec turns out to be already done.** If a sub-agent finds the acceptance criteria already met
  (code and unit tests exist and pass), it still runs the full build and tests, then moves the spec
  to `docs/specs/done/` with its Verification status and commits that move. Filing it correctly is
  part of the job.
- **The first spec usually creates the project.** Before `project.yml` exists, the `xcodegen`/
  `xcodebuild` commands can't run. The scaffolding spec creates them, and its own build and test
  run is the first real one. If the first spec in the queue *isn't* the scaffolding and there's no
  `project.yml`, stop and tell the user. The backlog is missing its foundation spec (the
  spec-writer skill can add one).
- **Permission resets.** Rebuilding with a different signature can reset Screen Recording
  permission. If the user reports blank captures after a batch, point to that first (`AGENTS.md`).
- **One release per session.** Don't publish a release per spec; the batch gets one release at
  the end (step 6). If the user only wants to build specs without releasing, skip step 6.
- **Don't squash the batch into one commit.** Each spec gets its own sub-agent and its own
  commit(s), so each spec maps to one change in the history, which stays reviewable and bisectable.
- **Recover on your own.** A failed, incomplete, or colliding build is not a reason to pause the
  batch. Clean up, re-run, fix every failure (including ones that were already there), commit the
  repair, and continue in order. Ask the user only about the blockers listed in step 4.
