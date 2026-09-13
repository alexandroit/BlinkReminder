# Technical decisions

Reference and consultation date: **2026-09-13**. Stable releases are required. Revalidate maintenance versions and OS support before distributing a later release.

## Implementation choices

- C# / WPF with a small MVVM layer keeps the app native and avoids a browser, local server or resident privileged service. Scheduling belongs to a plain .NET core library with an injected clock, independent of WPF and Win32.
- .NET 10 is the selected LTS line. The build uses SDK 10.0.401 and self-contained runtime 10.0.12. Runtime servicing requires a rebuilt installer because the runtime ships with the app.
- Windows 11 x64 is the first target. Build 22000 is only a technical lower bound. Product support requires a Windows release still supported by Microsoft. ARM64 and Windows 10 are not qualified distribution targets.
- The stable Windows App SDK 2.4.0 release provides `Microsoft.Windows.AppNotifications`. The project references its Foundation 2.3.9 and InteractiveExperiences 2.1.6 components directly, excluding unused AI/ML, WinUI, WebView and Search modules. These exact versions come from the 2.4.0 metapackage dependency graph; Base.targets supports self-contained component deployment. The application executable uses `WindowsAppSDKSelfContained=true`, `SelfContained=true`, `WindowsPackageType=None`, no trimming and no single-file publishing. It is packaged afterward with MakeAppx, and determines package identity at runtime. The current self-contained deployment guide explicitly excludes AppNotificationManager from the Singleton dependency; clean-machine notification testing remains necessary.
- MSIX uses a normal desktop process (`Windows.FullTrustApplication`) with `runFullTrust`. The capability supports WPF/Win32 integration; it does not request an administrator token. Startup is declared disabled and enabled only through an explicit per-user action.
- Direct installation uses Inno Setup 7.1.0 x64, an official stable release dated 2026-08-12. `PrivilegesRequired=lowest` plus `PrivilegesRequiredOverridesAllowed=dialog` makes the current user the default and exposes the supported explicit global choice. `UsePreviousPrivileges=yes` keeps upgrade scope; additional registry checks reject observable cross-scope conflicts. Shared files stay under Program Files and never contain per-user preferences.
- No installer launches the app on completion. This avoids both elevated execution and preference writes in a different account used to satisfy UAC. Global uninstall does not enter users' profiles. Its limited ability to clean per-user integrations is documented in INSTALLATION.md.
- The Store channel is MSIX, with Store-managed signing/distribution/updates. A Store submission of an EXE/MSI has different update obligations and is intentionally not introduced as a third distribution flow.
- Neutral geometric artwork is a build input only. Display branding is separate from immutable technical identity. No final identity, publisher, support domain or certification approval is invented.
- Ordinary CI produces unsigned test artifacts only. Signing is a separate manually dispatched workflow requiring an explicit boolean and a protected environment. Publication remains a separate authorized owner action.

## Official sources

“Not shown” means the consulted page did not expose an update date; it is not an inferred date.

| Topic / source | Page update or release date | Decision supported |
| --- | --- | --- |
| [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) | 2026-09-08 | .NET 10 LTS and current servicing line |
| [.NET 10 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) | Release 2026-09-08 | SDK 10.0.401 / runtime 10.0.12 pins |
| [Windows App SDK package dependency graph](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.4.0) | Stable 2.4.0 package | Foundation 2.3.9 and InteractiveExperiences 2.1.6 component selection |
| [Windows App SDK stable downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads) | See selected stable release entry | Stable Windows App SDK instead of preview packages |
| [Windows App SDK self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps) | 2026-09-11 | Bundled app runtime; local notification Singleton distinction |
| [WPF app notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet?pivots=wpf) | 2026-05-08 | Register/activation/unregister; packaged COM extensions; no elevated notifications |
| [Packaged desktop execution](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes) | 2025-09-09 | Per-user package registration and read-only package files |
| [Packaging overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/) | 2026-08-29 | MSIX versus EXE submission/update models |
| [First Windows app publication](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app) | 2026-08-29 | Separate Store and direct signing/distribution responsibilities |
| [MakeAppx tool](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool) | Not shown in the consulted extract | Validated package creation; MSIX differs from msixupload |
| [desktop:StartupTask schema](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-startuptask) | 2026-06-17 | Disabled-by-default startup task declaration |
| [Extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) | Not recorded | Non-activating tool-window banner |
| [SHQueryUserNotificationState](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate) | Not recorded | Documented suppression signal, without claiming perfect Do Not Disturb detection |
| [Inno Setup stable downloads](https://jrsoftware.org/isdl.php) | Release 2026-08-12 | Inno Setup 7.1.0 x64 |
| [Inno release and asset digest](https://github.com/jrsoftware/issrc/releases/tag/is-7_1_0) | Release 2026-08-12 | Pinned compiler installer SHA-256 |
| [Inno install-mode overrides](https://jrsoftware.org/ishelp/topic_setup_privilegesrequiredoverridesallowed.htm) | Not shown | Supported scope dialog, CURRENTUSER and ALLUSERS |
| [Inno previous scope](https://jrsoftware.org/ishelp/topic_setup_usepreviousprivileges.htm) | Not shown | Upgrade install-mode preservation |
| [Inno architecture identifiers](https://jrsoftware.org/ishelp/topic_archidentifiers.htm) | Not shown | Native x64-only qualification boundary |
| [Windows app icon construction](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-construction) | Not recorded | Validate real icon resources; do not equate manifest assets with Store listing images |

## Validation boundaries

Build success establishes compilation and package construction, not behavioral acceptance. Manual evidence must cover no-focus banners, DPI changes, UAC with another account, install scope upgrades, clean runtime deployment, Windows-disabled startup and notifications, high contrast and screen readers. Long-running CPU/memory measurements require an identified reference machine and recorded observations. No measurements or certification results are inferred from architecture choices.
