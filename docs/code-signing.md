# Code signing (Azure Artifact Signing)

`scripts/build-installer.ps1 -CodeSign` Authenticode-signs:

- every `.exe` and `.dll` in the package that has no valid signature yet (ColituVPN.exe,
  ColituKillSwitchService.exe, the WPF/SQLite native DLLs, xray.exe, sing-box.exe,
  libcronet.dll, colitu-engine.exe, csl-client.exe; wintun.dll keeps WireGuard LLC's signature);
- the setup and its uninstaller, through Inno Setup's `SignTool` (`#ifdef ColituSign` in
  `installer/ColituVPN.iss`).

It checks every signature (`Get-AuthenticodeSignature`) and stops on the first one that is not
valid. The Microsoft Store accepts an EXE installer only when it and all its PE files are signed by
a CA in the Microsoft Trusted Root Program.

## One-time setup

1. Azure subscription → register the `Microsoft.CodeSigning` resource provider.
2. Create an Artifact Signing account (Basic SKU, region North Europe or West Europe) and give your
   user the roles *Artifact Signing Identity Verifier* and *Artifact Signing Certificate Profile Signer*.
3. Identity validation → Organization → Public. Use COLITU LIMITED's legal name, address and
   company number, and two monitored mailboxes on colitu.com. Enter the Store Seller ID from Partner
   Center. Processing takes 1–20 business days.
4. Certificate profile → Public Trust, on that validation.
5. Install the client tools (SignTool, .NET 8 runtime, dlib):
   `winget install -e --id Microsoft.Azure.ArtifactSigningClientTools`,
   or `nuget install Microsoft.Windows.SDK.BuildTools -x` and `nuget install Microsoft.ArtifactSigning.Client -x`.
6. Write `_gizli_anahtarlar/colitu-artifact-signing.json` (outside the repository):

   ```json
   {
     "Endpoint": "https://neu.codesigning.azure.net",
     "CodeSigningAccountName": "<account>",
     "CertificateProfileName": "<profile>",
     "ExcludeCredentials": ["ManagedIdentityCredential", "WorkloadIdentityCredential", "SharedTokenCacheCredential", "VisualStudioCredential", "VisualStudioCodeCredential", "AzurePowerShellCredential", "AzureDeveloperCliCredential", "InteractiveBrowserCredential"]
   }
   ```

   The endpoint must match the account's region, or signing fails with 403.

## Signing a release

```powershell
az login
$env:COLITU_SIGNING_DLIB = "<...>\bin\x64\Azure.CodeSigning.Dlib.dll"
# optional: $env:COLITU_SIGNTOOL (default: newest x64 signtool.exe in Windows Kits)
pwsh scripts/build-installer.ps1 -Version 2.9.9 -CodeSign
```

Each signature needs the network (Azure + timestamp.acs.microsoft.com). The certificates are valid
for three days; the RFC 3161 timestamp keeps the signatures valid afterwards.

## Reproducible builds

Signatures and timestamps differ on every run, so a signed release is not byte-identical to a
rebuild. To compare, remove the signatures from both files (`signtool remove /s <file>`) or compare
an unsigned rebuild (`-SkipManifest`, no `-CodeSign`) file by file with the installed copy after
stripping its signatures.
