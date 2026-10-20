param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build tests/RainReminder.Tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & ./tests/RainReminder.Tests/bin/Release/net48/RainReminder.Tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet build src/RainReminder.Service -c Release --nologo -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw 'Service build failed.' }
    dotnet build src/RainReminder.Tray -c Release --nologo -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    $output = Join-Path $root 'dist/win-x64'
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    New-Item -ItemType Directory -Path "$output/service","$output/tray","$output/scripts" -Force | Out-Null
    Copy-Item src/RainReminder.Service/bin/Release/net48/* "$output/service"
    Copy-Item src/RainReminder.Tray/bin/Release/net48/* "$output/tray"
    Copy-Item scripts/Install.ps1,scripts/Install.cmd,scripts/Uninstall.ps1 "$output/scripts"
    Copy-Item README.md "$output/README.md"
    Copy-Item data/README.md "$output/DATA-SOURCES.md"
    Copy-Item data/UNICODE-LICENSE.txt "$output/UNICODE-LICENSE.txt"
    if (Get-ChildItem $output -Recurse -Include coreclr.dll,hostfxr.dll,System.Private.CoreLib.dll,*.runtimeconfig.json) {
        throw 'Runtime files must not be bundled.'
    }
    & "$output/service/RainReminder.Service.exe" --smoke-test
    if ($LASTEXITCODE -ne 0) { throw 'Service smoke test failed.' }
    $smoke = Start-Process "$output/tray/RainReminder.Tray.exe" -ArgumentList '--smoke-test' -Wait -PassThru
    if ($smoke.ExitCode -ne 0) { throw 'Desktop smoke test failed.' }
    $zip = Join-Path $root 'dist/rain-reminder-win-x64.zip'
    Compress-Archive -Path "$output/*" -DestinationPath $zip -Force
    $archive = Get-Item $zip
    if ($archive.Length -gt 1MB) { throw "Package exceeded 1 MiB: $($archive.Length) bytes" }
    Write-Host ("Built {0} ({1:N0} bytes)" -f $zip, $archive.Length)
    if ($env:GITHUB_STEP_SUMMARY) {
        "Windows package: **$($archive.Length) bytes**. Uses Windows 11's built-in .NET Framework 4.8; no extra runtime install." | Out-File $env:GITHUB_STEP_SUMMARY -Append
    }
} finally { Pop-Location }

