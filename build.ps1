$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'SlipperyTaskbar.exe'
$res = Join-Path $root 'app.res'
$syso = Join-Path $root 'resource_windows_amd64.syso'

function Find-LatestFile([string]$pattern) {
    $items = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
    if ($items) { return $items[0].FullName }
    return $null
}

Push-Location $root
try {
    Remove-Item $res, $syso -Force -ErrorAction SilentlyContinue

    # Optional PE resources: when the Windows SDK/MSVC tools are present this
    # embeds the supplied artwork as the .exe icon and adds version/manifest
    # metadata. The program also embeds the ICO in Go, so the tray/window icon
    # still works when these tools are unavailable.
    $kits = ${env:ProgramFiles(x86)}
    $rc = $null
    if ($kits) {
        $rc = Find-LatestFile (Join-Path $kits 'Windows Kits\10\bin\*\x64\rc.exe')
    }

    $cvtres = $null
    $vswhere = if ($kits) { Join-Path $kits 'Microsoft Visual Studio\Installer\vswhere.exe' } else { $null }
    if ($vswhere -and (Test-Path $vswhere)) {
        $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($vs) {
            $cvtres = Find-LatestFile (Join-Path $vs 'VC\Tools\MSVC\*\bin\Hostx64\x64\cvtres.exe')
        }
    }

    if ($rc -and $cvtres) {
        & $rc /nologo "/fo$res" (Join-Path $root 'app.rc')
        if ($LASTEXITCODE -ne 0) { throw "rc.exe failed with exit code $LASTEXITCODE" }
        & $cvtres /nologo /machine:x64 "/out:$syso" $res
        if ($LASTEXITCODE -ne 0) { throw "cvtres.exe failed with exit code $LASTEXITCODE" }
        Write-Host 'Embedded Slippery Taskbar icon and Windows metadata.'
    } else {
        Write-Warning 'Windows resource tools not found; building without the Explorer .exe icon. The in-app/tray icon is still embedded.'
    }

    go build -trimpath -ldflags "-H=windowsgui -s -w" -o $exe .
    if ($LASTEXITCODE -ne 0) { throw "go build failed with exit code $LASTEXITCODE" }

    $hash = (Get-FileHash -Algorithm SHA256 $exe).Hash.ToLowerInvariant()
    "$hash  SlipperyTaskbar.exe" | Set-Content -Encoding ascii (Join-Path $root 'SHA256SUMS.txt')
    Write-Host "Built: $exe"
    Write-Host "SHA-256: $hash"
} finally {
    Remove-Item $res, $syso -Force -ErrorAction SilentlyContinue
    Pop-Location
}
