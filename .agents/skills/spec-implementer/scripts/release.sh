#!/usr/bin/env bash
# Build and publish one combined GitHub release of Snapper (macOS + Windows).
#
# Usage:
#   release.sh --bump patch|minor|major --notes <file> [--dry-run]
#   release.sh --version X.Y.Z          --notes <file> [--dry-run]
#
# Steps:
#   1. Preflight: on main, clean tree, up to date with origin, gh logged in, tag unused.
#   2. Bump the version (windows/Directory.Build.props <Version>, project.yml MARKETING_VERSION,
#      and project.yml CURRENT_PROJECT_VERSION + 1) and commit "Release vX.Y.Z".
#   3. Run macOS and Windows tests.
#   4. Build a universal (arm64 + x86_64), ad hoc signed macOS Release app -> .zip and .dmg.
#   5. Publish self-contained single-file Windows exes for win-x64 and win-arm64.
#   6. Verify the artifacts are self-contained and carry the new version; write SHA256SUMS.txt.
#   7. Push the commit, tag vX.Y.Z, and create the GitHub release with all assets.
#
# If a "Release vX.Y.Z" commit for the version in the files exists but was never tagged (an
# earlier run failed), the script finishes that release instead of bumping again.
#
# --dry-run does steps 3-6 with the new version passed as build overrides, but edits no files,
# makes no commit, tag, push, or release. Artifacts land in build/release/ (git-ignored).
set -euo pipefail

die() { echo "release: error: $*" >&2; exit 1; }
log() { echo; echo "==> $*"; }

BUMP="" VERSION="" NOTES="" DRY_RUN=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump) BUMP="${2:-}"; shift 2 ;;
    --version) VERSION="${2:-}"; shift 2 ;;
    --notes) NOTES="${2:-}"; shift 2 ;;
    --dry-run) DRY_RUN=1; shift ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) die "unknown argument: $1" ;;
  esac
done

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"
REPO="devnixs/ScreenshotApp"
OUT="$ROOT/build/release"
PROPS="windows/Directory.Build.props"

[[ -n "$NOTES" ]] || die "--notes <file> is required"
[[ -f "$NOTES" ]] || die "notes file not found: $NOTES"
NOTES="$(cd "$(dirname "$NOTES")" && pwd)/$(basename "$NOTES")"
for tool in git gh xcodegen xcodebuild dotnet lipo codesign otool hdiutil ditto shasum plutil; do
  command -v "$tool" >/dev/null || die "$tool is not on PATH"
done

# --- Current and next version -------------------------------------------------------------
CUR_WIN="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROPS" | head -1)"
CUR_MAC="$(sed -n 's/^ *MARKETING_VERSION: *"\{0,1\}\([^"]*\)"\{0,1\} *$/\1/p' project.yml | head -1)"
CUR_BUILD="$(sed -n 's/^ *CURRENT_PROJECT_VERSION: *"\{0,1\}\([^"]*\)"\{0,1\} *$/\1/p' project.yml | head -1)"
[[ "$CUR_WIN" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "can't read <Version> from $PROPS (got '$CUR_WIN')"
[[ "$CUR_BUILD" =~ ^[0-9]+$ ]] || die "can't read CURRENT_PROJECT_VERSION from project.yml (got '$CUR_BUILD')"
[[ -n "$CUR_MAC" ]] || die "can't read MARKETING_VERSION from project.yml"

# The highest released vX.Y.Z tag also counts, so a bump never goes backwards.
LATEST_TAG="$( { git tag -l 'v[0-9]*'; git ls-remote --tags origin 'v[0-9]*' 2>/dev/null | sed 's:.*refs/tags/::; s:\^{}$::'; } \
  | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sed 's/^v//' | sort -V | tail -1 || true)"
BASE="$(printf '%s\n%s\n' "$CUR_WIN" "${LATEST_TAG:-0.0.0}" | sort -V | tail -1)"

RESUME=0
if git log --format=%s | grep -qxF "Release v$CUR_WIN" && [[ "${LATEST_TAG:-}" != "$CUR_WIN" ]] \
   && ! git rev-parse -q --verify "refs/tags/v$CUR_WIN" >/dev/null; then
  # An earlier run committed the bump but never tagged it: finish that release, don't bump again.
  RESUME=1
  VERSION="$CUR_WIN"
  echo "release: resuming untagged release v$VERSION (ignoring --bump/--version)"
elif [[ -n "$VERSION" ]]; then
  [[ -z "$BUMP" ]] || die "pass --bump or --version, not both"
  [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "--version must be X.Y.Z"
  [[ "$(printf '%s\n%s\n' "$BASE" "$VERSION" | sort -V | tail -1)" == "$VERSION" && "$VERSION" != "$BASE" ]] \
    || die "--version $VERSION must be greater than the current $BASE"
else
  IFS=. read -r MA MI PA <<<"$BASE"
  case "$BUMP" in
    major) VERSION="$((MA + 1)).0.0" ;;
    minor) VERSION="$MA.$((MI + 1)).0" ;;
    patch) VERSION="$MA.$MI.$((PA + 1))" ;;
    *) die "--bump must be patch, minor, or major" ;;
  esac
fi
if [[ $RESUME == 1 ]]; then BUILD_NUMBER="$CUR_BUILD"; else BUILD_NUMBER="$((CUR_BUILD + 1))"; fi
TAG="v$VERSION"
echo "release: $BASE -> $VERSION (build $BUILD_NUMBER), tag $TAG$([[ $DRY_RUN == 1 ]] && echo ', DRY RUN')"

# --- Preflight ---------------------------------------------------------------------------
log "Preflight"
[[ "$(git rev-parse --abbrev-ref HEAD)" == "main" ]] || die "not on main"
if [[ -n "$(git status --porcelain)" ]]; then
  [[ $DRY_RUN == 1 ]] || die "working tree is not clean"
  echo "warning: working tree is not clean (allowed for --dry-run)"
fi
git fetch --quiet origin main --tags
[[ "$(git rev-parse HEAD)" == "$(git rev-parse origin/main)" ]] \
  || [[ "$(git merge-base HEAD origin/main)" == "$(git rev-parse origin/main)" ]] \
  || die "main has diverged from origin/main"
if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then die "tag $TAG already exists"; fi
gh auth status >/dev/null 2>&1 || die "gh is not logged in (run: gh auth login)"
if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then die "release $TAG already exists"; fi

# --- Version bump ------------------------------------------------------------------------
if [[ $DRY_RUN == 0 && $RESUME == 0 ]]; then
  log "Bumping version files"
  sed -i '' "s:<Version>$CUR_WIN</Version>:<Version>$VERSION</Version>:" "$PROPS"
  sed -i '' -E "s/^( *MARKETING_VERSION: *).*/\1\"$VERSION\"/" project.yml
  sed -i '' -E "s/^( *CURRENT_PROJECT_VERSION: *).*/\1\"$BUILD_NUMBER\"/" project.yml
  grep -q "<Version>$VERSION</Version>" "$PROPS" || die "failed to bump $PROPS"
  grep -q "MARKETING_VERSION: \"$VERSION\"" project.yml || die "failed to bump MARKETING_VERSION"
  grep -q "CURRENT_PROJECT_VERSION: \"$BUILD_NUMBER\"" project.yml || die "failed to bump CURRENT_PROJECT_VERSION"
  git add "$PROPS" project.yml
  git commit --quiet -m "Release $TAG" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
fi
COMMIT="$(git rev-parse HEAD)"

rm -rf "$OUT"
mkdir -p "$OUT/assets"
ASSETS="$OUT/assets"

# --- Tests -------------------------------------------------------------------------------
log "xcodegen + macOS tests"
xcodegen generate --quiet
xcodebuild -scheme Snapper -derivedDataPath "$OUT/DerivedData-test" test \
  > "$OUT/macos-test.log" 2>&1 || { tail -50 "$OUT/macos-test.log"; die "macOS tests failed (log: $OUT/macos-test.log)"; }
# Swift Testing summary, e.g. "Test run with 405 tests in 73 suites passed".
MAC_TESTS="$(sed -nE 's/.*Test run with ([0-9]+) tests? in [0-9]+ suites? passed.*/Unit tests: \1 passed, 0 failed./p' "$OUT/macos-test.log" | tail -1)"
echo "${MAC_TESTS:-macOS tests passed}"

log "Windows tests (Release)"
dotnet test windows/Snapper.slnx -c Release > "$OUT/windows-test.log" 2>&1 \
  || { tail -50 "$OUT/windows-test.log"; die "Windows tests failed (log: $OUT/windows-test.log)"; }
WIN_TESTS="$(sed -nE 's/^Passed! +- Failed: +([0-9]+), Passed: +([0-9]+), Skipped: +([0-9]+).*/Unit tests: \2 passed, \1 failed, \3 skipped./p' "$OUT/windows-test.log" | tail -1)"
echo "${WIN_TESTS:-Windows tests passed}"

# --- macOS build -------------------------------------------------------------------------
log "Universal macOS Release build (ad hoc signed)"
xcodebuild -scheme Snapper -configuration Release -derivedDataPath "$OUT/DerivedData" \
  ARCHS="arm64 x86_64" ONLY_ACTIVE_ARCH=NO \
  CODE_SIGN_STYLE=Manual CODE_SIGN_IDENTITY=- DEVELOPMENT_TEAM= \
  MARKETING_VERSION="$VERSION" CURRENT_PROJECT_VERSION="$BUILD_NUMBER" \
  clean build -quiet > "$OUT/macos-build.log" 2>&1 \
  || { tail -50 "$OUT/macos-build.log"; die "macOS Release build failed (log: $OUT/macos-build.log)"; }
APP="$OUT/DerivedData/Build/Products/Release/Snapper.app"
BIN="$APP/Contents/MacOS/Snapper"
[[ -x "$BIN" ]] || die "missing $BIN"

log "Verifying the macOS app"
ARCHS_FOUND="$(lipo -archs "$BIN")"
[[ "$ARCHS_FOUND" == *arm64* && "$ARCHS_FOUND" == *x86_64* ]] || die "app is not universal: $ARCHS_FOUND"
PLIST="$APP/Contents/Info.plist"
[[ "$(plutil -extract CFBundleShortVersionString raw "$PLIST")" == "$VERSION" ]] || die "app version mismatch"
[[ "$(plutil -extract CFBundleVersion raw "$PLIST")" == "$BUILD_NUMBER" ]] || die "app build number mismatch"
# Self-contained: every linked library is a system library or embedded inside the bundle.
while read -r dep; do
  case "$dep" in
    /System/Library/*|/usr/lib/*) ;;
    @rpath/*|@executable_path/*|@loader_path/*)
      name="${dep#@*/}"
      [[ -e "$APP/Contents/Frameworks/$name" ]] || die "linked library $dep is not embedded in the app" ;;
    *) die "app links a non-system library outside the bundle: $dep" ;;
  esac
done < <(otool -L "$BIN" | grep -v ':$' | awk '{print $1}' | sort -u)  # skip per-arch header lines
codesign --verify --deep --strict "$APP" || die "codesign verification failed"
echo "universal ($ARCHS_FOUND), version $VERSION ($BUILD_NUMBER), only system or embedded libraries"

log "Packaging Snapper-macOS.zip and Snapper-macOS.dmg"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ASSETS/Snapper-macOS.zip"
DMG_SRC="$OUT/dmg"
mkdir -p "$DMG_SRC"
ditto "$APP" "$DMG_SRC/Snapper.app"
ln -s /Applications "$DMG_SRC/Applications"
hdiutil create -quiet -volname "Snapper $VERSION" -srcfolder "$DMG_SRC" -fs HFS+ -format UDZO \
  -ov "$ASSETS/Snapper-macOS.dmg"
hdiutil verify -quiet "$ASSETS/Snapper-macOS.dmg" || die "dmg verification failed"

# --- Windows publish ---------------------------------------------------------------------
for RID in win-x64 win-arm64; do
  log "Publishing self-contained single-file $RID"
  PUB="windows/artifacts/publish/$RID"
  rm -rf "$PUB"
  dotnet publish windows/src/Snapper.Windows -p:PublishProfile="$RID" -p:Version="$VERSION" \
    > "$OUT/publish-$RID.log" 2>&1 || { tail -50 "$OUT/publish-$RID.log"; die "$RID publish failed"; }
  EXE="$PUB/Snapper.exe"
  [[ -f "$EXE" ]] || die "missing $EXE"
  # Single-file: the exe must be the only shipped file (no loose DLLs or runtime folders).
  EXTRA="$(cd "$PUB" && find . -type f ! -name Snapper.exe ! -name '*.pdb' | head -5)"
  [[ -z "$EXTRA" ]] || die "$RID publish is not single-file; extra files: $EXTRA"
  # Self-contained bundles carry the .NET runtime, which makes them tens of MB.
  SIZE="$(stat -f %z "$EXE")"
  (( SIZE > 40000000 )) || die "$RID exe is only $SIZE bytes; it doesn't look self-contained"
  cp "$EXE" "$ASSETS/Snapper-$RID.exe"
  echo "$RID: $((SIZE / 1000000)) MB single-file exe"
done

# --- Checksums ---------------------------------------------------------------------------
(cd "$ASSETS" && shasum -a 256 Snapper-macOS.dmg Snapper-macOS.zip Snapper-win-x64.exe Snapper-win-arm64.exe > SHA256SUMS.txt)
cat "$ASSETS/SHA256SUMS.txt"

FINAL_NOTES="$OUT/notes.md"
{
  cat "$NOTES"
  echo
  echo "## Validation"
  echo "- macOS: universal (arm64 + x86_64) Release build. ${MAC_TESTS:-Unit tests passed.}"
  echo "- Windows: self-contained single-file x64 and Arm64 publishes. ${WIN_TESTS:-Unit tests passed.}"
  echo
  echo "Built from commit $COMMIT. SHA-256 checksums are attached."
} > "$FINAL_NOTES"

if [[ $DRY_RUN == 1 ]]; then
  log "Dry run complete. Nothing was committed, tagged, pushed, or released."
  echo "Assets: $ASSETS"; echo "Notes:  $FINAL_NOTES"
  exit 0
fi

# --- Publish -----------------------------------------------------------------------------
log "Pushing main and tag $TAG"
git push --quiet origin main
git tag -a "$TAG" -m "Snapper $VERSION" "$COMMIT"
git push --quiet origin "$TAG"

log "Creating GitHub release $TAG"
gh release create "$TAG" --repo "$REPO" --verify-tag --latest \
  --title "Snapper $VERSION" --notes-file "$FINAL_NOTES" \
  "$ASSETS/Snapper-macOS.dmg" "$ASSETS/Snapper-macOS.zip" \
  "$ASSETS/Snapper-win-x64.exe" "$ASSETS/Snapper-win-arm64.exe" "$ASSETS/SHA256SUMS.txt"
gh release view "$TAG" --repo "$REPO" --json url,assets --jq '.url, (.assets[] | "  " + .name)'
