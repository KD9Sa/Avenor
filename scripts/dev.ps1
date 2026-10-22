param([switch]$NoInstall)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$frontendRoot = Join-Path $projectRoot 'frontend'
$apiProject = Join-Path $projectRoot 'backend/Avenor.Api/Avenor.Api.csproj'
$localRoot = Join-Path $projectRoot '.local'
New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $localRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $localRoot 'nuget'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5080'
$env:Database__Provider = 'Sqlite'
$databasePath = Join-Path $localRoot 'avenor.db'
$env:ConnectionStrings__DefaultConnection = "Data Source=$databasePath"
$env:SeedDemo = 'true'

if (-not $NoInstall) {
    Push-Location $frontendRoot
    try {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    } finally { Pop-Location }
    & dotnet restore $apiProject
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
}

& dotnet build $apiProject --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'The API build failed.' }
$apiRoot = Split-Path $apiProject -Parent
$apiAssembly = Join-Path $apiRoot 'bin/Debug/net10.0/Avenor.Api.dll'
# Execute the built DLL as one tracked process so cleanup does not leave an apphost child running.
$apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('exec', ('"' + $apiAssembly + '"')) -WorkingDirectory $apiRoot -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $localRoot 'api.log') -RedirectStandardError (Join-Path $localRoot 'api-errors.log')
try {
    $apiReady = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($apiProcess.HasExited) { throw "The API exited. Inspect $localRoot/api.log and api-errors.log." }
        try {
            Invoke-RestMethod -Uri 'http://localhost:5080/api/health' -TimeoutSec 2 | Out-Null
            $apiReady = $true
            break
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $apiReady) { throw "The API did not become ready. Inspect $localRoot/api.log." }
    Write-Host 'API: http://localhost:5080/api/health'
    Write-Host 'Workspace: http://localhost:4200 (Ctrl+C stops both servers)'
    Push-Location $frontendRoot
    try { & npm.cmd start } finally { Pop-Location }
} finally {
    if (-not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
}
