# Reproducible builds

From the first release after 2.9.7, anyone can rebuild the Windows installer
from this repository and get the same bytes as the published file. Releases up
to 2.9.7 were built before this and cannot be reproduced.

## What makes the build reproducible

| Input | How it is fixed |
|---|---|
| .NET SDK (and so the runtime shipped in the self-contained app) | exact version in `global.json` (`rollForward: disable`); CI installs that version |
| NuGet packages | central versions in `src/Directory.Packages.props`, every resolved package and its content hash in `packages.lock.json` (CI restores in locked mode) |
| Compiler output | deterministic compilation, `PathMap` (no build folder in the binaries), fixed copyright year |
| Xray, sing-box, wintun, geo files, rule sets | official release assets pinned by SHA-256 in `scripts/cores.json` (`scripts/fetch-cores.ps1`) |
| Colitu sidecars (`colitu-engine.exe`, `csl-client.exe`) | rebuilt from a pinned commit of the public colitu/csl repository with a pinned Go release: `scripts/build-sidecars.ps1`, pins in `scripts/cores.json` |
| Line endings | `.gitattributes` gives every checkout LF; `build-installer.ps1` refuses CRLF sources (the updater script is always written with CRLF, whatever the checkout) |
| Installer | Inno Setup 6.7.3; every packaged file carries the commit time, stored as UTC (`TimeStampsInUTC`) |

## The one file that is not in the source

The API mirror origins and the ad-blocking DoH servers are kept out of the
public repository. `scripts/build-installer.ps1` writes them to a single file
next to the program, `colitu-build.json` (read by `ColituBuildValues`), instead
of compiling them in; the executables are the same with or without it. An
installed copy has the file in its install folder, so a rebuild can use it.

## Checking a release

Needs Git for Windows, PowerShell 7, the .NET SDK named in `global.json`,
Go (any recent release; the pinned one is fetched automatically) and
Inno Setup 6.7.3.

```powershell
git clone https://github.com/colitu/windows; cd windows; git checkout v<version>
pwsh scripts/fetch-cores.ps1
pwsh scripts/build-sidecars.ps1
# colitu-build.json from an installed copy of that version:
Copy-Item "C:\Program Files\Colitu VPN\colitu-build.json" .
pwsh scripts/build-installer.ps1 -Version <version> -BuildValuesPath colitu-build.json -SkipManifest
# Compare with the published ColituVPN-Setup-<version>-x64.exe:
Get-FileHash artifacts\installer\ColituVPN-Setup-<version>-x64.exe
```

## Keeping it reproducible

- Moving to a newer .NET SDK: change `global.json` (and install that SDK); the runtime in the app changes with it.
- After changing a package: `dotnet restore` (with `--source https://api.nuget.org/v3/index.json`) and commit the `packages.lock.json` files.
- Moving the sidecars to a newer colitu/csl commit or Go release: change `scripts/cores.json`, run `scripts/build-sidecars.ps1`, put the printed SHA-256 values into `cores.json`.
- In the first release of a new year, move `ColituCopyrightYear` in `src/Directory.Build.props`.
- Never compile build-time secrets into the program; add them to `colitu-build.json` instead.
