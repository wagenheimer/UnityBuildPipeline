<#
.SYNOPSIS
    Migrates a game that still uses its own GameConfig.cs / GameConfigEditor.cs (the pre-package build setup)
    to com.wagenheimer.buildpipeline. Run it with Unity CLOSED, before the package is installed.

.DESCRIPTION
    Old games declare GameConfig, Publisher, GameLanguage and GameVersion in the global namespace — the same types
    the package ships. With both present the game code does not compile and Unity never loads the package (so the
    in-editor migration assistant cannot run). This script does the part that must happen outside Unity:

      1. Finds the legacy GameConfig.cs and the GameConfigEditor*.cs scripts.
      2. Compares the legacy GameConfig fields with the package's. Fields the package does not have
         (MacAppStoreID, FreeToPlay, CanChangeLanguage...) go to a generated subclass "<Game>Config : GameConfig",
         so their serialized values survive.
      3. Points every GameConfig asset (m_Script) to that subclass, or to the package's GameConfig.
      4. Backs up and removes the legacy scripts (temp_migration_backup/<timestamp>, gitignored).
      5. Adds com.wagenheimer.buildpipeline and com.wagenheimer.packagehub to Packages/manifest.json.
      6. Lists the game code that still needs a manual touch (members/enum values the package does not have,
         VersionDate now typed GameBuildDate, references to the removed editor).
      7. Writes ProjectSettings/BuildPipelineLegacyMigration.json — the in-editor assistant reads it on the next
         Unity open and shows the remaining checklist.

    Default is a dry run: nothing is written. Use -Apply to change files.

.EXAMPLE
    ./migrate-legacy-project.ps1 -ProjectPath "K:\Games\Green Sauce Games\StormTale"
    ./migrate-legacy-project.ps1 -ProjectPath "K:\Games\Green Sauce Games\StormTale" -Apply
#>
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [switch]$Apply,
    [string]$PipelineRef = "v2.0.2",
    [string]$PackageHubRef = "v1.3.20",
    # Name of the generated subclass. Empty = derived from the project folder (StormTale -> StormTaleConfig).
    [string]$ConfigClassName = "",
    # Package source used to read the package's GameConfig fields (falls back to a built-in list).
    [string]$PackagePath = ""
)

$ErrorActionPreference = "Stop"
$PackageGameConfigGuid = "31cdb8337559cda4c91e423a77f466e1"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Fields of the package GameConfig (2.0). Used when the package source is not next to this script.
$BuiltInPackageFields = @(
    "HudSpriteAtlas", "LevelHudSpriteAtlas", "DefaultSpriteAtlas", "cursorTexture", "BuildFolderName", "Publisher",
    "GameLanguage", "CheatMode", "FullGame", "Demo", "UseAchievements", "NoCustomCursor", "LogLevelsInfo", "LevelEditor",
    "ExternalTranslation", "UseOnlyEditorPlayer", "GameVersion", "VersionDate", "AndroidBundleVersionCode",
    "iOSBuildNumber", "IconFree", "IconFull", "DefaultBundleIdentifier", "iOSGameName", "GameNameIOSAndroid", "GameName",
    "GameNameJapanese", "GameNameWSA", "AndroidFull", "AndroidFree", "IOSFull", "IOSFree", "iOSAppIDFree", "iOSAppIDFull",
    "AmazonFull", "AmazonFree", "SamsungFull", "SamsungFree", "buildVariants"
)
$BuiltInPackageMembers = @(
    "PublisherIsGoogleAndroid", "PublisherIsiOS", "PublisherIsAmazonAndroid", "PublisherIsSamsungAndroid",
    "PublisherIsStandalone", "CanRate", "HasBuildVariants", "FindBuildVariant", "GetDefaultBuildVariants",
    "ApplyBuildVariant", "GetBuildVariantGameName", "IsLanguageLocked"
)

function Write-Section([string]$title) { Write-Host ""; Write-Host "== $title" -ForegroundColor Cyan }
function Rel([string]$path) { return $path.Substring($ProjectPath.Length).TrimStart('\', '/').Replace('\', '/') }

function Read-Text([string]$path) { return [System.IO.File]::ReadAllText($path) }
function Write-Text([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, $Utf8NoBom) }

function Get-ClassBody([string]$text, [string]$className) {
    $m = [regex]::Match($text, "\bclass\s+$className\b[^{]*\{")
    if (-not $m.Success) { return $null }
    $depth = 1; $i = $m.Index + $m.Length
    while ($i -lt $text.Length -and $depth -gt 0) {
        if ($text[$i] -eq '{') { $depth++ } elseif ($text[$i] -eq '}') { $depth-- }
        $i++
    }
    return $text.Substring($m.Index + $m.Length, $i - ($m.Index + $m.Length) - 1)
}

# Top-level declarations of a class body: fields (with their attribute lines and the #if conditions around them)
# and other public members.
function Get-Members([string]$body) {
    $fields = @(); $others = @()
    $depth = 0; $pendingAttributes = @()
    $conditions = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($body -split "`r?`n")) {
        $trimmed = $line.Trim()

        # Preprocessor blocks (e.g. '#if UNITY_EDITOR' around editor-only fields) are kept per field.
        $directive = [regex]::Match($trimmed, '^#\s*(if|elif|else|endif)\b(.*)$')
        if ($directive.Success) {
            $kind = $directive.Groups[1].Value; $expr = $directive.Groups[2].Value.Trim() -replace '//.*$', ''
            switch ($kind) {
                "if" { $conditions.Add("($expr)") }
                "elif" { if ($conditions.Count -gt 0) { $conditions[$conditions.Count - 1] = "!$($conditions[$conditions.Count - 1]) && ($expr)" } }
                "else" { if ($conditions.Count -gt 0) { $conditions[$conditions.Count - 1] = "!$($conditions[$conditions.Count - 1])" } }
                "endif" { if ($conditions.Count -gt 0) { $conditions.RemoveAt($conditions.Count - 1) } }
            }
            continue
        }

        $startDepth = $depth
        $depth += ([regex]::Matches($line, '\{')).Count - ([regex]::Matches($line, '\}')).Count
        if ($startDepth -ne 0) { continue }

        if ($trimmed.StartsWith("[") -and $trimmed.EndsWith("]")) { $pendingAttributes += $line; continue }
        if ($trimmed -eq "" -or $trimmed.StartsWith("//")) { if ($trimmed -eq "") { $pendingAttributes = @() }; continue }

        $field = [regex]::Match($trimmed, '^public\s+(?!static\b|const\b|override\b|virtual\b|abstract\b|class\b|enum\b)([\w<>\[\],\.\s]+?)\s+(\w+)\s*(=[^;]*)?;')
        if ($field.Success -and -not $trimmed.Contains("=>")) {
            $fields += [pscustomobject]@{
                Name = $field.Groups[2].Value; Type = $field.Groups[1].Value.Trim(); Lines = @($pendingAttributes + $line)
                Condition = ($conditions -join " && ")
            }
        }
        else {
            $member = [regex]::Match($trimmed, '^public\s+(?:static\s+|virtual\s+|override\s+)*[\w<>\[\],\.\?]+\s+(\w+)\s*(\(|=>|\{)')
            if ($member.Success) { $others += $member.Groups[1].Value }
        }
        $pendingAttributes = @()
    }
    return [pscustomobject]@{ Fields = $fields; Others = $others }
}

function Get-EnumNames([string]$text, [string]$enumName) {
    $m = [regex]::Match($text, "enum\s+$enumName\s*\{([^}]*)\}")
    if (-not $m.Success) { return @() }
    $names = @()
    foreach ($part in ($m.Groups[1].Value -split ',')) {
        $clean = ($part -replace '//[^\r\n]*', '').Trim()
        $n = [regex]::Match($clean, '^(\w+)')
        if ($n.Success) { $names += $n.Groups[1].Value }
    }
    return $names
}

function New-Guid32 { return ([guid]::NewGuid().ToString("N")) }

# Types the package already ships in the global namespace (the legacy copies are dropped).
$PackageTypes = @("GameConfig", "Publisher", "GameLanguage", "GameVersion", "GameBuildDate", "BuildVariant",
    "PublisherAndLanguageExternsions", "PublisherAndLanguageExtensions", "PublisherCatalog")

# Top-level type declarations of a file, each with its attribute lines and full source text.
function Get-TopLevelTypes([string]$text) {
    $result = @()
    $pattern = '(?m)^(?<attrs>(?:[ \t]*\[[^\r\n]*\][ \t]*\r?\n)*)[ \t]*(?:public\s+|internal\s+)?(?:static\s+|sealed\s+|abstract\s+|partial\s+)*(?<kind>class|enum|struct)\s+(?<name>\w+)[^{;]*\{'
    foreach ($m in [regex]::Matches($text, $pattern)) {
        # Only depth-0 declarations (nested types are part of their parent's block).
        $before = $text.Substring(0, $m.Index)
        $depth = ([regex]::Matches($before, '\{')).Count - ([regex]::Matches($before, '\}')).Count
        if ($depth -ne 0) { continue }

        $depth = 1; $i = $m.Index + $m.Length
        while ($i -lt $text.Length -and $depth -gt 0) {
            if ($text[$i] -eq '{') { $depth++ } elseif ($text[$i] -eq '}') { $depth-- }
            $i++
        }
        $result += [pscustomobject]@{ Name = $m.Groups["name"].Value; Kind = $m.Groups["kind"].Value; Text = $text.Substring($m.Index, $i - $m.Index).TrimStart("`r", "`n") }
    }
    return $result
}

# 'using' lines (and the #if blocks around them) before the first type declaration.
function Get-Header([string]$text) {
    $first = [regex]::Match($text, '(?m)^[ \t]*(?:\[[^\r\n]*\][ \t]*\r?\n[ \t]*)*(?:public\s+|internal\s+)?(?:static\s+|sealed\s+)*(?:class|enum|struct)\s+\w+')
    if (-not $first.Success) { return "" }
    $lines = ($text.Substring(0, $first.Index).TrimStart([char]0xFEFF) -split "`r?`n") | Where-Object { $_.Trim() -match '^(using\s|#)' }
    return ($lines -join "`n")
}

# ───────────────────────────────────────────────────────────────── discovery
$ProjectPath = (Resolve-Path $ProjectPath).Path.TrimEnd('\')
$assets = Join-Path $ProjectPath "Assets"
if (-not (Test-Path $assets)) { throw "Não é um projeto Unity (sem pasta Assets): $ProjectPath" }

$mode = "DRY-RUN (nada será alterado; use -Apply)"
if ($Apply) { $mode = "APLICANDO" }
Write-Host "Projeto: $ProjectPath" -ForegroundColor White
Write-Host "Modo   : $mode" -ForegroundColor Yellow

$manifestPath = Join-Path $ProjectPath "Packages/manifest.json"
$manifest = ""
if (Test-Path $manifestPath) { $manifest = Read-Text $manifestPath }
$hasPipeline = $manifest -match '"com\.wagenheimer\.buildpipeline"'

$csFiles = Get-ChildItem -Path $assets -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\(Plugins|ThirdParty)\\' }

$legacyConfig = $csFiles | Where-Object { (Read-Text $_.FullName) -match '\bclass\s+GameConfig\s*:\s*ScriptableObject\b' } | Select-Object -First 1
if ($null -eq $legacyConfig) {
    if ($hasPipeline) { Write-Host "Nada a migrar: o projeto já usa o pacote e não tem GameConfig próprio." -ForegroundColor Green }
    else { Write-Host "Nenhum GameConfig.cs antigo encontrado. Instale o pacote e use o assistente dentro do Unity." -ForegroundColor Green }
    exit 0
}

$legacyText = Read-Text $legacyConfig.FullName
$legacyMeta = "$($legacyConfig.FullName).meta"
$legacyGuid = ""
if (Test-Path $legacyMeta) { $legacyGuid = ([regex]::Match((Read-Text $legacyMeta), 'guid:\s*([0-9a-f]{32})')).Groups[1].Value }

$editorScripts = $csFiles | Where-Object {
    $_.FullName -match '\\Editor\\' -and $_.Name -match '^GameConfigEditor.*\.cs$'
}
$editorScripts = @($editorScripts) + @($csFiles | Where-Object {
    $_.FullName -match '\\Editor\\' -and $_.Name -notmatch '^GameConfigEditor' -and (Read-Text $_.FullName) -match 'CustomEditor\(\s*typeof\(\s*GameConfig\s*\)'
})
$editorScripts = @($editorScripts | Sort-Object FullName -Unique)

# Package GameConfig members
$pkgFields = $BuiltInPackageFields; $pkgMembers = $BuiltInPackageMembers; $pkgPublishers = @()
if ([string]::IsNullOrEmpty($PackagePath)) { $PackagePath = Split-Path -Parent $PSScriptRoot }
$pkgConfigFile = Join-Path $PackagePath "Runtime/GameConfig.cs"
if (Test-Path $pkgConfigFile) {
    $pkgParsed = Get-Members (Get-ClassBody (Read-Text $pkgConfigFile) "GameConfig")
    $pkgFields = @($pkgParsed.Fields | ForEach-Object { $_.Name })
    $pkgMembers = @($pkgParsed.Others)
}
$pkgPublisherFile = Join-Path $PackagePath "Runtime/Publisher.cs"
if (Test-Path $pkgPublisherFile) { $pkgPublishers = Get-EnumNames (Read-Text $pkgPublisherFile) "Publisher" }

$legacyParsed = Get-Members (Get-ClassBody $legacyText "GameConfig")
$extraFields = @($legacyParsed.Fields | Where-Object { $pkgFields -notcontains $_.Name })
$extraMembers = @($legacyParsed.Others | Where-Object { ($pkgMembers -notcontains $_) -and ($pkgFields -notcontains $_) } | Sort-Object -Unique)
$legacyPublishers = Get-EnumNames $legacyText "Publisher"
$missingPublishers = @()
if ($pkgPublishers.Count -gt 0) { $missingPublishers = @($legacyPublishers | Where-Object { $pkgPublishers -notcontains $_ }) }

# Assets that use the legacy GameConfig script
$configAssets = @()
if ($legacyGuid -ne "") {
    $configAssets = @(Get-ChildItem -Path $assets -Recurse -Filter *.asset -File | Where-Object {
        (Select-String -Path $_.FullName -Pattern "m_Script: {fileID: 11500000, guid: $legacyGuid" -SimpleMatch -Quiet)
    })
}

if ([string]::IsNullOrEmpty($ConfigClassName)) {
    $ConfigClassName = (([regex]::Replace((Split-Path -Leaf $ProjectPath), '[^A-Za-z0-9]', '')) + "Config")
    if ($ConfigClassName -match '^\d') { $ConfigClassName = "Game$ConfigClassName" }
}
$subclassPath = Join-Path (Split-Path -Parent $legacyConfig.FullName) "$ConfigClassName.cs"

# Other types declared in the legacy GameConfig.cs that the package does not ship (e.g. a GameDate class) are kept.
$keptTypes = @(Get-TopLevelTypes $legacyText | Where-Object { $PackageTypes -notcontains $_.Name })
$legacyHeader = Get-Header $legacyText
$needsGeneratedFile = ($extraFields.Count -gt 0) -or ($keptTypes.Count -gt 0)

# ───────────────────────────────────────────────────────────────── report
Write-Section "Formato antigo encontrado"
Write-Host "  GameConfig.cs     : $(Rel $legacyConfig.FullName) (GUID $legacyGuid)"
foreach ($e in $editorScripts) { Write-Host "  Editor antigo     : $(Rel $e.FullName)" }
foreach ($a in $configAssets) { Write-Host "  Asset GameConfig  : $(Rel $a.FullName)" }
if ($configAssets.Count -eq 0) { Write-Host "  ⚠ Nenhum asset usa o script GameConfig antigo (GUID não encontrado)." -ForegroundColor Yellow }
if ($hasPipeline) { Write-Host "  Pacote            : já está no manifest.json" } else { Write-Host "  Pacote            : será adicionado (#$PipelineRef)" }

Write-Section "Campos que só existem no GameConfig do jogo"
if ($extraFields.Count -eq 0) {
    Write-Host "  Nenhum: o asset passa a usar o GameConfig do pacote diretamente."
}
else {
    foreach ($f in $extraFields) {
        $cond = ""
        if ($f.Condition -ne "") { $cond = "   (#if $($f.Condition))" }
        Write-Host "  + $($f.Type) $($f.Name)$cond"
    }
    Write-Host "  → vão para a subclasse $ConfigClassName : GameConfig ($(Rel $subclassPath)); os valores do asset são preservados." -ForegroundColor Green
}
if ($keptTypes.Count -gt 0) {
    Write-Host "  Tipos do arquivo antigo que o pacote não tem (mantidos no arquivo gerado): $(($keptTypes | ForEach-Object { "$($_.Kind) $($_.Name)" }) -join ', ')" -ForegroundColor Green
}

$scanFiles = @($csFiles | Where-Object { $_.FullName -ne $legacyConfig.FullName -and ($editorScripts | ForEach-Object { $_.FullName }) -notcontains $_.FullName })
$manual = New-Object System.Collections.Generic.List[string]
$automatic = New-Object System.Collections.Generic.List[string]

function Add-Usages([string]$pattern, [string]$why, [System.Collections.Generic.List[string]]$list = $manual) {
    foreach ($file in $scanFiles) {
        $hits = Select-String -Path $file.FullName -Pattern $pattern
        foreach ($hit in $hits) { $list.Add("$(Rel $file.FullName):$($hit.LineNumber)  [$why]  $($hit.Line.Trim())") }
    }
}

if ($extraFields.Count -gt 0) {
    $names = ($extraFields | ForEach-Object { [regex]::Escape($_.Name) }) -join '|'
    Add-Usages '(?m)^\s*(\[[^\]]*\]\s*)*((public|private|protected|internal|static|readonly)\s+)+GameConfig\s+\w+\s*(;|=)' "declaração GameConfig → $ConfigClassName (troca automática no -Apply)" $automatic
    Add-Usages "\.($names)\b" "usa campo movido para ${ConfigClassName}: compila se a variável for uma das declarações trocadas" $automatic
    Add-Usages '\(\s*GameConfig\s+\w+\s*[,)]|<GameConfig>|as GameConfig\b' "parâmetro/genérico GameConfig: se acessar campos do jogo, troque por $ConfigClassName"
}
foreach ($member in $extraMembers) { Add-Usages "\.$member\b" "membro '$member' não existe no GameConfig do pacote" }
foreach ($publisher in $missingPublishers) { Add-Usages "Publisher\.$publisher\b" "Publisher.$publisher não existe no pacote (use variantes de build ou um perfil)" }
Add-Usages 'GameDate[^\r\n]*VersionDate|VersionDate[^\r\n]*GameDate' "VersionDate agora é GameBuildDate (Day/Month/Year iguais; use new GameDate(...) se precisar do tipo do jogo)"
Add-Usages '\bGameConfigEditor\w*\b' "referência ao editor antigo removido"

Write-Section "Ajustes automáticos no código ($($automatic.Count))"
if ($automatic.Count -eq 0) { Write-Host "  Nenhum." } else { $automatic | ForEach-Object { Write-Host "  $_" } }

Write-Section "Revisar à mão depois ($($manual.Count))"
if ($manual.Count -eq 0) { Write-Host "  Nenhum." } else { $manual | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow } }
if ($missingPublishers.Count -gt 0) { Write-Host "  ⚠ Publishers só do jogo: $($missingPublishers -join ', ') (o valor numérico no asset é mantido)." -ForegroundColor Yellow }

$guidAction = "nenhuma (o GameConfig do jogo já usa o GUID do pacote)"
if ($extraFields.Count -gt 0) { $guidAction = "assets passam a usar o script $ConfigClassName" }
elseif ($legacyGuid -ne $PackageGameConfigGuid) { $guidAction = "assets passam do GUID $legacyGuid para o GameConfig do pacote ($PackageGameConfigGuid)" }
Write-Section "Ajuste de script dos assets"
Write-Host "  $guidAction"

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry-run: nada foi alterado. Rode de novo com -Apply (com o Unity FECHADO) para migrar." -ForegroundColor Yellow
    exit 0
}

# ───────────────────────────────────────────────────────────────── apply
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $ProjectPath "temp_migration_backup/$stamp"
New-Item -ItemType Directory -Force -Path $backup | Out-Null

function Backup-And-Remove([string]$path) {
    foreach ($p in @($path, "$path.meta")) {
        if (-not (Test-Path $p)) { continue }
        $target = Join-Path $backup (Rel $p)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $p -Destination $target -Force
        Remove-Item -LiteralPath $p -Force
    }
}

$targetGuid = $PackageGameConfigGuid
if ($needsGeneratedFile) {
    $generatedPath = $subclassPath
    if ($extraFields.Count -eq 0) { $generatedPath = Join-Path (Split-Path -Parent $legacyConfig.FullName) "$($ConfigClassName -replace 'Config$', '')LegacyTypes.cs" }
    $fileGuid = New-Guid32

    $body = New-Object System.Text.StringBuilder
    [void]$body.AppendLine("// Gerado por migrate-legacy-project.ps1 em $stamp a partir do GameConfig.cs antigo")
    [void]$body.AppendLine("// (backup em temp_migration_backup/). O GameConfig, os enums Publisher/GameLanguage e o GameVersion")
    [void]$body.AppendLine("// agora vêm do pacote com.wagenheimer.buildpipeline; aqui fica só o que é deste jogo.")
    if ($legacyHeader -ne "") { [void]$body.AppendLine($legacyHeader) }
    if ($legacyHeader -notmatch 'using UnityEngine;') { [void]$body.AppendLine("using UnityEngine;") }
    [void]$body.AppendLine("")

    foreach ($t in $keptTypes) {
        [void]$body.AppendLine($t.Text.TrimEnd())
        [void]$body.AppendLine("")
    }

    if ($extraFields.Count -gt 0) {
        $targetGuid = $fileGuid
        [void]$body.AppendLine("/// <summary>Campos deste jogo que o GameConfig do pacote não tem (os valores do asset foram preservados).</summary>")
        [void]$body.AppendLine("[CreateAssetMenu(fileName = `"GameConfig`", menuName = `"Game Configuration`", order = 1)]")
        [void]$body.AppendLine("public class $ConfigClassName : GameConfig")
        [void]$body.AppendLine("{")
        [void]$body.AppendLine("    [Header(`"Campos deste jogo (migrados do GameConfig antigo)`")]")
        foreach ($f in $extraFields) {
            if ($f.Condition -ne "") { [void]$body.AppendLine("#if $($f.Condition)") }
            foreach ($l in $f.Lines) {
                if ($l.Trim() -match '^\[Header\(') { continue }
                [void]$body.AppendLine("    " + $l.Trim())
            }
            if ($f.Condition -ne "") { [void]$body.AppendLine("#endif") }
        }
        [void]$body.AppendLine("}")
    }

    Write-Text $generatedPath $body.ToString()
    Write-Text "$generatedPath.meta" ("fileFormatVersion: 2`nguid: $fileGuid`nMonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n")
    Write-Host "Criado $(Rel $generatedPath)" -ForegroundColor Green
}

# Fields declared as 'GameConfig X;' in the game code become the subclass, so Config.MacAppStoreID keeps compiling.
$retyped = @()
if ($extraFields.Count -gt 0) {
    foreach ($file in $scanFiles) {
        $text = Read-Text $file.FullName
        $updated = [regex]::Replace($text, '(?m)^(?<lead>[ \t]*(?:\[[^\]\r\n]*\][ \t]*)*(?:(?:public|private|protected|internal|static|readonly)\s+)+)GameConfig(?<rest>\s+\w+\s*(?:;|=))', "`${lead}$ConfigClassName`${rest}")
        if ($updated -ne $text) {
            $target = Join-Path $backup (Rel $file.FullName)
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            Write-Text $file.FullName $updated
            $retyped += (Rel $file.FullName)
        }
    }
    foreach ($r in $retyped) { Write-Host "Declaração GameConfig → $ConfigClassName em $r" -ForegroundColor Green }
}

foreach ($a in $configAssets) {
    $text = Read-Text $a.FullName
    if ($legacyGuid -ne $targetGuid) {
        Copy-Item -LiteralPath $a.FullName -Destination (New-Item -ItemType Directory -Force -Path (Split-Path -Parent (Join-Path $backup (Rel $a.FullName)))).FullName -Force
        Write-Text $a.FullName ($text.Replace("guid: $legacyGuid", "guid: $targetGuid"))
        Write-Host "Asset $(Rel $a.FullName) → script $targetGuid" -ForegroundColor Green
    }
}

Backup-And-Remove $legacyConfig.FullName
foreach ($e in $editorScripts) { Backup-And-Remove $e.FullName }
Write-Host "Removidos (com backup em $(Rel $backup)): GameConfig.cs e $($editorScripts.Count) script(s) de editor." -ForegroundColor Green

if (-not $hasPipeline -and (Test-Path $manifestPath)) {
    $insert = "    `"com.wagenheimer.buildpipeline`": `"https://github.com/wagenheimer/UnityBuildPipeline.git#$PipelineRef`","
    if ($manifest -notmatch '"com\.wagenheimer\.packagehub"') {
        $insert += "`n    `"com.wagenheimer.packagehub`": `"https://github.com/wagenheimer/UnityPackageHub.git#$PackageHubRef`","
    }
    $newManifest = [regex]::Replace($manifest, '("dependencies"\s*:\s*\{\s*\r?\n)', "`$1$insert`n", 1)
    Write-Text $manifestPath $newManifest
    Write-Host "manifest.json: pacote adicionado (#$PipelineRef)." -ForegroundColor Green
}

$gitignore = Join-Path $ProjectPath ".gitignore"
if ((Test-Path $gitignore) -and -not ((Read-Text $gitignore) -match 'temp_migration_backup')) {
    Add-Content -Path $gitignore -Value "`ntemp_migration_backup/" -Encoding UTF8
}

$record = [ordered]@{
    schema            = 1
    migratedAt        = (Get-Date).ToString("o")
    status            = "pending-unity"
    configClass       = $(if ($extraFields.Count -gt 0) { $ConfigClassName } else { "GameConfig" })
    movedFields       = @($extraFields | ForEach-Object { $_.Name })
    keptTypes         = @($keptTypes | ForEach-Object { $_.Name })
    retypedFiles      = @($retyped)
    removedScripts    = @(@($legacyConfig) + $editorScripts | ForEach-Object { Rel $_.FullName })
    configAssets      = @($configAssets | ForEach-Object { Rel $_.FullName })
    missingPublishers = @($missingPublishers)
    manualReview      = @($manual)
    backup            = (Rel $backup)
}
$recordPath = Join-Path $ProjectPath "ProjectSettings/BuildPipelineLegacyMigration.json"
Write-Text $recordPath ($record | ConvertTo-Json -Depth 4)
Write-Text (Join-Path $backup "MIGRATION_REPORT.json") ($record | ConvertTo-Json -Depth 4)

Write-Host ""
Write-Host "Pronto. Abra o projeto no Unity: o assistente do Build Pipeline cria o ProjectBuildConfig e mostra o que falta." -ForegroundColor Cyan
if ($manual.Count -gt 0) { Write-Host "Antes, corrija os $($manual.Count) ponto(s) do código listados acima (senão o projeto não compila)." -ForegroundColor Yellow }
