# 0051 — Give Snapper.app a Liquid Glass viewfinder app icon

> Source: docs/PRD.md §3 (*App icon*, added in this change), §12 (new app icon criterion); user request
> ("create a better icon for the macOS app")
> Depends on: 0001-xcodegen-app-shell (`project.yml`, test host). No other spec.

## Context
The macOS app has **no app icon**. `project.yml` has no asset catalog, no `.icon` document, and no
`ASSETCATALOG_COMPILER_APPICON_NAME` (`project.yml:12-41`), and `Snapper/` contains no
`*.xcassets`/`*.icon`. So every place macOS shows Snapper's app icon displays the generic blank
icon:
- Finder / `/Applications`, Get Info, Spotlight;
- System Settings › Privacy & Security › Screen Recording and Microphone, and General › Login Items
  (Snapper registers via `SMAppService`, PRD §3);
- Snapper's own `NSAlert`s, which show the app icon by default:
  `Snapper/App/AppCoordinator.swift:185`, `Snapper/Annotate/AnnotationWindowController.swift:215`,
  `Snapper/Support/ErrorAlerts.swift:11` and `:21`.

The menu bar glyph (SF Symbol `camera.viewfinder`, red `record.circle` while recording, PRD §4) is
**not** part of this spec and doesn't change.

**User decisions (2026-11-04), recorded in PRD §3:**
1. Format: an **Icon Composer `.icon` document** (layered, Liquid Glass on macOS 26), not a
   classic `AppIcon.appiconset`.
2. Design: a **white viewfinder** (four rounded corner brackets around a lens ring) on a **blue
   `#0F6CBD`** background. It matches the menu bar glyph and the Windows icon's colour
   (`windows/src/Snapper.Core/App/AppIconDesign.cs`, `TileArgb = 0xFF0F6CBD`).
3. **Mac only.** The Windows icon (`windows/…/Assets/Snapper.ico`, spec 0047) stays as it is.

### Facts verified by the spec author (macOS 26.6.2, Xcode 26.3, XcodeGen 2.46.0)
A throwaway XcodeGen app with exactly the files below established that:
- **XcodeGen 2.46 already understands `.icon`.** A `Foo.icon` folder inside a `sources` path becomes
  a single `PBXFileReference` with `lastKnownFileType = wrapper.icon`, in the Resources build
  phase. No `fileTypes` option and no hand-edits to `project.pbxproj` are needed.
- With `ASSETCATALOG_COMPILER_APPICON_NAME: AppIcon` and `GENERATE_INFOPLIST_FILE: YES`, the built
  app contains `Contents/Resources/AppIcon.icns` (Xcode's flattened fallback for macOS < 26) and
  `Assets.car`. Its `Info.plist` has `CFBundleIconFile = AppIcon` and `CFBundleIconName = AppIcon`.
  The deployment target was macOS 15.0 and the build reported no warnings about the icon.
- **`/usr/bin/ictool` is not Icon Composer's tool** (it's an actool shim and rejects
  `--export-image`). The real previewer is
  `"/Applications/Xcode.app/Contents/Applications/Icon Composer.app/Contents/Executables/ictool"`
  (bundle version 76), used like this:
  `ictool AppIcon.icon --export-image --output-file out.png --platform macOS --rendition Default --width 256 --height 256 --scale 2`.
  The renditions it accepts include `Default`, `Dark`, `ClearLight`, and `TintedDark`.
- **The lens must be a filled even-odd ring, not a stroked `<circle>`.** With a stroked circle, the
  `Dark` rendition fills the inside and shows a solid disc. The filled ring below renders as a ring
  in `Default`, `Dark`, and `TintedDark`.
- Splitting the lens and the brackets into **two groups** gives them separate glass depth and
  shadows. Both groups render correctly.

## User story
As a Snapper user, I see a recognisable blue viewfinder icon for Snapper in Finder, in System
Settings' privacy and Login Items lists, and in Snapper's alerts, instead of a blank generic icon.
On macOS 26 it follows my icon style (default, dark, clear, tinted).

## Scope & changes

### Asset (platform resource, checked in)
Create the Icon Composer document `Snapper/AppIcon.icon/` (a folder bundle) with exactly these
three files. The contents were rendered and verified as described above. Don't redesign them. Small
geometry tweaks are only allowed if the step-1 preview renders below show a defect, and then the
change and the reason go in the commit message.

`Snapper/AppIcon.icon/icon.json`:
```json
{
  "fill" : { "automatic-gradient" : "srgb:0.05882,0.42353,0.74118,1.00000" },
  "groups" : [
    {
      "layers" : [ { "image-name" : "lens.svg", "name" : "lens" } ],
      "shadow" : { "kind" : "neutral", "opacity" : 0.5 },
      "translucency" : { "enabled" : true, "value" : 0.5 }
    },
    {
      "layers" : [ { "image-name" : "brackets.svg", "name" : "brackets" } ],
      "shadow" : { "kind" : "neutral", "opacity" : 0.5 },
      "translucency" : { "enabled" : true, "value" : 0.5 }
    }
  ],
  "supported-platforms" : { "squares" : [ "macOS" ] }
}
```
(`0.05882, 0.42353, 0.74118` is `#0F6CBD`. `automatic-gradient` lets the system add its own subtle
top-to-bottom gradient. The first group in `groups` is the front-most.)

`Snapper/AppIcon.icon/Assets/brackets.svg` (1024 canvas, 64-pt rounded strokes, 160-pt bracket
arms, glyph box 232…792):
```svg
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024"><g fill="none" stroke="#FFFFFF" stroke-width="64" stroke-linecap="round" stroke-linejoin="round"><path d="M232 392V296a64 64 0 0 1 64-64h96"/><path d="M632 232h96a64 64 0 0 1 64 64v96"/><path d="M792 632v96a64 64 0 0 1-64 64h-96"/><path d="M392 792h-96a64 64 0 0 1-64-64v-96"/></g></svg>
```

`Snapper/AppIcon.icon/Assets/lens.svg` (ring centred at 512,512, outer radius 182, inner radius 118,
filled even-odd):
```svg
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024"><path fill="#FFFFFF" fill-rule="evenodd" d="M512 330a182 182 0 1 0 0 364a182 182 0 1 0 0-364ZM512 394a118 118 0 1 1 0 236a118 118 0 1 1 0-236Z"/></svg>
```

The SVGs are white so the system can apply its dark, clear, and tinted treatments. Don't add colour,
gradients, or shadows to them. Icon Composer owns those through `icon.json`.

### Project (platform config)
- `project.yml`: add `ASSETCATALOG_COMPILER_APPICON_NAME: AppIcon` to `targets.Snapper.settings.base`,
  next to the other `INFOPLIST_KEY_*` entries. `sources: [Snapper]` already picks up
  `Snapper/AppIcon.icon` as a resource, so don't add a separate `sources` entry. Don't touch the
  `SnapperTests` target settings.
- Run `xcodegen generate`. Confirm that `AppIcon.icon` appears once as `wrapper.icon` in the
  Snapper target's Resources phase, and that it is **not** expanded into a group with
  `icon.json`/SVG children.

### Unit tests (pure checks + host-bundle checks)
There's no product logic to extract: an icon is a static resource. Two kinds of regression guard
are worth having, so this spec adds `SnapperTests/AppIconTests.swift` (Swift Testing, like
`SnapperTests/SmokeTests.swift`; the tests are hosted by Snapper.app, so `Bundle.main` is the app):
- **Source document is well-formed** (pure file/JSON checks). Locate the document from the test
  file with `URL(filePath: #filePath)` → `../Snapper/AppIcon.icon`.
- **Built app wires the icon** (`Bundle.main` checks). This catches a lost
  `ASSETCATALOG_COMPILER_APPICON_NAME` or a `.icon` that stopped being compiled.

See the Test plan for the cases.

### Docs
- `docs/PRD.md` §3: an **App icon** bullet was added *in this spec-writing change* (format, design,
  colour, where it shows, macOS 15 fallback, Windows separate). §12: a new criterion was added after
  "The app runs as a menu bar app only…". Check that they match what ships. If a verified defect
  forced a tweak, update §3's wording too.
- `AGENTS.md` *Project layout*: add a line under `Snapper/`:
  `AppIcon.icon/         # Icon Composer app icon (icon.json + white SVG layers)`.

## Acceptance criteria
- [ ] `Snapper/AppIcon.icon/{icon.json, Assets/brackets.svg, Assets/lens.svg}` exist with the
      contents above (or with documented, preview-justified tweaks).
- [ ] `project.yml` sets `ASSETCATALOG_COMPILER_APPICON_NAME: AppIcon`. After `xcodegen generate`,
      the generated project lists `AppIcon.icon` as one `wrapper.icon` resource.
- [ ] The built `Snapper.app` contains `Contents/Resources/AppIcon.icns` and `Assets.car`, and its
      `Info.plist` has `CFBundleIconName = AppIcon` and `CFBundleIconFile = AppIcon`.
- [ ] The Icon Composer `ictool` previews for `Default`, `Dark`, `ClearLight`, and `TintedDark` show
      four corner brackets around a **ring** (never a filled disc), centred, with nothing clipped.
- [ ] `xcodebuild -scheme Snapper -configuration Debug build` and `xcodebuild -scheme Snapper test`
      succeed, and `AppIconTests` passes.
- [ ] PRD §3/§12 and AGENTS.md describe the shipped icon.
- [ ] Nothing under `windows/` changes. The menu bar glyph code (`StatusItemController`) doesn't
      change.

## Edge cases
- **macOS 15 (deployment target):** Liquid Glass doesn't exist there. Xcode compiles the flattened
  `AppIcon.icns` fallback (verified), so `CFBundleIconFile` must stay present. Don't add an
  `@available` code path or a second, classic `.appiconset`. Two icon sources with the same name
  would conflict.
- **Launch Services icon cache:** after rebuilding in place, Finder may keep showing the old blank
  icon. That's a cache artefact, not a bug. Manual step 1 tells the user how to refresh it.
- **Signing / TCC:** adding a resource changes the bundle's code signature. If Screen Recording
  suddenly stops working after this build, re-grant permission (AGENTS.md) before debugging
  anything.
- **Small sizes (16/32 px in list views):** the 64-pt stroke on a 1024 canvas stays at least 1 px
  at 16 px. Check the 16 px look in manual step 3 instead of thinning the strokes.
- **Name clash:** the asset must be named `AppIcon` (document `AppIcon.icon`) to match
  `ASSETCATALOG_COMPILER_APPICON_NAME`. Snapper has no other asset catalog, so there's nothing to
  clash with today.

## Test plan
- **Unit tests** (`SnapperTests/AppIconTests.swift`, new; Swift Testing):
  1. `iconDocumentDeclaresMacOS`: decode `icon.json` with `JSONSerialization`. Expect
     `supported-platforms.squares` to contain `"macOS"`.
  2. `everyLayerImageExists`: collect every `groups[].layers[].image-name`. Expect the set to be
     exactly `["brackets.svg", "lens.svg"]`, and expect each file to exist under
     `AppIcon.icon/Assets/`.
  3. `assetsFolderHasNoOrphans`: expect the `.svg` files in `AppIcon.icon/Assets/` to be exactly the
     referenced `image-name`s. This catches leftovers after a redesign.
  4. `fillIsSnapperBlue`: expect `fill.automatic-gradient` to equal
     `"srgb:0.05882,0.42353,0.74118,1.00000"`.
  5. `layersAreWhite`: read each SVG as UTF-8. Expect it to contain `#FFFFFF` and to contain no other
     `#RRGGBB` colour (regex `#[0-9A-Fa-f]{6}`).
  6. `lensIsFilledRingNotStrokedCircle`: expect `lens.svg` to contain `fill-rule="evenodd"` and not to
     contain `<circle`. This guards the verified `Dark` rendition bug.
  7. `hostBundleNamesAppIcon`: expect `Bundle.main.object(forInfoDictionaryKey: "CFBundleIconName")
     as? String == "AppIcon"`.
  8. `hostBundleHasFallbackIcns`: expect `Bundle.main.url(forResource: "AppIcon", withExtension:
     "icns")` to be non-nil.
- **Preview check (implementer runs it; not a unit test):** export the four renditions to a
  scratch directory with Icon Composer's `ictool` (full path in *Context*) at 256×256 @2x. Look at
  each one, and confirm the ring isn't filled and nothing is clipped.
- **Build check:** `xcodegen generate`, then `xcodebuild -scheme Snapper -configuration Debug build`
  and `xcodebuild -scheme Snapper test`, all green. Also run
  `plutil -p <built app>/Contents/Info.plist | grep -i icon` and `ls <built app>/Contents/Resources`
  to confirm the acceptance criterion.
- **Manual verification** (GUI; can't be automated):
  1. Build and copy (or run) Snapper.app. In Finder, select it and press ⌘I. Get Info shows the blue
     viewfinder icon. If Finder shows the old blank icon, run
     `/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f <path>/Snapper.app`
     and `killall Finder`, then check again.
  2. On macOS 26: System Settings › Appearance › Icon & widget style. Switch between Default, Dark,
     Clear, and Tinted, and watch Snapper.app in a Finder icon-view window. Each style shows the
     viewfinder with a hollow lens ring, and the Dark style keeps the ring hollow.
  3. In a Finder list view (16 px icons), Snapper's icon is still recognisable as a viewfinder.
  4. System Settings › Privacy & Security › Screen Recording: Snapper's row shows the new icon.
     Do the same check in General › Login Items if launch at login is on.
  5. Trigger a Snapper alert (for example, set the save folder to a folder you then delete, and take
     a screenshot; or close an annotation window with unsaved changes). The alert shows the new icon.
  6. If a macOS 15 machine is available: install the build there. Finder shows the flat blue
     viewfinder fallback, not the generic icon.

## Out of scope
- The menu bar status item glyph (`camera.viewfinder` / red `record.circle`, PRD §4).
- The Windows app icon and `AppIconDesign` (spec 0047). The user chose Mac only. Aligning Windows
  with the new design would be a separate spec.
- Document icons, DMG background/volume icon, or notarisation and distribution packaging.
- Using the icon inside the onboarding or Settings UI. Neither shows an app icon today, and the PRD
  doesn't ask for one.
