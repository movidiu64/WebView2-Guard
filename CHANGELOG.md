# Changelog

All notable changes to WebView2 Guard are listed here. Versions follow [Semantic Versioning](https://semver.org/):
a new major version may need changes on your side, minor versions add features, patches fix bugs.

## [Unreleased]

## [0.1.0] - 2026-11-21

First public release, after the 0.1.0-beta.1 to beta.6 test builds. Licensed under the GNU GPL v3.

### Protection
- Every Evergreen WebView2 version, machine-wide and per user, gets an outbound block rule in Windows Firewall (group `WebView2 Guard`); a scheduled task running as SYSTEM keeps the rules current at startup and every hour.
- Approved apps are routed to an allowed Fixed Version runtime (x64 and x86) through the per-exe `BrowserExecutableFolder` policy; everything else, including new apps, stays blocked.
- **Allowed domains** per app: limit an allowed app to a list of host names through `--host-resolver-rules`.
- A `.reg` backup of the policy key before every change; policies and arguments set by other programs are never changed without asking.

### Window
- **Applications**: every app that uses WebView2, with access, allowed domains, runtime in use, architecture and a restart hint; new apps are marked; search and *Only running now* filter.
- **Runtime**: install the Fixed runtime from a `.cab` or download the newest one from Microsoft, with Microsoft signature check, App Container permissions, rollback to the previous version and an optional daily automatic update.
- **Firewall** and **Log** tabs; a status banner with the problems found and how to fix them.
- Opens at once with the last known state, then refreshes in the background with a progress bar.
- Notification-area mode that starts with Windows without a UAC prompt and notifies you when an app uses WebView2 for the first time.
- English and Romanian interface.

### Installing and updating
- Installs to Program Files with a Start menu shortcut; *Uninstall everything* restores the previous state.
- Checks GitHub Releases for new versions and updates itself after a SHA-256 check.
- Starting a newer exe while an older version runs closes the older one and offers to update the Program Files copy.
- Two builds: framework-dependent (needs the .NET 10 Desktop Runtime) and self-contained.
