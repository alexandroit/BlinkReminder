# Third-party components

BlinkReminder source is licensed separately from the Microsoft runtimes and build tools it uses. Preserve the license and notice files shipped by dependency packages when building distributable artifacts. Do not apply BlinkReminder's source license to bundled Microsoft binary components.

## Runtime components

| Component | Selected version | Purpose | License source |
| --- | --- | --- | --- |
| .NET / Windows Desktop runtime | 10.0.12 | C#, WPF, WinForms tray integration, base libraries | License and third-party notices in the official runtime distribution; [.NET runtime repository](https://github.com/dotnet/runtime) and [WPF repository](https://github.com/dotnet/wpf) |
| Microsoft.WindowsAppSDK.Foundation | 2.3.9 | Local Windows app notifications and runtime integration | `license.txt` in the [official NuGet package](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Foundation/2.3.9) |
| Microsoft.WindowsAppSDK.InteractiveExperiences | 2.1.6 | Required Foundation component dependency, kept at the stable release's version | `license.txt` in the [official NuGet package](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.InteractiveExperiences/2.1.6) |
| Microsoft.WindowsAppSDK.Base | 2.0.4, transitive | Windows App SDK build/deployment support | `license.txt` in the [official NuGet package](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Base/2.0.4) |

These Windows App SDK component versions are the versions included by the stable [Microsoft.WindowsAppSDK 2.4.0 metapackage](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.4.0). Referencing the required components directly avoids including unrelated WinUI, WebView, AI, ML and search modules. The component packages have their own version numbers.

The Windows App SDK NuGet distribution contains Microsoft Software License Terms, even where source repositories publish code under open-source licenses. Redistributed binary components retain their package terms and notices. Review the package's included distributable-code terms before publishing an installer.

Windows operating-system components are not redistributed as BlinkReminder source. Operating-system notifications, installation and diagnostic policies remain controlled by Windows.

## Development and packaging tools

- .NET SDK 10.0.401: build tool; its bundled license and notices apply.
- Microsoft.NET.Test.Sdk 18.10.0 and xunit.runner.visualstudio 3.1.5: test infrastructure. These are not product runtime dependencies.
- xUnit 2.9.3: test framework, [Apache-2.0 license](https://github.com/xunit/xunit/blob/main/license.txt). The restored package's metadata and license remain authoritative for that version.
- Windows SDK build tools: used to generate manifests, package resources, signatures and MSIX files. They are not copied wholesale into product installations.
- Inno Setup: installer compiler. See its [official license](https://jrsoftware.org/files/is/license.txt) and the version pinned by the repository's build scripts.

Exact direct and transitive package identities are recorded in `Directory.Packages.props` and each `packages.lock.json`. When dependency versions change, regenerate the dependency inventory and preserve the new accompanying notices. No icon set, commercial font, photograph, or final third-party logo is included; the generated branding is a neutral project placeholder.
