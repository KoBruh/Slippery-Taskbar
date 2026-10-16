$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish .\SlipperyTaskbar.csproj -c Release -r win-x64 --self-contained true -o ..\publish
} finally {
    Pop-Location
}
