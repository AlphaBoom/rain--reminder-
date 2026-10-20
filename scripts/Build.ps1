param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet run --project tests/RainReminder.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $output = Join-Path $root "dist/$Runtime"
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    dotnet publish src/RainReminder.Service -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "$output/service"
    if ($LASTEXITCODE -ne 0) { throw 'Service publish failed.' }
    dotnet publish src/RainReminder.Tray -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "$output/tray"
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    New-Item -ItemType Directory -Path "$output/scripts" -Force | Out-Null
    Copy-Item scripts/Install.ps1,scripts/Install.cmd,scripts/Uninstall.ps1 "$output/scripts"
    Copy-Item README.md "$output/README.md"
    Copy-Item data/README.md "$output/DATA-SOURCES.md"
    if ($env:OS -eq 'Windows_NT' -and $Runtime -eq 'win-x64') {
        & "$output/service/RainReminder.Service.exe" --smoke-test
        if ($LASTEXITCODE -ne 0) { throw 'Service smoke test failed.' }
        $smoke = Start-Process "$output/tray/RainReminder.Tray.exe" -ArgumentList '--smoke-test' -Wait -PassThru
        if ($smoke.ExitCode -ne 0) { throw 'Desktop smoke test failed.' }
    }
    $zip = Join-Path $root "dist/rain-reminder-$Runtime.zip"
    Compress-Archive -Path "$output/*" -DestinationPath $zip -Force
    Write-Host "Built $zip"
} finally { Pop-Location }
