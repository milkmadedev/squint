// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Squint;

public partial class ToastWindow : Window
{
    private static readonly Dictionary<Verdict, BitmapImage> Icons = new()
    {
        [Verdict.Processing] = Load("processing"),
        [Verdict.Verified] = Load("verified"),
        [Verdict.Caution] = Load("caution"),
        [Verdict.Suspect] = Load("suspect"),
    };

    private static BitmapImage Load(string name) =>
        new(new Uri($"pack://application:,,,/Assets/{name}.png", UriKind.Absolute));

    private readonly DispatcherTimer _dismiss = new();
    private bool _hovered;

    public ToastWindow()
    {
        InitializeComponent();

        _dismiss.Tick += (_, _) =>
        {
            _dismiss.Stop();
            if (_hovered) { _dismiss.Start(); return; }   // keep it up while the cursor is on it
            FadeOut();
        };

        MouseEnter += (_, _) => _hovered = true;
        MouseLeave += (_, _) => _hovered = false;
        MouseLeftButtonDown += (_, _) => FadeOut();
        Loaded += (_, _) => { NoActivate(); Reposition(); };
    }

    /// <summary>
    /// The "working on it" state. Stays up until a result replaces it.
    /// <paramref name="resolvedTo"/> is the redirect destination once it's known.
    /// </summary>
    public void ShowProcessing(string url, string? resolvedTo = null, string status = "Checking link…") =>
        ShowMessage(Verdict.Processing, "PROCESSING", Headline(url, resolvedTo), status,
            resolvedTo ?? url, autoDismissSeconds: 0);

    /// <summary>Swap the same toast over to the finished verdict.</summary>
    public void ShowResult(string url, string finalUrl, CheckResult result)
    {
        var (label, seconds) = result.Verdict switch
        {
            Verdict.Verified => ("VERIFIED", 4.5),
            Verdict.Caution => ("CAUTION", 8.0),
            Verdict.Suspect => ("SUSPECT", 14.0),
            _ => ("PROCESSING", 6.0),
        };

        ShowMessage(result.Verdict, label, Headline(url, finalUrl), result.Detail, finalUrl, seconds);
    }

    /// <summary>
    /// The one way anything reaches the screen. <paramref name="autoDismissSeconds"/> of 0 keeps it up.
    /// </summary>
    public void ShowMessage(
        Verdict verdict, string label, string title, string detail,
        string url = "", double autoDismissSeconds = 5)
    {
        _dismiss.Stop();
        Apply(verdict, label, title, detail, url);
        Present();

        if (autoDismissSeconds > 0)
        {
            _dismiss.Interval = TimeSpan.FromSeconds(autoDismissSeconds);
            _dismiss.Start();
        }
    }

    /// <summary>"bit.ly → evil.com" when redirected, so the real destination is the headline.</summary>
    private static string Headline(string url, string? finalUrl)
    {
        var from = HostOf(url);
        if (finalUrl is null) return from;

        var to = HostOf(finalUrl);
        return string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? from : $"{from} → {to}";
    }

    private void Apply(Verdict verdict, string label, string title, string detail, string url)
    {
        IconImage.Source = Icons[verdict];
        StatusLabel.Text = label;
        StatusLabel.Foreground = new SolidColorBrush(AccentOf(verdict));
        Card.BorderBrush = new SolidColorBrush(AccentOf(verdict)) { Opacity = 0.55 };
        HostText.Text = title;
        DetailText.Text = detail;

        // Hidden rather than Collapsed: the row keeps its height so every toast is the same size.
        UrlText.Text = url.Length > 104 ? url[..104] + "…" : url;
        UrlText.Visibility = url.Length == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    private static Color AccentOf(Verdict v) => v switch
    {
        Verdict.Verified => Color.FromRgb(0x22, 0xC5, 0x5E),
        Verdict.Caution => Color.FromRgb(0xF5, 0x9E, 0x0B),
        Verdict.Suspect => Color.FromRgb(0xEF, 0x44, 0x44),
        _ => Color.FromRgb(0x60, 0xA5, 0xFA),
    };

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

    /// <summary>Show (sliding in if hidden) and pin to the corner.</summary>
    private void Present()
    {
        var wasHidden = !IsVisible;

        if (wasHidden)
        {
            Opacity = 0;
            Show();
        }

        Reposition();

        // Cancel any in-flight fade-out from a previous toast.
        BeginAnimation(OpacityProperty, null);

        if (wasHidden) SlideIn();
        else Opacity = 1;
    }

    private void SlideIn()
    {
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (Opacity <= 0.01) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Pin to the bottom-right of the primary monitor's work area (above the taskbar).</summary>
    private void Reposition()
    {
        var work = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
        if (work is null) return;

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var deviceCorner = new Point(work.Value.Right, work.Value.Bottom);
        var corner = transform is { } t ? t.Transform(deviceCorner) : deviceCorner;

        Left = corner.X - Width;
        Top = corner.Y - Height;
    }

    /// <summary>WS_EX_NOACTIVATE — clicking or showing the toast must never steal focus.</summary>
    private void NoActivate()
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var style = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }
}
