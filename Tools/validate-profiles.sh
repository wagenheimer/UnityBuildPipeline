#!/usr/bin/env bash
# Validates the build profile matrix without building. Exit code 1 when the validator finds an error.
# Usage: UNITY_BIN=/path/Unity ./Tools/validate-profiles.sh /path/to/project
set -euo pipefail

UNITY_BIN="${UNITY_BIN:?set UNITY_BIN to the Unity executable}"
PROJECT="${1:-.}"

"$UNITY_BIN" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.ValidateProfiles \
  -logFile -
