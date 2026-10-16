$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & .\build-framework.cmd
    if ($LASTEXITCODE -ne 0) { throw "Framework build/icon generation failed with exit code $LASTEXITCODE" }
    dotnet publish .\SlipperyTaskbar.csproj -c Release -r win-x64 --self-contained true -o ..\publish
} finally {
    Pop-Location
}
