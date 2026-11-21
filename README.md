# WebView2 Guard

Per-application internet whitelist for **Microsoft Edge WebView2** on Windows 10/11.

[Română mai jos ↓](#română)

Many desktop apps (Autodesk, Office, Teams, Widgets…) open web content and send data through the shared WebView2 runtime. A firewall rule on the app's own exe does not catch that traffic, because it leaves through `msedgewebview2.exe`, from a folder whose name changes with every update.

WebView2 Guard turns this around:

- the **Evergreen** runtime (the one Windows updates) gets an outbound **block** rule for every version folder, kept current by a scheduled task;
- a separate **Fixed Version** runtime ("Allowed", x64/x86) gets an **allow** rule;
- only the apps you approve are routed to the Allowed runtime, through the official per-exe policy
  `HKLM\SOFTWARE\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder`;
- everything else, including apps you have never seen, stays on Evergreen and has no internet.

You can split one product: for example allow `AdskIdentityManager.exe` (license sign-in) and keep `acad.exe` (Home / Start page) blocked.

## Requirements

- Windows 10 1709+ or Windows 11, administrator rights
- `WebView2Guard-win-x64.exe` needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0); the `-selfcontained` build needs nothing
- a WebView2 **Fixed Version** `.cab` from Microsoft (x64; x86 only for 32-bit apps)

## Getting started

1. Download the exe from [Releases](../../releases) (or the latest CI artifact) and run it.
2. **App → Install to Program Files.** This copies the exe to `C:\Program Files\WebView2Guard`, creates the
   `\WebView2Guard\Sync` task (runs as SYSTEM at startup and hourly) and syncs the firewall rules.
3. **Runtime → Install from .cab…** with the Fixed Version package. The app extracts it, checks the Microsoft
   Authenticode signature, sets the App Container permissions and adds the allow rule.
4. **Applications:** start the programs you care about; they appear in the list. Select one → **Allow internet**,
   then restart that program.

**Updates:** App → Check for app updates (GitHub Releases, SHA-256 checked). Runtime tab → Check for update downloads
the newest Fixed runtime straight from Microsoft; *Update automatically* lets the sync task do it once a day.

**Allowed domains (per app):** select an allowed app → *Domains…* and list the host names it may reach
(e.g. only the sign-in servers). Applied through the `AdditionalBrowserArguments` policy with
`--host-resolver-rules`; every other name fails to resolve. Not effective when the app goes through a proxy.

**Notification area:** App → *Run in the notification area and start with Windows* keeps the app in the tray
(started elevated at logon by a scheduled task, no UAC prompt) and notifies you when an app uses WebView2 for the
first time.

**Uninstall:** App → Uninstall everything… removes the policies, rules, task and (optionally) the runtimes.
A `.reg` backup of the policy key is written to `C:\ProgramData\WebView2Guard\backups` before every change.

## Using it with Malwarebytes Windows Firewall Control

With *Secure Rules* on, WFC deletes rules created by other programs: add the group **`WebView2 Guard`** to WFC's
authorized groups.

## Notes and limits

- Apps that call Microsoft's WAM/web account manager for sign-in do not use WebView2 for that step.
- The Allowed runtime does not update itself: update it from the Runtime tab when the app warns you.
- The sync task runs hourly: a brand-new Evergreen version is unblocked by these rules until the next run
  (in WFC *Medium Filtering* it is blocked anyway, because nothing allows it).

## Build

```
dotnet publish src/WebView2Guard/WebView2Guard.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

CI (`.github/workflows/build.yml`) builds both variants on every push and publishes a release for `v*` tags.

## License

GNU General Public License v3.0 or later. See [LICENSE](LICENSE).

---

## Română

Whitelist de acces la internet pentru **Microsoft Edge WebView2**, pe fiecare aplicație (Windows 10/11).

Multe programe (Autodesk, Office, Teams, Widgets…) afișează conținut web și trimit date prin runtime-ul WebView2
comun. O regulă de firewall pe exe-ul programului nu prinde traficul ăsta, pentru că iese prin `msedgewebview2.exe`,
dintr-un folder al cărui nume se schimbă la fiecare update.

Cum funcționează:

- runtime-ul **Evergreen** (cel actualizat de Windows) primește regulă de **blocare** pentru fiecare versiune, ținută la zi de un task programat;
- un runtime **Fixed Version** separat („Permis”, x64/x86) primește regulă de **permitere**;
- doar aplicațiile aprobate de tine sunt trimise spre runtime-ul permis, prin politica oficială pe exe
  `BrowserExecutableFolder`;
- tot restul, inclusiv aplicațiile noi, rămâne pe Evergreen, fără internet.

### Primii pași

1. Descarci exe-ul din Releases și îl pornești (cere drepturi de administrator).
2. **Aplicație → Instalează în Program Files**: copiază exe-ul, creează task-ul de sync și sincronizează regulile.
3. **Runtime → Instalează din .cab…** cu pachetul Fixed Version x64 de la Microsoft.
4. **Aplicații**: pornești programele, le selectezi și apeși **Permite internet**, apoi le repornești.

**Dezinstalare:** Aplicație → Dezinstalează tot… Înainte de orice modificare se salvează un backup `.reg`
în `C:\ProgramData\WebView2Guard\backups`.

Cu **Malwarebytes WFC** și *Secure Rules* activ, adaugă grupul `WebView2 Guard` la grupurile autorizate.

Licență: GNU GPL v3 sau ulterioară.
