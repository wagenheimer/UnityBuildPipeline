#!/usr/bin/env bash
# Headless Unity Build Pipeline runner for macOS / Linux CI agents.
#
# Resolves the Unity Editor from ProjectSettings/ProjectVersion.txt (Unity Hub layout),
# then invokes BuildCLI.Build (or .BuildMatrix) in batchmode.
#
# Examples:
#   ./build.sh --project /Users/ci/work/Storm-Tale2 --build-profile stormtale-full-android --manifest out.json
#   ./build.sh --project . --build-profile stormtale-full-ios --defines "NO_ADS" --manifest out.json
#   ./build.sh --project . --matrix --matrix-profiles "st-free-android,st-full-android" --manifest out.json
#
set -euo pipefail

PROJECT_PATH="."
UNITY_PATH=""
BUILD_PROFILE=""
PUBLISHER=""
PLATFORM=""
LANGUAGE="AutoDetect"
DEFINES=""
VERSION=""
OUTPUT_PATH=""
MANIFEST=""
LOG_FILE="-"
APP_BUNDLE=""
DEVELOPMENT=""
MATRIX=0
MATRIX_PROFILES=""
MATRIX_PUBLISHERS=""
MATRIX_LANGUAGES=""
EXTRA_ARGS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project|--project-path)   PROJECT_PATH="$2"; shift 2;;
    --unity-path)               UNITY_PATH="$2"; shift 2;;
    --build-profile)            BUILD_PROFILE="$2"; shift 2;;
    --publisher)                PUBLISHER="$2"; shift 2;;
    --platform)                 PLATFORM="$2"; shift 2;;
    --language)                 LANGUAGE="$2"; shift 2;;
    --defines)                  DEFINES="$2"; shift 2;;
    --version)                  VERSION="$2"; shift 2;;
    --output-path)              OUTPUT_PATH="$2"; shift 2;;
    --manifest)                 MANIFEST="$2"; shift 2;;
    --log-file)                 LOG_FILE="$2"; shift 2;;
    --app-bundle)               APP_BUNDLE="$2"; shift 2;;
    --development)              DEVELOPMENT="true"; shift 1;;
    --matrix)                   MATRIX=1; shift 1;;
    --matrix-profiles)          MATRIX_PROFILES="$2"; shift 2;;
    --matrix-publishers)        MATRIX_PUBLISHERS="$2"; shift 2;;
    --matrix-languages)         MATRIX_LANGUAGES="$2"; shift 2;;
    --)                         shift; EXTRA_ARGS+=("$@"); break;;
    *)                          echo "Unknown arg: $1" >&2; exit 2;;
  esac
done

PROJECT_PATH="$(cd "$PROJECT_PATH" && pwd)"
echo "=================================================="
echo "   Unity Build Pipeline - CLI Headless Runner"
echo "=================================================="
echo "Project: $PROJECT_PATH"

# --- Resolve Unity Editor ---------------------------------------------------
if [[ -z "$UNITY_PATH" ]]; then
  VERSION_FILE="$PROJECT_PATH/ProjectSettings/ProjectVersion.txt"
  if [[ -f "$VERSION_FILE" ]]; then
    EDITOR_VER="$(grep -E '^m_EditorVersion:' "$VERSION_FILE" | head -n1 | sed -E 's/^m_EditorVersion:[[:space:]]*//' | tr -d '\r')"
    if [[ -n "$EDITOR_VER" ]]; then
      case "$(uname -s)" in
        Darwin)
          for c in \
            "/Applications/Unity/Hub/Editor/$EDITOR_VER/Unity.app/Contents/MacOS/Unity" \
            "$HOME/Applications/Unity/Hub/Editor/$EDITOR_VER/Unity.app/Contents/MacOS/Unity"; do
            [[ -x "$c" ]] && UNITY_PATH="$c" && break
          done
          ;;
        Linux)
          for c in \
            "$HOME/Unity/Hub/Editor/$EDITOR_VER/Editor/Unity" \
            "/opt/unity/editors/$EDITOR_VER/Editor/Unity"; do
            [[ -x "$c" ]] && UNITY_PATH="$c" && break
          done
          ;;
      esac
    fi
  fi
fi

if [[ -z "$UNITY_PATH" || ! -x "$UNITY_PATH" ]]; then
  echo "ERROR: Could not locate the Unity Editor. Pass --unity-path <path>." >&2
  exit 1
fi
echo "Unity:   $UNITY_PATH"

# --- Assemble CLI args ----------------------------------------------------------
ARGS=(-batchmode -nographics -quit -projectPath "$PROJECT_PATH" -logFile "$LOG_FILE")

if [[ "$MATRIX" -eq 1 ]]; then
  ARGS+=(-executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildMatrix)
  [[ -n "$MATRIX_PROFILES" ]]   && ARGS+=(-matrixProfiles "$MATRIX_PROFILES")
  [[ -n "$MATRIX_PUBLISHERS" ]] && ARGS+=(-matrixPublishers "$MATRIX_PUBLISHERS")
  [[ -n "$MATRIX_LANGUAGES" ]]  && ARGS+=(-matrixLanguages "$MATRIX_LANGUAGES")
else
  ARGS+=(-executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.Build)
  [[ -n "$BUILD_PROFILE" ]] && ARGS+=(-buildProfile "$BUILD_PROFILE")
  [[ -n "$PUBLISHER" ]]     && ARGS+=(-publisher "$PUBLISHER")
  [[ -n "$PLATFORM" ]]      && ARGS+=(-platform "$PLATFORM" -buildTarget "$PLATFORM")
  ARGS+=(-language "$LANGUAGE")
  [[ -n "$APP_BUNDLE" ]]    && ARGS+=(-appBundle "$APP_BUNDLE")
  [[ -n "$OUTPUT_PATH" ]]   && ARGS+=(-outputPath "$OUTPUT_PATH")
fi

[[ -n "$DEFINES" ]]     && ARGS+=(-defines "$DEFINES")
[[ -n "$VERSION" ]]     && ARGS+=(-version "$VERSION")
[[ -n "$MANIFEST" ]]    && ARGS+=(-manifest "$MANIFEST")
[[ -n "$DEVELOPMENT" ]] && ARGS+=(-development "true")
[[ ${#EXTRA_ARGS[@]} -gt 0 ]] && ARGS+=("${EXTRA_ARGS[@]}")

echo "Starting build execution..."
START=$(date +%s)
set +e
"$UNITY_PATH" "${ARGS[@]}"
CODE=$?
set -e
ELAPSED=$(( $(date +%s) - START ))

echo ""
if [[ "$CODE" -eq 0 ]]; then
  echo "[SUCCESS] Build completed in ${ELAPSED}s."
else
  echo "[FAILED] Unity exited with code $CODE after ${ELAPSED}s." >&2
fi
exit $CODE
