param(
    # Version of the installer the manifest points at (X.Y.Z).
    [Parameter(Mandatory = $true)][string]$Version,
    # The released installer: its SHA-256 goes into the manifest.
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    # Hide "Later" in the update dialog (a required update).
    [switch]$ForceUpdate,
    [string]$SigningKeyPath = $(if ($env:COLITU_UPDATE_SIGNING_KEY) { $env:COLITU_UPDATE_SIGNING_KEY } else { Join-Path $PSScriptRoot "..\..\_gizli_anahtarlar\colitu-windows--update-signing-private.pem" }),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\artifacts\installer\latest.json")
)
# Writes downloads/windows/latest.json for an installer that is already built, e.g. to make a
# release a required update afterwards. Same manifest and signatures as build-installer.ps1.
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Run this script with PowerShell 7 (pwsh): signing the update manifest needs .NET's PEM support."
}
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$versionParts = $Version.Split('.') | ForEach-Object { [int]$_ }
$versionCode = ($versionParts[0] * 100) + ($versionParts[1] * 10) + $versionParts[2]
$downloadUrl = "https://colitu.com/downloads/windows/ColituVPN-Setup-$Version-x64.exe"
$sha256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$force = $ForceUpdate.IsPresent
$issuedAt = [DateTime]::UtcNow
$issuedAtText = $issuedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", [System.Globalization.CultureInfo]::InvariantCulture)
$expiresAtText = $issuedAt.AddDays(30).ToString("yyyy-MM-ddTHH:mm:ssZ", [System.Globalization.CultureInfo]::InvariantCulture)

# Same text as ColituUpdateSignature.Message in the app; any change there must be mirrored here.
$signedText = @(
    "colitu-windows-update-v1",
    $versionCode.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    $Version,
    $downloadUrl,
    $sha256,
    $(if ($force) { "true" } else { "false" })
) -join "`n"
$signedTextV2 = @($signedText, $issuedAtText, $expiresAtText) -join "`n"
$signingKey = [System.Security.Cryptography.ECDsa]::Create()
try {
    $signingKey.ImportFromPem((Get-Content -LiteralPath $SigningKeyPath -Raw))
    $signature = [Convert]::ToBase64String($signingKey.SignData([System.Text.Encoding]::UTF8.GetBytes($signedText), [System.Security.Cryptography.HashAlgorithmName]::SHA256))
    $signatureV2 = [Convert]::ToBase64String($signingKey.SignData([System.Text.Encoding]::UTF8.GetBytes($signedTextV2), [System.Security.Cryptography.HashAlgorithmName]::SHA256))
}
finally {
    $signingKey.Dispose()
}

$manifest = [ordered]@{
    latestVersionCode = $versionCode
    versionName = $Version
    downloadUrl = $downloadUrl
    sha256 = $sha256
    forceUpdate = $force
    releaseNotes = ""
    issued_at = $issuedAtText
    expires_at = $expiresAtText
    signature = $signature
    signature_v2 = $signatureV2
}
# A manifest signed with the wrong key would be ignored by every installed copy without any error.
$appSource = Get-Content -LiteralPath (Join-Path $repoRoot "src\ColituVPN\Services\ColituUpdateService.cs") -Raw
$publicKeyPem = [regex]::Match($appSource, '-----BEGIN PUBLIC KEY-----[\s\S]+?-----END PUBLIC KEY-----').Value -replace '(?m)^\s+', ''
$verifyKey = [System.Security.Cryptography.ECDsa]::Create()
try {
    $verifyKey.ImportFromPem($publicKeyPem)
    if (-not $verifyKey.VerifyData([System.Text.Encoding]::UTF8.GetBytes($signedText), [Convert]::FromBase64String($signature), [System.Security.Cryptography.HashAlgorithmName]::SHA256) `
        -or -not $verifyKey.VerifyData([System.Text.Encoding]::UTF8.GetBytes($signedTextV2), [Convert]::FromBase64String($signatureV2), [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
        throw "latest.json does not verify against the app's public key: wrong signing key ($SigningKeyPath)."
    }
}
finally {
    $verifyKey.Dispose()
}
$manifest | ConvertTo-Json | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Manifest: $OutputPath (version $Version, forceUpdate $force, expires $expiresAtText)"
