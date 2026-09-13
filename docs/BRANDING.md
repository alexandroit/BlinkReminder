# Development branding

`BlinkReminder` is provisional. The geometric rings in `assets/branding/placeholder.svg` are neutral development artwork, not an approved product identity. No marketing campaign, social image or final logo has been generated.

Display name, publisher display name, description and links live in `assets/branding/brand.json`. Technical identity lives separately in `packaging/identity.json`: do not change the Inno AppId, notification GUID, startup task ID or an already published package identity when replacing a logo. The app executable remains `BlinkReminder.exe` for technical continuity.

## Source and generated files

The placeholder SVG is the editable vector source. `build/Generate-Assets.ps1` renders its deliberately simple circle geometry using Windows System.Drawing. It is not a general-purpose SVG renderer. For an approved replacement, provide square transparent PNG sources, preferably 1024×1024 or larger, including their intended safe margins:

```powershell
./build/Generate-Assets.ps1 -SourcePng C:\Brand\approved-light.png -DarkSourcePng C:\Brand\approved-dark.png
```

Use an approved vector editor to export complex SVG to PNG first. The script preserves the source aspect ratio, rejects non-square input and never crops it. Leave enough transparent padding for the artwork to remain legible at 16 pixels; inspect actual outputs rather than assuming a large source will downscale well. A light and dark source may differ in contrast while representing the same mark. Omitting the dark source reuses the light source.

| Generated file | Actual use |
| --- | --- |
| `app.ico` | Executable and installer; PNG-compressed frames at 16, 20, 24, 32, 40, 48, 64 and 256 pixels |
| `tray-dark.ico` | Alternative tray resource for contrast-sensitive use |
| `Square44x44Logo.png` | MSIX manifest square logo, 44×44 |
| `Square150x150Logo.png` | MSIX manifest square logo, 150×150 |
| `StoreLogo.png` | MSIX package property logo, 50×50 |
| `icon-light.png`, `icon-dark.png` | 256×256 source previews and reusable app resources |

These are the minimum assets used by this manifest and executable. They are not a claim to cover every Store promotional field or all optional Windows scale/target-size variants. Add and validate qualified resources when final artwork is available. See [Microsoft's current icon construction guidance](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-construction).

Commit reviewed sources and regenerated build inputs together. CI regenerates the neutral placeholder by default. When an approved logo is supplied, update the generator inputs in CI in the same reviewed change; do not let CI overwrite approved artwork with the placeholder. Rebuild both channels and verify the icon on light/dark taskbars, at multiple DPI values, in Installed apps and in the package manifest. Replacing artwork must not touch reminder scheduling or other business logic.
