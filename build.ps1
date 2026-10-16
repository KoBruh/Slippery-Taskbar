$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'SlipperyTaskbar.exe'
$syso = Join-Path $root 'rsrc_windows_amd64.syso'
$versionFile = Join-Path $root 'VERSION'

Push-Location $root
try {
    if (-not (Test-Path $versionFile)) {
        throw 'VERSION file is missing.'
    }

    $version = (Get-Content $versionFile -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "VERSION must be in X.Y.Z format, got '$version'"
    }
    $version4 = "$version.0"

    Remove-Item $syso -Force -ErrorAction SilentlyContinue

    # Embed manifest and version metadata with a Go-native resource packer.
    # The application/tray icon is embedded separately in icon_windows.go.
    # Keeping icon decoding out of this step avoids Windows rc.exe RC2176 and
    # malformed-ICO parser failures from breaking CI/release builds.
    & go run github.com/tc-hib/go-winres@v0.3.1 simply `
        --arch amd64 `
        --out rsrc `
        --manifest gui `
        --file-version $version4 `
        --product-version $version4 `
        --file-description 'Slippery Taskbar' `
        --product-name 'Slippery Taskbar' `
        --original-filename 'SlipperyTaskbar.exe'
    if ($LASTEXITCODE -ne 0) {
        throw "go-winres failed with exit code $LASTEXITCODE"
    }
    if (-not (Test-Path $syso)) {
        throw "go-winres did not create $syso"
    }

    go build -trimpath -ldflags "-H=windowsgui -s -w" -o $exe .
    if ($LASTEXITCODE -ne 0) {
        throw "go build failed with exit code $LASTEXITCODE"
    }

    $hash = (Get-FileHash -Algorithm SHA256 $exe).Hash.ToLowerInvariant()
    "$hash  SlipperyTaskbar.exe" | Set-Content -Encoding ascii (Join-Path $root 'SHA256SUMS.txt')
    Write-Host "Built Slippery Taskbar $version"
    Write-Host "Built: $exe"
    Write-Host "SHA-256: $hash"
} finally {
    Remove-Item $syso -Force -ErrorAction SilentlyContinue
    Pop-Location
}
