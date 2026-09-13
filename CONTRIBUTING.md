# Contributing

Open an issue before making a large behavioral change. Describe the affected Windows version, installation channel and a minimal reproduction without personal messages or diagnostic files you have not reviewed.

Keep the scheduler independent of WPF and Windows APIs. Test timing with an injected `TimeProvider`; do not use real sleeps for scheduler unit tests. Changes to startup, IPC, focus behavior, privacy or installers need the relevant checks in [TEST-PLAN.md](docs/TEST-PLAN.md).

Use readable C#, nullable references and the repository formatting rules. Put user-facing text in all three resource files. Build and run the Windows validation workflow before requesting review. Document manual tests as passed only when someone actually performed them.

Do not add analytics, automatic network calls, runtime AI dependencies or privilege escalation. Credentials, certificates, real Store publisher configuration and personal preference exports do not belong in the repository.
