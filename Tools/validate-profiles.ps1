# Validates the build profile matrix without building. Exit code 1 when the validator finds an error.
# Usage: .\Tools\validate-profiles.ps1 -Project C:\path\to\project -Unity C:\path\Unity.exe
param(
    [string]$Project = ".",
    [string]$Unity = $env:UNITY_BIN
)

if ([string]::IsNullOrWhiteSpace($Unity)) {
    throw "Set UNITY_BIN or pass -Unity <path to Unity.exe>."
}

& $Unity -batchmode -nographics -quit -projectPath $Project `
    -executeMethod Wagenheimer.BuildPipeline.Editor.BuildCLI.ValidateProfiles `
    -logFile -

exit $LASTEXITCODE
