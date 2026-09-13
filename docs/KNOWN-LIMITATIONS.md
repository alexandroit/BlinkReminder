# Known limitations

BlinkReminder is an early Windows desktop application. Source publication is not Microsoft Store certification or a signed production release. The acceptance matrix in [TEST-PLAN.md](TEST-PLAN.md) is the source of truth for executed tests.

## Platform validation

- The implementation environment is macOS. C# compilation and the portable Core tests can run here; Windows desktop behavior cannot. Windows SDK programs such as `mt.exe`, `makepri.exe`, and `MakeAppx.exe` require Windows.
- Windows 11 x64 is the first distribution target. The Windows build workflow performs automated compilation and integration tests. It does not establish acceptance for real typing, screen readers, mixed DPI, UAC, multiple users, deployment policies, or Store installation.
- ARM64 is not an advertised or packaged target. Windows 10 is not a supported product target. A Windows-version target in the project file is not a promise of support for out-of-support OS editions.
- CPU below 0.5% and roughly 100 MB idle memory are measurement goals, not measured results. Real installer size, installed footprint, and prolonged handle/memory stability must be recorded from the generated release artifacts on a reference Windows machine.

## Presentation and accessibility

`SHQueryUserNotificationState` detects some presentation and busy states. It is not a complete query of every current Windows Do not disturb rule. Unknown or failed state queries suppress reminders conservatively. Choose native Windows notifications when Windows should make the final presentation decision.

Native notification submission means only that Windows accepted a request. It does not prove display, reading, or a real blink. Blocked notifications do not fall back automatically to a banner. Registration and submission failures are reported without discarding settings.

The banner uses a small layered, nonactivating tool window. Click-through requires both the native layered/transparent styles and input handling; visual transparency alone is insufficient. The automated HWND test checks styles and a hidden positioning operation. It does not prove behavior while someone types in another application. A passive banner is not a substitute for the accessible main window or native notification mode.

Protected input desktops, locked sessions, and suspension are suppressed. No overlay on UAC or the lock screen is supported. Idle detection, when enabled, reads only elapsed time since last input. Someone may be reading without typing or moving a mouse.

## Startup, coexistence, and data

The packaged channel uses `StartupTask`. The EXE channel uses the current user's `Run` key with explicit consent. Windows has no documented public API for all legacy `StartupApproved` details: the adapter only reads recognized records and treats unfamiliar records as unavailable. It never edits those records or tries to re-enable a Windows-disabled entry. This read-only compatibility check requires maintenance if Windows changes the format.

MSIX and EXE use distinct preference directories. Export/import is the explicit migration path. A user/session-specific named object and a current-user-only named pipe avoid simultaneous reminders from two channels; they do not uninstall or merge either installation. IPC accepts only known commands. Different users and sessions have independent app state.

Global installation changes where binaries are stored, not app privileges or ownership of preferences. A global uninstall must not traverse other profiles. MSIX removal is managed by Windows and does not provide a custom uninstall preference dialog.

## Notifications and deployment

The selected Windows App SDK component versions come from stable release 2.4.0. They are deployed beside the EXE and inside the MSIX, together with the .NET runtime. Unused WinUI, WebView, AI, ML and search packages are excluded.

The Microsoft self-contained deployment guide updated 2026-09-11 says local `AppNotificationManager` APIs do not require the Singleton package. Older API reference pages still contain contradictory Singleton boilerplate. The implementation follows the current deployment guide; clean-machine tests for both channels remain required. No push service or online notification dependency is used.

Production Authenticode certificates, trusted timestamps, Store identity reservations, publisher details, final logo, real listing screenshots, account access, and certification remain external release prerequisites. A signature does not guarantee that SmartScreen will show no warning. Development artifacts must be labeled as such.

## Optional features outside this version

There are no global keyboard hooks, global shortcut configuration, meeting-content inspection, camera-based blink measurements, app self-updater, system service, or hidden recovery task. The app must remain running for banners and scheduled reminders. EXE updates use the installer; Store updates use the Store.
