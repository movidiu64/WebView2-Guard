# WebView2 Guard guide

- [How it works](#how-it-works)
- [Installing](#installing)
- [Applications](#applications)
- [Allowed domains](#allowed-domains)
- [Runtime](#runtime)
- [Firewall](#firewall)
- [Log](#log)
- [Notification area](#notification-area)
- [Updating WebView2 Guard](#updating-webview2-guard)
- [Using it with Windows Firewall Control](#using-it-with-windows-firewall-control)
- [Uninstalling](#uninstalling)
- [Export and import](#export-and-import)
- [Files and settings](#files-and-settings)
- [Command line](#command-line)
- [Limits](#limits)
- [How the project is built](#how-the-project-is-built)
- [Releasing](#releasing)

## How it works

An app that uses WebView2 does not open web pages itself. It starts `msedgewebview2.exe` from the shared **Evergreen** runtime, which Windows keeps up to date in `C:\Program Files (x86)\Microsoft\EdgeWebView\Application\<version>\` (or, for some apps, per user under `%LOCALAPPDATA%`). All the network traffic comes from that process, so a firewall rule on the app's own exe does not catch it, and a rule on `msedgewebview2.exe` stops working at the next update because the folder name changes.

WebView2 Guard turns this around:

| Part | What it gets |
|---|---|
| Evergreen runtime, every version folder found | an outbound **block** rule (`WV2G-Block-<version>`) |
| A **Fixed Version** runtime in `C:\Program Files\WebView2Guard\Runtime\x64` (and `x86`) | an outbound **allow** rule (`WV2G-Allow-x64`, `WV2G-Allow-x86`) |
| Each app you approve | the per-exe policy `HKLM\SOFTWARE\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder` → `<exe name>` = the Fixed runtime folder |
| Every other app, including new ones | nothing: it keeps using Evergreen, which is blocked |

`BrowserExecutableFolder` is Microsoft's documented policy for pointing an app at a Fixed Version runtime. Windows Firewall applies block rules before allow rules, so the two kinds of rule never conflict: they cover different files.

A scheduled task (`\WebView2Guard\Sync`, running as SYSTEM at startup and every hour) adds a block rule for each new Evergreen version and removes the rules of versions that are gone. Protection therefore does not depend on the window being open.

## Installing

1. Download one of the two builds from [Releases](https://github.com/movidiu64/WebView2-Guard/releases/latest):
   - `WebView2Guard-win-x64-selfcontained.exe`: about 50 MB, needs nothing else;
   - `WebView2Guard-win-x64.exe`: under 1 MB, needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
   
   Both are 64-bit, for Windows 10 1709 or later and Windows 11, and need administrator rights. `SHA256SUMS.txt` in the release has their checksums.
2. Run it and choose **App → Install to Program Files**. The exe is copied to `C:\Program Files\WebView2Guard`, a Start menu shortcut and the sync task are created, and the first sync adds the block rules. The sync task runs as SYSTEM, so it only ever runs the copy in Program Files, where ordinary users cannot replace it.
3. Install the allowed runtime from the [Runtime](#runtime) tab.
4. Allow the apps you want in the [Applications](#applications) tab.

Before the first block, the app warns you that new Outlook, Teams, Widgets or sign-in windows may show no web content until you allow them.

Running a newer exe while an older version is already running (for example in the notification area) closes the older one and takes over. If the copy in Program Files is older than the exe you started, the app offers to update it.

## Applications

The list shows every app that has used WebView2 since WebView2 Guard was installed, plus the ones you added by hand:

| Column | Meaning |
|---|---|
| Application | the exe name (the policy works by exe name) and its folder |
| Internet access | **Allowed** or **Blocked** |
| Domains | for allowed apps: *all*, or how many domains are allowed |
| Running | whether it runs now |
| Runtime in use | *Evergreen*, *Allowed x64/x86* or *Other runtime*; **restart** when the app still runs on the runtime it had before your last change |
| Arch | x64 or x86, read from the exe; x86 apps need the x86 runtime |
| Last seen | when it last started WebView2 |

New apps are marked **new**. **Only running now** and the search box filter the list.

- **Allow internet** writes the policy for the selected exe and asks you to install the right runtime if it is missing. Restart the app afterwards: a running app keeps the runtime it started with.
- **Block** removes the policy; the app returns to Evergreen and is blocked.
- **Add exe…** puts an app in the list before it has ever run, so you can allow it in advance.
- **Remove from list** forgets a blocked app you no longer use (block an allowed app first).

The app is found through the `msedgewebview2.exe` process tree: the `--webview-exe-name` argument when present, otherwise the parent process. Policies for an exe that another program set are shown and never changed without asking. A global `*` policy set outside the app is reported in the status banner.

## Allowed domains

For an allowed app, **Domains…** limits which host names it can reach, one per line. `*.example.com` covers the subdomains of `example.com` but not `example.com` itself; list both if you need both. Every other name fails to resolve, so pages and telemetry endpoints that are not on the list cannot be reached.

The list is passed to the app's runtime through the per-exe `AdditionalBrowserArguments` policy as `--host-resolver-rules="MAP * ~NOTFOUND, EXCLUDE <host>…"`. The app must be restarted. It has no effect when the app's traffic goes through a proxy, and arguments that another program set for the same exe are left unchanged. Your firewall log (Windows Firewall Control, OPNsense…) shows which domains an app actually uses. **No restriction** removes the list.

## Runtime

**Allowed runtime · x64 / x86** cards show the installed Fixed version, its signature, the firewall rule, the apps that use it and the previous version kept for rollback. x86 is only needed for 32-bit apps.

Installing or updating:

- **Download from Microsoft** reads the download links from Microsoft's [WebView2 page](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) and downloads the newest Fixed Version `.cab` (about 200 MB).
- **Install from .cab… / Update from .cab…** uses a package you downloaded yourself: on the same page, *Fixed Version*, the newest version, architecture x64 (x86 only for 32-bit apps): `Microsoft.WebView2.FixedVersionRuntime.<version>.x64.cab`.

Either way the package is extracted with `expand.exe`, `msedgewebview2.exe` must carry a valid Microsoft Authenticode signature and the right architecture, and the runtime must not be in use (close the allowed apps first). The old version is kept as `x64.prev`, the App Container permissions WebView2 needs are applied with `icacls`, and the allow rule is refreshed. **Roll back** returns to the kept version; **Remove** deletes the runtime (allowed apps lose internet access until you install it again).

A Fixed runtime never updates itself. The app warns you when the allowed runtime is older than Evergreen. **Update the allowed runtime automatically** lets the sync task check Microsoft's page once a day and install a new version when no app is using the runtime; the signature is checked as for a manual install.

The **Evergreen runtime (Windows)** card lists every Evergreen version found, machine-wide and per user, with its block rule. Versions left over after an update are marked as such.

## Firewall

All the rules in the `WebView2 Guard` group, with their action, program and state. **Sync now** does what the scheduled task does: adds missing rules, removes rules for folders that no longer exist. **Remove all rules** deletes the group; Evergreen is no longer blocked by WebView2 Guard until the next sync.

Rules outside the group are never touched.

## Log

Everything the app and the sync task did: policies, rules, runtime installs, new apps, errors. Filter by level, copy the selection or open the file (`C:\ProgramData\WebView2Guard\guard.log`, rotated at 1 MB).

## Notification area

**App → Run in the notification area and start with Windows** keeps the app running with an icon next to the clock. A second scheduled task (`\WebView2Guard\Tray`) starts it elevated when you sign in, so there is no UAC prompt. Closing the window leaves it in the notification area; right-click the icon and choose **Exit** to quit.

With **Notify about new apps that use WebView2** on, a notification appears the first time an app uses WebView2; clicking it opens the app with that entry selected.

The sync task protects the system whether or not the notification-area mode is on.

## Updating WebView2 Guard

**App → Check for app updates…** looks at this repository's [releases](https://github.com/movidiu64/WebView2-Guard/releases), downloads the build of the same kind you use (self-contained or not), checks it against `SHA256SUMS.txt` of the same release, replaces the exe and restarts. Pre-releases are included. Downloading a newer exe by hand and running it works too.

## Using it with Windows Firewall Control

With *Secure Rules* on, Malwarebytes Windows Firewall Control deletes rules created by other programs. Add the group **`WebView2 Guard`** to WFC's authorized groups (or turn off *Secure Rules* for it). In WFC's *Medium Filtering* mode a new Evergreen version is blocked even before the next sync, because nothing allows it.

## Uninstalling

**App → Uninstall everything…** removes:

- the policies this app created (other programs' policies stay);
- the rules in the `WebView2 Guard` group;
- the scheduled tasks and the Start menu shortcut;
- optionally, the Fixed runtimes in `Program Files\WebView2Guard\Runtime`.

All apps then use the Evergreen runtime again, unblocked. Afterwards you can delete `C:\Program Files\WebView2Guard` and `C:\ProgramData\WebView2Guard`.

## Files and settings

| Path | Contents |
|---|---|
| `C:\Program Files\WebView2Guard\WebView2Guard.exe` | the installed app (run by the scheduled tasks) |
| `C:\Program Files\WebView2Guard\Runtime\x64`, `x86` | the allowed Fixed runtimes; `*.prev` the kept previous versions |
| `C:\ProgramData\WebView2Guard\hosts.json` | the apps seen and when |
| `C:\ProgramData\WebView2Guard\settings.json` | language, notification-area mode, notifications, automatic runtime update |
| `C:\ProgramData\WebView2Guard\cache.json` | the last state shown, so the window opens at once |
| `C:\ProgramData\WebView2Guard\guard.log` | the log |
| `C:\ProgramData\WebView2Guard\backups\*.reg` | a backup of the policy key before every change |

The data folder is writable only by administrators and SYSTEM.

## Export and import

**App → Export configuration…** saves a JSON file with the apps in the list, which of them are allowed (and for which runtime), their allowed domains and the settings (language, notifications, automatic runtime update). **App → Import configuration…** applies such a file on another PC or after a reinstall: it adds the apps to the list and allows the ones allowed in the file. It adds to what is there: apps already allowed stay allowed. An app is skipped, and named in the result, when its runtime is not installed yet or another program already set a policy for it.

## Command line

Every command runs without a window and needs administrator rights. The app is a Windows (GUI) program, so in PowerShell pipe the output to make the shell wait for it and show it, from an elevated window:

```powershell
& "C:\Program Files\WebView2Guard\WebView2Guard.exe" --status | Out-Host
```

| Command | Effect |
|---|---|
| *(none)* | opens the window; if WebView2 Guard already runs, shows it |
| `--tray` | starts in the notification area (used by the `\WebView2Guard\Tray` task) |
| `--install` | installs to Program Files, creates the sync task and syncs the firewall |
| `--sync` | updates the allowed runtime if automatic updates are on and due, then syncs the firewall rules (used by the `\WebView2Guard\Sync` task) |
| `--runtime download [x64\|x86]` | downloads the newest Fixed runtime from Microsoft and installs it, unless it is already up to date |
| `--runtime <file.cab>` | installs a Fixed runtime package |
| `--allow <exe> [x64\|x86]` | lets an app use the allowed runtime (x64 unless said otherwise) |
| `--block <exe>` | sends an app back to Evergreen and removes its domain list |
| `--domains <exe> <a.com,*.b.com>` | allowed domains for an allowed app; `none` removes the limit |
| `--export <file.json>`, `--import <file.json>` | see [Export and import](#export-and-import) |
| `--status` | prints the protection state, the runtimes, the problems found and the apps |
| `--uninstall [--keep-runtimes]` | removes policies, rules, tasks, the shortcut and (unless kept) the runtimes |
| `--help` | lists the commands |

Exit codes: 0 done, 1 failed (the reason is printed and written to the log), 2 wrong arguments. `--import` returns 1 when an app was skipped.

## Limits

- The policy works by exe name: two different programs with the same exe name share the decision.
- An app that runs before you allow it keeps the Evergreen runtime until it is restarted.
- Sign-in through the Windows account manager (WAM) does not use WebView2, so it is not affected either way.
- A brand-new Evergreen version is not blocked until the next sync (at most an hour, or at the next startup), unless your firewall blocks unknown programs anyway.
- Allowed domains need the app to resolve names itself; they do not apply through a proxy.
- WebView2 Guard controls WebView2 only. The apps' own exes, their updaters and other helper processes are a matter for your firewall.

## How the project is built

One C# project (`src/WebView2Guard`, .NET 10, Windows Forms) and a unit test project (`tests/WebView2Guard.Tests`, xUnit).

| Folder / file | Role |
|---|---|
| `Program.cs` | single instance, takeover from older versions, `--sync` and `--tray` |
| `Core/ProcessScanner.cs` | finds the apps behind `msedgewebview2.exe` through WMI, and their architecture |
| `Core/PolicyStore.cs` | reads and writes the per-exe policies, with `.reg` backups |
| `Core/RuntimeManager.cs` | Fixed runtime install, signature check, App Container permissions, rollback |
| `Core/FirewallManager.cs` | the rules in the `WebView2 Guard` group (PowerShell `NetSecurity` cmdlets) |
| `Core/SyncTask.cs` | the two scheduled tasks |
| `Core/Guard.cs` | collects the state, evaluates problems, applies decisions |
| `Core/Updaters.cs` | runtime downloads from Microsoft, app updates from GitHub Releases |
| `Core/DomainRules.cs` | the `--host-resolver-rules` argument for allowed domains |
| `Core/ConfigTransfer.cs` | export and import of the configuration |
| `Cli.cs` | the command line |
| `UI/` | the window, tabs, dialogs and theme |
| `Localization/Strings.cs` | every text, Romanian and English |

```powershell
dotnet test tests/WebView2Guard.Tests
dotnet publish src/WebView2Guard/WebView2Guard.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
dotnet publish src/WebView2Guard/WebView2Guard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

The in-app updater picks the asset by the `GuardFlavor` property (`fd` or `selfcontained`), which CI sets.

## Releasing

`.github/workflows/windows.yml` runs on every push and pull request: unit tests, both builds, then an end-to-end test of the self-contained build on the Windows runner through the command line (install, block rules, the Fixed runtime downloaded from Microsoft, policies, domains, export and import, the window, uninstall).

- On a branch other than `main`, the builds are published as a draft release named **Test build**, replaced by the next one. Drafts are visible only to the repository owner and the in-app updater ignores them.
- On `main`, when the version in `WebView2Guard.csproj` (`VersionPrefix`, plus `VersionSuffix` for a pre-release such as `beta.1`) has no release yet, CI creates the `v<version>` tag and publishes the release with that version's `CHANGELOG.md` section as notes. A version with a suffix is published as a pre-release.

To release:

1. Turn `## [Unreleased]` in `CHANGELOG.md` into `## [x.y.z] - YYYY-MM-DD` and start a new empty `## [Unreleased]`.
2. Set `VersionPrefix` (and `VersionSuffix`, or remove it) in `src/WebView2Guard/WebView2Guard.csproj`.
3. Merge to `main`. The build fails if the changelog has no section for the version.
