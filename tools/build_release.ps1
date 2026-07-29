<#
    build_release.ps1 -- produce ready-to-upload release artifacts in dist\.

    Steps:
      1. Regenerate mod\ids.json from the apworld (tools\export_ids.py).
      2. Build the .apworld (zip of what_the_golf\, excluding __pycache__).
      3. Release-build the mod (skips the local-install auto-deploy).
      4. Stage the mod files (+ wtg_ids.json + INSTALL.txt) into a versioned zip.

    Version is read from mod\WtgArchipelago.csproj <Version>.
    Requires: python on PATH, .NET 6 SDK (dotnet).

    Usage:   pwsh tools\build_release.ps1
#>
$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$dist    = Join-Path $root 'dist'
$csproj  = Join-Path $root 'mod\WtgArchipelago.csproj'

# Run everything from the repo root so Python (which may be the MSYS build that
# can't parse Windows absolute-path args) gets simple relative, forward-slash
# paths that work for both MSYS and native Windows Python.
Push-Location $root
try {

# --- version from csproj ---
[xml]$xml = Get-Content $csproj
$version  = ($xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "Could not read <Version> from $csproj" }
Write-Host "Building release v$version" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $dist | Out-Null

# --- 1. regenerate ids.json ---
Write-Host "[1/4] Regenerating ids.json..." -ForegroundColor Cyan
python 'tools/export_ids.py'
if ($LASTEXITCODE -ne 0) { throw "export_ids.py failed" }

# --- 2. build the .apworld (python guarantees forward-slash paths + clean excludes) ---
Write-Host "[2/4] Packing what_the_golf.apworld..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$py = @'
import os, sys, zipfile
src, out = sys.argv[1], sys.argv[2]
base = os.path.dirname(src) or "."
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for dirpath, dirnames, filenames in os.walk(src):
        dirnames[:] = [d for d in dirnames if d != "__pycache__"]
        for f in filenames:
            if f.endswith(".pyc"):
                continue
            full = os.path.join(dirpath, f)
            arc  = os.path.relpath(full, base).replace(os.sep, "/")
            z.write(full, arc)
print("  wrote", out)
'@
$py | python - 'what_the_golf' 'dist/what_the_golf.apworld'
if ($LASTEXITCODE -ne 0) { throw "apworld packing failed" }

# --- 3. Release-build the mod ---
Write-Host "[3/4] Building mod (Release)..." -ForegroundColor Cyan
dotnet build $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$binRel = Join-Path $root 'mod\bin\Release'
$modDll = Join-Path $binRel 'WtgArchipelago.dll'
$apDll  = Join-Path $binRel 'Archipelago.MultiClient.Net.dll'
foreach ($f in @($modDll, $apDll)) {
    if (-not (Test-Path $f)) { throw "Expected build output missing: $f" }
}

# --- 4. stage + zip the mod bundle ---
Write-Host "[4/4] Staging mod bundle..." -ForegroundColor Cyan
$stage = Join-Path $dist 'mod-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $modDll $stage
Copy-Item $apDll  $stage
Copy-Item (Join-Path $root 'mod\ids.json')     (Join-Path $stage 'wtg_ids.json')
Copy-Item (Join-Path $root 'mod\INSTALL.txt')  $stage

$modZip = Join-Path $dist "WtgArchipelago-mod-v$version.zip"
if (Test-Path $modZip) { Remove-Item $modZip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $modZip
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Release v$version ready in dist\:" -ForegroundColor Green
Write-Host "  - what_the_golf.apworld"
Write-Host "  - WtgArchipelago-mod-v$version.zip  (WtgArchipelago.dll, Archipelago.MultiClient.Net.dll, wtg_ids.json, INSTALL.txt)"
Write-Host ""
Write-Host "Upload both as assets to a GitHub Release tagged v$version." -ForegroundColor Green
}
finally {
    Pop-Location
}
