# Microsoft Store preparation

There is no Store listing, reserved identity or certification approval implied by this repository. The current MSIX uses an explicit development identity. Do not upload that identity or the neutral placeholder artwork as an approved public brand.

## Identity and package

Reserve the product in Partner Center. Copy its exact identity name and publisher distinguished name into `packaging/identity.json`, and set `storeIdentityConfigured` to `true`. Set the real publisher display name and support/privacy URLs in `assets/branding/brand.json`. Reserve and verify the final identity before the first Store release; changing a published identity creates a different app and breaks normal update continuity. Keep development validation packages separate from the production identity.

The same `BlinkReminder.exe` code serves both distributions. At runtime, adapters detect package identity and select package storage and startup APIs. The manifest declares one desktop process, one disabled startup task and the COM activation required for local Windows app notifications. `runFullTrust` is necessary for the WPF/Win32 process and desktop integrations; it denotes a normal desktop process, not administrative elevation. No network, microphone, camera, broad filesystem or privileged-startup capability is requested.

After configuring the identity and brand fields:

```powershell
./build/Publish.ps1
./build/Package-Msix.ps1 -ForStore
./build/Write-ArtifactManifest.ps1
```

This creates `artifacts/installers/BlinkReminder-0.1.0.0-win-x64-store-unsigned.msix`. MakeAppx creates a package, not an `.msixupload` container. Partner Center supports package submission; use its current accepted-package instructions and Visual Studio packaging if the chosen submission requires the recommended upload container. Do not rename a ZIP/MSIX to fake that container. The Store handles distribution signing; development sideload signing is separate. See [MakeAppx](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool) and [publishing guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app).

The bundled runtime and Windows App SDK files must pass certification and clean-machine testing. `AppNotificationManager` is a local notification API; the current self-contained deployment guidance says it does not require the Singleton package. Test both first registration and activation on a machine without development runtimes. No push-notification infrastructure is used.

## Explicit local development signing

For a disposable test machine, use an existing development code-signing certificate whose private key is accessible in `Cert:\CurrentUser\My`. Its subject must match the package publisher exactly (`CN=BlinkReminder.Development` in the initial development configuration). The following example signs a copy; it does not create a certificate, import a trust anchor or change machine policy:

```powershell
$certificate = Get-Item 'Cert:\CurrentUser\My\YOUR_40_CHARACTER_CERTIFICATE_THUMBPRINT'
$identity = Get-Content ./packaging/identity.json -Raw | ConvertFrom-Json
if (-not $certificate.Subject.Equals($identity.packagePublisher, [StringComparison]::Ordinal)) {
    throw 'The certificate subject must match the package Publisher exactly.'
}
$signtool = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
$unsigned = './artifacts/installers/BlinkReminder-0.1.0.0-win-x64-development-unsigned.msix'
$signed = './artifacts/installers/BlinkReminder-0.1.0.0-win-x64-development-signed.msix'
Copy-Item $unsigned $signed
& $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint /tr 'https://YOUR_RFC3161_TIMESTAMP_SERVICE' /td SHA256 $signed
if ($LASTEXITCODE -ne 0) { throw 'Development package signing failed.' }
& $signtool verify /pa /v $signed
if ($LASTEXITCODE -ne 0) { throw 'The package signature is not trusted on this machine.' }
# Only after the signature verifies and installation is permitted by test-machine policy:
Add-AppxPackage -Path $signed
```

A self-signed development certificate is not a production identity and is not inherently trusted. Arrange trust deliberately through the test-machine owner if needed; these commands never install a certificate or bypass an existing policy. Keep the development identity separate from the reserved Store identity. See [Microsoft's SignTool guidance for app packages](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool).

## Listing material that remains to be provided

- Approved product name, publisher/legal identity, reserved package identity and developer account.
- Real support destination and public privacy policy describing the offline, per-user behavior.
- Product description and keywords in Portuguese (Brazil), English and French (Canada), without medical efficacy claims.
- Real screenshots of the built app, including settings and a representative banner. The neutral development icon is not an approved marketing asset.
- Final visual resources, age rating questionnaire, markets, availability and any accessibility declarations supported by actual testing.
- Certification notes explaining `runFullTrust`, optional per-user startup, local notification registration, no account requirement and no privileged services.

Follow the image fields and validation shown by the current Partner Center listing form. The repository's small manifest assets do not replace screenshots or all Store editorial image requirements.

## Validation and submission

Run the Windows App Certification Kit and the manual acceptance matrix in `TEST-PLAN.md` on the candidate package. Check install/update/removal, startup consent, user-disabled startup, notifications disabled by Windows, package storage, coexistence with EXE, high contrast and reader navigation. Record results and the exact package hash. A successful MakeAppx run only validates package construction.

Submit manually after release-owner authorization. No workflow in this repository logs in to Partner Center or uploads a submission. Store approval and staged rollout are external steps and must be recorded as such.

Normal MSIX installation is per user. Administrative provisioning and managed deployment use Windows/MDM facilities; the package must not invent an all-users UI. Follow organization's licensing and deployment policies. For direct-download EXE installs, manual installer-based updates are separate. The alternative Store MSI/EXE submission model exists but is intentionally not implemented as a third channel here.
