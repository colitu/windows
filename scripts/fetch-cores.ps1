<#
.SYNOPSIS
Downloads the VPN cores pinned in scripts/cores.json and puts them in xray-dosyalari / singbox-dosyalari.

.DESCRIPTION
Only the official GitHub release assets named in cores.json are used. The archive must have the
pinned SHA-256 (for Xray also the SHA2-256 line of the release's .dgst file), and every extracted
file must have its pinned SHA-256; otherwise nothing is copied. -FromDir uses archives that are
already downloaded (same checks).

.EXAMPLE
pwsh scripts/fetch-cores.ps1
pwsh scripts/fetch-cores.ps1 -FromDir D:\downloads
pwsh scripts/fetch-cores.ps1 -Verify   # only check the files in place
#>
param(
    [string]$FromDir,
    [switch]$Verify
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "cores.json") -Raw | ConvertFrom-Json

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Test-PinnedCores {
    $bad = @()
    foreach ($core in $manifest.cores) {
        foreach ($file in $core.files.PSObject.Properties) {
            $path = Join-Path (Join-Path $repoRoot $core.folder) $file.Name
            if (-not (Test-Path -LiteralPath $path)) {
                $bad += "$($core.folder)\$($file.Name): missing"
            }
            elseif ((Get-Sha256 $path) -ne $file.Value) {
                $bad += "$($core.folder)\$($file.Name): SHA-256 is not the pinned $($core.name) $($core.version)"
            }
        }
    }
    # The files that ship next to the cores (wintun.dll, geo files, rule sets): pinned too.
    foreach ($folder in $manifest.files.PSObject.Properties) {
        foreach ($file in $folder.Value.PSObject.Properties) {
            $path = Join-Path (Join-Path $repoRoot $folder.Name) $file.Name
            if (-not (Test-Path -LiteralPath $path)) {
                $bad += "$($folder.Name)\$($file.Name): missing"
            }
            elseif ((Get-Sha256 $path) -ne $file.Value) {
                $bad += "$($folder.Name)\$($file.Name): SHA-256 is not the pinned one"
            }
        }
    }
    # Nothing else in these folders: a stray DLL there could be loaded by an elevated core.
    $known = @{}
    foreach ($core in $manifest.cores) {
        foreach ($file in $core.files.PSObject.Properties) { $known["$($core.folder)\$($file.Name)".ToLowerInvariant()] = $true }
    }
    foreach ($folder in $manifest.files.PSObject.Properties) {
        foreach ($file in $folder.Value.PSObject.Properties) { $known["$($folder.Name)\$($file.Name)".ToLowerInvariant()] = $true }
    }
    # Colitu's sidecars (built from colitu/csl by scripts/build-sidecars.ps1): pinned the same way,
    # and nothing else in their runtime folders.
    $sidecarDirs = @{}
    foreach ($file in $manifest.sidecars.files.PSObject.Properties) {
        $path = Join-Path $repoRoot $file.Name
        if (-not (Test-Path -LiteralPath $path)) {
            $bad += "$($file.Name): missing (run scripts/build-sidecars.ps1)"
        }
        elseif ((Get-Sha256 $path) -ne $file.Value.sha256) {
            $bad += "$($file.Name): SHA-256 is not the one colitu/csl $($manifest.sidecars.commit.Substring(0, 7)) builds (run scripts/build-sidecars.ps1)"
        }
        $dir = Split-Path -Parent $path
        if (-not $sidecarDirs.ContainsKey($dir)) { $sidecarDirs[$dir] = @() }
        $sidecarDirs[$dir] += (Split-Path -Leaf $path).ToLowerInvariant()
    }
    foreach ($dir in $sidecarDirs.Keys) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($item in Get-ChildItem -LiteralPath $dir -Force) {
            if ($sidecarDirs[$dir] -notcontains $item.Name.ToLowerInvariant()) {
                $bad += "$([System.IO.Path]::GetRelativePath("$repoRoot",$item.FullName)): not listed in scripts/cores.json"
            }
        }
    }
    $folders = @($manifest.cores | ForEach-Object { $_.folder }) + @($manifest.files.PSObject.Properties | ForEach-Object { $_.Name }) | Sort-Object -Unique
    foreach ($folder in $folders) {
        $dir = Join-Path $repoRoot $folder
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($item in Get-ChildItem -LiteralPath $dir -Force) {
            if ($item.PSIsContainer) {
                $bad += "$folder\$($item.Name): unexpected folder"
            }
            elseif (-not $known.ContainsKey("$folder\$($item.Name)".ToLowerInvariant()) -and $manifest.documents -notcontains $item.Name) {
                $bad += "$folder\$($item.Name): not listed in scripts/cores.json"
            }
        }
    }
    return $bad
}

if ($Verify) {
    $bad = Test-PinnedCores
    if ($bad.Count -gt 0) { throw "Core files do not match scripts/cores.json:`n  $($bad -join "`n  ")`nRun scripts/fetch-cores.ps1." }
    Write-Host "Core files match scripts/cores.json."
    return
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "colitu-cores-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    foreach ($core in $manifest.cores) {
        $uri = [Uri]$core.url
        if ($uri.Scheme -ne "https" -or $uri.Host -ne "github.com" -or -not $uri.AbsolutePath.Contains("/releases/download/")) {
            throw "$($core.name): only GitHub release downloads are allowed ($($core.url))."
        }
        $archiveName = [System.IO.Path]::GetFileName($uri.AbsolutePath)
        $archive = Join-Path $work $archiveName
        if ($FromDir) {
            Copy-Item -LiteralPath (Join-Path $FromDir $archiveName) -Destination $archive
        }
        else {
            Write-Host "Downloading $($core.url)"
            Invoke-WebRequest -Uri $core.url -OutFile $archive -UseBasicParsing
        }
        $actual = Get-Sha256 $archive
        if ($actual -ne $core.sha256) {
            throw "$($core.name) $($core.version): archive SHA-256 $actual is not the pinned $($core.sha256)."
        }
        if ($core.digestUrl) {
            $digestFile = Join-Path $work "$archiveName.dgst"
            if ($FromDir -and (Test-Path -LiteralPath (Join-Path $FromDir "$archiveName.dgst"))) {
                Copy-Item -LiteralPath (Join-Path $FromDir "$archiveName.dgst") -Destination $digestFile
            }
            else {
                Invoke-WebRequest -Uri $core.digestUrl -OutFile $digestFile -UseBasicParsing
            }
            $line = Select-String -LiteralPath $digestFile -Pattern '^SHA2-256=\s*([0-9a-fA-F]{64})' | Select-Object -First 1
            if (-not $line -or $line.Matches[0].Groups[1].Value.ToLowerInvariant() -ne $core.sha256) {
                throw "$($core.name) $($core.version): the release's .dgst file does not list the pinned SHA-256."
            }
        }
        $extract = Join-Path $work $core.name
        Expand-Archive -LiteralPath $archive -DestinationPath $extract
        $staged = @{}
        foreach ($file in $core.files.PSObject.Properties) {
            $found = Get-ChildItem -LiteralPath $extract -Recurse -File -Filter $file.Name | Select-Object -First 1
            if (-not $found) { throw "$($core.name): $($file.Name) is not in $archiveName." }
            $hash = Get-Sha256 $found.FullName
            if ($hash -ne $file.Value) { throw "$($core.name): $($file.Name) SHA-256 $hash is not the pinned $($file.Value)." }
            $staged[$file.Name] = $found.FullName
        }
        # Every file checked before anything is replaced.
        $target = Join-Path $repoRoot $core.folder
        foreach ($name in $staged.Keys) {
            Copy-Item -LiteralPath $staged[$name] -Destination (Join-Path $target $name) -Force
        }
        Write-Host "$($core.name) $($core.version): $(($staged.Keys | Sort-Object) -join ', ') in $($core.folder)"
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$bad = Test-PinnedCores
if ($bad.Count -gt 0) { throw "Core files do not match scripts/cores.json:`n  $($bad -join "`n  ")" }
Write-Host "Core files match scripts/cores.json."
