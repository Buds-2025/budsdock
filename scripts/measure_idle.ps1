param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Label
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$previousDataDirectory = $env:BUDSDOCK_DATA_DIR
$evidenceDirectory = Join-Path $projectRoot "artifacts/benchmarks/$Label"
$env:BUDSDOCK_DATA_DIR = $evidenceDirectory
$benchmarkProcess = $null
try {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $benchmarkProcess = Start-Process -FilePath (Resolve-Path -LiteralPath $Executable).Path -ArgumentList '--ui-test' -WindowStyle Hidden -PassThru
    $inputIdle = $benchmarkProcess.WaitForInputIdle(10000)
    $inputIdleMilliseconds = $clock.ElapsedMilliseconds
    Start-Sleep -Seconds 3
    $benchmarkProcess.Refresh()
    $initialCpu = $benchmarkProcess.TotalProcessorTime.TotalMilliseconds
    $initialPrivateBytes = $benchmarkProcess.PrivateMemorySize64
    Start-Sleep -Seconds 5
    $benchmarkProcess.Refresh()
    $result = [pscustomobject]@{
        Label=$Label; InputIdle=$inputIdle; InputIdleMilliseconds=$inputIdleMilliseconds
        PrivateBytes=$benchmarkProcess.PrivateMemorySize64; WorkingSetBytes=$benchmarkProcess.WorkingSet64
        PrivateGrowthBytes=($benchmarkProcess.PrivateMemorySize64 - $initialPrivateBytes)
        IdleCpuMilliseconds=($benchmarkProcess.TotalProcessorTime.TotalMilliseconds - $initialCpu)
        SampleSeconds=5; Handles=$benchmarkProcess.HandleCount
        Scenario='UI-test, five default items, settings window created; local single sample, not a benchmark guarantee'
    }
    $result | ConvertTo-Json | Set-Content (Join-Path $evidenceDirectory 'metrics.json') -Encoding UTF8
    $result | ConvertTo-Json
} finally {
    if ($benchmarkProcess -and -not $benchmarkProcess.HasExited) { Stop-Process -Id $benchmarkProcess.Id }
    $env:BUDSDOCK_DATA_DIR = $previousDataDirectory
}
