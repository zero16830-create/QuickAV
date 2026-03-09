param(
    [string]$RustAVRoot = "D:\TestProject\Video\RustAV",

    [string]$UnityProjectRoot = "D:\TestProject\Video\UnityAV\UnityAVExample",

    [string]$UnityCsproj = "D:\TestProject\Video\UnityAV\Solution\UnityAV\UnityAV.csproj",

    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe",

    [string]$RtspUri = "",

    [string]$RtmpUri = "",

    [int]$ValidationSeconds = 6,

    [int]$WindowWidth = 0,

    [int]$WindowHeight = 0,

    [string]$LogDir = "artifacts\unity-validation"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [string]$LogPath = ""
    )

    Write-Host ""
    Write-Host "[unity-qa] running $Name"
    Write-Host "[unity-qa] cmd=$Command"

    $tmpOutputPath = Join-Path $env:TEMP ("rustav-unity-qa-" + [guid]::NewGuid().ToString("N") + ".log")
    Push-Location $WorkingDirectory
    try {
        & cmd /c "$Command > `"$tmpOutputPath`" 2>&1"
        $output = if (Test-Path $tmpOutputPath) { Get-Content $tmpOutputPath } else { @() }
    }
    finally {
        Pop-Location
        if (Test-Path $tmpOutputPath) {
            Remove-Item -Path $tmpOutputPath -Force -ErrorAction SilentlyContinue
        }
    }

    if ($LogPath -ne "") {
        $output | Tee-Object -FilePath $LogPath | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "[unity-qa] $Name failed"
    }

    return $output
}

function Sync-UnityPlugins {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RustRoot,

        [Parameter(Mandatory = $true)]
        [string]$UnityProject,

        [Parameter(Mandatory = $true)]
        [string]$UnityCsprojPath
    )

    $nativeSrc = Join-Path $RustRoot "target\unity-package\windows\Assets\Plugins\x86_64"
    $nativeDst = Join-Path $UnityProject "Assets\Plugins\x86_64"
    $managedSrc = Join-Path ([System.IO.Path]::GetDirectoryName($UnityCsprojPath)) "bin\Debug\UnityAV.dll"
    $managedDstDir = Join-Path $UnityProject "Assets\UnityAV\Plugins"

    New-Item -ItemType Directory -Force -Path $nativeDst | Out-Null
    New-Item -ItemType Directory -Force -Path $managedDstDir | Out-Null

    Copy-Item -Path (Join-Path $nativeSrc "*") -Destination $nativeDst -Force
    Copy-Item -Path $managedSrc -Destination (Join-Path $managedDstDir "UnityAV.dll") -Force
}

function Invoke-ValidationPlayer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExePath,

        [Parameter(Mandatory = $true)]
        [string]$CaseName,

        [Parameter(Mandatory = $true)]
        [string]$LogPath,

        [string]$Uri = "",

        [int]$Seconds = 6,

        [int]$WindowWidthValue = 0,

        [int]$WindowHeightValue = 0
    )

    $args = @(
        "-logFile", $LogPath,
        "-validationSeconds=$Seconds"
    )

    if (-not [string]::IsNullOrWhiteSpace($Uri)) {
        $args += "-uri=$Uri"
    }

    if ($WindowWidthValue -gt 0) {
        $args += "-windowWidth=$WindowWidthValue"
    }

    if ($WindowHeightValue -gt 0) {
        $args += "-windowHeight=$WindowHeightValue"
    }

    $process = Start-Process -FilePath $ExePath -ArgumentList $args -PassThru -WindowStyle Normal
    if (-not $process.WaitForExit(25000)) {
        Stop-Process -Id $process.Id -Force
        throw "[unity-qa] $CaseName timeout"
    }

    if ($process.ExitCode -ne 0) {
        throw "[unity-qa] $CaseName exit code=$($process.ExitCode)"
    }
}

function Get-ValidationSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CaseName,

        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    $lines = Get-Content -Path $LogPath
    $windowLine = ($lines | Select-String "\[CodexValidation\] window_configured=" | Select-Object -Last 1)
    $timeLine = ($lines | Select-String "\[CodexValidation\] time=" | Select-Object -Last 1)
    $completeLine = ($lines | Select-String "\[CodexValidation\] complete" | Select-Object -Last 1)

    [pscustomobject]@{
        Case = $CaseName
        Window = if ($windowLine) { $windowLine.Line.Trim() } else { "" }
        LastTick = if ($timeLine) { $timeLine.Line.Trim() } else { "" }
        Completed = [bool]$completeLine
        LogPath = $LogPath
    }
}

$resolvedRustRoot = (Resolve-Path $RustAVRoot).Path
$resolvedUnityProjectRoot = (Resolve-Path $UnityProjectRoot).Path
$resolvedUnityCsproj = (Resolve-Path $UnityCsproj).Path
$resolvedUnityExe = (Resolve-Path $UnityExe).Path
$resolvedLogDir = Join-Path $resolvedRustRoot $LogDir

New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null

$buildLog = Join-Path $resolvedLogDir "unity-build.log"
$nativeLog = Join-Path $resolvedLogDir "native-build.log"
$managedLog = Join-Path $resolvedLogDir "managed-build.log"
$unityBatchLog = Join-Path $resolvedUnityProjectRoot "Build\codex-unity-build.log"

Invoke-Step `
    -Name "managed-build" `
    -Command "dotnet msbuild `"$resolvedUnityCsproj`" /t:Build /p:Configuration=Debug /p:TargetFrameworkVersion=v4.8 /p:PostBuildEvent=" `
    -WorkingDirectory $resolvedRustRoot `
    -LogPath $managedLog | Out-Null

Invoke-Step `
    -Name "native-build" `
    -Command "python scripts/ci/build_unity_plugins.py --project-root `"$resolvedRustRoot`" --platform windows --output-root `"$resolvedRustRoot\target\unity-package\windows`"" `
    -WorkingDirectory $resolvedRustRoot `
    -LogPath $nativeLog | Out-Null

Sync-UnityPlugins `
    -RustRoot $resolvedRustRoot `
    -UnityProject $resolvedUnityProjectRoot `
    -UnityCsprojPath $resolvedUnityCsproj

Invoke-Step `
    -Name "unity-batch-build" `
    -Command "`"$resolvedUnityExe`" -batchmode -quit -nographics -projectPath `"$resolvedUnityProjectRoot`" -logFile `"$unityBatchLog`" -executeMethod UnityAV.Editor.CodexValidationBuild.BuildWindowsValidationPlayer" `
    -WorkingDirectory $resolvedUnityProjectRoot `
    -LogPath $buildLog | Out-Null

$playerExe = Join-Path $resolvedUnityProjectRoot "Build\CodexPullValidation\CodexPullValidation.exe"
if (-not (Test-Path $playerExe)) {
    throw "[unity-qa] player exe not found: $playerExe"
}

Get-Process CodexPullValidation -ErrorAction SilentlyContinue | Stop-Process -Force

$cases = @(
    @{
        Name = "file"
        Uri = ""
    }
)

if (-not [string]::IsNullOrWhiteSpace($RtspUri)) {
    $cases += @{
        Name = "rtsp"
        Uri = $RtspUri
    }
}

if (-not [string]::IsNullOrWhiteSpace($RtmpUri)) {
    $cases += @{
        Name = "rtmp"
        Uri = $RtmpUri
    }
}

$summaries = @()
foreach ($case in $cases) {
    $caseLog = Join-Path $resolvedLogDir ($case.Name + ".log")
    Invoke-ValidationPlayer `
        -ExePath $playerExe `
        -CaseName $case.Name `
        -LogPath $caseLog `
        -Uri $case.Uri `
        -Seconds $ValidationSeconds `
        -WindowWidthValue $WindowWidth `
        -WindowHeightValue $WindowHeight

    $summaries += Get-ValidationSummary -CaseName $case.Name -LogPath $caseLog
}

$summaryPath = Join-Path $resolvedLogDir "summary.txt"
$summaries | ForEach-Object {
    $_.Case
    $_.Window
    $_.LastTick
    ("completed=" + $_.Completed)
    ("log=" + $_.LogPath)
    ""
} | Set-Content -Path $summaryPath

Write-Host ""
Write-Host "[unity-qa] summary"
$summaries | ForEach-Object {
    Write-Host ("case=" + $_.Case)
    Write-Host ("  " + $_.Window)
    Write-Host ("  " + $_.LastTick)
    Write-Host ("  completed=" + $_.Completed)
    Write-Host ("  log=" + $_.LogPath)
}
Write-Host "[unity-qa] summary_path=$summaryPath"
