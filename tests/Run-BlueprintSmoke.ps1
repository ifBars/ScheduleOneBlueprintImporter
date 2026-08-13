[CmdletBinding()]
param(
    [ValidateSet("Mono", "Il2cpp")]
    [string]$Runtime = "Mono",

    [string]$MonoGamePath = "",

    [string]$Il2CppGamePath = "",

    [Parameter(Mandatory)]
    [string]$SourceSave,

    [Parameter(Mandatory)]
    [string]$S1ApiDllPath,

    [Parameter(Mandatory)]
    [string]$BlueprintId,

    [Parameter(Mandatory)]
    [string]$PropertyCode,

    [Parameter(Mandatory)]
    [string]$PropertyFileName,

    [string]$OutputRoot = "",

    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 210,

    [switch]$AllowLiveInstallMutation
)

$ErrorActionPreference = "Stop"
if (-not $AllowLiveInstallMutation) {
    throw "This runner temporarily replaces the selected live install's Mods directory. Re-run with -AllowLiveInstallMutation to acknowledge the reversible mutation."
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $projectRoot "artifacts\smoke"
}

$configuredGamePath = if ($Runtime -eq "Mono") { $MonoGamePath } else { $Il2CppGamePath }
if ([string]::IsNullOrWhiteSpace($configuredGamePath)) {
    throw "Pass -$($Runtime)GamePath with the selected Schedule I installation path."
}
$gamePath = [System.IO.Path]::GetFullPath($configuredGamePath)
$sourceSavePath = [System.IO.Path]::GetFullPath($SourceSave)
$gameExe = Join-Path $gamePath "Schedule I.exe"
$modsPath = Join-Path $gamePath "Mods"
$runId = "{0}-{1}" -f $Runtime.ToLowerInvariant(), (Get-Date -Format "yyyyMMdd-HHmmss")
$runDirectory = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot $runId))
$smokeSavePath = Join-Path $runDirectory "SaveGame_BlueprintSmoke"
$backupModsPath = Join-Path $gamePath ("Mods.BlueprintImporterBackup." + $runId)
$temporaryModsPath = Join-Path $gamePath ("Mods.BlueprintImporterTemporary." + $runId)
$buildPath = if ($Runtime -eq "Mono") {
    Join-Path $projectRoot "bin\Mono\netstandard2.1\ScheduleOneBlueprintImporter_Mono.dll"
} else {
    Join-Path $projectRoot "bin\Il2cpp\net6.0\ScheduleOneBlueprintImporter_Il2Cpp.dll"
}
$s1ApiDeployName = if ($Runtime -eq "Mono") { "S1API.Mono.MelonLoader.dll" } else { "S1API.Il2Cpp.MelonLoader.dll" }
$s1ApiBuildPath = [System.IO.Path]::GetFullPath($S1ApiDllPath)
$resultPath = Join-Path $runDirectory "result.txt"
$playerLogPath = Join-Path $runDirectory "Player.log"
$process = $null
$movedMods = $false
$createdMods = $false

function Assert-DescendantPath {
    param([string]$Candidate, [string]$Parent)
    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $candidateFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing filesystem mutation outside '$parentFull': '$candidateFull'."
    }
}

foreach ($required in @($gameExe, $buildPath, $s1ApiBuildPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
if (-not (Test-Path -LiteralPath $sourceSavePath -PathType Container)) {
    throw "Source save not found: $sourceSavePath"
}

$existing = Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and [System.IO.Path]::GetFullPath($_.ExecutablePath) -eq $gameExe
}
if ($existing) {
    throw "The selected Schedule I install is already running."
}

New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceSavePath -Destination $smokeSavePath -Recurse
$propertyPath = Join-Path $smokeSavePath ("Properties\" + $PropertyFileName)
$property = Get-Content -Raw -LiteralPath $propertyPath | ConvertFrom-Json
if (-not $property.IsOwned) {
    throw "The source save does not own property '$PropertyCode'."
}
$property.Objects = @()
$property.Employees = @()
$property | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $propertyPath -Encoding utf8
$moneyPath = Join-Path $smokeSavePath "Money.json"
$money = Get-Content -Raw -LiteralPath $moneyPath | ConvertFrom-Json
$money.OnlineBalance = 1000000.0
$money | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $moneyPath -Encoding utf8

try {
    Assert-DescendantPath $modsPath $gamePath
    Assert-DescendantPath $backupModsPath $gamePath
    Assert-DescendantPath $temporaryModsPath $gamePath
    if (Test-Path -LiteralPath $modsPath -PathType Container) {
        Move-Item -LiteralPath $modsPath -Destination $backupModsPath
        $movedMods = $true
    }
    New-Item -ItemType Directory -Path $modsPath | Out-Null
    $createdMods = $true
    Copy-Item -LiteralPath $buildPath -Destination $modsPath
    Copy-Item -LiteralPath $s1ApiBuildPath -Destination (Join-Path $modsPath $s1ApiDeployName)

    $arguments = @(
        "--blueprint-import-smoke",
        "--blueprint-smoke-save", "`"$smokeSavePath`"",
        "--blueprint-smoke-id", $BlueprintId,
        "--blueprint-smoke-property", $PropertyCode,
        "--blueprint-smoke-dir", "`"$runDirectory`"",
        "-screen-width", "1280",
        "-screen-height", "720",
        "-screen-fullscreen", "0",
        "-logFile", "`"$playerLogPath`""
    )
    $process = Start-Process -FilePath $gameExe -ArgumentList $arguments -WorkingDirectory $gamePath -WindowStyle Hidden -PassThru
    $started = Get-Date
    while (((Get-Date) - $started).TotalSeconds -lt $TimeoutSeconds) {
        if (Test-Path -LiteralPath $resultPath -PathType Leaf) { break }
        $process.Refresh()
        if ($process.HasExited) { throw "Game exited before producing a result. ExitCode=$($process.ExitCode)." }
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "Timed out waiting for smoke result."
    }
    $result = Get-Content -Raw -LiteralPath $resultPath
    Write-Host $result
    if (-not $result.StartsWith("PASS|", [System.StringComparison]::Ordinal)) {
        throw "Smoke probe reported failure: $result"
    }
}
finally {
    $latestLog = Join-Path $gamePath "MelonLoader\Latest.log"
    if (Test-Path -LiteralPath $latestLog -PathType Leaf) {
        Copy-Item -LiteralPath $latestLog -Destination (Join-Path $runDirectory "MelonLoader-Latest.log") -Force
    }
    if ($process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
            $process.WaitForExit(15000)
        }
    }
    if ($createdMods -and (Test-Path -LiteralPath $modsPath -PathType Container)) {
        Move-Item -LiteralPath $modsPath -Destination $temporaryModsPath
    }
    if ($movedMods -and (Test-Path -LiteralPath $backupModsPath -PathType Container)) {
        Move-Item -LiteralPath $backupModsPath -Destination $modsPath
    }
    if (Test-Path -LiteralPath $temporaryModsPath -PathType Container) {
        Assert-DescendantPath $temporaryModsPath $gamePath
        for ($attempt = 1; $attempt -le 10; $attempt++) {
            try {
                Remove-Item -LiteralPath $temporaryModsPath -Recurse -Force
                break
            }
            catch {
                if ($attempt -eq 10) {
                    Write-Warning "Temporary Mods cleanup remains at '$temporaryModsPath': $($_.Exception.Message)"
                }
                Start-Sleep -Milliseconds 500
            }
        }
    }
}
