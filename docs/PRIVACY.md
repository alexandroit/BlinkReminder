# Privacy

BlinkReminder provides habit reminders. It does not diagnose or treat a condition, measure actual blinking, or make clinical efficacy claims.

## Normal operation

The application operates offline without an account. It does not send application telemetry, use advertising services, inspect meeting software, or monitor employee activity. It has no camera or microphone feature. It does not read typed characters, messages, documents, browser history, window titles, or application content.

Reminder intervals use elapsed time. The optional inactivity preference reads only the time since the last keyboard or mouse interaction. It does not store that input or build an activity history. Lack of input does not necessarily mean that the person is absent; they may be reading.

Windows session and notification-state APIs are used to avoid interruptions during unavailable or protected states. Monitor selection uses window/monitor placement information without recording a history of applications. Native notifications are handed to the operating system, which controls their display and retention.

Support and release links, when configured, open only following an explicit action. Opening such a link is handled by the default browser, with that browser's network and privacy settings. It is separate from normal offline reminder operation.

## Local files

All personal data is scoped to the current Windows user, even if the EXE binaries are installed in Program Files for all users.

| Data | Content and behavior |
| --- | --- |
| `settings.json` | Reminder text and intervals, appearance, schedule, language, startup preference, and privacy choices. A message entered by the user may contain personal information. |
| `settings.corrupt.json` | At most one invalid previous settings file retained for recovery. It may contain the same personal settings as the original. |
| `statistics.json` | Optional aggregate counts of shown custom banners, native-notification requests, and explicit confirmations. No application usage history or actual blink count. |
| `Diagnostics/diagnostics.log` and `.1` | UTC timestamps and short diagnostic event codes. Each file is capped at 64 KiB, with one previous log retained. Freeform exception text and window/input content are not accepted. |

For a direct EXE installation, settings are under `%LOCALAPPDATA%\BlinkReminder`. For MSIX, the application obtains its package-local user storage directory through the Windows package storage API. It does not assume the EXE and MSIX directories are identical or write in the installed package directory.

Statistics are disabled by default. When enabled, a successful native API submission increments a **request** counter; it does not mean that Windows displayed the notification or that the user saw it. An explicit confirmation is not evidence of an actual blink. Disabling statistics stops recording; retained counters can be removed using the local-data controls.

## Exports, imports, and deletion

Settings and diagnostic exports require a user action and write to a chosen file. An exported settings file includes the customized reminder message. Review it before sharing. Exported diagnostics remain local unless the user chooses to share them through another application.

Imports have size, schema, type, and range validation and are treated as configuration data. They cannot supply commands, plugins, executable code, or a new data directory. Validation alone does not apply the imported preferences; application controls explicitly save a valid import.

Restoring default preferences does not require deleting another user's data. Local-data cleanup targets only files owned by this application's current profile. Settings exported to a user-selected location are not automatically removed. Uninstall retention differs by installation channel; consult [Installation](INSTALLATION.md). The application never scans or clears other users' profiles.

## Security and operating-system policies

The application runs with ordinary user privileges. It does not install services, drivers, privileged scheduled tasks, firewall exceptions, or trusted certificates. Store `runFullTrust` identifies the desktop application model; it does not mean administrator execution.

Installation and execution restrictions imposed by Windows or an organization remain in force. If installation is blocked, contact the organization's IT team. BlinkReminder does not disable or bypass those restrictions.

This document describes the application's implementation, not a guarantee about independent Windows, Store, browser, or development-tool telemetry. Build tools and CI may contact package registries to restore dependencies; the installed reminder application does not use those restore mechanisms.
