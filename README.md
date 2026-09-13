# BlinkReminder

Small, offline reminders to blink and take a visual break. Built with C#, WPF and .NET 10 for Windows 11 x64.

BlinkReminder is a habit reminder, not a medical device. It does not measure blinking, diagnose conditions or make health claims.

## Project status

This is an initial open-source implementation. CI builds and tests the application on Windows and produces unsigned engineering installers. Physical Windows acceptance tests, production signing and Microsoft Store submission are separate release gates; see [known limitations](docs/KNOWN-LIMITATIONS.md). Do not treat a passing build as a claim that those gates passed.

## What it does

- Independent blink and visual-break reminders, configurable intervals and durations.
- A small click-through banner that leaves keyboard focus with your current app, or Windows native notifications.
- Work hours, weekdays, overnight quiet periods, manual pauses, snooze and presentation mode.
- Conservative suppression during lock, suspend, protected desktops and Windows interruption suppression, followed by a fresh interval.
- Tray controls, per-user startup consent and one running instance per user/session across EXE and MSIX installations.
- Portuguese (Brazil), English and French (Canada); system, light, dark and high-contrast appearance.
- Validated, atomic local preferences; explicit import/export and optional aggregate counters.

No accounts, telemetry, network requests, AI, camera, microphone or content monitoring. Opening a GitHub link is an explicit user action. Idle detection is off by default and consults only elapsed time since the last input.

## First use

Open the application, review the options and select **Save and enable**. By default, a five-second blink reminder runs every ten minutes; visual breaks, startup, sounds, idle detection and statistics are off. Closing settings leaves the tray app running. **Exit** stops it.

A native notification request does not establish that Windows displayed it or that it was seen. The app does not bypass Windows suppression by switching to banners. A snooze suppresses reminders for five minutes, then starts a full reminder interval.

## Build

Use Windows 11 x64 with the .NET SDK pinned in `global.json`, Windows SDK 10.0.26100.0 and PowerShell 7:

```powershell
./build/Generate-Assets.ps1
./build/Restore.ps1
./build/Build.ps1
./build/Test.ps1
./build/Publish.ps1
./build/Install-InnoSetup.ps1
./build/Package-Exe.ps1
./build/Package-Msix.ps1
./build/Write-ArtifactManifest.ps1
```

The portable scheduler tests also run on macOS/Linux:

```sh
dotnet test tests/BlinkReminder.Core.Tests -c Release
```

The app includes its .NET and Windows App SDK runtime components. Users do not install a separate .NET runtime. WPF compilation, Windows integration tests and installer generation require Windows tools.

## Installation and releases

The EXE installer defaults to the current user without elevation. An explicitly selected all-users installation requires UAC; the app itself always runs with normal user privileges. MSIX is a separate Store/development packaging path. No script automatically publishes to the Store. See [installation](docs/INSTALLATION.md) and [build and release](docs/BUILD-AND-RELEASE.md).

CI artifacts are unsigned validation builds, not Store-ready releases. A real publisher identity, production artwork and signing credentials must be supplied before public binary distribution.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Privacy](docs/PRIVACY.md)
- [Test plan](docs/TEST-PLAN.md)
- [Known limitations](docs/KNOWN-LIMITATIONS.md)
- [Installation](docs/INSTALLATION.md)
- [Build and release](docs/BUILD-AND-RELEASE.md)
- [Microsoft Store submission](docs/STORE-PUBLISHING.md)
- [Branding](docs/BRANDING.md)
- [Technical decisions and primary sources](docs/TECHNICAL-DECISIONS.md)
- [Third-party notices](docs/THIRD-PARTY-NOTICES.md)

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md). Licensed under [MIT](LICENSE).
