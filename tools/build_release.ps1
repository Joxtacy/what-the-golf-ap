<#
    build_release.ps1 -- produce ready-to-upload release artifacts in dist\.

    Steps:
      1. Regenerate mod\ids.json from the apworld (tools\export_ids.py).
      2. Build the .apworld (zip of what_the_golf\, excluding __pycache__).
      3. Release-build the mod (skips the local-install auto-deploy).
      4. Stage the mod files (+ wtg_ids.json + INSTALL.txt) into a versioned zip.
      5. Pack the PopTracker pack + record it in poptracker-versions.json.

    Two versions are in play and they are deliberately independent:
      * mod + apworld -- mod\WtgArchipelago.csproj <Version>
      * PopTracker pack -- tools\poptracker_src\pack_version.txt
    Coupling them would force pointless pack releases for mod-only changes.

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

# --- 0. pre-flight: the committed pack must match a fresh build ---
# Catches a poptracker/ that was hand-edited or left stale after a data change,
# BEFORE anything gets packaged.
Write-Host "[0/5] Checking poptracker/ is up to date..." -ForegroundColor Cyan
python 'tools/build_poptracker.py' '--check'
if ($LASTEXITCODE -ne 0) { throw "poptracker/ is stale -- run: python tools/build_poptracker.py" }

# --- 1. regenerate ids.json ---
Write-Host "[1/5] Regenerating ids.json..." -ForegroundColor Cyan
python 'tools/export_ids.py'
if ($LASTEXITCODE -ne 0) { throw "export_ids.py failed" }

# --- 2. build the .apworld (python guarantees forward-slash paths + clean excludes) ---
Write-Host "[2/5] Packing what_the_golf.apworld..." -ForegroundColor Cyan
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
Write-Host "[3/5] Building mod (Release)..." -ForegroundColor Cyan
dotnet build $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$binRel = Join-Path $root 'mod\bin\Release'
$modDll = Join-Path $binRel 'WtgArchipelago.dll'
$apDll  = Join-Path $binRel 'Archipelago.MultiClient.Net.dll'
foreach ($f in @($modDll, $apDll)) {
    if (-not (Test-Path $f)) { throw "Expected build output missing: $f" }
}

# --- 4. stage + zip the mod bundle ---
Write-Host "[4/5] Staging mod bundle..." -ForegroundColor Cyan
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

# --- 5. pack the PopTracker pack + record it for auto-update ---
Write-Host "[5/5] Packing the PopTracker pack..." -ForegroundColor Cyan
$packVersion = (python 'tools/build_poptracker.py' '--print-version').Trim()
if ($LASTEXITCODE -ne 0 -or -not $packVersion) { throw "could not read the pack version" }
$packZipRel = "dist/what-the-golf-poptracker-v$packVersion.zip"
$packZip    = Join-Path $root $packZipRel
if (Test-Path $packZip) { Remove-Item $packZip -Force }

# Python writes the zip, not Compress-Archive: PopTracker needs manifest.json at
# the archive ROOT with forward-slash entries, and Compress-Archive emits
# back-slashes.
python 'tools/build_poptracker.py' '--zip' $packZipRel
if ($LASTEXITCODE -ne 0) { throw "poptracker zip failed" }

$packSha = (Get-FileHash $packZip -Algorithm SHA256).Hash.ToLower()
python 'tools/build_poptracker.py' '--record-version' $packVersion $packSha
if ($LASTEXITCODE -ne 0) { throw "recording the pack version failed" }

Write-Host ""
Write-Host "Release ready in dist\:" -ForegroundColor Green
Write-Host "  mod + apworld  v$version"
Write-Host "    - what_the_golf.apworld"
Write-Host "    - WtgArchipelago-mod-v$version.zip  (WtgArchipelago.dll, Archipelago.MultiClient.Net.dll, wtg_ids.json, INSTALL.txt)"
Write-Host "  PopTracker pack  v$packVersion"
Write-Host "    - what-the-golf-poptracker-v$packVersion.zip"
Write-Host "      sha256 $packSha"
Write-Host ""
Write-Host "Next:" -ForegroundColor Green
Write-Host "  1. Commit the regenerated poptracker-versions.json (and poptracker/ if it changed)."
Write-Host "  2. Tag v$version and push."
Write-Host "  3. Upload all three zips as assets on that release."
Write-Host ""
Write-Host "The pack's download_url points at tag v$packVersion, so the pack zip must be" -ForegroundColor Yellow
Write-Host "on a release tagged v$packVersion or PopTracker's auto-update will 404." -ForegroundColor Yellow
}
finally {
    Pop-Location
}
