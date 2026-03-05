param(
    [string[]]$Cases = @("file_loop"),
    [int]$MaxSeconds = 0,
    [switch]$SkipRtsp,
    [switch]$Ci
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$exampleName = "test_player"

function Resolve-CaseList {
    param([string[]]$RawCases, [bool]$SkipRtspCases)

    $ordered = @("file_loop", "file_once", "invalid_uri", "alloc_rtsp", "rtsp_single", "rtsp_multi")
    $valid = @("all") + $ordered
    $normalized = @()

    foreach ($raw in $RawCases) {
        foreach ($part in ($raw -split ",")) {
            $caseName = $part.Trim()
            if ([string]::IsNullOrWhiteSpace($caseName)) {
                continue
            }
            if ($valid -notcontains $caseName) {
                throw "invalid case: $caseName"
            }
            $normalized += $caseName
        }
    }

    if ($normalized.Count -eq 0) {
        $normalized = @("all")
    }

    $expanded = @()

    if ($normalized -contains "all") {
        $expanded = $ordered
    } else {
        foreach ($c in $ordered) {
            if ($normalized -contains $c) {
                $expanded += $c
            }
        }
    }

    if ($SkipRtspCases) {
        $expanded = $expanded | Where-Object { $_ -notlike "rtsp_*" -and $_ -ne "alloc_rtsp" }
    }

    return $expanded
}

function Invoke-Case {
    param(
        [string]$BinaryPath,
        [string]$CaseName,
        [int]$CaseMaxSeconds
    )

    $args = @("--case=$CaseName")
    if ($CaseName -ne "alloc_rtsp") {
        $args += "--max-seconds=$CaseMaxSeconds"
    }

    Write-Host "[RUN] case=$CaseName"
    & $BinaryPath $args | Out-Host
    return [int]$LASTEXITCODE
}

Push-Location $root
try {
    if ($Ci.IsPresent) {
        if (-not $PSBoundParameters.ContainsKey("Cases")) {
            $Cases = @("file_loop")
        }
        if (-not $PSBoundParameters.ContainsKey("SkipRtsp")) {
            $SkipRtsp = [System.Management.Automation.SwitchParameter]::new($false)
        }
        if (-not $PSBoundParameters.ContainsKey("MaxSeconds")) {
            $MaxSeconds = 0
        }
    }

    if ($MaxSeconds -lt 0) {
        throw "invalid max-seconds: $MaxSeconds"
    }

    $caseList = Resolve-CaseList -RawCases $Cases -SkipRtspCases $SkipRtsp.IsPresent
    if ($caseList.Count -eq 0) {
        Write-Host "[FAIL] no cases selected"
        exit 10
    }

    Write-Host "[BUILD] example=$exampleName"
    cargo build --example $exampleName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] build failed"
        exit 20
    }

    $binary = Join-Path $root "target\debug\examples\$exampleName.exe"
    if (-not (Test-Path $binary)) {
        Write-Host "[FAIL] binary not found: $binary"
        exit 20
    }

    $failed = @()
    foreach ($caseName in $caseList) {
        $code = Invoke-Case -BinaryPath $binary -CaseName $caseName -CaseMaxSeconds $MaxSeconds
        if ($code -ne 0) {
            $failed += "${caseName}:$code"
        }
    }

    if ($failed.Count -gt 0) {
        Write-Host "[FAIL] cases failed: $($failed -join ', ')"
        exit 20
    }

    Write-Host "[PASS] all cases passed"
    exit 0
}
catch {
    Write-Host "[FAIL] $($_.Exception.Message)"
    exit 10
}
finally {
    Pop-Location
}
