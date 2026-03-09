param(
    [string]$ProjectRoot = "D:\TestProject\Video\RustAV",

    [string]$LogDir = "artifacts\production-gate",

    [string]$SampleUri = "../RustAV/TestFiles/SampleVideo_1280x720_10mb.mp4",

    [int]$SampleSeconds = 2,

    [string]$RtspUri = "",

    [string]$RtmpUri = "",

    [int]$RealtimeSeconds = 3,

    [string]$RtspAvUri = "",

    [string]$RtmpAvUri = "",

    [int]$AvSeconds = 60
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
        [string]$LogPath
    )

    Write-Host ""
    Write-Host "[gate] running $Name"
    Write-Host "[gate] cmd=$Command"

    $tmpOutputPath = Join-Path $env:TEMP ("rustav-gate-" + [guid]::NewGuid().ToString("N") + ".log")
    try {
        & cmd /c "$Command > `"$tmpOutputPath`" 2>&1"
        $output = if (Test-Path $tmpOutputPath) {
            Get-Content $tmpOutputPath
        } else {
            @()
        }
    }
    finally {
        if (Test-Path $tmpOutputPath) {
            Remove-Item -Path $tmpOutputPath -Force -ErrorAction SilentlyContinue
        }
    }
    $output | Tee-Object -FilePath $LogPath | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "[gate] $Name failed, see $LogPath"
    }

    return $output
}

function Get-AbsoluteLogPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    return (Join-Path $Root $RelativePath)
}

$resolvedRoot = (Resolve-Path $ProjectRoot).Path
Set-Location $resolvedRoot

$resolvedLogDir = Get-AbsoluteLogPath -Root $resolvedRoot -RelativePath $LogDir
New-Item -ItemType Directory -Force -Path $resolvedLogDir | Out-Null

$summary = New-Object System.Collections.Generic.List[string]

$null = Invoke-Step `
    -Name "cargo-check" `
    -Command "cargo check --manifest-path Cargo.toml --lib --examples --locked" `
    -LogPath (Join-Path $resolvedLogDir "cargo-check.log")
$summary.Add("cargo-check=ok")

$null = Invoke-Step `
    -Name "cargo-test" `
    -Command "cargo test --manifest-path Cargo.toml --lib --tests --locked" `
    -LogPath (Join-Path $resolvedLogDir "cargo-test.log")
$summary.Add("cargo-test=ok")

$null = Invoke-Step `
    -Name "ios-staticlib-check" `
    -Command "cargo check --manifest-path ios-staticlib/Cargo.toml --lib --locked" `
    -LogPath (Join-Path $resolvedLogDir "ios-staticlib-check.log")
$summary.Add("ios-staticlib-check=ok")

$null = Invoke-Step `
    -Name "ci-entrypoints" `
    -Command "python scripts/ci/validate_ci_entrypoints.py" `
    -LogPath (Join-Path $resolvedLogDir "ci-entrypoints.log")
$summary.Add("ci-entrypoints=ok")

$audioOutput = Invoke-Step `
    -Name "audio-probe" `
    -Command "cargo run --manifest-path Cargo.toml --example audio_probe -- `"$SampleUri`" $SampleSeconds" `
    -LogPath (Join-Path $resolvedLogDir "audio-probe.log")
$summary.Add("audio-probe=ok")

$audioFirstVideo = $audioOutput | Select-String "first_video_final=" | Select-Object -Last 1
$audioFirstAudio = $audioOutput | Select-String "first_audio_final=" | Select-Object -Last 1
if ($audioFirstVideo) { $summary.Add($audioFirstVideo.Line.Trim()) }
if ($audioFirstAudio) { $summary.Add($audioFirstAudio.Line.Trim()) }

$hasRealtimeUris = -not [string]::IsNullOrWhiteSpace($RtspUri) -and -not [string]::IsNullOrWhiteSpace($RtmpUri)
if ($hasRealtimeUris) {
    $null = Invoke-Step `
        -Name "realtime-probes" `
        -Command "powershell -ExecutionPolicy Bypass -File scripts/qa/run_realtime_probes.ps1 -RtspUri `"$RtspUri`" -RtmpUri `"$RtmpUri`" -Seconds $RealtimeSeconds -LogDir `"$resolvedLogDir\realtime`"" `
        -LogPath (Join-Path $resolvedLogDir "realtime-probes.log")
    $summary.Add("realtime-probes=ok")
} else {
    $summary.Add("realtime-probes=skipped")
}

$hasAvUris = -not [string]::IsNullOrWhiteSpace($RtspAvUri) -and -not [string]::IsNullOrWhiteSpace($RtmpAvUri)
if ($hasAvUris) {
    $null = Invoke-Step `
        -Name "av-soak" `
        -Command "powershell -ExecutionPolicy Bypass -File scripts/qa/run_av_soak.ps1 -RtspUri `"$RtspAvUri`" -RtmpUri `"$RtmpAvUri`" -Seconds $AvSeconds -LogDir `"$resolvedLogDir\av-soak`"" `
        -LogPath (Join-Path $resolvedLogDir "av-soak.log")
    $summary.Add("av-soak=ok")
} else {
    $summary.Add("av-soak=skipped")
}

$summaryPath = Join-Path $resolvedLogDir "summary.txt"
$summary | Set-Content -Path $summaryPath

Write-Host ""
Write-Host "[gate] summary"
$summary | ForEach-Object { Write-Host $_ }
Write-Host "[gate] summary_path=$summaryPath"
