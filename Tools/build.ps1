<#
.SYNOPSIS
    Automated Headless Build Runner for Unity Build Pipeline.
.DESCRIPTION
    Runs Unity CLI in batchmode to compile single targets or publisher/language matrices.
.EXAMPLE
    ./build.ps1 -projectPath "k:\Games\Green Sauce Games\Storm-Tale2" -publisher "BigFish" -language "en"
    ./build.ps1 -projectPath "k:\Games\Green Sauce Games\Storm-Tale2" -publisher "GoogleAndroidFree" -development -autoRun
    ./build.ps1 -projectPath "k:\Games\Green Sauce Games\Storm-Tale2" -matrix -matrixPublishers "BigFish,Steam" -matrixLanguages "en,de"
#>

param (
    [string]$projectPath = ".",
    [string]$publisher = "Default",
    [string]$language = "AutoDetect",
    [string]$platform = "Windows64",
    [switch]$matrix,
    [string]$matrixPublishers = "",
    [string]$matrixLanguages = "",
    [switch]$cheat,
    [switch]$development,
    [switch]$demo,
    [switch]$autoRun,
    [string]$vaultUrl = "",
    [string]$vaultProfile = "",
    [string]$vaultToken = "",
    [string]$keystoreSource = "",
    [string]$version = "",
    [string]$outputPath = "",
    [string]$logFile = "build.log",
    [string]$unityExe = ""
)

$ErrorActionPreference = "Stop"
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$resolvedProject = (Resolve-Path $projectPath).Path
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   Unity Build Pipeline - CLI Headless Runner   " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "Project: $resolvedProject"

# Detect Unity Editor path if not provided
if ([string]::IsNullOrEmpty($unityExe)) {
    $versionFile = Join-Path $resolvedProject "ProjectSettings\ProjectVersion.txt"
    if (Test-Path $versionFile) {
        $content = Get-Content $versionFile -Raw
        if ($content -match 'm_EditorVersion:\s*([^\s\r\n]+)') {
            $editorVer = $matches[1]
            $candidate = "C:\Program Files\Unity\Hub\Editor\$editorVer\Editor\Unity.exe"
            if (Test-Path $candidate) {
                $unityExe = $candidate
            }
        }
    }
}

if ([string]::IsNullOrEmpty($unityExe) -or !(Test-Path $unityExe)) {
    # Try finding any Unity in Hub
    $hubEditors = Get-ChildItem "C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe" -ErrorAction SilentlyContinue
    if ($hubEditors.Count -gt 0) {
        $unityExe = $hubEditors[-1].FullName
    } else {
        Write-Error "Could not locate Unity.exe. Please pass -unityExe <path>."
        exit 1
    }
}

Write-Host "Unity:   $unityExe" -ForegroundColor Gray

# Build CLI arguments
$argsList = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", "`"$resolvedProject`"",
    "-logFile", "`"$logFile`""
)

if ($matrix) {
    $argsList += "-executeMethod"
    $argsList += "Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildMatrix"
    if (![string]::IsNullOrEmpty($matrixPublishers)) {
        $argsList += "-matrixPublishers"
        $argsList += "`"$matrixPublishers`""
    }
    if (![string]::IsNullOrEmpty($matrixLanguages)) {
        $argsList += "-matrixLanguages"
        $argsList += "`"$matrixLanguages`""
    }
    $argsList += "-cheatOff"
    $argsList += "$(!$cheat)"
    $argsList += "-cheatOn"
    $argsList += "$([bool]$cheat)"
} else {
    $argsList += "-executeMethod"
    $argsList += "Wagenheimer.BuildPipeline.Editor.BuildCLI.Build"
    $argsList += "-publisher"
    $argsList += "$publisher"
    $argsList += "-language"
    $argsList += "$language"
    $argsList += "-platform"
    $argsList += "$platform"
    $argsList += "-cheat"
    $argsList += "$([bool]$cheat)"
    $argsList += "-development"
    $argsList += "$([bool]$development)"
    $argsList += "-demo"
    $argsList += "$([bool]$demo)"

    if ($autoRun) {
        $argsList += "-autoRun"
        $argsList += "true"
    }

    if (![string]::IsNullOrEmpty($outputPath)) {
        $argsList += "-outputPath"
        $argsList += "`"$outputPath`""
    }
}

if (![string]::IsNullOrEmpty($version)) {
    $argsList += "-version"
    $argsList += "$version"
}

if (![string]::IsNullOrEmpty($vaultUrl)) {
    $argsList += "-vaultUrl"
    $argsList += "`"$vaultUrl`""
}

if (![string]::IsNullOrEmpty($vaultProfile)) {
    $argsList += "-vaultProfile"
    $argsList += "$vaultProfile"
}

if (![string]::IsNullOrEmpty($vaultToken)) {
    $argsList += "-vaultToken"
    $argsList += "$vaultToken"
}

if (![string]::IsNullOrEmpty($keystoreSource)) {
    $argsList += "-keystoreSource"
    $argsList += "$keystoreSource"
}

Write-Host "Starting build execution..." -ForegroundColor Yellow
$proc = Start-Process -FilePath $unityExe -ArgumentList $argsList -PassThru -NoNewWindow -Wait

$stopwatch.Stop()
$elapsed = $stopwatch.Elapsed.ToString("mm\:ss")

Write-Host ""
if ($proc.ExitCode -eq 0) {
    Write-Host "==================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] Build completed in $elapsed!" -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Green
} else {
    Write-Host "==================================================" -ForegroundColor Red
    Write-Host " [FAILED] Unity exited with code $($proc.ExitCode) after $elapsed." -ForegroundColor Red
    Write-Host "Check log at: $logFile" -ForegroundColor Yellow
    Write-Host "==================================================" -ForegroundColor Red
}

exit $proc.ExitCode
