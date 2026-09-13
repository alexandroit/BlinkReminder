# Installation

The supported target is Windows 11 x64 on a Windows release still supported by Microsoft. A Windows 11 build number check is a technical minimum, not a promise that an obsolete Windows release is supported. Windows 10 and ARM64 have not been qualified.

Engineering artifacts from GitHub Actions are unsigned. They are useful for controlled validation, and are not a signed production release or a Microsoft Store listing. Do not disable Windows security policies to run them.

## EXE installation

Open `BlinkReminder-0.1.0-win-x64-setup.exe` normally. The default option is **Somente para mim — recomendado**. It installs below `%LOCALAPPDATA%\Programs\BlinkReminder`, without requesting administrator rights. The alternative **Para todos os usuários deste computador — requer administrador** asks for elevation and installs below `%ProgramFiles%\BlinkReminder`.

The installer contains the application and its .NET and Windows App SDK runtime files. No global .NET installation is required. The download is one installer EXE; the installed application intentionally contains multiple files.

Neither installation mode starts the application automatically. Open it from the Start menu as your normal user. Onboarding, settings and startup consent then belong to that user. Supplying another account at UAC never causes an application launch or a preferences write in that administrator's profile.

Updates use the same installer. The stable Inno AppId and `UsePreviousPrivileges=yes` preserve the previous scope. An explicit conflicting scope is rejected: uninstall the old installation first, then deliberately select the new scope. Settings are retained by EXE uninstall. The destination cannot be moved outside the standard directory for its scope; this prevents placing shared binaries in a user-writable directory.

The installer checks machine registration and loaded user registry hives for cross-scope conflicts. It does not open other users' profile files or load their offline registry hives. A machine administrator must inventory installations belonging to signed-out users before moving the machine to a global deployment. Runtime single-instance coordination additionally prevents Store and EXE copies from running simultaneous reminder loops in the same user session.

## Silent installation for IT

Run from the initiating user's normal session for per-user installation:

```powershell
.\BlinkReminder-0.1.0-win-x64-setup.exe /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG
```

For an intentionally administrative deployment:

```powershell
.\BlinkReminder-0.1.0-win-x64-setup.exe /ALLUSERS /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG
```

`/ALLUSERS` does not bypass UAC. An IT deployment system may provide an already authorized administrative context. Success uses exit code 0; treat other codes as failure or a documented restart condition. Retain and inspect the Inno log when deployment fails. Never place credentials in a command line.

The app creates no Windows service, privileged scheduled task, driver or firewall rule. Shared binaries inherit Program Files protections. Preferences remain individual. Startup is disabled initially and, after consent, uses the current user's Windows startup integration only.

AppLocker, WDAC, Smart App Control, Store restrictions and other organization policy take precedence. Ask IT to review an allowed distribution path when blocked. Per-user installation is not a policy bypass.

## Microsoft Store / MSIX

The Store channel uses MSIX with Windows-managed installation and updates. Normal registration is per user; there is no all-users selector inside the package. Files are stored in the package location controlled by Windows and are read-only to the application. Administrative provisioning through supported Windows/MDM tooling is a separate IT operation, subject to licensing and package availability. Provisioning a package is not the same as immediately registering it for every existing user. See [Microsoft's MSIX deployment documentation](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes).

The current repository has a development package identity. An unsigned `development-unsigned.msix` cannot be installed as a normal trusted package. Use a deliberately signed and explicitly trusted development package on a disposable test machine; do not install a test certificate globally or change trust policy silently. See [Store publishing](STORE-PUBLISHING.md).

## Removal and channel migration

Use Windows **Settings > Apps > Installed apps**. The EXE uninstaller removes its binaries, shortcut and uninstall entry. In an ordinary per-user, non-elevated uninstall it asks the app to remove its own startup/notification registrations. EXE settings are retained; use the app's local-data controls before uninstalling to remove personal settings. No other user's data is deleted.

A global uninstall deliberately does not launch an app under the administrator's profile or visit every user's profile. Before global removal, each user should disable startup and exit the app. User registrations in signed-out profiles cannot be cleaned by this installer and may require that user's later cleanup. This limitation must be included in enterprise deployment planning.

Windows manages MSIX package data removal. There is no custom MSIX uninstall preference dialog. Export settings explicitly before removing a channel if you want to transfer them. Install the other channel, import using its settings UI, and remove the old channel when ready. Neither channel silently removes the other.
