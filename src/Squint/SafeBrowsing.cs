// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Squint;

public enum GsbStatus
{
    /// <summary>Not on any Google threat list. Says nothing about whether it is safe.</summary>
    NoMatch,

    /// <summary>Google has confirmed this is dangerous.</summary>
    Match,

    NotConfigured,
    Failed,
}

public sealed record GsbResult(GsbStatus Status, string Threats = "", string MatchedUrl = "", string Error = "");

/// <summary>
/// Google Safe Browsing v4 lookup. This is a *blocklist*: a match is a hard "this is bad",
/// and a non-match means only "not listed" — never "safe".
/// </summary>
public static class SafeBrowsing
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly string[] ThreatTypes =
        ["MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION"];

    /// <summary>Checks every hop of a redirect chain in a single request.</summary>
    public static async Task<GsbResult> LookupAsync(
        IReadOnlyList<string> urls, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new GsbResult(GsbStatus.NotConfigured);

        var distinct = urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(400).ToArray();

        var body = new
        {
            client = new { clientId = "squint", clientVersion = "1.0.0" },
            threatInfo = new
            {
                threatTypes = ThreatTypes,
                platformTypes = new[] { "ANY_PLATFORM" },
                threatEntryTypes = new[] { "URL" },
                threatEntries = distinct.Select(u => new { url = u }).ToArray(),
            },
        };

        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(
                "https://safebrowsing.googleapis.com/v4/threatMatches:find?key=" + Uri.EscapeDataString(apiKey),
                content, ct).ConfigureAwait(false);

            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return new GsbResult(GsbStatus.Failed, Error: $"Safe Browsing error {(int)resp.StatusCode}: {ShortError(text)}");

            using var doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("matches", out var matches) || matches.GetArrayLength() == 0)
                return new GsbResult(GsbStatus.NoMatch);

            var kinds = new List<string>();
            var matched = "";

            foreach (var match in matches.EnumerateArray())
            {
                if (match.TryGetProperty("threatType", out var t) && t.GetString() is { } kind)
                {
                    var friendly = Friendly(kind);
                    if (!kinds.Contains(friendly)) kinds.Add(friendly);
                }

                if (matched.Length == 0 &&
                    match.TryGetProperty("threat", out var threat) &&
                    threat.TryGetProperty("url", out var mu))
                {
                    matched = mu.GetString() ?? "";
                }
            }

            return new GsbResult(GsbStatus.Match, string.Join(", ", kinds), matched);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new GsbResult(GsbStatus.Failed, Error: "cancelled");
        }
        catch (OperationCanceledException)
        {
            return new GsbResult(GsbStatus.Failed, Error: "Safe Browsing timed out");
        }
        catch (Exception ex)
        {
            return new GsbResult(GsbStatus.Failed, Error: ex.Message);
        }
    }

    private static string Friendly(string threatType) => threatType switch
    {
        "MALWARE" => "malware",
        "SOCIAL_ENGINEERING" => "phishing",
        "UNWANTED_SOFTWARE" => "unwanted software",
        "POTENTIALLY_HARMFUL_APPLICATION" => "harmful app",
        _ => threatType.ToLowerInvariant().Replace('_', ' '),
    };

    private static string ShortError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "unknown";
        }
        catch { /* not JSON */ }

        return body.Length > 120 ? body[..120] : body;
    }
}
