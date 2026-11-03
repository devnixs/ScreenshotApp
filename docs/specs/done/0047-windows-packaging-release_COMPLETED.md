# 0047 — Publish Snapper.exe as a self-contained single file with an app icon, version info, docs, and a final acceptance pass

> Source: docs/PRD.md §13.1 (Distribution, Tray-only app), §13.11 (publish criterion and the whole Windows checklist), §12 (every criterion, with the §13.2 mapping), §2 (Windows non-goals: no MSIX/installer/signing/auto-update)
> Depends on: 0025–0046 (all Windows specs; directly on 0025-windows-solution-tray-shell for the projects, `Directory.Build.props`, and `TrayGlyphGeometry`)

## Context

When this spec starts, every Windows feature is implemented (0025–0046), and
`dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` are green.
What's missing is the *release*:

- PRD §13.1: "**Distribution:** unpackaged. `dotnet publish` produces a self-contained, single-file
  `Snapper.exe` for win-x64 and win-arm64 that runs without installing .NET. No installer and no
  signing in v1."
- PRD §13.11 (last item): "`dotnet publish` produces a single `Snapper.exe` per architecture that runs
  on a clean Windows 11 machine without .NET installed."
- PRD §13.11 also says every §12 criterion applies with the §13.2 mapping (the onboarding criterion
  is replaced by the shortcut-conflict criterion). Each Windows spec has its own manual checks. This
  spec adds the **single, end-to-end pass on the published exe** that gates v1.

State left by earlier specs (names from their specs; confirm against the code):

- `windows/src/Snapper.Windows/Snapper.Windows.csproj` (0025): `WinExe`,
  `net10.0-windows10.0.22621.0`, `UseWPF` + `UseWindowsForms`, `EnableWindowsTargeting`,
  `ApplicationManifest` `app.manifest`, `AssemblyName` `Snapper`. **No `RuntimeIdentifiers`, no
  publish settings, no `ApplicationIcon`.** The WPF application class is
  `Snapper.Windows.SnapperApplication` (`App.xaml`).
- `windows/Directory.Build.props` (0025): `Company` `Thera Engineering`, `Product` `Snapper`,
  `Copyright` `© 2026 Thera Engineering`. 0025 explicitly leaves version info and the icon to this
  spec.
- `windows/src/Snapper.Core/App/TrayGlyphGeometry.cs` (0025): `TrayGlyphDrawing Viewfinder(int size)`
  returns the viewfinder as pure geometry (four corner brackets as 8 `GlyphLine`s + one unfilled lens
  `GlyphCircle`, stroke `max(1, round(size/16))`, mirror-symmetric). The tray icon is drawn from it at
  run time (PRD §13.3: tray icons are drawn in code, no assets). The **app icon** reuses this same
  geometry so the exe, the taskbar, and Alt+Tab show the same glyph as the tray.
- The macOS reference has no app icon asset and uses `MARKETING_VERSION: "1.0"` (`project.yml:37`).
  There's no Swift code to port here. Packaging is Windows-only.

### Facts verified on the author's Mac (macOS, .NET SDK 10.0.102), with a throwaway WPF + WinForms project

- `dotnet publish -c Release -r win-x64` with `SelfContained`, `PublishSingleFile`,
  `IncludeNativeLibrariesForSelfExtract`, and `EnableCompressionInSingleFile` succeeds on macOS and
  produces **one `Snapper.exe`** (about 82 MB for x64 and 78 MB for arm64 for an empty app), plus a
  `Snapper.pdb` unless `DebugType` is `embedded`.
- **The SDK on macOS does write the apphost's Win32 resources.** No NETSDK1074 warning. The
  published exe's `.rsrc` contains the `ApplicationIcon` (PNG-entry ICO accepted), the version block
  (`CompanyName`, `FileDescription Snapper`, `FileVersion 1.0.0.0`, …), and the `app.manifest`
  (`PerMonitorV2`). So a release built on the Mac is complete, but the manual pass still confirms it
  on Windows.
- **A RID set only inside a `.pubxml` doesn't trigger a csproj `Condition="'$(RuntimeIdentifier)' != ''"`
  group.** The publish profile is imported after the project body is evaluated. A profile that only set
  `RuntimeIdentifier` produced a framework-dependent multi-file folder. So each profile must set
  every publish property itself (see below).
- `PublishTrimmed=true` fails with **NETSDK1175** ("Windows Forms is not supported or recommended
  with trimming enabled"). WPF isn't trim-compatible either (NETSDK1168). Trimming stays off.

## User story

As a Windows 11 user (x64 or Arm64), I download one `Snapper.exe`, double-click it on a machine
without .NET, and Snapper runs from the tray with its viewfinder icon. The exe has the Snapper icon
and version in Explorer, and every feature in PRD §12/§13.11 works.

## Scope & changes

### Publish configuration (build config)

- **`windows/src/Snapper.Windows/Snapper.Windows.csproj`** (edit):
  - Add `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>` so restore fetches both RIDs'
    assets. Debug `dotnet build` without a RID stays framework-dependent and unchanged.
  - Add `<ApplicationIcon>Assets\Snapper.ico</ApplicationIcon>` (the file is generated and checked in;
    see "App icon").
  - Add `<AssemblyTitle>Snapper</AssemblyTitle>` (Explorer's *File description*).
  - Add a RID-conditional group, so the plain AGENTS.md form `dotnet publish … -r win-x64` also
    gives a single file:
    ```xml
    <PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
      <SelfContained>true</SelfContained>
      <PublishSingleFile>true</PublishSingleFile>
      <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
      <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
      <DebugType>embedded</DebugType>
      <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
      <PublishTrimmed>false</PublishTrimmed>   <!-- WPF/WinForms don't support trimming (NETSDK1168/1175) -->
      <PublishReadyToRun>false</PublishReadyToRun>
    </PropertyGroup>
    ```
  - Why each setting:
    - `SelfContained`: runs without .NET installed (§13.11).
    - `PublishSingleFile`: one exe.
    - `IncludeNativeLibrariesForSelfExtract`: WPF's native libraries (`wpfgfx_cor3`,
      `PresentationNative_cor3`, `D3DCompiler_47_cor3`, `vcruntime140_cor3`, `PenImc_cor3`) go inside
      the exe instead of next to it. At first run the host extracts them to
      `%TEMP%\.net\Snapper\<hash>\`, which is documented in the README.
    - `EnableCompressionInSingleFile`: roughly halves the download. It costs a little startup time,
      which is acceptable for a tray app that starts once per session.
    - `DebugType embedded`: no loose `.pdb`, but stack traces keep line numbers.
    - `SatelliteResourceLanguages en`: drops the framework's localized resource DLLs. Snapper's UI is
      English-only (AGENTS.md). Common dialogs (Save, Color, folder picker) and TaskDialog buttons come
      from Windows and stay localized.
    - `PublishReadyToRun` off: cross-compiling R2R from macOS is untested and not needed. It is out of
      scope.
- **`windows/src/Snapper.Windows/Properties/PublishProfiles/win-x64.pubxml`** and **`win-arm64.pubxml`**
  (new). Each one **repeats every property of the conditional group** above, because the profile's
  `RuntimeIdentifier` isn't visible when the csproj condition is evaluated (verified). Each also sets:
  `Configuration` `Release`, `RuntimeIdentifier` `win-x64`/`win-arm64`, and
  `PublishDir` `$(MSBuildThisFileDirectory)..\..\..\..\artifacts\publish\win-x64\` (or `win-arm64`).
  That resolves to `windows/artifacts/publish/<rid>/`. Add a comment at the top of each profile:
  "Keep in sync with the RID-conditional PropertyGroup in Snapper.Windows.csproj."
- **`.gitignore`** (append under the existing "# Windows app" block from 0025): `windows/artifacts/`.
- **Single-file analyzer.** `PublishSingleFile` enables the single-file analyzer, and warnings are
  errors. Fix any IL3000/IL3001/IL3002 the publish reports at its root: `Assembly.Location` and
  `Assembly.GetFile*` are empty or throw in a single-file app. Use `Environment.ProcessPath` for the
  exe path (0033's launch-at-login `Run` value must point to `Snapper.exe`, never to a `.dll` or an
  empty string) and `AppContext.BaseDirectory` for the folder. Grep `windows/src` for `.Location` and
  `CodeBase` and fix every hit. Don't suppress the warnings.

### Version info (build config)

- **`windows/Directory.Build.props`** (edit, next to 0025's Company/Product/Copyright):
  `<Version>1.0.0</Version>` (matches macOS `MARKETING_VERSION` 1.0), and
  `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`,
  so the informational and product versions read exactly `1.0.0`, without a `+<git sha>` suffix, and
  builds stay deterministic. The SDK derives `FileVersion`/`AssemblyVersion` `1.0.0.0` from `Version`.
- The version isn't shown in any Snapper UI. The PRD doesn't ask for it, and adding it would be new
  product behavior. It appears in Explorer → Properties → Details.

### App icon (pure logic in `Snapper.Core` + a small generator tool + a checked-in asset)

**Decision:** the `.ico` is generated by C# code in `Snapper.Core` (unit-tested), run on demand by a
tiny console tool, and the output is **checked in** at `windows/src/Snapper.Windows/Assets/Snapper.ico`.
This adds no dependency and no Python. The publish doesn't depend on running the tool, and a golden
test fails if the checked-in file drifts from the code. Each icon size reuses
`TrayGlyphGeometry.Viewfinder` (0025), so the glyph matches the tray.

- **Design** (`windows/src/Snapper.Core/App/AppIconDesign.cs`, new, namespace `Snapper.Core.App`,
  `public static class AppIconDesign`):
  - `public static IReadOnlyList<int> Sizes` = `[16, 20, 24, 32, 40, 48, 64, 256]` (small icons at
    100–200%, Explorer's medium/large views, and the 256 "extra large"/Alt+Tab size).
  - `public const uint TileArgb = 0xFF0F6CBD;` (Windows' Fluent communication blue) and
    `public const uint GlyphArgb = 0xFFFFFFFF;` (white). The tray glyph is white or black on a
    transparent background to follow the taskbar theme, but the app icon appears on white (Explorer
    light) and black (dark) surfaces alike, so it uses a white glyph on a blue rounded-square tile.
  - `public static int CornerRadius(int size)` = `round(size × 0.1875, AwayFromZero)` (16→3, 32→6,
    256→48). `public static int GlyphInset(int size)` = `round(size × 0.125, AwayFromZero)` (16→2,
    32→4, 256→32).
  - `public static RgbaImage Render(int size)`: (1) fill the rounded-square tile `(0,0,size,size)` with
    `CornerRadius(size)` in `TileArgb`; (2) take `g = TrayGlyphGeometry.Viewfinder(size − 2 × GlyphInset(size))`,
    offset every line and circle by `(GlyphInset, GlyphInset)`, and draw it as **one coverage mask**
    (the union of all its shapes, so overlapping strokes don't double up) in `GlyphArgb`. Lines use
    `g.StrokeWidth` with **square caps**, so bracket corners are solid like the tray's joined
    polylines. The free ends extend half a stroke further than the tray's flat caps, which isn't
    visible. Unfilled circles are rings of width `g.StrokeWidth` centered on the radius, and filled
    circles are discs. `size` must be 1…256, otherwise `ArgumentOutOfRangeException`.
- **Raster** (`windows/src/Snapper.Core/Support/Imaging/`, new, namespace
  `Snapper.Core.Support.Imaging`). Pure, no `System.Drawing`:
  - `RgbaImage` (`sealed class`): `Width`, `Height`, `byte[] Pixels` (row-major RGBA8, straight alpha),
    `GetPixel(x,y)` → `uint` ARGB, constructor zero-filled (transparent).
  - `CoverageRasterizer` (`static class`): `4 × 4` supersampling at sample offsets `(i + 0.5) / 4`
    within each pixel. The sample positions are dyadic, so results are exact and identical on every OS.
    It uses only `+ − × ÷` and `Math.Sqrt`, with no trig or `Pow`. The API:
    `double[] Coverage(int w, int h, Func<double,double,bool> inside)` returns the fraction of the
    16 samples inside each pixel, and helpers return `Func<double,double,bool>` for
    `RoundedRect(x,y,w,h,r)`, `Segment(x1,y1,x2,y2,width)` (a rectangle: the segment extended by `width/2` at both ends
    (square caps), `width/2` either side of it), `Ring(cx,cy,r,width)`, and
    `Disc(cx,cy,r)`, plus `Union(params …)`.
    `void Paint(RgbaImage img, double[] coverage, uint argb)` composites the color with alpha × coverage
    **source-over** onto the image (straight alpha; channels rounded `AwayFromZero`).
  - `Crc32` (`static class`): the standard IEEE CRC-32, table-driven. `System.IO.Hashing` is a NuGet
    package, so it isn't used.
  - `PngWriter` (`static class`): `byte[] Encode(RgbaImage)` writes the signature, `IHDR` (8-bit, color
    type 6, no interlace), one `IDAT` (each row filter byte 0, compressed with
    `System.IO.Compression.ZLibStream`, `CompressionLevel.SmallestSize`), and `IEND`, with CRCs from
    `Crc32`.
  - `IcoWriter` (`static class`): `byte[] Write(IReadOnlyList<(int Size, byte[] Png)> entries)` writes
    `ICONDIR` (reserved 0, type 1, count) and one 16-byte `ICONDIRENTRY` per entry: width/height bytes
    (`0` for 256), 0 colors, reserved 0, planes 1, bit count 32, byte size, and offset (data
    contiguous, starting at `6 + 16 × count`). Then the PNG bytes, in the given order. It throws
    `ArgumentException` on an empty list, a size outside 1…256, or duplicate sizes. PNG-compressed
    entries are valid for every size on Windows 11.
  - `AppIconDesign.WriteIco()` → `IcoWriter.Write(Sizes.Select(s => (s, PngWriter.Encode(Render(s)))))`.
- **Generator tool** (`windows/tools/Snapper.IconGen/Snapper.IconGen.csproj`, new): `net10.0` `Exe`,
  `ProjectReference` to `Snapper.Core`, `IsPackable false`, no packages. `Program.cs` writes
  `AppIconDesign.WriteIco()` to the path in `args[0]` (usage message and exit code 2 if it's missing).
  Add it to `windows/Snapper.slnx` under a `/tools/` folder so `dotnet build` keeps it compiling.
  Regenerate with:
  ```sh
  dotnet run --project windows/tools/Snapper.IconGen -- windows/src/Snapper.Windows/Assets/Snapper.ico
  ```
  Run it once in this spec and commit the `.ico`. The tray icon is still drawn at run time (0025,
  PRD §13.3). The `.ico` is only the exe/window icon.
- **Windows' use of the icon:** `ApplicationIcon` puts it in the exe's resources. WPF windows that
  don't set `Icon` show the exe's icon in the title bar, taskbar, and Alt+Tab, which covers the
  annotation and Settings windows. If the manual check shows a generic icon, add
  `<Resource Include="Assets\Snapper.ico" />` and set
  `Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Snapper.ico"))` on
  `Annotate/AnnotationWindow` and `Settings/SettingsWindow`. Overlays, the pill, and the border have no
  taskbar presence (§13.1) and need nothing.

### Docs

- **`windows/README.md`** (new). It is short and covers the following:
  1. What it is (one paragraph, link to `../docs/PRD.md` §13 and `../AGENTS.md`).
  2. Requirements: .NET 10 SDK to build (macOS or Windows), Windows 11 22000+ x64/Arm64 to run. The
     published exe needs no .NET.
  3. Build and test: the AGENTS.md commands. On macOS the app compiles but can't run.
  4. Publish: both profile commands, the output paths `windows/artifacts/publish/win-x64/Snapper.exe` /
     `win-arm64/…`, and the expected size (~80 MB). Mention that `-r win-x64` gives the same result.
     No trimming (WPF/WinForms), no installer, and no signing (PRD §2).
  5. Run: copy `Snapper.exe` anywhere (for example `%LOCALAPPDATA%\Programs\Snapper\`) and
     double-click it. It's unsigned, so a downloaded copy shows SmartScreen ("Windows protected your
     PC" → *More info* → *Run anyway*), or use *Properties → Unblock*. The first start extracts native
     libraries to `%TEMP%\.net\Snapper\`. Launch at login stores the exe's path, so turn it off before
     moving the exe, or turn it on again afterwards.
  6. Where things live: settings `%APPDATA%\Snapper\settings.json`, captures
     `%USERPROFILE%\Pictures\Screenshots`, login entry
     `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Snapper`.
  7. Uninstall: turn off *Launch at login* (or delete the `Run` value), *Exit Snapper*, delete the exe,
     `%APPDATA%\Snapper`, and `%TEMP%\.net\Snapper`.
  8. Regenerating the app icon (the `dotnet run` command above).
- **`AGENTS.md`** (edit the "Windows app" section only):
  - *Commands*: replace the publish line with
    `dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-x64   # or win-arm64 → windows/artifacts/publish/<rid>/Snapper.exe`,
    and add the icon regeneration command.
  - *Layout*: add `tools/Snapper.IconGen/   # regenerates src/Snapper.Windows/Assets/Snapper.ico`,
    `artifacts/  # publish output (git-ignored)`, and `README.md`.
  - *Architecture rules*: one bullet: "The app is published single-file: never use
    `Assembly.Location`. Use `Environment.ProcessPath`/`AppContext.BaseDirectory`. Keep the
    RID-conditional publish group in `Snapper.Windows.csproj` and the two `.pubxml` profiles in sync."
- **`docs/PRD.md`**: no change. §13.1 and §13.11 already describe this. (Batch rule: don't edit it.)

## Acceptance criteria

- [ ] `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx` are green
      with zero warnings, on macOS.
- [ ] On macOS, `dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-x64` and
      `-p:PublishProfile=win-arm64` each succeed with zero warnings. Each output folder contains
      **exactly one file, `Snapper.exe`**. `-c Release -r win-x64` also produces a single
      `Snapper.exe`.
- [ ] `windows/src/Snapper.Windows/Assets/Snapper.ico` is checked in, holds the 8 sizes, and equals
      what `Snapper.IconGen` produces (golden test).
- [ ] Explorer → Properties → Details of the published exe shows File description **Snapper**,
      Product name **Snapper**, File/Product version **1.0.0.0 / 1.0.0**, Copyright
      **© 2026 Thera Engineering**, and the Snapper icon.
- [ ] Both exes run on a clean Windows 11 machine of their architecture without .NET installed
      (§13.11).
- [ ] Every PRD §12 criterion (mapped through §13.2/§13.11) and every §13.11 criterion passes on the
      published exe in the manual pass below.
- [ ] `windows/README.md` exists with the sections listed above, and the AGENTS.md Windows section
      matches the commands and layout.

## Edge cases

- **Publishing without network:** restore needs the `Microsoft.NETCore.App.Runtime.win-*`,
  `Microsoft.WindowsDesktop.App.Runtime.win-*`, and apphost packs. The first publish per RID
  downloads them (about 100 MB). These are SDK runtime packs, not new dependencies.
- **Publish profile vs `-r` drift:** if someone changes one block and not the other, `-r` and profile
  output differ. The AGENTS.md rule and the pubxml comment flag it. The manual publish check builds both
  forms once.
- **Single-file paths:** `Assembly.Location` is `""`. The launch-at-login value must be the quoted
  `Environment.ProcessPath` of `Snapper.exe`, not the extraction folder. A moved exe leaves a stale
  `Run` value, and the 0033 toggle shows it as off (it only counts as on when it points to the running
  exe). Turning it on rewrites the value. The README documents this.
- **`%TEMP%` not writable or cleaned:** the host re-extracts native libraries on the next start. Nothing
  to handle in code.
- **Arm64 running the x64 exe:** it works under emulation, but it isn't a substitute for testing the
  arm64 exe. The checklist tests each exe natively.
- **VMs without a hardware H.264 encoder** (Parallels, Hyper-V, Windows Sandbox): Media Foundation
  falls back to its software encoder (0034). Recording must still work, only slower. Note the VM type in
  the results.
- **Mixed-DPI and negative-origin monitors** are covered by the checklist (items marked *multi-monitor*).
  They need a real second monitor, or a VM with two virtual displays at different scales.
- **Icon at tiny sizes:** at 16 px the glyph is 12 px with 1-px strokes, and the lens radius is 2. The
  design test asserts it stays inside the tile and is symmetric.
- **This is the last spec:** there's no later spec to defer to. A failing checklist item is reported
  as a bug against the spec that owns the behavior (see the owning spec in each item), not fixed by
  widening this spec. The exception is a fix needed only because of single-file publishing (paths,
  analyzer warnings, icon), which belongs here.

## Test plan

- **Unit tests** (`windows/tests/Snapper.Core.Tests/`):
  - `Crc32Tests.cs`
    - `Crc32("123456789")` = `0xCBF43926` (the standard check value).
    - Empty input = `0x00000000`.
  - `CoverageRasterizerTests.cs`
    - `RoundedRect(0,0,8,8,0)` over 8×8 → every pixel coverage `1.0`.
    - `RoundedRect(0,0,0.5,1,0)` over 2×1 → pixel (0,0) `0.5`, pixel (1,0) `0`.
    - `Segment(1,1.5,5,1.5,1)` (square caps) over 8×4 → pixels x = 1…4 in row 1 are `1.0`; (0,1) and
      (5,1) are `0.5`; rows 0 and 2 are `0`.
    - `Disc(4,4,3)` → (4,4) `1.0`, (0,0) `0`.
    - `Ring(4,4,3,1)` → pixel (4,4) (at the center) is `0`; pixel (4,1) (crossing the radius) is > 0.
    - `Union(a, b)` of two overlapping full squares → coverage `1.0` in the overlap, not `2`.
    - `Paint`: opaque white at coverage `0.5` over opaque `0xFF0F6CBD` → channels
      `round(0.5×255 + 0.5×c)` with `AwayFromZero` (`0x87, 0xB6, 0xDE`), alpha 255. Paint onto a
      transparent pixel at coverage `0.25` → white RGB with alpha `64`.
  - `PngWriterTests.cs`
    - It starts with `89 50 4E 47 0D 0A 1A 0A`, then the `IHDR` chunk with length 13, the width and
      height big-endian, bit depth 8, color type 6, and compression/filter/interlace 0.
    - It ends with exactly `00 00 00 00 49 45 4E 44 AE 42 60 82` (the IEND chunk).
    - Every chunk's CRC matches `Crc32(type + data)`.
    - Round trip: a 3×2 image with distinct pixels (including partial alpha), decoded by a
      **test-only helper** `PngTestDecoder` (concatenate the IDATs, `ZLibStream` decompress, strip the
      filter-0 bytes; asserts filter bytes are 0), equals the input bytes.
  - `IcoWriterTests.cs`
    - Two entries (16, 256): the header is `00 00 01 00 02 00`. Entry 0 has width/height `16`, planes
      1, bpp 32, size = png length, and offset `6 + 32 = 38`. Entry 1 has width/height bytes `0`
      (=256), and its offset = 38 + entry 0's length. The data at each offset equals the given PNG.
    - Empty list, size 0, size 257, and duplicate sizes each throw `ArgumentException`.
  - `AppIconDesignTests.cs`
    - `Sizes` = `[16, 20, 24, 32, 40, 48, 64, 256]`. `CornerRadius(16)=3`, `(256)=48`.
      `GlyphInset(16)=2`, `(256)=32`.
    - For every size: `Render(s)` is `s×s`. The four corner pixels have alpha 0 (outside the rounded
      corner). The pixel `(s/2, GlyphInset(s)/2)` (between the tile edge and the glyph, top middle)
      equals `TileArgb` exactly. The image is mirror-symmetric horizontally and vertically (pixel-exact).
    - `Render(16)`: pixel (3,3) (the top-left bracket corner) is opaque white. The center pixel (8,8)
      equals `TileArgb` (inside the lens ring). Pixels (2,8) and (13,8) are tile-colored (left and
      right of the glyph, beyond the bracket's 1-px margin).
    - `Render(256)`: inset 32, inner 192, stroke 12, so the top-left bracket's horizontal stroke covers
      y 33…45 from x 33. Pixel (38,38) is opaque white, and (128,128) (inside the lens) is `TileArgb`.
    - The glyph lines come from `TrayGlyphGeometry.Viewfinder(s − 2·inset)` offset by the inset:
      assert that the first bracket line's start, offset, lies on an opaque white pixel for `s = 32`
      (guards against someone drawing a different glyph).
    - `Render(0)` and `Render(257)` throw `ArgumentOutOfRangeException`.
  - `AppIconAssetTests.cs` (golden drift guard). Add to `Snapper.Core.Tests.csproj`
    `<None Include="..\..\src\Snapper.Windows\Assets\Snapper.ico" Link="Assets\Snapper.ico" CopyToOutputDirectory="PreserveNewest" />`.
    - Parse the checked-in `Snapper.ico` header: type 1, 8 entries, sizes in `AppIconDesign.Sizes`
      order, offsets in bounds and contiguous.
    - Each entry's PNG decodes (via `PngTestDecoder`) to exactly `AppIconDesign.Render(size).Pixels`.
      Compare pixels, not compressed bytes, so zlib-version differences can't break the test. The
      failure message names the regeneration command.
  - `VersionInfoTests.cs`
    - `typeof(AppIconDesign).Assembly`'s `AssemblyInformationalVersionAttribute` is exactly `"1.0.0"`
      (no `+sha` suffix), and `GetName().Version` is `1.0.0.0`. This guards the
      `Directory.Build.props` settings that the exe shares.
- **Build check (macOS):**
  1. `dotnet build windows/Snapper.slnx -c Debug` and `dotnet test windows/Snapper.slnx`: green,
     0 warnings.
  2. `dotnet publish windows/src/Snapper.Windows -p:PublishProfile=win-x64` and `…=win-arm64`: 0
     warnings. `ls windows/artifacts/publish/win-x64 windows/artifacts/publish/win-arm64` shows only
     `Snapper.exe` in each. Record both sizes in the verification status.
  3. `dotnet publish windows/src/Snapper.Windows -c Release -r win-x64 -o <scratch>`: a single
     `Snapper.exe`.
  4. `git status` shows no `windows/artifacts/` entries.
- **Manual verification**: the final v1 pass on the **published** exes, not a Debug build. Machines:
  **(A)** a clean Windows 11 x64 machine without .NET (a fresh VM, or Windows Sandbox, where
  `dotnet --list-runtimes` fails or lists no `Microsoft.WindowsDesktop.App 10.*`); **(B)** a clean
  Windows 11 Arm64 machine without .NET (for example Parallels on Apple Silicon, or a Snapdragon PC);
  **(C)** a Windows 11 machine with two monitors at different scales (100% + 150%, with the second
  monitor placed **left of** the primary so it has a negative origin). C can be A or B with a second
  display. Run items 1–4 on A and B. Run the rest on at least one of them with the matching exe, and
  repeat items 7, 14, 18, and 22 on the other architecture. The owning spec is in brackets, so a
  failure can be filed against it.

  *Install and identity* (§13.1, §13.11)
  1. Copy the exe to `%LOCALAPPDATA%\Programs\Snapper\Snapper.exe` and double-click it (accept
     SmartScreen if shown). **Expected:** it starts within a few seconds with no ".NET is required"
     prompt. A viewfinder tray icon appears, and there's no window, no taskbar button, and no Alt+Tab
     entry. [0025, 0047]
  2. Properties → Details: description, product, versions, copyright, and icon as in the acceptance
     criteria. Explorer's *Large icons* view shows the white viewfinder on the blue tile, and it's
     crisp at 100% and 150%. [0047]
  3. Launch the exe again. **Expected:** the running instance's Settings opens, and no second tray
     icon appears (Task Manager shows one Snapper process after a moment). [0025, 0033]
  4. Settings and annotation windows show the Snapper icon in the title bar, taskbar, and Alt+Tab.
     [0047]

  *Tray* (§12 menu items → §13.3, §13.11)
  5. Left-click and right-click the tray icon. **Expected:** the same menu opens each time: Screenshot
     `Ctrl+Shift+4`, Record `Ctrl+Shift+5`, Annotate `Ctrl+Shift+6`, separator, Settings…, Exit Snapper.
     The icon is white on a dark taskbar and black on a light one, and switching the Windows mode
     updates it. [0025, 0029]

  *Screenshot* (§12, §13.4, §13.5, §13.11)
  6. Ctrl+Shift+4, drag a rectangle. **Expected:** the shutter click plays, and
     `%USERPROFILE%\Pictures\Screenshots\Screenshot YYYY-MM-DD at HH.MM.SS.png` exists (the folder
     is created if it was missing) at the monitor's native pixel size. The image also pastes into
     Paint and into WordPad/Word (PNG + DIB). There's no cursor in the image. [0026, 0030, 0031]
  7. *Multi-monitor (C):* with monitors at 100% and 150%, start a drag on each monitor and drag past
     its edge into the other. **Expected:** the overlay covers both monitors (taskbars included), the
     rectangle stays on the starting monitor, the W×H readout shows DIPs, and each PNG has that
     monitor's native pixel size (e.g. a 200×100-DIP drag at 150% → 300×150 px). Repeat on the
     negative-origin monitor. [0028, 0030, 0031]
  8. Ctrl+Shift+4, then Space, and click a window that another window partly covers. **Expected:** the
     PNG contains only that window, whole, with no shadow and no covering window. Space during a drag
     discards the drag. [0032]
  9. Esc with the overlay open on two monitors. **Expected:** both overlays close, nothing is captured,
     and focus returns to the previous app. A click or a drag below 4×4 DIPs does nothing and keeps the
     overlay open. Connecting or disconnecting a monitor, or changing a scale, while the overlay is open
     cancels it. [0030]
  10. Settings → General → Screenshot destination *Copy to clipboard*, then *Save to file*. **Expected:**
      each does only that, and *Both* (the default) does both. Turning off *Play shutter sound*
      silences the click. [0031, 0033]
  11. Set the save folder to a read-only folder (e.g. `C:\Windows`) and take a screenshot.
      **Expected:** "Couldn't save to C:\Windows" with **Choose Folder…** and Cancel. With *Both*, the
      image is still on the clipboard. Choosing a writable folder saves the pending screenshot there and
      updates the setting. [0026, 0031]
  12. Grab a screenshot, a recording, and an annotation while a Snapper window (Settings, annotation)
      and the overlay/pill/border are visible. **Expected:** overlays, the pill, and the border never
      appear in any output. [0030, 0035, §13.11]

  *Recording* (§12, §13.6, §13.11)
  13. Ctrl+Shift+5, drag a rectangle, wait 5 s, and click Stop in the pill. **Expected:** recording
      starts immediately. The pill shows the elapsed time · Stop · Cancel below the area (or above it,
      or inside for a full-screen area), with a dashed border. The tray icon turns into the red dot
      with the tooltip "Snapper — Recording". On Stop, `Recording … .mp4` (H.264, 30 fps, cursor
      visible, even dimensions) is saved, Explorer opens with it selected, and the clipboard is
      unchanged. Ctrl+Shift+4/6 are ignored while recording, and Esc does not cancel. [0034, 0035]
  14. Repeat, stopping with Ctrl+Shift+5, then with a tray click (no menu opens). Then repeat and
      click Cancel. **Expected:** the first two save and reveal. Cancel discards with no file. The video
      contains neither the pill nor the border. [0034, 0035]
  15. Window recording: Ctrl+Shift+5, Space, click a window, then move, resize, cover, and minimize it,
      then close it. **Expected:** the video follows the window at a fixed size (letterboxed when
      resized). The pill and border follow it about 4×/s. Minimizing hides the border and keeps the pill.
      Closing the window stops and saves the recording. [0036]
  16. Settings → Recording → *Record microphone* on (on a machine with microphone access denied in
      Privacy, then allowed). **Expected:** with access denied, the toggle switches back off and the
      note shows **Open Privacy Settings**. With access allowed, the MP4 has AAC audio. [0037]
  17. MP4 quality: record the same rectangle for 10 s at High, Medium, and Low. **Expected:** all have
      the same pixel size, Medium and Low are smaller than High, and High is the plain capture. During
      the conversion, the tray stays red and clicks and Ctrl+Shift+5 do nothing. [0045]
  18. Format GIF (quality High/Medium/Low). **Expected:** a looping `Recording … .gif` at the chosen
      quality, with no pill or border. *Record microphone* is disabled with "GIFs don't include
      sound.". [0046]
  19. *Exit Snapper* while recording, and sign out while recording. **Expected:** the recording is saved
      before exit. On sign-out, Windows shows "Saving recording…" briefly and the file is saved.
      [0034, 0045]

  *Annotate* (§12, §13.7, §13.11)
  20. Ctrl+Shift+6, then select an area while another app is focused. **Expected:** the annotation window
      opens in front with keyboard focus, about 80% of the work area, never upscaled. [0040]
  21. In the window: R/A/T/V, the colors (+ the Color dialog), S/M/L, drawing, Shift constraints, select,
      move, resize, Delete/Backspace, Esc deselects (never closes), Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z
      across every kind of edit, recoloring the selection, and text (place, edit, re-edit with the caret
      at the click, outline, Esc commits). **Expected:** everything matches PRD §8.2/§8.3. Color and
      width persist after relaunch. [0040–0042]
  22. On the 150% monitor, annotate a capture, then Ctrl+S (dialog prefilled with the save folder and
      `Screenshot … .png`), Ctrl+C, and the checkmark Copy & Close / Ctrl+Enter (also while editing
      text). **Expected:** the saved and copied images have the capture's full pixel size. Copy & Close
      closes without a prompt. [0043, 0044]
  23. Close a modified window (Ctrl+W, Alt+F4, X). **Expected:** "Discard changes?". *Exit Snapper*
      with 2 modified windows asks once "Exit and discard 2 unsaved annotations?". Signing out lists
      Snapper with "Unsaved annotations" on the shutdown screen, with no Snapper prompt. [0043]

  *Settings, shortcuts, login* (§12, §13.9, §13.10, §13.11)
  24. Rebind Screenshot to Ctrl+Alt+S. **Expected:** it works at once, the old binding is free, and the
      tray menu shows the new text. A binding used by another Snapper shortcut is rejected. A
      Win-key combination isn't accepted. [0029, 0033]
  25. Let another app hold Ctrl+Shift+5 (e.g. a second test app, or AutoHotkey `^+5::return`) and then
      start Snapper. **Expected:** a tray notification "Ctrl+Shift+5 is used by another app. Change it
      in Settings." Clicking it opens Settings → Shortcuts with "Another app is using this shortcut."
      The other two shortcuts work, and Record still works from the tray menu. [0029, 0033]
  26. Turn on *Launch at login*. **Expected:** `HKCU\…\Run\Snapper` = `"<full path>\Snapper.exe"`.
      Sign out and in, and Snapper starts. Turn it off, and the value is gone. [0033, 0047]
  27. Delete `%APPDATA%\Snapper\settings.json` and relaunch. **Expected:** all defaults. [0027]

## Out of scope

- An installer, MSIX/Store packaging, code signing, auto-update, and a Windows 10 build (PRD §2).
- ReadyToRun/AOT compilation and trimming (unsupported by WPF/WinForms).
- CI pipelines and GitHub Releases. Publishing is a local command.
- Showing the version in the UI (the PRD doesn't ask for it).
- Fixing feature bugs found by the checklist. They're filed against the owning spec (0025–0046), unless
  they're caused by single-file publishing.
- macOS packaging (unchanged).


## Verification status

Automated verification on 2026-11-03 is green:

- `dotnet build windows/Snapper.slnx -c Debug`: **0 warnings, 0 errors**.
- `dotnet test windows/Snapper.slnx`: **1,383 passed, 0 failed, 0 skipped**, including 25 new packaging/icon tests. Tests cover raster coverage/compositing, CRC-32, PNG round trips/CRCs, ICO entries, all eight generated icon sizes, the checked-in asset's decoded pixels, and exact version metadata.
- Both publish-profile commands and `dotnet publish windows/src/Snapper.Windows -c Release -r win-x64 -o /tmp/snapper-0047-plain-x64` passed with **zero warnings/errors**. Every output directory contains exactly one file, `Snapper.exe`.
- `windows/artifacts/publish/win-x64/Snapper.exe`: **77,920,445 bytes**, PE32+ GUI **x64 (0x8664)**. All five bundled native DLLs have the matching x64 machine type.
- `windows/artifacts/publish/win-arm64/Snapper.exe`: **73,703,218 bytes**, PE32+ GUI **Arm64 (0xAA64)**. All four bundled native DLLs have the matching Arm64 machine type.
- The plain `-r win-x64` executable is byte-identical to the x64 profile output (SHA-256 `c9fd6ccf32cc5fc327510fba79226f83b9e75b96f44cc723847b7487b0d7735c`). Arm64 SHA-256: `3cd5d0d66d801d388ad735a29f3acb0df4fe3b483fe2c589ebd7c3ace85e8bc8`.
- PE resource/bundle inspection confirms all eight PNG icon resources; exact company/product/file version fields; PerMonitorV2, asInvoker, Common Controls 6 and Windows 10/11 compatibility manifest; bundled .NET and Windows Desktop **10.0.2** runtimes and native WPF libraries. The icon's 256-pixel raster was visually inspected. No Windows executable was run.
- No `Assembly.Location`, `CodeBase`, or assembly-file lookup hits exist in `windows/src`; the single-file analyzer reported no warnings. Debug remains framework-dependent with its existing output path. Publish output is git-ignored.

Implementation adjustments to the starting config:

- Earlier specs set a default Debug Windows RID. The publish group is therefore **Release + RID conditional**, preserving framework-dependent Debug behavior. Profiles repeat its properties.
- Release builds enable RID-specific output/intermediate paths. Without this, sequential x64/Arm64 publishing reused an x64 apphost in an Arm64 bundle; machine-type inspection caught it, and both outputs were rebuilt and checked after the fix.
- Shared Release props embed symbols in referenced Core projects as well as the app. App-only embedded symbols otherwise left a loose `Snapper.Core.pdb` next to the executable.
- The Windows wording in manual item 23 is corrected to **Exit** per PRD §13.2. No PRD change was needed.

Logs: `/tmp/snapper-0047-build.log`, `/tmp/snapper-0047-test.log`, `/tmp/snapper-0047-publish-x64.log`, `/tmp/snapper-0047-publish-arm64.log`, `/tmp/snapper-0047-publish-plain.log`, and `/tmp/snapper-0047-inspection.jsonl`.

**Release acceptance is still pending.** GUI, capture, audio, native codec playback, and clean-Windows runtime behavior have not been verified. Run the full Manual verification section above on clean Windows 11 x64 and Arm64 machines without .NET (A/B), with mixed-DPI, negative-origin monitors for C. Run 1–4 on both architectures, the remaining items on at least one, and repeat 7, 14, 18, and 22 on the other architecture. Record VM/device details, including hardware/software H.264 encoding when relevant.

- [ ] not yet run by user: **1 — Clean-machine launch (A/B):** copy the matching published executable, launch without a .NET prompt, and confirm tray-only presence.
- [ ] not yet run by user: **2 — Identity (A/B):** inspect Explorer versions, copyright and icon, including crisp 100%/150% large-icon rendering.
- [ ] not yet run by user: **3 — Single instance (A/B):** relaunch and confirm existing Settings opens with one process/tray icon.
- [ ] not yet run by user: **4 — Window icons (A/B):** confirm Settings and annotation title-bar/taskbar/Alt+Tab icons.
- [ ] not yet run by user: **5 — Tray:** exercise both menu buttons, current shortcut text, and light/dark theme switching.
- [ ] not yet run by user: **6 — Screenshot:** verify default save + PNG/DIB clipboard output, filename/folder creation, shutter sound, native dimensions and no cursor.
- [ ] not yet run by user: **7 — Mixed monitors (C/both architectures):** check overlay coverage, DIP readout, starting-monitor clamp, native pixel output and negative origins.
- [ ] not yet run by user: **8 — Window screenshot:** test covered-window capture without shadow and Space during a drag.
- [ ] not yet run by user: **9 — Selection cancellation:** check Esc, tiny drags, focus return and display-change cancellation.
- [ ] not yet run by user: **10 — Destinations/sound:** test each destination independently and the shutter toggle.
- [ ] not yet run by user: **11 — Folder failure:** test Choose Folder/Cancel, pending-save retry, settings update and clipboard preservation.
- [ ] not yet run by user: **12 — Exclusion:** confirm overlays/pill/border never appear in screenshots, recordings or annotated captures; normal Settings/annotation windows may appear per PRD §13.6.
- [ ] not yet run by user: **13 — Recording:** verify immediate start, pill/border, red tray/tooltip, dimensions/timing/cursor, Explorer reveal, ignored shortcuts and unchanged clipboard.
- [ ] not yet run by user: **14 — Stop/cancel (both architectures):** test pill/hotkey/tray stop and Cancel; inspect output for overlay exclusion.
- [ ] not yet run by user: **15 — Window recording:** move/resize/cover/minimize/close; verify fixed letterboxing, following controls and stop/save on closure.
- [ ] not yet run by user: **16 — Microphone:** test privacy denied/allowed states, privacy link and AAC audio.
- [ ] not yet run by user: **17 — MP4 quality:** compare High/Medium/Low dimensions/sizes and conversion control suppression.
- [ ] not yet run by user: **18 — GIF (both architectures):** test all qualities, looping, overlay exclusion and disabled microphone behavior.
- [ ] not yet run by user: **19 — Recording exit/session end:** verify save-before-exit/sign-out and Windows' Saving recording reason.
- [ ] not yet run by user: **20 — Annotation focus:** capture from another app and confirm frontmost focused window, initial fit and no upscaling.
- [ ] not yet run by user: **21 — Annotation tools:** test all tools/styles/constraints/editing/undo/delete/text/caret/outline and style persistence.
- [ ] not yet run by user: **22 — Export (both architectures):** test Save/Copy/Copy & Close at full capture resolution on 150% DPI, including Ctrl+Enter during text editing.
- [ ] not yet run by user: **23 — Unsaved work:** test all close methods, one Exit prompt with two dirty editors, and Unsaved annotations shutdown blocking without a Snapper prompt.
- [ ] not yet run by user: **24 — Rebinding:** test immediate changes, tray display, duplicates and Windows-key rejection.
- [ ] not yet run by user: **25 — Conflicts:** occupy a binding externally, confirm notification/Shortcuts warning and working remaining shortcuts/tray action.
- [ ] not yet run by user: **26 — Login:** verify quoted published-exe Run path, sign-in launch and removing the value when disabled.
- [ ] not yet run by user: **27 — Defaults:** remove the settings file and confirm defaults after relaunch.
