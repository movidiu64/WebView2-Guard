# Changelog

All notable changes to WebView2 Guard are listed here. Versions follow [Semantic Versioning](https://semver.org/):
a new major version may need changes on your side, minor versions add features, patches fix bugs.

## [Unreleased]

## [0.2.0] - 2026-11-21

### Added
- While WebView2 Guard runs (window or notification area), the Evergreen folders are watched: a version installed by Windows gets its block rule within about ten seconds instead of at the next hourly sync, with a notification.
- In the notification area, a daily check for new versions of WebView2 Guard with a notification that starts the update (App menu: *Notify about new versions of WebView2 Guard*).

## [0.1.1] - 2026-11-21

### Added
- **Export / import** of the configuration (App menu): the apps in the list, which are allowed and on which runtime, their allowed domains and the settings, to move them to another PC or keep them across a reinstall. Import adds to what is there and lists the apps it had to skip.
- Command line for scripts, without a window: `--install`, `--runtime download|<file.cab>`, `--allow`, `--block`, `--domains`, `--export`, `--import`, `--status`, `--uninstall`, `--help`. See the [guide](docs/guide.md#command-line).

### Fixed
- *Uninstall everything* stopped halfway (runtimes and the sync task left in place) when the notification-area mode had never been turned on.
- Installing the runtime failed with an empty error when WebView2 Guard was started from PowerShell 7. PowerShell errors now show their real text.

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
