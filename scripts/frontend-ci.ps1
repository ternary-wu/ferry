param(
    [ValidateSet('build', 'test')]
    [string]$Task = 'build',
    [string]$WorkBase = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repoRoot 'frontend'
if ([string]::IsNullOrWhiteSpace($WorkBase)) {
    $WorkBase = [System.IO.Path]::GetTempPath()
}
$work = Join-Path $WorkBase 'ferry-frontend-ci'

Write-Host "src=$src"
Write-Host "work=$work"
Write-Host "srcExists=$(Test-Path -LiteralPath $src)"
Write-Host "srcItems=$((Get-ChildItem -LiteralPath $src -Force | Measure-Object).Count)"

if (-not $work.StartsWith($WorkBase, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Temp dir out of bounds: $work"
}

# Resolve bundled Node from pnpm location (Codex runtime provides Node, system PATH may not)
$pnpmCmd = Get-Command pnpm.cmd -ErrorAction SilentlyContinue
$nodeBin = $null
if ($pnpmCmd) {
    $fallbackDir = Split-Path $pnpmCmd.Source -Parent
    $nodeBin = Join-Path (Split-Path (Split-Path $fallbackDir -Parent) -Parent) 'node\bin'
}
if (-not $nodeBin -or -not (Test-Path (Join-Path $nodeBin 'node.exe'))) {
    $nodeBin = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin'
}
if (Test-Path (Join-Path $nodeBin 'node.exe')) {
    $env:PATH = "$nodeBin;$env:PATH"
}
$env:CI = 'true'

if (Test-Path $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Path $work | Out-Null

# Copy sources and configs (exclude node_modules/dist)
try {
    Copy-Item -Path (Join-Path $src '*') -Destination $work -Recurse -Force -Exclude node_modules,dist -ErrorAction Stop
}
catch {
    Write-Host "copy-error: $($_.Exception.Message)"
    throw
}
Write-Host "copied=$(Get-ChildItem -Force -LiteralPath $work | Measure-Object | Select-Object -ExpandProperty Count)"

Push-Location $work
# Install a standalone node_modules inside the mirror (offline from pnpm store).
# The repo path contains '#', which breaks Vite/Rollup; a real mirror keeps every path '#'-free.
pnpm install --offline --frozen-lockfile
if ($LASTEXITCODE -ne 0) {
    Write-Host "Offline install failed, retrying with network..."
    pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

try {
    if ($Task -eq 'build') {
        pnpm build
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
        $targetDist = Join-Path $src 'dist'
        if (Test-Path $targetDist) {
            Remove-Item -LiteralPath $targetDist -Recurse -Force
        }
        Copy-Item -LiteralPath (Join-Path $work 'dist') -Destination $targetDist -Recurse
        Write-Host "Build output copied to $targetDist"
    }
    else {
        pnpm test
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
