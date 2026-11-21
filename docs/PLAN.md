# WebView2 Guard — plan

## Scop
Whitelist de acces la internet pentru WebView2, pe fiecare exe:
Evergreen blocat în firewall pentru toți; runtime Fixed „Allowed” (x64/x86) permis; aplicațiile aprobate
sunt trimise spre Allowed prin `BrowserExecutableFolder\<exe>`; tot restul rămâne fără internet.

## Arhitectură
- C# .NET 10, WinForms, un singur exe, manifest `requireAdministrator`.
- Moduri: GUI și `--sync` (task programat `\WebView2Guard\Sync`, SYSTEM, la pornire + orar).

| Modul | Rol |
|---|---|
| `ProcessScanner` | WMI `Win32_Process`; procesul `msedgewebview2.exe` principal (fără `--type=`) → gazda din `--webview-exe-name` sau procesul părinte; arhitectura din IsWow64Process2 |
| `PolicyStore` | citire/scriere `HKLM\SOFTWARE\Policies\Microsoft\Edge\WebView2\BrowserExecutableFolder`; atinge doar valorile proprii; backup `.reg` înainte de modificare |
| `RuntimeManager` | `.cab` → `expand` → semnătură Authenticode Microsoft → arhitectură din PE → refuz dacă e folosit → înlocuire (păstrează `.prev`) → `icacls` App Container |
| `FirewallManager` | grup „WebView2 Guard”: `WV2G-Allow-x64/x86`, `WV2G-Block-<versiune>`; sync idempotent |
| `SyncTask` | creare/interogare/ștergere task prin cmdlet-urile ScheduledTasks |
| `HostCatalog` | `hosts.json` cu aplicațiile văzute |
| `Log` | `guard.log` în ProgramData, rotație 1 MB |

Fișiere: `C:\Program Files\WebView2Guard\` (exe, `Runtime\x64`, `Runtime\x86`),
`C:\ProgramData\WebView2Guard\` (ACL: Administrators/SYSTEM scriere, Users citire).

Securitate: task-ul rulează ca SYSTEM, deci exe-ul trebuie să fie în Program Files; altfel task-ul nu se creează.

## Interfață
Banner de stare (verde / galben / roșu cu acțiune) + tab-uri: Aplicații, Runtime, Firewall, Jurnal.
Dialoguri: actualizare runtime (pași de verificare, aplicații de închis), dezinstalează tot.
Limbi: română + engleză (`Localization/Strings.cs`).

## Etape
- **v0.1** — tot ce e mai sus.
- **v0.2** — restricție pe domenii per aplicație (`AdditionalBrowserArguments` + `--host-resolver-rules`),
  Evergreen per-utilizator (`AppData`), marcaj pentru aplicații noi.
- **v0.3** — iconiță în tray cu notificare la aplicație nouă.

## De verificat pe sistem real
1. existența argumentului `--webview-exe-name` (altfel rămâne procesul părinte);
2. că aplicațiile Autodesk respectă politica pe exe;
3. interacțiunea cu Secure Rules din WFC;
4. aplicațiile care fac login prin WAM, nu prin WebView2.
