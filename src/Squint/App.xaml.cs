// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Squint;

public partial class App : Application
{
    private const string MutexName = "Squint.SingleInstance";
    private const string ShowSettingsSignalName = "Squint.ShowSettings";

    private static Mutex? _instanceMutex;
    private EventWaitHandle? _showSettingsSignal;
    private RegisteredWaitHandle? _signalRegistration;

    private Settings _settings = null!;
    private ClipboardWatcher _watcher = null!;
    private ToastWindow _toast = null!;
    private System.Windows.Forms.NotifyIcon _tray = null!;
    private System.Windows.Forms.ToolStripMenuItem _pauseItem = null!;
    private System.Drawing.Icon _iconActive = null!;
    private System.Drawing.Icon _iconPaused = null!;

    private CancellationTokenSource? _inFlight;
    private readonly Dictionary<string, (CheckResult Result, string FinalUrl, DateTime At)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Already running: hand off to that copy so launching the app again opens settings
            // rather than nagging. This is the reliable way in, since Windows 11 buries new
            // tray icons in the overflow flyout.
            if (EventWaitHandle.TryOpenExisting(ShowSettingsSignalName, out var signal))
            {
                signal.Set();
                signal.Dispose();
            }

            Shutdown();
            return;
        }

        // Listen for later launches asking us to surface the settings window.
        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
        _signalRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showSettingsSignal,
            (_, _) => Dispatcher.Invoke(ShowApiKeyWindow),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _settings = Settings.Load();
        _toast = new ToastWindow();

        _watcher = new ClipboardWatcher();
        _watcher.UrlCopied += OnUrlCopied;

        BuildTray();

        // Checking always starts off: the tray icon being there means the app is running, and
        // turning it on is a deliberate click. Nothing leaves this machine until you do.
        SetPaused(true, announce: false);

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            ShowApiKeyWindow();
        }
        else if (e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
        {
            PreviewToasts();
        }
        else if (string.IsNullOrWhiteSpace(_settings.ApiKey)
                 && string.IsNullOrWhiteSpace(_settings.VirusTotalKey)
                 && string.IsNullOrWhiteSpace(_settings.UrlHausKey))
        {
            _toast.ShowMessage(Verdict.Caution, "SETUP", "Squint",
                "Add an API key, then click the tray icon to start checking links.",
                autoDismissSeconds: 8);
            ShowApiKeyWindow();
        }
        else
        {
            _toast.ShowMessage(Verdict.Caution, "OFF", "Squint",
                "Running, but link checking is off. Click the tray icon under the ^ arrow to turn it on.",
                autoDismissSeconds: 6);
        }
    }

    private void BuildTray()
    {
        _iconActive = LoadIcon("app.ico");
        _iconPaused = LoadIcon("app-paused.ico");

        var menu = new System.Windows.Forms.ContextMenuStrip();

        _pauseItem = new System.Windows.Forms.ToolStripMenuItem("Turn link checking off") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => SetPaused(_pauseItem.Checked);

        menu.Items.Add("Check clipboard now", null, (_, _) => _watcher.Read(force: true));
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Preview toast styles", null, (_, _) => PreviewToasts());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("API keys…", null, (_, _) => ShowApiKeyWindow());
        menu.Items.Add("Open settings folder", null, (_, _) =>
        {
            Directory.CreateDirectory(Settings.Folder);
            Process.Start(new ProcessStartInfo(Settings.Folder) { UseShellExecute = true });
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = _iconActive,
            Text = "Squint — watching clipboard",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Left-click is the on/off switch; right-click opens the menu.
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left) SetPaused(!_watcher.Paused);
        };

        PinTrayIcon();
    }

    /// <summary>
    /// Windows 11 files new tray icons into the overflow flyout. Promote ours onto the taskbar
    /// proper, because "the icon is there" is how you know the app is running at all.
    ///
    /// Windows only creates the registry entry once the icon has actually been shown, and it
    /// does that lazily — hence the retries. Explorer caches the list, so the change lands at
    /// the next sign-in, which is fine given we start with Windows.
    /// </summary>
    private static void PinTrayIcon() => _ = Task.Run(async () =>
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        // Explorer can take a while to persist a newly-seen icon, so keep looking for a few
        // minutes. Worst case this misses and the next sign-in's run gets it instead.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));

            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\NotifyIconSettings", writable: true);
                if (root is null) continue;

                foreach (var name in root.GetSubKeyNames())
                {
                    using var entry = root.OpenSubKey(name, writable: true);
                    if (entry?.GetValue("ExecutablePath") is not string path) continue;
                    if (!string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) continue;

                    if (entry.GetValue("IsPromoted") is not int promoted || promoted != 1)
                        entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);

                    return;
                }
            }
            catch
            {
                // Registry busy or the key vanished mid-enumeration; just try again.
            }
        }
    });

    /// <summary>
    /// Single place that changes paused state, so the icon and menu never drift apart.
    /// <paramref name="announce"/> is false when setting the initial state at startup, where a
    /// toast saying "paused" the instant you log in would just be noise.
    /// </summary>
    private void SetPaused(bool paused, bool announce = true)
    {
        var alreadyThere = _watcher.Paused == paused && _pauseItem.Checked == paused;

        _watcher.Paused = paused;
        if (_pauseItem.Checked != paused) _pauseItem.Checked = paused;   // re-enters once, then settles

        _tray.Icon = paused ? _iconPaused : _iconActive;
        _tray.Text = paused
            ? "Squint — checking is OFF (click to turn on)"
            : "Squint — checking links";

        if (alreadyThere || !announce) return;

        _toast.ShowMessage(
            paused ? Verdict.Caution : Verdict.Processing,
            paused ? "OFF" : "ON",
            "Squint",
            paused
                ? "Link checking is off. Click the tray icon again to turn it back on."
                : "Link checking is on — copy a link to check it.",
            autoDismissSeconds: 3);
    }

    private static System.Drawing.Icon LoadIcon(string name)
    {
        var stream = GetResourceStream(new Uri($"Assets/{name}", UriKind.Relative))?.Stream;
        return stream is not null ? new System.Drawing.Icon(stream) : System.Drawing.SystemIcons.Application;
    }

    private void ShowApiKeyWindow()
    {
        // Non-modal: a modal dialog here would block the clipboard hook's message pump.
        foreach (Window w in Windows)
        {
            if (w is ApiKeyWindow existing) { BringToFront(existing); return; }
        }

        BringToFront(new ApiKeyWindow(_settings));
    }

    /// <summary>
    /// Windows denies foreground rights to a process that isn't already active, so Activate()
    /// alone can leave the window buried. Bouncing Topmost forces it in front.
    /// </summary>
    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    /// <summary>Cycles the toast states so you can see what each one looks like.</summary>
    private async void PreviewToasts()
    {
        _toast.ShowProcessing("https://bit.ly/xY7q", null, "Following redirects…");
        await Task.Delay(1400);

        _toast.ShowResult("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            new CheckResult(Verdict.Verified,
                "Redirects to youtube.com, a known site. Clear on Google, VirusTotal (0/94) and URLhaus."));
        await Task.Delay(3200);

        _toast.ShowResult("https://some-blog.example/post", "https://some-blog.example/post",
            new CheckResult(Verdict.Caution,
                "Not on the known-good list. Clear on Google and URLhaus. No data from VirusTotal."));
        await Task.Delay(3200);

        _toast.ShowResult("https://bit.ly/xY7q", "http://malware.testing.google.test/testing/malware/",
            new CheckResult(Verdict.Suspect,
                "Google flagged the destination: malware. URLhaus lists this as malware download, currently live."));
    }

    private async void OnUrlCopied(string url)
    {
        // A newer copy always wins — drop whatever check was still running.
        _inFlight?.Cancel();
        _inFlight?.Dispose();
        _inFlight = new CancellationTokenSource();
        var token = _inFlight.Token;

        if (TryCache(url, out var cached, out var cachedFinal))
        {
            _toast.ShowResult(url, cachedFinal, cached);
            return;
        }

        if (SteamLinks.IsSteamLink(url))
        {
            await InspectSteamLinkAsync(url, token);
            return;
        }

        _toast.ShowProcessing(url, null, _settings.FollowRedirects ? "Following redirects…" : "Checking link…");

        var redirects = _settings.FollowRedirects
            ? await RedirectResolver.ResolveAsync(url, token)
            : new RedirectResult([url], null);

        if (token.IsCancellationRequested) return;

        _toast.ShowProcessing(url, redirects.Final, "Checking Google Safe Browsing and VirusTotal…");

        var result = await Scanner.ScanAsync(redirects, _settings, token);
        if (token.IsCancellationRequested) return;

        // Only cache real answers — a timeout or a missing key should be retried next time.
        if (result.Verdict is Verdict.Verified or Verdict.Suspect)
        {
            if (_cache.Count > 200) _cache.Clear();
            _cache[url] = (result, redirects.Final, DateTime.UtcNow);
        }

        _toast.ShowResult(url, redirects.Final, result);
    }

    /// <summary>
    /// steam:// links can't go to the online feeds — those only handle http(s). We read the
    /// command instead, and when the link merely wraps a web address, scan that properly.
    /// </summary>
    private async Task InspectSteamLinkAsync(string url, CancellationToken token)
    {
        var analysis = SteamLinks.Analyse(url);

        // A dangerous command is dangerous regardless of what it points at.
        if (analysis.Verdict == Verdict.Suspect || analysis.EmbeddedUrl is null)
        {
            _toast.ShowMessage(analysis.Verdict, analysis.Verdict.ToString().ToUpperInvariant(),
                analysis.Headline, analysis.Detail, url,
                analysis.Verdict == Verdict.Suspect ? 14 : 8);
            return;
        }

        _toast.ShowMessage(Verdict.Processing, "PROCESSING", analysis.Headline,
            "Checking the page this opens…", analysis.EmbeddedUrl, autoDismissSeconds: 0);

        var redirects = _settings.FollowRedirects
            ? await RedirectResolver.ResolveAsync(analysis.EmbeddedUrl, token)
            : new RedirectResult([analysis.EmbeddedUrl], null);

        if (token.IsCancellationRequested) return;

        var scan = await Scanner.ScanAsync(redirects, _settings, token);
        if (token.IsCancellationRequested) return;

        // Steam's browser is a weaker sandbox than a real one, so never promote to green.
        var verdict = scan.Verdict == Verdict.Verified ? Verdict.Caution : scan.Verdict;

        _toast.ShowMessage(verdict, verdict.ToString().ToUpperInvariant(), analysis.Headline,
            $"Opens {HostOf(redirects.Final)} in Steam's browser. {scan.Detail}",
            redirects.Final,
            verdict == Verdict.Suspect ? 14 : 8);
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

    private bool TryCache(string url, out CheckResult result, out string finalUrl)
    {
        if (_cache.TryGetValue(url, out var hit) && DateTime.UtcNow - hit.At < CacheTtl)
        {
            result = hit.Result;
            finalUrl = hit.FinalUrl;
            return true;
        }

        result = null!;
        finalUrl = url;
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _signalRegistration?.Unregister(null);
        _showSettingsSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
