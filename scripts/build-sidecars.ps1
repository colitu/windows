<#
.SYNOPSIS
Builds colitu-engine.exe and csl-client.exe from the colitu/csl commit pinned in scripts/cores.json
and puts them in engine-dosyalari / csl-dosyalari.

.DESCRIPTION
The sidecars come from source, never from a developer's working tree: the script clones the pinned
commit of the public colitu/csl repository (without line-ending conversion), runs its own build
scripts with exactly the pinned Go toolchain (GOTOOLCHAIN) and a reset build environment, and
copies the files only when every one has its pinned SHA-256. Go builds with -trimpath and an empty
build id are byte-for-byte reproducible on any host, so anyone can check the shipped files this way.

To move to a newer colitu/csl commit or Go release: change "commit" / "go" in cores.json, run this
script, and put the SHA-256 values it prints into cores.json.

Needs git, Go (any recent release; GOTOOLCHAIN fetches the pinned one when it differs) and sh
(Git for Windows).

.EXAMPLE
pwsh scripts/build-sidecars.ps1
pwsh scripts/build-sidecars.ps1 -Source ..\colitu-csl   # clone from a local clone instead of GitHub
#>
param(
    [string]$Source
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "cores.json") -Raw | ConvertFrom-Json
$sidecars = $manifest.sidecars
if (-not $Source) { $Source = $sidecars.repo }

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

$sh = Get-Command sh -ErrorAction SilentlyContinue
$sh = if ($sh) { $sh.Source } else {
    @("$env:ProgramFiles\Git\bin\sh.exe", "F:\DEV\Git\bin\sh.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $sh) { throw "sh not found: install Git for Windows or put its bin folder on PATH." }
if (-not (Get-Command go -ErrorAction SilentlyContinue)) { throw "go not found on PATH." }

$work = Join-Path ([System.IO.Path]::GetTempPath()) "colitu-sidecars-$([guid]::NewGuid().ToString('N'))"
$csl = Join-Path $work "csl"
$saved = @{}
$buildEnv = @{ GOTOOLCHAIN = $sidecars.go; GOAMD64 = "v1"; GOARM64 = "v8.0"; GO386 = "sse2"; GOFLAGS = $null; GOEXPERIMENT = $null; GOARM = $null; CGO_ENABLED = $null }
try {
    git -c core.autocrlf=false -c core.eol=lf clone --quiet --no-checkout $Source $csl
    if ($LASTEXITCODE -ne 0) { throw "git clone $Source failed." }
    git -C $csl config core.autocrlf false
    git -C $csl -c advice.detachedHead=false checkout --quiet $sidecars.commit
    if ($LASTEXITCODE -ne 0) { throw "commit $($sidecars.commit) is not in $Source." }
    $head = (git -C $csl rev-parse HEAD).Trim()
    if ($head -ne $sidecars.commit) { throw "checkout is at $head, cores.json pins $($sidecars.commit)." }

    foreach ($name in $buildEnv.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $buildEnv[$name])
    }
    Push-Location $csl
    try {
        Write-Host "colitu/csl $($head.Substring(0, 12)) with $(go version)" -ForegroundColor Cyan
        foreach ($script in @("engine/build.sh", "cmd/csl-client/build.sh")) {
            & $sh $script | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "$script failed." }
        }
    }
    finally { Pop-Location }

    $different = @()
    foreach ($file in $sidecars.files.PSObject.Properties) {
        $built = Join-Path $csl $file.Value.dist
        $hash = Get-Sha256 $built
        Write-Host "$hash  $($file.Name)"
        if ($hash -ne $file.Value.sha256) { $different += "$($file.Name): built $hash, cores.json pins $($file.Value.sha256)" }
    }
    if ($different.Count -gt 0) {
        throw "The rebuilt sidecars do not match scripts/cores.json (nothing was copied):`n  $($different -join "`n  ")"
    }
    # Every file matched before anything is replaced.
    foreach ($file in $sidecars.files.PSObject.Properties) {
        $target = Join-Path $repoRoot $file.Name
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath (Join-Path $csl $file.Value.dist) -Destination $target -Force
    }
    Write-Host "Sidecars match scripts/cores.json and are in place." -ForegroundColor Green
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
