# Build and release

Use Windows 11 x64 and PowerShell 7. Install the .NET SDK specified by `global.json` (10.0.401), Windows SDK 10.0.26100.0 and Inno Setup 7.1.0 x64. Visual Studio / Build Tools with the .NET desktop workload is a convenient way to install desktop development tools; the scripts use the .NET CLI and Windows SDK directly. No Visual Studio instance is required at application runtime.

`build/tool-versions.json` records the SDK/runtime/compiler pins and the Inno installer checksum. `Install-InnoSetup.ps1` downloads the exact official release and verifies its SHA-256 and Authenticode publisher before running it. Installing a compiler may require administrator permission; that is a development-machine operation, independent of application install scope.

From the repository root:

```powershell
./build/Generate-Assets.ps1
./build/Restore.ps1
./build/Build.ps1
./build/Test.ps1
./build/Publish.ps1
./build/Install-InnoSetup.ps1 # only if this compiler is not already installed
./build/Package-Exe.ps1
./build/Package-Msix.ps1
./build/Write-ArtifactManifest.ps1
```

`Package-Exe.ps1 -CompilerPath C:\Tools\Inno\ISCC.exe` supports a custom compiler location. Scripts stop on native command failure. Packaging uses MakeAppx validation; it does not pass `/nv` to suppress validation.

Outputs:

| Path | Meaning |
| --- | --- |
| `artifacts/publish/win-x64/` | Self-contained application folder, including both runtimes |
| `artifacts/installers/BlinkReminder-0.1.0-win-x64-setup.exe` | Direct installer, unsigned unless explicit signing was used |
| `artifacts/installers/BlinkReminder-0.1.0.0-win-x64-development-unsigned.msix` | Unsigned development package; not a Store submission |
| `artifacts/installers/artifact-manifest.json` | Actual artifact sizes, SHA-256 values, source commit and published payload bytes |
| `artifacts/test-results/` | Test results in TRX format |

Publish also collects original license/notice files from restored NuGet packages and the bundled .NET runtime packs into `Licenses/`, together with an index of restored dependencies. That index includes build dependencies and does not imply that each listed package ships runtime code. Missing required runtime license files stop publication staging.

The manifest's `publishedPayloadBytes` measures the publish folder, not an installed application. The installer smoke report separately records `installedFileBytes` and `installedFileCount` from each actual installation directory, including the uninstaller. These are logical file lengths; filesystem allocation, OS deduplication and per-user data can change disk usage. CI artifacts and logs are the evidence of a particular build. Compilation alone does not validate UAC, user switching, Store certification, screen-reader behavior or the passive banner on a real desktop.

## Version and dependency maintenance

The product version comes from `Directory.Build.props`. MSIX adds a fourth `.0` component. Keep the package name, publisher, AppId, startup task ID and notification activation GUID stable after their first public release. Changing display text or artwork does not require new technical identifiers.

Both .NET and the selected Windows App SDK components are bundled. Foundation 2.3.9 and InteractiveExperiences 2.1.6 are taken from stable Windows App SDK 2.4.0; unused AI/ML, WinUI, WebView and Search components are excluded. Framework security patches are not automatically inherited from a separately installed runtime: update the pins, review dependencies, rebuild and retest both installers when servicing releases are available. Do not strip, trim or force single-file publishing without validating WPF and Windows App SDK requirements.

NuGet direct versions are pinned in `Directory.Packages.props`, and project lockfiles pin the resolved dependency graphs. Normal restore uses locked mode, and publish reuses that restored graph without restoring again. For an intentional dependency update, run `./build/Restore.ps1 -UpdateLockFiles`, review every lockfile diff and commit it alongside the version change. `dotnet list BlinkReminder.slnx package --vulnerable --include-transitive` is an available dependency review command, requiring access to advisory metadata.

## Unsigned CI

`.github/workflows/windows-ci.yml` builds and tests on a Windows runner and uploads EXE/MSIX engineering artifacts. It has read-only repository permissions and no signing or Store credentials. It also installs, upgrades, checks observable opposite-scope conflicts and uninstalls the EXE on the disposable runner. `artifacts/test-results/installer-smoke.json` records whether the runner already had administrator privileges. This is not an interactive UAC test or proof of ordinary-user behavior. To run the same destructive-to-test-installation check manually on a clean disposable machine, use `./build/Test-Installer.ps1 -DisposableMachine`. It refuses an existing app installation or directory.

It never creates a GitHub Release or submits to a store. Action implementations are pinned by commit to verified stable releases: checkout 7.0.1, setup-dotnet 6.0.0 and upload-artifact 7.0.1. These use Node 24; a self-hosted replacement must meet their runner requirements (at least 2.327.1 for checkout).

## Direct-distribution signatures

Before signing, replace the publisher placeholder, validate the release on a clean Windows machine and configure an approved code-signing certificate. A software PFX is supported by the sample workflow; a hardware/service-based signing provider needs a separate reviewed integration. Do not commit a private key or production certificate container.

For a signing certificate already installed in the current user's certificate store:

```powershell
./build/Sign-DirectRelease.ps1 -CertificateThumbprint YOUR_40_CHARACTER_THUMBPRINT -TimestampUrl https://YOUR_RFC3161_SERVICE
./build/Write-ArtifactManifest.ps1
```

This signs project-owned application binaries, verifies them, asks Inno to sign its embedded uninstaller and installer, and verifies the final installer. Dependency binaries retain their upstream identities. `signtool verify /pa /all /v <file>` is the verification command. An RFC 3161 timestamp is required. Signing proves publisher/integrity information; it does not guarantee SmartScreen reputation or removal of every warning.

The separate `signed-build.yml` workflow requires all of the following: a manual dispatch on `main`, `authorize_signing=true`, and the `release-signing` environment. Configure that environment with required reviewers and a branch restriction before adding `SIGNING_CERTIFICATE_BASE64`, `SIGNING_CERTIFICATE_PASSWORD` and the `TIMESTAMP_URL` variable. Without the environment's protection rules, GitHub will not provide the intended human review gate. The job imports the certificate only into the ephemeral runner's user store, deletes the temporary PFX and private key, and uploads artifacts for review. It does not publish them.

Store MSIX signing is a different channel and is handled through the Store submission process. The EXE signing script does not sign or publish MSIX. Production distribution remains a deliberate release-owner action after review.
