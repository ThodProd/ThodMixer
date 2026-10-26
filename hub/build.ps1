param([Parameter(Mandatory=$true)][string]$HubRoot,[string]$OutputDirectory='artifacts/hub-release')
$ErrorActionPreference='Stop'
$HubRoot=[IO.Path]::GetFullPath($HubRoot)
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $stage=Join-Path (Get-Location).Path ('artifacts/hub-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    dotnet publish ThodMixer.csproj -c Release -r win-x64 --self-contained true -p:TreatWarningsAsErrors=false -o (Join-Path $stage "app") --nologo
    if ($LASTEXITCODE) { throw "Build failed" }
    Copy-Item hub/manifest.json (Join-Path $stage 'manifest.json')
    & (Join-Path $HubRoot 'scripts/package-module.ps1') -PackageDirectory $stage -Repository 'ThodProd/ThodMixer' -OutputDirectory $OutputDirectory -Notes 'Самостоятельное окно, установка и удаление через ThodHUB, отдельная папка конфигурации. Автозапуск в сборке хаба пока отключён.'
} finally { Pop-Location }
