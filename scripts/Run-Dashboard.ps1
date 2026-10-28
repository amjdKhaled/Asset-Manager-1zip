param(
    [ValidateRange(1, 65535)]
    [int]$Port = 5050
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\src\LFPortal.Web\LFPortal.Web.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK, then run this script again.'
}

Write-Host "Starting Dashboard at http://localhost:$Port"
& dotnet run --project $projectPath --no-launch-profile -- --urls "http://localhost:$Port"
exit $LASTEXITCODE
