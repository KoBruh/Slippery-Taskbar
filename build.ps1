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

    # Use a Go-native resource packer instead of Microsoft's rc.exe.
    # This accepts modern ICO/PNG icon data directly and avoids RC2176
    # ('old DIB') failures while still embedding the Explorer icon,
    # GUI manifest, and version metadata in the final PE executable.
    & go run github.com/tc-hib/go-winres@v0.3.1 simply `
        --arch amd64 `
        --out rsrc `
        --manifest gui `
        --icon 'assets\slippery-taskbar.ico' `
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
