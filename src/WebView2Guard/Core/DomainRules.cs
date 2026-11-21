// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using System.Text.RegularExpressions;

namespace WebView2Guard.Core;

/// <summary>
/// Per-app domain whitelist as a Chromium switch passed through the AdditionalBrowserArguments policy:
/// <c>--host-resolver-rules="MAP * ~NOTFOUND, EXCLUDE a.com, EXCLUDE *.b.com"</c> — every host name fails to resolve
/// except the excluded ones. It acts on WebView2's own DNS lookups (not when a proxy resolves the names).
/// </summary>
public static class DomainRules
{
    private const string Prefix = "--host-resolver-rules=\"MAP * ~NOTFOUND";

    private static readonly Regex DomainRx = new(
        @"^(\*\.)?[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsValid(string domain) => domain == "localhost" || DomainRx.IsMatch(domain);

    public static bool IsOurs(string? args) => args != null && args.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string Build(IEnumerable<string> domains) =>
        Prefix + string.Concat(domains.Select(d => ", EXCLUDE " + d)) + "\"";

    /// <summary>Domains in one of our values, or null when the value is not ours.</summary>
    public static List<string>? Parse(string? args)
    {
        if (!IsOurs(args)) return null;
        var inner = args![Prefix.Length..].Trim().TrimEnd('"');
        return inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith("EXCLUDE ", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[8..].Trim())
            .ToList();
    }

    /// <summary>Turns free text (lines, commas, URLs) into a clean, de-duplicated list of host names.</summary>
    public static List<string> Normalize(string text)
    {
        var result = new List<string>();
        foreach (var raw in text.Split(new[] { '\n', '\r', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var d = raw.Trim().ToLowerInvariant();
            var scheme = d.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) d = d[(scheme + 3)..];
            var slash = d.IndexOfAny(new[] { '/', ':', '?' });
            if (slash >= 0) d = d[..slash];
            d = d.Trim('.');
            if (d.Length > 0 && !result.Contains(d)) result.Add(d);
        }
        return result;
    }
}
