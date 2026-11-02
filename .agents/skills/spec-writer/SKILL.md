---
name: spec-writer
description: >
  Author a precise, implementation-ready technical spec for a Snapper feature, change, or bug
  fix, and later verify and file it as done. Use this whenever the user hands you a feature
  request, a PRD section, a bug, or "we should add/support X" and wants it turned into a spec
  under docs/specs/todo/ — WITHOUT implementing it yet. Also use it to break the whole PRD (or a
  large part of it) into an ordered backlog of specs. And use it when a feature that already has
  a spec in docs/specs/todo/ is finished and needs the build and tests run, failures fixed, and
  the spec moved to docs/specs/done/. Trigger even when the user doesn't say the word "spec":
  "write this up", "plan out this feature", "turn this into a slice", "break the PRD into
  tasks", "we need to support …", or "mark this feature done / it's implemented" all belong here.
---

# Spec Writer

This skill turns a feature request into an implementation-ready spec for **Snapper**, the macOS
menu bar screenshot, recording, and annotation app, and closes the loop once the feature is built.
It has two modes. Pick the one that matches what the user is asking.

- **Mode A: author a spec** (the default when given requirements). Research the PRD and the
  codebase, write a precise spec into `docs/specs/todo/`, and stop. **Do not implement the
  feature.**
- **Mode B: close out a spec** (when a feature is reported done). Build, run the tests, fix what's
  broken, then move the spec from `docs/specs/todo/` to `docs/specs/done/`.

If it's unclear which mode applies, ask one short question instead of guessing.

The specs this skill writes are consumed by the **spec-implementer** skill, which builds them one
at a time, in filename order, each in a fresh sub-agent that has none of your context. Write every
spec so that a cold reader can implement it from the spec, `AGENTS.md`, and `docs/PRD.md` alone.

## Why this exists

`AGENTS.md` makes `docs/PRD.md` the source of truth for behavior. When the PRD is silent or
conflicts with the code, it says to ask the user rather than guess. A good spec does that asking
up front. It pins the exact PRD behavior, names the exact files and types that change, separates
the pure, unit-testable logic from the AppKit/ScreenCaptureKit glue, and lists both the unit tests
that must exist and the manual checks that can't be automated. The implementer then has nothing
left to re-derive or guess. Vague specs lead to vague implementations, untested geometry, and
"verified" claims that nobody checked. Those are the failures this skill prevents.

---

## Mode A: author a spec

### Batches of 4 or more specs: one sub-agent per spec

When a single request produces **four or more** specs (for example "break the PRD into a
backlog"), don't write them all yourself one after another. Delegate each spec to its own
sub-agent and run them in parallel. With three or fewer, write them inline using the steps below.

Sub-agents running at the same time would each pick "the next free number" and collide. Their
order also matters, because the implementer builds specs in filename order. So you plan the batch
before spawning anything:

1. Split the request into specs. Each one is a vertical slice that builds and passes its tests on
   its own (see "Slicing" below). Give each a one-line title.
2. **Order them by dependency.** Foundations come first (XcodeGen project and app shell, settings
   type, file naming, permissions), then features that build on them. A spec must never depend on
   a spec with a higher number.
3. List `docs/specs/todo/` and `docs/specs/done/`, find the next free ID (step 4), and assign one
   unique ID per spec, in dependency order. Fix the full filename for each one
   (`<ID>-<slug>_TODO.md`).
4. Resolve open product questions **before** spawning (step 1 below). Sub-agents can't ask the user
   anything, so include the answers in their prompts.
5. Spawn one sub-agent per spec, all in the same turn so they run in parallel. Each sub-agent's
   prompt must contain:
   - the requirement for its one spec, the PRD sections it covers, and whatever the user said that
     applies to it, including the answers to open questions;
   - **its assigned spec ID and exact filename**, with the instruction to use them as given. It
     must not choose its own number, renumber, or create any other spec file;
   - the IDs and titles of every sibling spec in the batch, and which ones it depends on, so that
     it can fill in **Depends on** and point to siblings under **Out of scope** instead of
     overlapping with them;
   - the instruction to follow Mode A steps 2–7 of this skill for that single spec. It must not
     edit `docs/PRD.md` (you do that once, in step 6, to avoid conflicting edits). If it finds a PRD
     gap you didn't resolve, it records the gap under **Open questions** in its spec and reports it
     back;
   - the instruction to report back the path, a short summary, the tests and manual checks it
     prescribed, and any open questions.
6. When all sub-agents have finished, check that every assigned file exists under its assigned
   name, that no two specs claim the same change, and that the dependency order holds. Resolve any
   reported open questions with the user, then update the affected specs and the PRD. Give the user
   one combined report (step 7).

### Slicing

Every spec has to leave the app in a state that builds, passes its tests, and runs. The
implementer commits after each spec. Prefer thin vertical slices over horizontal layers. For
example, "rectangle screenshot to file" is better than "all the overlay code" followed by "all the
capture code". Pure logic that a later slice needs (the shape model, geometry helpers, file
naming) can be its own spec if it is large enough to be worth reviewing alone, and it comes before
the slices that use it. Aim for specs that a sub-agent can implement and verify in under an hour.

### 1. Read the source of truth first, and ask instead of guessing

`docs/PRD.md` wins over every other document, including older specs, the current code, and your
own memory. Read the PRD sections the feature touches and cite them as `docs/PRD.md §N` (with line
numbers when it helps).

If the PRD is **silent**, **ambiguous**, or **conflicts** with the request or the code (for
example: what the pill shows while paused, how a 1-pixel drag behaves, what happens when the save
folder is unwritable), **ask the user**. Don't invent the behavior. Batch the questions into one
round, number them, and give your recommended answer for each so the user can just say "yes". If
there are many decisions, suggest the `grilling` skill.

Every answer that changes or adds product behavior goes into `docs/PRD.md` **in the same change**,
as `AGENTS.md` requires. Update the relevant section (and §12 acceptance criteria if it applies),
bump "Last updated", and reference the updated section from the spec. Never leave a decision only
in the spec, because the PRD has to stay the single source of truth.

Also read `AGENTS.md` in full. Its stack, layout, architecture rules, and conventions are binding,
and the spec must not prescribe anything that breaks them. For example, it must not prescribe a new
dependency, deprecated capture APIs, hand-edits to `project.pbxproj`, or logic buried in a view. If
a feature truly needs a new dependency, it is an open question for the user.

### 2. Research the codebase so the spec is concrete

The whole value of a spec is its precision. Before writing, find the real code the feature will
touch and record file paths and line numbers. If the code doesn't exist yet (early specs), name
the files and types the spec will create, following the layout in `AGENTS.md`:

- `project.yml`: targets, sources, Info.plist keys (`LSUIElement`, usage descriptions),
  entitlements, the SwiftPM package. Any added or removed file or target means
  `xcodegen generate`.
- `Snapper/App/`, `Capture/`, `Recording/`, `Annotate/` (with `Annotate/Model/` for the pure shape
  model), `Settings/`, `Support/`.
- `SnapperTests/` for unit tests.

For every change, say which side of the **pure / platform boundary** it sits on:

- **Pure Swift, unit-tested:** the annotation model (shapes, hit-testing, move/resize, undo/redo),
  geometry (AppKit bottom-left ↔ CoreGraphics/ScreenCaptureKit top-left, points ↔ pixels, display
  selection, pill placement), file naming (timestamps, collision suffixes), settings defaults and
  encoding, and any other decision logic you can extract.
- **Platform glue, manually verified:** `NSStatusItem`, overlays/panels, `NSView` drawing, global
  hotkeys, `SCScreenshotManager` and `SCStream`, TCC permissions, `SMAppService`, Finder reveal, the
  clipboard, sounds.

Push as much logic as you can to the pure side, and say so in the spec. That is what makes the
behavior testable. Also call out the cross-cutting rules the change must respect: everything UI on
`@MainActor`; ScreenCaptureKit callbacks hop back to the main actor; our own windows excluded via
`SCContentFilter`; selections confined to one display; annotation shapes in image pixel
coordinates; preferences behind the one typed settings type.

Read a couple of neighbouring specs in `docs/specs/todo/` and `docs/specs/done/` (if there are any)
to match their tone and level of detail. Prefer grep and search over guessing. Cite files as
`path:line` so the references are clickable and can be checked.

### 3. Note the dependencies

List which specs must already be done for this one to build (`Depends on:`). The implementer works
in filename order, so a dependency must always have a lower number. If the spec you're writing
needs something that isn't specced yet, either spec that first (with a lower number) or put it in
this spec's scope.

### 4. Pick a filename

If you are a sub-agent that was given a spec ID and filename (see the batch section), use them
exactly as given and skip the rest of this step.

Specs live in `docs/specs/todo/` and are named `NNNN-<kebab-slug>_TODO.md`: a zero-padded,
four-digit, globally increasing ID followed by a short slug. Examples:
`0001-xcodegen-app-shell_TODO.md`, `0002-file-naming_TODO.md`. List both `todo/` and `done/` and use
the highest existing number + 1 (start at `0001` if both are empty). Never reuse or renumber an ID,
because commit messages and the implementer refer to specs by it. Create `docs/specs/todo/` and
`docs/specs/done/` if they don't exist yet.

### 5. Write the spec using the template

```markdown
# NNNN — <Imperative one-line summary of the change>

> Source: <docs/PRD.md §N / user request / bug report>
> Depends on: <NNNN-slug, … or "none">

## Context
<What is true today and what is missing or wrong. Cite real code as `path:line` (or "not yet
implemented"). Quote or cite the PRD behavior this delivers.>

## User story
As a <Snapper user>, <what they can do or see once this is done>.

## Scope & changes
- <Concrete change: the file(s)/type(s) and the behavior. One bullet per coherent change.>
- <Keep pure logic (Model/geometry/naming/settings) and platform glue (AppKit/SCK/TCC) as
  separate bullets, and say which is which.>
- <project.yml changes (targets, Info.plist keys, entitlements) and "run `xcodegen generate`".>
- <PRD update made in this change, if any: "docs/PRD.md §N updated: …".>

## Acceptance criteria
- [ ] <Observable, checkable outcome, matching the PRD wording where possible.>
- [ ] <Another.>

## Edge cases
- <Boundaries the implementer must handle: multiple displays with different scales, Retina vs
  non-Retina, a negative-origin display left of or below the main one, drag starting on one display
  and ending on another, 0×0 or tiny selections, missing or revoked permission, missing or
  unwritable save folder, name collisions, a window closing mid-capture or mid-recording,
  conflicting shortcuts, etc.>

## Test plan
- **Unit tests** (`SnapperTests/<File>Tests.swift`): <each test case and its assertion, for every
  piece of pure logic this spec adds or changes. Coordinate conversions always get explicit cases
  for multi-display, non-zero-origin, and 1×/2× scale.>
- **Build check:** `xcodegen generate` (if files or targets changed) and `xcodebuild` build + test
  succeed.
- **Manual verification** (needs a real GUI session with permission granted; can't be automated):
  1. <Step-by-step check taken from the PRD acceptance criteria, with the expected result.>
  2. <…>

## Open questions
- <Only if something is still unresolved. Leave this section out when everything is decided. A
  spec with open questions isn't ready for the implementer.>

## Out of scope
- <Explicitly excluded work, pointing to sibling specs or PRD non-goals (§2) where they apply.>
```

### 6. Always specify both kinds of verification

Under `AGENTS.md`, work isn't done until it builds and its tests pass, and GUI paths are never
claimed as verified unless someone actually ran them. So the **Test plan** must always contain:

- **Unit tests** for every piece of pure logic the spec touches, naming the concrete test file and
  the assertion of each case. If a spec is purely platform glue with no logic to extract, say so
  explicitly and justify it. Don't just leave the unit tests out. (Even then there is usually
  something worth extracting: a placement rule, a state machine, a mapping from settings to
  behavior.)
- **Manual verification** steps for anything that involves permissions, hotkeys, overlays, capture,
  recording, the menu bar, or windows. Write them so the user can follow them without thinking:
  what to press, what to look for, and what the correct outcome is.

### 7. Stop

Write the file into `docs/specs/todo/` (plus any PRD update) and report back the path, a short
summary, the unit tests and manual checks you prescribed, and the dependency position. **Do not
implement the feature, write the tests, or touch application code.** That happens later, usually
through the spec-implementer skill.

---

## Mode B: close out a completed spec

Use this once the feature described by a `docs/specs/todo/…_TODO.md` spec has been built and the
user wants it checked and filed as done. (The spec-implementer skill does this automatically for
each spec it builds.)

### 1. Re-read the spec

Load the spec and check each acceptance criterion against the current code, and check that every
prescribed unit test exists. If a criterion or a required test is missing, say so and fix it (or
flag it) before moving the file. A spec isn't done until its own test plan is satisfied.

### 2. Build and run the tests

From the repo root (the commands are in `AGENTS.md`; keep them in sync with it):

```bash
xcodegen generate                                        # if files/targets changed
xcodebuild -scheme Snapper -configuration Debug build
xcodebuild -scheme Snapper test
```

To iterate on one failing area, filter: `xcodebuild -scheme Snapper test
-only-testing:SnapperTests/<TestClass>`. Use `xcodebuild -quiet` or pipe through `tail` to keep the
output readable, but read the actual errors before concluding anything.

### 3. Fix failures, with the PRD as the reference

When a test and the PRD disagree, the PRD is correct: fix the code and the test to match it. If the
PRD itself looks wrong or silent, ask the user, and update the PRD with their answer. Keep fixes
within this feature's scope. If you find an unrelated failure, report it instead of widening the
change. Re-run until the build and the tests are green.

### 4. Move the spec to done

Once everything is green, move the spec from `docs/specs/todo/` to `docs/specs/done/`, renaming
`_TODO.md` → `_COMPLETED.md`. Use `git mv` so the history is kept:

```bash
git mv docs/specs/todo/<name>_TODO.md docs/specs/done/<name>_COMPLETED.md
```

Before moving it, add a short **Verification status** section at the end of the spec that records
the build and test result and lists each manual verification step as `[ ] not yet run by user` (or
`[x]` only if the user confirms they ran it). If the change affected the layout, commands, or
architecture rules, update `AGENTS.md` in the same change.

### 5. Report

Summarize what you checked, any fixes you made, and the final build and test results. Say plainly
whether they are green, include the command output, and don't claim green if anything was skipped.
List the manual verification steps the user still needs to run, and give the new
`docs/specs/done/` path.
