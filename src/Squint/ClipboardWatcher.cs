// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Squint;

/// <summary>
/// Message-only window that listens for clipboard changes and reports any URL it finds.
/// </summary>
public sealed partial class ClipboardWatcher : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveClipboardFormatListener(IntPtr hwnd);

    [GeneratedRegex(@"(?:https?|ftp)://[^\s<>""'`\\^{}|]+", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeUrl();

    // steam:// can't be checked by the online feeds, but the command it carries is worth reading.
    [GeneratedRegex(@"steam://[^\s<>""'`\\^{}|]+", RegexOptions.IgnoreCase)]
    private static partial Regex SteamUrl();

    // A whole-clipboard bare domain, e.g. highlighting "example.com/page" and hitting Ctrl+C.
    [GeneratedRegex(@"^(?:www\.)?[a-z0-9][a-z0-9\-]*(?:\.[a-z0-9\-]+)*\.(?<tld>[a-z]{2,24})(?::\d{2,5})?(?:[/?#]\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BareDomain();

    /// <summary>
    /// Copying a filename is common and "report.docx" parses as a perfectly good bare domain.
    /// Only applies without a scheme — "https://example.zip/x" is still checked.
    /// </summary>
    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "dll", "msi", "bat", "cmd", "ps1", "sh", "bin", "iso", "img", "dmg", "apk",
        "zip", "rar", "tar", "gz", "bz2", "xz", "cab",
        "png", "jpg", "jpeg", "gif", "bmp", "svg", "ico", "webp", "psd", "ai",
        "mp3", "mp4", "mkv", "avi", "wav", "flac", "webm", "m4a", "wmv",
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "rtf", "txt", "csv", "tsv",
        "json", "xml", "yml", "yaml", "toml", "ini", "cfg", "conf", "log", "bak", "tmp", "lock",
        "html", "htm", "css", "js", "ts", "jsx", "tsx", "py", "rb", "go", "rs", "java", "cs",
        "cpp", "cc", "hpp", "php", "sql", "db", "sqlite", "md", "lnk", "url", "torrent",
    };

    private readonly HwndSource _source;
    private readonly DispatcherTimer _debounce;
    private string _lastText = "";

    /// <summary>Raised on the UI thread with the URL that was copied.</summary>
    public event Action<string>? UrlCopied;

    public bool Paused { get; set; }

    public ClipboardWatcher()
    {
        _source = new HwndSource(new HwndSourceParameters("Squint.ClipboardWatcher")
        {
            ParentWindow = HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);

        // Apps often write the clipboard two or three times in a row; settle first.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Read(); };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && !Paused)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        return IntPtr.Zero;
    }

    /// <summary>Reads the clipboard now and raises <see cref="UrlCopied"/> if it holds a link.</summary>
    public void Read(bool force = false)
    {
        var text = TryGetText();
        if (string.IsNullOrWhiteSpace(text)) return;

        // Ctrl+C on the same link twice shouldn't re-toast unless the user asked for it.
        if (!force && text == _lastText) return;
        _lastText = text;

        if (TryGetUrl(text, out var url))
            UrlCopied?.Invoke(url);
    }

    /// <summary>The clipboard is a shared lock; the owning app may hold it for a few ms.</summary>
    private static string? TryGetText()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static bool TryGetUrl(string text, out string url)
    {
        url = "";
        var trimmed = text.Trim();
        if (trimmed.Length is 0 or > 4000) return false;

        // Checked first: a steam:// link is often pasted alongside a harmless web address,
        // and the steam:// one is the part that can actually do something.
        var steam = SteamUrl().Match(trimmed);
        if (steam.Success)
        {
            url = TrimTrailing(steam.Value);
            return url.Length > "steam://".Length;
        }

        var m = SchemeUrl().Match(trimmed);
        if (m.Success)
        {
            url = TrimTrailing(m.Value);
        }
        else
        {
            var bare = BareDomain().Match(trimmed);
            if (!bare.Success) return false;
            if (FileExtensions.Contains(bare.Groups["tld"].Value)) return false;

            url = "https://" + TrimTrailing(trimmed);
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host);
    }

    /// <summary>Drops sentence punctuation and unbalanced brackets glued to the end of a link.</summary>
    private static string TrimTrailing(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            if (".,;:!?'\"".Contains(last)) { url = url[..^1]; continue; }
            if (last is ')' or ']' && url.Count(c => c == last) > url.Count(c => c == (last == ')' ? '(' : '[')))
            {
                url = url[..^1];
                continue;
            }

            break;
        }

        return url;
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_source.Handle != IntPtr.Zero) RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
