param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
$previousDataDirectory = $env:BUDSDOCK_DATA_DIR
try {
    if (-not $SkipBuild) {
        dotnet build BudsDock.sln -c Release -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    dotnet run --project tests/BudsDock.Tests/BudsDock.Tests.csproj -c Release -p:Platform=x64 --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    [xml]$projectXml = Get-Content src/BudsDock/BudsDock.csproj -Encoding UTF8
    $releaseVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    $evidenceRoot = Join-Path $projectRoot ('artifacts/validation/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $results = @()
    foreach ($variant in @('portable', 'compact')) {
        $executable = Join-Path $projectRoot "artifacts/publish/BudsDock-$releaseVersion-win-x64-$variant/BudsDock.exe"
        if (-not (Test-Path -LiteralPath $executable)) { throw "Missing release: $executable" }
        if ((Get-Item -LiteralPath $executable).VersionInfo.FileVersion -ne "$releaseVersion.0") { throw 'Release version mismatch.' }
        foreach ($theme in @('Dark', 'Light')) {
            $env:BUDSDOCK_DATA_DIR = Join-Path $evidenceRoot "$variant-$theme"
            $language = if ($theme -eq 'Dark') { 'ChineseSimplified' } else { 'English' }
            $size = if ($theme -eq 'Dark') { '960x680' } else { '640x480' }
            $testProcess = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru -ArgumentList @(
                '--smoke-test', "--ui-test-theme=$theme", "--ui-test-language=$language", "--ui-test-size=$size")
            if (-not $testProcess.WaitForExit(30000)) {
                Stop-Process -Id $testProcess.Id
                throw "Smoke test timed out: $variant-$theme"
            }
            if ($testProcess.ExitCode -ne 0) { throw "Smoke test failed: $variant-$theme; inspect $env:BUDSDOCK_DATA_DIR" }
            $checks = Get-Content (Join-Path $env:BUDSDOCK_DATA_DIR 'integration-results.json') -Encoding UTF8 | ConvertFrom-Json
            $results += [pscustomobject]@{ Variant=$variant; Theme=$theme; Checks=$checks; ExitCode=$testProcess.ExitCode }
        }
    }
    $results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidenceRoot 'results.json') -Encoding UTF8
    Write-Output "Evidence: $evidenceRoot"
} finally {
    $env:BUDSDOCK_DATA_DIR = $previousDataDirectory
    Pop-Location
}
