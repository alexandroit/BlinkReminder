# Test plan and evidence

## Automated checks

The scheduler and storage tests run without WPF or Windows:

```sh
dotnet test tests/BlinkReminder.Core.Tests/BlinkReminder.Core.Tests.csproj -c Release
```

Use the SDK pinned by `global.json`. If the local NuGet HTTP cache is not writable, set `NUGET_HTTP_CACHE_PATH` to a writable task-specific directory and force a restore; do not disable vulnerability auditing to hide a cache error.

On the supported Windows build host, run the full scripted sequence from the repository root:

```powershell
./build/Restore.ps1
./build/Build.ps1
./build/Test.ps1
```

See [Build and release](BUILD-AND-RELEASE.md) for SDK and installer prerequisites. Test output is written under `artifacts/test-results` by the Windows script.

The core suite checks monotonic intervals despite wall-clock changes; setup and privacy defaults; manual/snooze/session pause coexistence; fresh intervals on resume, settings changes and schedule re-entry; interruption cancellation; no overlap; coincident reminder priority; independent visual-break cadence; overnight working and quiet periods; local-date pauses; optional inactivity; validated/atomic settings round trips; corruption backups; import isolation and bounds; and opt-in, bounded local data.

### Recorded local execution

On 2026-09-13, macOS with .NET SDK 10.0.401 successfully ran the Core tests in Release configuration: **42 passed, 0 failed, 0 skipped**. A forced restore using a writable NuGet HTTP cache completed without the earlier cache-permission warning. This evidence covers the core implementation at that point, not the Windows desktop acceptance matrix. Later source changes require an updated test run; use CI results for the exact published commit.

No Windows desktop session was available for these local checks. A successful cross-platform core test or Windows cross-compilation does not prove installation, notification delivery, accessibility, or focus behavior.

## Windows acceptance matrix

Record the exact Windows build, CPU architecture, installer/package SHA-256, application commit, user privilege context, display scale, and test outcome. “Prepared” means a reproducible path exists; it does not mean the scenario passed.

| Scenario | Required observation | Initial interactive evidence |
| --- | --- | --- |
| First launch | Setup is shown; reminders wait for completion; startup, sound, inactivity and statistics are initially off. Preview does not require waiting a full interval. | Not executed |
| Current-user EXE install | Standard user chooses the default scope; no UAC; files go to the correct user profile. | Not executed |
| All-user EXE install | Explicit all-user choice prompts for administration; protected binaries go to Program Files. | Not executed |
| Alternate UAC credentials | A standard user enters a different administrator account; preferences remain with the standard user; the app is not launched elevated. | Not executed |
| Ordinary launch after global install | Each user runs without elevation and has separate preferences. | Not executed |
| Same-scope upgrade | Existing identity, scope, settings, startup choice and user data remain intact. | Not executed |
| Scope conflict | Existing user/machine installation is detected; installer does not silently duplicate or convert scope. | Not executed |
| MSIX install/update/remove | Package installs under the correct user; stable identity preserves update behavior; package storage rules are respected. | Not executed |
| Banner during typing | Foreground window and text entry remain unchanged before/during/after display; no taskbar or Alt+Tab item. | Not executed |
| Banner interaction | Click-through behavior matches the chosen passive design; actions remain usable from settings/tray. | Not executed |
| Simultaneous reminders | One presentation only; visual break takes priority; no queued burst afterward. | Core state tested; desktop pending |
| Pause, then lock/unlock | Manual pause survives unlock; no banner appears over lock or secure UAC desktop. | Core state tested; desktop pending |
| Suspend/resume | Interrupted presentation is dismissed; a fresh interval starts without catch-up notifications. | Core state tested; hardware pending |
| Manual presentation | All reminders are suppressed independent of conferencing software; turning it off observes a fresh interval. | Core state tested; desktop pending |
| Quiet/working hours | Weekdays, overnight periods, local midnight, DST changes and re-entry behave consistently. | Core calendar cases tested; DST/desktop pending |
| Two monitors | Test mixed DPI, a negative-coordinate secondary monitor, different taskbar positions, and each banner position. | Not executed |
| Display changes | Disconnect/rotate a monitor while visible and while idle; bounds and selected-monitor fallback remain safe. | Not executed |
| Native notifications | Test enabled, blocked and unavailable registration; no custom-banner fallback on OS suppression; activation opens existing settings. | Not executed |
| Startup disabled in Windows | Disable in Settings/Task Manager, restart the app, and save unrelated preferences; startup is not re-enabled. | Not executed |
| Two process launches | Existing instance handles the known request; no second scheduler; other users/sessions remain independent. | Not executed |
| Store/EXE coexistence | Both channels coordinate a single reminder process for the current session; data migration is explicit. | Not executed |
| Explorer restart | Tray icon returns; settings remain reachable; no duplicate icons or timers. | Not executed |
| Corrupted settings | App continues with defaults and a recovery warning; backup is bounded in count; invalid import leaves current preferences intact. | Core storage tested; UI pending |
| Offline operation | Disable networking; reminders/settings continue; no retries or network-dependent startup delays. | Not executed |
| Local cleanup | Statistics, diagnostics and configured reset actions affect only current-user owned files; exported files remain untouched. | Core components tested; UI pending |
| Uninstall | Removes only owned binaries/shortcuts/startup integrations; documents the channel's handling of personal data. | Not executed |
| Corporate policy | AppLocker/WDAC/Store restrictions remain respected; no automatic exception or elevation bypass. | Not executed |
| Exit | Tray Exit ends the process, timers and reminders; no automatic relaunch. | Not executed |

## Accessibility

On a Windows 11 desktop, complete setup, edit each settings area, save, import/export, preview, pause/resume and exit using only the keyboard. Check traversal order, visible focus, validation feedback, and no trapped focus.

Repeat with Narrator and Windows high contrast. Verify accessible names, the currently focused value, reminder status and an accessible path to the last reminder's content. A passive banner being visible is not an accessibility pass. Test the native-notification alternative with the operating system's accessibility settings.

Disable animation in the application and enable Windows reduced-animation preferences. Check that both are respected. Compare light, dark, system and high-contrast themes at 100%, 150% and 200% scaling, with long translated messages in Portuguese, English and French Canadian. Confirm clipping does not hide commands or required information.

All accessibility observations above require interactive verification and initially remain **not executed**.

## Resource and soak checks

The initial targets are average idle CPU below 0.5% and stable resting memory near 100 MB or less on a documented reference machine. These are targets, not measured results.

Measure an installed Release build, not a debugger-attached process. Record private bytes, working set, handle count, thread count and average CPU after a ten-minute warm-up; then repeat after at least eight hours with real reminder presentations, locking, display changes, and settings interactions. Keep a baseline with the application stopped. Record the definition of CPU percentage, including whether it is normalized across logical processors.

Use Windows Performance Recorder/Analyzer or an equivalent documented Windows performance tool to investigate steady CPU work or growth. Count the running application processes and inspect timer/handle growth around repeated preview and settings-window cycles. Confirm that hidden windows do not render continuously and the process does not keep the machine awake.

Initial results: **CPU not measured; memory not measured; long-duration stability not measured**. Installer and installed directory sizes must be taken from real produced artifacts, not inferred from source or an SDK package size.

## Release gates

Automated build/test success, signed artifact verification, Store certification and interactive acceptance are separate gates. Do not label ARM64, Store approval, multi-user/UAC behavior, mixed-DPI focus behavior, or accessibility as passed based only on compilation. Development packages and placeholder branding are not production-signed public releases. Record unresolved checks in [Known limitations](KNOWN-LIMITATIONS.md).

## WPF construction and optional local renders

`BlinkReminder.App.Tests` loads the actual application resources and all six settings tabs, including a quiet-period editor, across three languages and three themes. It checks bindings and rejects invalid numeric drafts without saving or authorizing startup. The test does not run application startup or register notifications.

Content renders are opt-in and local. To inspect them on a Windows development machine, set `BLINK_UI_RENDER_DIRECTORY` to a directory you choose before running the App tests. The application content is rendered at 96 DPI; these images are not evidence of keyboard focus, physical display scaling or screen-reader behavior. CI does not enable or upload image renders. Final branding and Store images require separate approval.
