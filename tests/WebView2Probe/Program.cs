// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).
//
// WebView2Probe <url> <result.json> [timeout seconds]
// Opens the URL in WebView2 with the default environment (so the per-exe policies apply) and writes
// which browser process ran it and whether the navigation succeeded.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WebView2Probe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2) return 2;
        var url = args[0];
        var output = args[1];
        var timeout = TimeSpan.FromSeconds(args.Length > 2 ? int.Parse(args[2]) : 60);
        var result = new Dictionary<string, object?> { ["url"] = url };

        ApplicationConfiguration.Initialize();
        using var form = new Form { Width = 800, Height = 600, ShowInTaskbar = false, Text = "WebView2Probe" };
        using var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);

        form.Shown += async (_, _) =>
        {
            try
            {
                var data = Path.Combine(Path.GetTempPath(), "WebView2Probe-" + Environment.ProcessId);
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                result["browserVersion"] = env.BrowserVersionString;
                await web.EnsureCoreWebView2Async(env);
                var pid = (int)web.CoreWebView2.BrowserProcessId;
                result["browserPath"] = Process.GetProcessById(pid).MainModule?.FileName;

                var done = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>();
                web.CoreWebView2.NavigationCompleted += (_, e) => done.TrySetResult(e);
                web.CoreWebView2.Navigate(url);
                var finished = await Task.WhenAny(done.Task, Task.Delay(timeout));
                if (finished == done.Task)
                {
                    result["success"] = done.Task.Result.IsSuccess;
                    result["status"] = done.Task.Result.WebErrorStatus.ToString();
                    result["httpStatus"] = done.Task.Result.HttpStatusCode;
                    result["title"] = web.CoreWebView2.DocumentTitle;
                }
                else
                {
                    result["success"] = false;
                    result["status"] = "Timeout";
                }
            }
            catch (Exception ex)
            {
                result["success"] = false;
                result["status"] = "Exception";
                result["error"] = ex.GetType().Name + ": " + ex.Message;
            }
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            form.Close();
        };
        Application.Run(form);
        return 0;
    }
}
