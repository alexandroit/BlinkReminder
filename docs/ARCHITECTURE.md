# Architecture

BlinkReminder is a per-user Windows desktop application. The application process owns reminders; there is no Windows service, privileged scheduled task, browser, local HTTP server, or background updater.

## Projects and responsibilities

| Project | Responsibility |
| --- | --- |
| `BlinkReminder.Core` | Validated settings, reminder scheduling, local JSON storage, optional aggregate statistics, and bounded diagnostic event codes. Targets `net10.0`; it does not reference WPF, Win32, or Windows App SDK. |
| `BlinkReminder.Windows` | Session and presentation state, monitor positioning, startup registration, package-aware user paths, native notifications, and one instance per user and session. |
| `BlinkReminder.App` | WPF windows, accessible settings, localized resources, tray actions, banner presentation, and the application lifecycle. Maps Windows state into the core scheduler. |
| `BlinkReminder.Core.Tests` | Deterministic time, schedule, storage, validation, and privacy tests that can run without Windows. |
| `BlinkReminder.Windows.Tests` | Windows integration contracts and adapter tests. These do not replace interactive desktop acceptance testing. |
| `BlinkReminder.App.Tests` | Loads actual WPF resources, all tabs and a quiet-period template across three languages and themes; checks draft validation and produces optional CI content renders. |

The EXE and MSIX channels publish the same application. `ApplicationPaths` and `WindowsStartupAdapter` select the appropriate integration from the process package identity. Display branding is independent from the stable technical identity used for startup, packaging, and single-instance coordination.

## Scheduler

`ReminderScheduler` is a synchronous state machine, protected by one lock. It does not create timers. `ReminderHost` owns one scheduler and one 15-second `PeriodicTimer`, dispatching `Evaluate(SessionSnapshot)` onto the WPF UI thread and also refreshing immediately when Windows session/display events arrive. This means a due interval can be presented up to one polling period later. UI presentation may have its own lifetime/countdown mechanism; that does not create a second reminder schedule.

`TimeProvider.GetTimestamp()` measures intervals, finite manual pauses, and snoozes. `GetLocalNow()` evaluates working days, quiet periods, and the “until tomorrow” action. Moving the wall clock does not shorten an elapsed reminder interval. Time zones and local time still intentionally affect calendar rules. Windows time-change events clear the cached local time zone and restart reminder intervals; existing manual pauses remain in place.

The scheduler returns at most one `ReminderRequest` with a unique identifier. It keeps that request as `CurrentReminder` until completion or cancellation. Further evaluations cannot create another presentation. The host dismisses its visible presentation when the scheduler clears `CurrentReminder`, and calls `CompletePresentation()` when a presentation finishes normally. A cancellation generation prevents completion of an old banner from clearing a newer presentation. Explicit previews wait for the previous presentation to close, preserve manual pause settings, and start fresh intervals afterward. Preview can bypass setup and calendar timing, but cannot bypass a protected, busy, unknown, or presentation session state.

Pause reasons are flags that coexist:

- Initial setup is incomplete, or both reminder types are disabled.
- A finite or indefinite manual pause, and an independent snooze.
- Outside the working schedule, or inside a quiet period.
- Locked or suspended session, manual/system presentation mode, Windows suppression, unknown notification state, or optional inactivity.
- A reminder is currently being presented.

Unlocking or returning from a Windows suppression cannot remove an existing manual pause. `Resume()` is an explicit user action that clears manual pause and snooze; it does not override the session, work, quiet-period, or inactivity rules.

Protected or unknown session states are suppressed conservatively. Lock and suspend safety remains enforced even if a settings file contains `pauseOnLockOrSuspend: false`; this flag is not permission to draw on a protected desktop. A detected return from suspension cancels any interrupted presentation and starts fresh intervals.

### Timing rules

The initial process start, settings changes, explicit reset, and re-entry after suppression begin a complete interval. Reminders missed while paused are discarded. A snooze suppresses reminders for the requested duration; when it ends, a complete new interval starts. It does not queue a reminder for the exact snooze deadline.

Blink and visual-break intervals are independent. When both are due, the visual break wins and satisfies the coincident blink reminder. A short blink presentation does not continually restart the visual-break interval. On completion, the presented reminder gets a new interval. An alternative reminder that became overdue during a long presentation is discarded, preventing immediate backlog delivery.

Calendar intervals include their start and exclude their end. An overnight interval belongs to its starting weekday: Monday 22:00–02:00 includes early Tuesday until 02:00, but does not enable the same period on early Monday. Equal start/end means the whole selected calendar day. Quiet periods work independently of whether the working schedule is enabled.

## Settings and local data

`AppSettings` is schema version 1. Its mutable public properties support WPF editing. The scheduler validates and clones settings when constructed or updated, so partially edited UI objects cannot mutate a running schedule.

Validation accepts supported languages and named enum values, intervals of 1–1440 minutes, presentation durations of 2–600 seconds, nonblank messages up to 240 characters, margins of 0–200 logical units, font sizes of 12–36, inactivity thresholds of 1–240 minutes, and up to 16 quiet periods. Days must be distinct and nonempty. Imported settings must include the schema version; unsupported versions, unknown properties, invalid types, and excessive depth are rejected.

`ISettingsStore` is implemented by `JsonSettingsStore`. The directory is injected rather than inferred from installation scope. JSON reads are bounded at 256 KiB. Saves use a temporary file in the same directory, flush it, and replace the destination. Invalid saved settings recover to defaults and are retained in one `settings.corrupt.json` backup when possible. Read and backup failures return a warning rather than terminating the scheduler.

Import validates without changing current or stored preferences. The application explicitly saves and applies a successful import. Export writes only to a user-selected destination. Restoring preferences and clearing diagnostic/statistical data are separate operations; no component enumerates other users' profiles.

`LocalStatistics` records only opt-in aggregate counters. `LocalDiagnostics` accepts short event codes and rotates two local files with a maximum of 64 KiB each. Neither component makes network requests. See [Privacy](PRIVACY.md).

## Windows boundaries

`WindowsSessionStateProvider` reads lock/suspend events, desktop availability, `SHQueryUserNotificationState`, and optional elapsed time since the last input. It never reads key contents, mouse paths, window titles, messages, camera, or microphone data. Notification state is an approximation; it is not a complete API for every current Windows Do Not Disturb decision.

`NativeNotifications` registers through Windows App SDK `AppNotificationManager`, constructs XML with an escaping XML API, and accepts a fixed activation command. A submitted notification is not proof that Windows displayed it. A blocked native notification must not trigger a custom-banner fallback.

`SingleInstanceCoordinator` uses a name derived from application identity, current user SID, and session ID. Both channels use the same name. A current-user-only named pipe accepts known commands and has connection/read deadlines; it does not accept executable paths or shell commands. This coordinates coexistence without migrating data or uninstalling the other channel.

The Store adapter uses `StartupTask`. The EXE adapter owns one quoted HKCU Run entry and changes it only after explicit consent. It never writes an enable decision during normal startup. The implementation reads Windows' undocumented `StartupApproved` data only to recognize a user-disabled entry, never writes it, and fails closed on unknown records. This compatibility limitation needs validation on supported Windows releases; see [Test plan](TEST-PLAN.md).

## Design boundaries

Windows 11 x64 is the initial packaging target. Compiling Windows code on another operating system cannot validate focus behavior, secure desktops, UAC, UI Automation, tray recovery, native notification delivery, or MSIX deployment. ARM64 and Windows 10 are not declared supported merely because a target framework can compile. Build, release, and manual-validation evidence are kept separate.
