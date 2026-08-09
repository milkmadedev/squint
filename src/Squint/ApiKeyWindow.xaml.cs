// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Squint;

public partial class ApiKeyWindow : Window
{
    /// <summary>Google publishes this specifically so clients can confirm they get a hit.</summary>
    private const string GoogleTestUrl = "https://testsafebrowsing.appspot.com/s/malware.html";

    private readonly Settings _settings;

    public ApiKeyWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        GsbBox.Text = settings.ApiKey;
        VtBox.Text = settings.VirusTotalKey;
        HausBox.Text = settings.UrlHausKey;
        TrustedBox.Text = string.Join(Environment.NewLine, settings.TrustedDomains);
        FollowRedirectsBox.IsChecked = settings.FollowRedirects;
        Loaded += (_, _) => { GsbBox.Focus(); GsbBox.SelectAll(); };
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        var gsbKey = GsbBox.Text.Trim();
        var vtKey = VtBox.Text.Trim();
        var hausKey = HausBox.Text.Trim();

        if (gsbKey.Length == 0 && vtKey.Length == 0 && hausKey.Length == 0)
        {
            Status("Enter at least one key first.", ok: false);
            return;
        }

        TestButton.IsEnabled = false;
        Status("Testing…", ok: true);

        var report = new StringBuilder();
        var allOk = true;

        if (gsbKey.Length > 0)
        {
            var gsb = await SafeBrowsing.LookupAsync([GoogleTestUrl], gsbKey, CancellationToken.None);
            allOk &= gsb.Status == GsbStatus.Match;
            report.Append("Safe Browsing: ").Append(gsb.Status switch
            {
                GsbStatus.Match => "working (correctly flagged Google's test URL).",
                GsbStatus.NoMatch => "key accepted, but the test URL came back clean — unexpected.",
                _ => gsb.Error.Length > 0 ? gsb.Error : "failed.",
            });
        }

        if (vtKey.Length > 0)
        {
            if (report.Length > 0) report.Append('\n');
            var vt = await VirusTotal.LookupAsync(GoogleTestUrl, vtKey, CancellationToken.None);
            allOk &= vt.Status is VtStatus.Analyzed or VtStatus.Unknown;
            report.Append("VirusTotal: ").Append(vt.Status switch
            {
                VtStatus.Analyzed => $"working ({vt.Malicious + vt.Suspicious}/{vt.Engines} engines flagged the test URL).",
                VtStatus.Unknown => "working (key accepted; that URL isn't in their database).",
                VtStatus.RateLimited => "rate limited — wait a minute and try again.",
                _ => vt.Error.Length > 0 ? vt.Error : "failed.",
            });
        }

        if (hausKey.Length > 0)
        {
            if (report.Length > 0) report.Append('\n');
            var haus = await UrlHaus.LookupAsync([GoogleTestUrl], hausKey, CancellationToken.None);
            allOk &= haus.Status is HausStatus.Listed or HausStatus.NoResults;
            report.Append("URLhaus: ").Append(haus.Status switch
            {
                HausStatus.Listed => $"working (listed as {haus.Threat}).",
                HausStatus.NoResults => "working (key accepted; that URL isn't in their feed).",
                _ => haus.Error.Length > 0 ? haus.Error : "failed.",
            });
        }

        TestButton.IsEnabled = true;
        Status(report.ToString(), allOk);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.ApiKey = GsbBox.Text.Trim();
        _settings.VirusTotalKey = VtBox.Text.Trim();
        _settings.UrlHausKey = HausBox.Text.Trim();
        _settings.FollowRedirects = FollowRedirectsBox.IsChecked == true;
        _settings.TrustedDomains = TrustedBox.Text
            .Split(['\r', '\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.Save();
        Close();
    }

    private void Status(string text, bool ok)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(ok
            ? Color.FromRgb(0x22, 0xC5, 0x5E)
            : Color.FromRgb(0xF5, 0x9E, 0x0B));
        StatusText.Visibility = Visibility.Visible;
    }
}
