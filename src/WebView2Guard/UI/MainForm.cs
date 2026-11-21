// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Diagnostics;
using WebView2Guard.Core;
using WebView2Guard.Localization;
using static WebView2Guard.UI.Theme;

namespace WebView2Guard.UI;

internal interface IShell
{
    GuardSnapshot? Current { get; }
    IWin32Window Window { get; }
    Task RunAsync(Action work, bool refresh = true, string? status = null);
    Task RefreshAsync();
    Task SyncAsync();
    void ShowTab(int index);
    void ShowStatus(string text, int? value = null, int? max = null);
    void ShowIdle(string? text = null);
}

internal sealed class MainForm : Form, IShell
{
    public const string RepoUrl = "https://github.com/movidiu64/WebView2-Guard";

    private readonly Banner _banner = new();
    private readonly TabStrip _tabs = new();
    private readonly Panel _content = new();
    private readonly GuardButton _menuButton;
    private readonly ContextMenuStrip _menu = new();
    private readonly AppsPage _apps;
    private readonly RuntimePage _runtime;
    private readonly FirewallPage _firewall;
    private readonly LogPage _log;
    private readonly Control[] _pages;
    private readonly System.Windows.Forms.Timer _hostsTimer = new() { Interval = 10_000 };
    private bool _busy;
    private bool _refreshing;

    public GuardSnapshot? Current { get; private set; }
    public IWin32Window Window => this;

    public static string VersionText => AppVersion.Current;

    private readonly Label _statusText = Ui.Lbl(string.Empty, Small, TextMuted);
    private readonly ProgressBar _statusProgress = new()
    {
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 30,
        Visible = false
    };
    private DateTime? _lastUpdate;

    public MainForm()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = "WebView2 Guard";
        Font = Body;
        BackColor = Bg;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(S(980), S(640));
        Size = new Size(S(1280), S(820));
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(AppPaths.CurrentExe); } catch { /* default icon */ }

        _menuButton = new GuardButton(L.T("menu.app"), Glyph.ChevronDown, ButtonKind.Ghost) { Margin = new Padding(0) };
        _menuButton.Click += (_, _) => _menu.Show(_menuButton, new Point(0, _menuButton.Height));
        BuildMenu();

        _apps = new AppsPage(this);
        _runtime = new RuntimePage(this);
        _firewall = new FirewallPage(this);
        _log = new LogPage();
        _pages = new Control[] { _apps, _runtime, _firewall, _log };

        _content.Dock = DockStyle.Fill;
        _content.Padding = P(20, 16, 20, 16);
        _content.BackColor = Bg;
        foreach (var p in _pages)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            _content.Controls.Add(p);
        }
        _apps.Visible = true;

        _tabs.Dock = DockStyle.Top;
        _tabs.BackColor = Bg;
        _tabs.SetTabs(new[] { L.T("tab.apps"), L.T("tab.runtime"), L.T("tab.firewall"), L.T("tab.log") });
        _tabs.SelectedChanged += (_, _) => SwitchPage(_tabs.Selected);

        var bannerHost = new Panel { Dock = DockStyle.Top, Height = S(66), Padding = P(20, 16, 20, 0), BackColor = Bg };
        _banner.Dock = DockStyle.Fill;
        _banner.ActionClicked += (_, a) => _ = DoProblemAction(a);
        bannerHost.Controls.Add(_banner);

        // Dock order: the control added last is docked first (topmost).
        Controls.Add(_content);
        Controls.Add(BuildStatusBar());
        Controls.Add(_tabs);
        Controls.Add(bannerHost);
        Controls.Add(BuildHeader());

        _hostsTimer.Tick += async (_, _) => await RefreshHostsAsync();
        ResumeLayout(true);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Show the last known state at once; the real scan replaces it a moment later.
        var (cached, savedAt) = SnapshotCache.Load();
        if (cached != null)
        {
            try
            {
                Bind(cached);
                _statusText.Text = L.F("status.fromCache", savedAt.ToString("g"));
            }
            catch
            {
                // incompatible cache from an older version: ignore
            }
        }
        await RefreshAsync();
        _hostsTimer.Start();
    }

    private Control BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = S(30), BackColor = Surface };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = P(20, 0, 20, 0), BackColor = Surface };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(180)));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _statusText.Anchor = AnchorStyles.Left;
        _statusText.AutoEllipsis = true;
        _statusText.Margin = new Padding(0);
        _statusProgress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _statusProgress.Height = S(10);
        _statusProgress.Margin = new Padding(0);
        t.Controls.Add(_statusText, 0, 0);
        t.Controls.Add(_statusProgress, 1, 0);
        bar.Controls.Add(t);
        return bar;
    }

    /// <summary>Shows what is running; with value/max a determinate bar, otherwise a moving one.</summary>
    public void ShowStatus(string text, int? value = null, int? max = null)
    {
        _statusText.Text = text;
        _statusProgress.Visible = true;
        if (value is { } v && max is { } m && m > 0)
        {
            _statusProgress.Style = ProgressBarStyle.Continuous;
            _statusProgress.Maximum = m;
            _statusProgress.Value = Math.Clamp(v, 0, m);
        }
        else
        {
            _statusProgress.Style = ProgressBarStyle.Marquee;
        }
    }

    public void ShowIdle(string? text = null)
    {
        _statusProgress.Visible = false;
        _statusText.Text = text ?? (_lastUpdate is { } t ? L.F("status.updatedAt", t.ToString("HH:mm:ss")) : string.Empty);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hostsTimer.Stop();
        _hostsTimer.Dispose();
        base.OnFormClosed(e);
    }

    // ---- header & menu -----------------------------------------------------------------------

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = S(52), BackColor = Surface };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = P(16, 0, 12, 0),
            BackColor = Surface
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var icon = Ui.GlyphLabel(Glyph.Shield, Accent, 14f);
        var title = Ui.Lbl("WebView2 Guard", Theme.Heading);
        var ver = Ui.Lbl("v" + VersionText, Small, TextMuted);
        foreach (var c in new Control[] { icon, title, ver })
        {
            c.Anchor = AnchorStyles.Left;
            c.Margin = P(0, 0, 8, 0);
        }
        _menuButton.Anchor = AnchorStyles.Right;

        t.Controls.Add(icon, 0, 0);
        t.Controls.Add(title, 1, 0);
        t.Controls.Add(ver, 2, 0);
        t.Controls.Add(_menuButton, 4, 0);
        header.Controls.Add(t);
        return header;
    }

    private void BuildMenu()
    {
        _menu.Font = Body;
        _menu.Items.Add(L.T("menu.install"), null, (_, _) => _ = InstallAsync());

        var lang = new ToolStripMenuItem(L.T("menu.language"));
        var current = Settings.Load().Language;
        foreach (var (code, key) in new[] { ("auto", "menu.lang.auto"), ("ro", "menu.lang.ro"), ("en", "menu.lang.en") })
        {
            var item = new ToolStripMenuItem(L.T(key)) { Checked = current == code };
            item.Click += (_, _) => ChangeLanguage(code);
            lang.DropDownItems.Add(item);
        }
        _menu.Items.Add(lang);
        _menu.Items.Add(L.T("menu.dataFolder"), null, (_, _) => OpenFolder(AppPaths.DataDir));
        _menu.Items.Add(L.T("menu.checkUpdates"), null, (_, _) => _ = CheckAppUpdateAsync());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.T("menu.undo"), null, (_, _) => _ = UndoAsync());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(L.T("menu.about"), null, (_, _) => ShowAbout());
    }

    private void ChangeLanguage(string code)
    {
        var s = Settings.Load();
        s.Language = code;
        s.Save();
        L.Init(code);
        Program.RestartUi = true;
        Close();
    }

    private void ShowAbout()
    {
        var r = MessageBox.Show(this, L.F("about.text", VersionText, RepoUrl), L.T("menu.about"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (r == DialogResult.Yes) OpenUrl(RepoUrl);
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser */ }
    }

    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { dir }, UseShellExecute = false });
        }
        catch { /* ignore */ }
    }

    public static void RevealFile(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = false });
        }
        catch { /* ignore */ }
    }

    // ---- pages -------------------------------------------------------------------------------

    public void ShowTab(int index) => _tabs.Selected = index;

    private void SwitchPage(int index)
    {
        _content.SuspendLayout();
        for (var i = 0; i < _pages.Length; i++) _pages[i].Visible = i == index;
        _content.ResumeLayout(true);
        if (index == 3 && !_log.Loaded) _log.Reload();
    }

    // ---- state -------------------------------------------------------------------------------

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (Current == null) _banner.ShowLoading();
            var progress = new Progress<CollectStep>(st => ShowStatus(st.Text, st.Step, st.Total));
            var snap = await Task.Run(() => Guard.Collect(progress));
            Bind(snap);
            _lastUpdate = DateTime.Now;
            ShowIdle();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            ShowIdle(L.F("status.failed", ex.Message));
            Ui.Error(this, ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshHostsAsync()
    {
        if (_busy || _refreshing || Current == null || !_apps.Visible || WindowState == FormWindowState.Minimized) return;
        try
        {
            var hosts = await Task.Run(Guard.CollectHosts);
            if (Current == null || _busy || _refreshing) return;
            if (Current.Hosts.SequenceEqual(hosts)) return; // nothing changed: no repaint
            Current = Current with { Hosts = hosts };
            _apps.Bind(Current);
        }
        catch
        {
            // Background refresh errors are ignored; a manual refresh shows them.
        }
    }

    private void Bind(GuardSnapshot s)
    {
        Current = s;
        _banner.Bind(s);
        _apps.Bind(s);
        _runtime.Bind(s);
        _firewall.Bind(s);
    }

    public async Task RunAsync(Action work, bool refresh = true, string? status = null)
    {
        if (_busy) return;
        SetBusy(true);
        ShowStatus(status ?? L.T("status.working"));
        try
        {
            await Task.Run(work);
            ShowIdle();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            ShowIdle(L.F("status.failed", ex.Message));
            Ui.Error(this, ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
        if (refresh) await RefreshAsync();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _content.Enabled = !busy;
        _menuButton.Enabled = !busy;
        _banner.Enabled = !busy;
    }

    // ---- actions -----------------------------------------------------------------------------

    private async Task DoProblemAction(ProblemAction action)
    {
        switch (action)
        {
            case ProblemAction.Install:
            case ProblemAction.UpdateInstall:
                await InstallAsync();
                break;
            case ProblemAction.CreateTask:
                await RunAsync(SyncTask.Create, status: L.T("status.createTask"));
                break;
            case ProblemAction.Sync:
                await SyncAsync();
                break;
            case ProblemAction.OpenRuntime:
                ShowTab(1);
                break;
            case ProblemAction.OpenApps:
                ShowTab(0);
                break;
        }
    }

    /// <summary>
    /// True if the user accepts that the first sync blocks Evergreen for every app (asked only while no block rule exists).
    /// </summary>
    private bool ConfirmFirstBlock()
    {
        var s = Current;
        if (s == null || s.Rules.Any(r => !r.IsAllow)) return true;
        return Ui.Confirm(this, L.T("confirm.firstBlock"), danger: true);
    }

    public async Task SyncAsync()
    {
        if (!ConfirmFirstBlock()) return;
        await RunAsync(() => Guard.SyncFirewall(), status: L.T("status.sync"));
    }

    /// <summary>Copies the app to Program Files, enables the sync task and offers to restart from there.</summary>
    private async Task InstallAsync()
    {
        if (!ConfirmFirstBlock()) return;
        var installed = false;
        await RunAsync(() =>
        {
            Installer.InstallToProgramFiles();
            SyncTask.Create();
            Guard.SyncFirewall();
            installed = true;
        }, status: L.T("status.install"));
        if (!installed || AppPaths.IsRunningFromInstallDir) return;

        if (Ui.Confirm(this, L.F("msg.installDone", AppPaths.InstallDir)))
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppPaths.InstalledExe) { UseShellExecute = true });
                Close();
            }
            catch (Exception ex)
            {
                Ui.Error(this, ex.Message);
            }
        }
    }

    /// <summary>Checks GitHub for a newer release of this app, downloads it (SHA-256 checked) and restarts.</summary>
    private async Task CheckAppUpdateAsync()
    {
        if (_busy) return;
        if (!Installer.IsSingleFile)
        {
            Ui.Error(this, L.T("err.updateDevBuild"));
            return;
        }

        AppRelease? release;
        ShowStatus(L.T("status.checkApp"));
        try
        {
            release = await AppUpdater.LatestAsync();
            ShowIdle();
        }
        catch (Exception ex)
        {
            ShowIdle();
            Ui.Error(this, L.F("err.updateCheck", ex.Message));
            return;
        }

        if (release == null || !AppUpdater.IsNewer(release))
        {
            MessageBox.Show(this, L.F("msg.upToDate", AppVersion.Current), "WebView2 Guard",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Ui.Confirm(this, L.F("confirm.appUpdate", release.Version, AppVersion.Current, AppUpdater.TargetExe)))
            return;

        var progress = new Progress<DownloadProgress>(p =>
            ShowStatus(L.F("status.downloadApp", release.Version, p.Percent ?? 0), p.Percent, 100));
        string? exe = null;
        await RunAsync(() =>
        {
            var file = AppUpdater.DownloadAsync(release, progress).GetAwaiter().GetResult();
            exe = AppUpdater.Apply(file, release.Version);
        }, refresh: false, status: L.F("status.downloadApp", release.Version, 0));
        if (exe == null) return;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private async Task UndoAsync()
    {
        var s = Current;
        using var dlg = new UndoDialog(Guard.CountOurPolicies(), s?.Rules.Count ?? 0);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var options = dlg.Options;
        var ok = false;
        await RunAsync(() =>
        {
            Guard.UndoAll(options);
            if (options.Task) Installer.RemoveShortcut();
            ok = true;
        }, status: L.T("status.undo"));
        if (ok) MessageBox.Show(this, L.T("undo.done"), "WebView2 Guard", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

/// <summary>Coloured status strip under the header.</summary>
internal sealed class Banner : Card
{
    private readonly Label _icon = Ui.GlyphLabel(Glyph.Shield, OkText, 12f);
    private readonly Label _title = Ui.Lbl(string.Empty, Strong);
    private readonly Label _details = Ui.Lbl(string.Empty, Body);
    private readonly GuardButton _action = new(string.Empty, null, ButtonKind.WarnFilled);
    private readonly ToolTip _tip = new();
    private ProblemAction _currentAction;

    public event EventHandler<ProblemAction>? ActionClicked;

    public Banner()
    {
        Padding = P(14, 0, 8, 0);
        Margin = new Padding(0);
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _details.AutoSize = false;
        _details.AutoEllipsis = true;
        _details.Dock = DockStyle.Fill;
        _details.TextAlign = ContentAlignment.MiddleLeft;
        _icon.Anchor = AnchorStyles.Left;
        _title.Anchor = AnchorStyles.Left;
        _title.Margin = P(0, 0, 20, 0);
        _action.Anchor = AnchorStyles.Right;
        _action.Margin = new Padding(0);
        _action.Visible = false;
        _action.Click += (_, _) => ActionClicked?.Invoke(this, _currentAction);

        t.Controls.Add(_icon, 0, 0);
        t.Controls.Add(_title, 1, 0);
        t.Controls.Add(_details, 2, 0);
        t.Controls.Add(_action, 3, 0);
        Controls.Add(t);
    }

    public void ShowLoading()
    {
        Apply(NeutralBg, Border, TextMuted, Glyph.Refresh, L.T("banner.loading"), string.Empty, null, ProblemAction.None);
    }

    public void Bind(GuardSnapshot s)
    {
        switch (s.State)
        {
            case OverallState.Active:
            {
                var parts = new List<string> { L.F("banner.evergreen", s.Evergreen.Count) };
                var rt = s.X64.Installed ? s.X64 : s.X86.Installed ? s.X86 : null;
                parts.Add(rt != null ? L.F("banner.runtime", rt.Arch.Label(), rt.Version ?? "?") : L.T("banner.runtimeNone"));
                if (s.Task is { Exists: true } task)
                    parts.Add(task.LastRun is { } last
                        ? L.F("banner.taskActive", last.ToString("HH:mm"))
                        : L.T("banner.taskNever"));
                Apply(OkBg, OkBorder, OkText, Glyph.Shield, L.T("banner.active"), string.Join("     ·     ", parts),
                    null, ProblemAction.None);
                _tip.SetToolTip(_details, string.Empty);
                break;
            }
            default:
            {
                var incomplete = s.State == OverallState.Incomplete;
                var first = s.Problems.OrderBy(p => p.Severity == OverallState.Incomplete ? 0 : 1).First();
                var text = first.Text + (s.Problems.Count > 1 ? "  " + L.F("banner.more", s.Problems.Count - 1) : string.Empty);
                Apply(incomplete ? ErrBg : WarnBg,
                    incomplete ? ErrBorder : WarnBorder,
                    incomplete ? ErrText : WarnText,
                    incomplete ? Glyph.Error : Glyph.Warning,
                    L.T(incomplete ? "banner.incomplete" : "banner.warning"),
                    text,
                    ActionText(first.Action),
                    first.Action);
                _details.ForeColor = incomplete ? ErrTextDark : WarnTextDark;
                _action.Kind = incomplete ? ButtonKind.DangerFilled : ButtonKind.WarnFilled;
                _tip.SetToolTip(_details, string.Join(Environment.NewLine, s.Problems.Select(p => "• " + p.Text)));
                break;
            }
        }
    }

    private static string? ActionText(ProblemAction a) => a switch
    {
        ProblemAction.Install => L.T("action.install"),
        ProblemAction.UpdateInstall => L.T("action.updateInstall"),
        ProblemAction.CreateTask => L.T("action.createTask"),
        ProblemAction.Sync => L.T("action.sync"),
        ProblemAction.OpenRuntime => L.T("action.openRuntime"),
        ProblemAction.OpenApps => L.T("action.openApps"),
        _ => null
    };

    private void Apply(Color fill, Color stroke, Color accent, string glyph, string title, string details,
        string? actionText, ProblemAction action)
    {
        Fill = fill;
        Stroke = stroke;
        _icon.Text = glyph;
        _icon.ForeColor = accent;
        _title.Text = title;
        _title.ForeColor = accent;
        _details.Text = details;
        _details.ForeColor = Theme.Text;
        _currentAction = action;
        _action.Visible = actionText != null;
        if (actionText != null) _action.Text = actionText;
    }
}
