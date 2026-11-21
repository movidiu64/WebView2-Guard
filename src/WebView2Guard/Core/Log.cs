// WebView2 Guard — Copyright (C) 2026 movidiu64. Licensed under the GNU GPL v3 or later.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WebView2Guard.Core;

public enum LogLevel { Info, Warn, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message);

/// <summary>Append-only log in ProgramData, rotated at 1 MB. Also raises an event for the UI.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Regex LineRx =
        new(@"^(?<t>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[(?<l>INFO|WARN|ERROR)\] (?<m>.*)$", RegexOptions.Compiled);

    public static event Action<LogEntry>? Written;

    /// <summary>Prefix for lines written by the headless sync task.</summary>
    public static string Prefix { get; set; } = string.Empty;

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    private static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, Prefix + message.Replace("\r", " ").Replace("\n", " "));
        lock (Gate)
        {
            try
            {
                AppPaths.EnsureDataDir();
                Rotate();
                File.AppendAllText(AppPaths.LogFile, Format(entry) + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never break the app.
            }
        }
        Written?.Invoke(entry);
    }

    public static string Format(LogEntry e) =>
        $"{e.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} [{Tag(e.Level)}] {e.Message}";

    private static string Tag(LogLevel l) => l switch
    {
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO"
    };

    private static void Rotate()
    {
        var fi = new FileInfo(AppPaths.LogFile);
        if (!fi.Exists || fi.Length < MaxBytes) return;
        var old = AppPaths.LogFile + ".1";
        File.Copy(AppPaths.LogFile, old, overwrite: true);
        File.WriteAllText(AppPaths.LogFile, string.Empty);
    }

    /// <summary>Most recent entries, newest first.</summary>
    public static List<LogEntry> ReadRecent(int max = 1000)
    {
        var result = new List<LogEntry>();
        string[] lines;
        lock (Gate)
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile)) return result;
                using var fs = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                lines = sr.ReadToEnd().Split('\n');
            }
            catch
            {
                return result;
            }
        }
        for (var i = lines.Length - 1; i >= 0 && result.Count < max; i--)
        {
            var m = LineRx.Match(lines[i].TrimEnd('\r'));
            if (!m.Success) continue;
            var t = DateTime.ParseExact(m.Groups["t"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var l = m.Groups["l"].Value switch
            {
                "WARN" => LogLevel.Warn,
                "ERROR" => LogLevel.Error,
                _ => LogLevel.Info
            };
            result.Add(new LogEntry(t, l, m.Groups["m"].Value));
        }
        return result;
    }
}
