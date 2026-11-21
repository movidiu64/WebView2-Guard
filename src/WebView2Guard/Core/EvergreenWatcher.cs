// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

namespace WebView2Guard.Core;

/// <summary>
/// Watches the Evergreen runtime folders while the app runs, so a version installed by Windows Update
/// gets its block rule within seconds instead of at the next hourly sync. Raises <see cref="Changed"/>
/// (on a thread-pool thread) once the folders have been quiet for a few seconds.
/// </summary>
public sealed class EvergreenWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly System.Threading.Timer _quiet;
    private readonly TimeSpan _delay;
    private bool _disposed;

    public event Action? Changed;

    public EvergreenWatcher(TimeSpan? delay = null)
    {
        _delay = delay ?? TimeSpan.FromSeconds(10);
        _quiet = new System.Threading.Timer(_ => { if (!_disposed) Changed?.Invoke(); });
    }

    /// <summary>The folders being watched (the Evergreen roots that exist now).</summary>
    public IReadOnlyList<string> Folders => _watchers.Select(w => w.Path).ToList();

    public void Start()
    {
        foreach (var root in AppPaths.EvergreenRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                    InternalBufferSize = 64 * 1024
                };
                w.Created += (_, e) => OnEvent(root, e.FullPath);
                w.Deleted += (_, e) => OnEvent(root, e.FullPath);
                w.Renamed += (_, e) => { OnEvent(root, e.OldFullPath); OnEvent(root, e.FullPath); };
                w.Error += (_, _) => Kick(); // buffer overflow: just sync
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex)
            {
                Log.Warn("Evergreen watcher: " + root + ": " + ex.Message);
            }
        }
    }

    /// <summary>True for a version folder itself or its msedgewebview2.exe: the paths the rules depend on.</summary>
    public static bool IsRelevant(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return false;
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length == 1 ||
               (parts.Length == 2 && parts[1].Equals(AppPaths.WebViewExe, StringComparison.OrdinalIgnoreCase));
    }

    private void OnEvent(string root, string path)
    {
        if (IsRelevant(root, path)) Kick();
    }

    /// <summary>(Re)starts the quiet period; <see cref="Changed"/> fires when it ends.</summary>
    public void Kick()
    {
        if (!_disposed) _quiet.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _quiet.Dispose();
    }
}
