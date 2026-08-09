// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Squint;

public enum VtStatus
{
    /// <summary>VirusTotal has an analysis for this URL — the counts are meaningful.</summary>
    Analyzed,

    /// <summary>VirusTotal has never seen this URL. No opinion either way.</summary>
    Unknown,

    NotConfigured,
    RateLimited,
    Failed,
}

public sealed record VtResult(
    VtStatus Status,
    int Malicious = 0,
    int Suspicious = 0,
    int Engines = 0,
    string Error = "")
{
    /// <summary>True when enough engines actually looked at this to mean something.</summary>
    public bool HasRealCoverage => Status == VtStatus.Analyzed && Engines >= 5;
}

/// <summary>
/// VirusTotal v3 URL lookup — roughly 90 engines instead of Google's single list.
/// The free tier is personal, non-commercial use: 4 lookups/minute, 500/day.
/// </summary>
public static class VirusTotal
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task<VtResult> LookupAsync(string url, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new VtResult(VtStatus.NotConfigured);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://www.virustotal.com/api/v3/urls/" + UrlId(url));
            request.Headers.Add("x-apikey", apiKey);

            using var resp = await Http.SendAsync(request, ct).ConfigureAwait(false);

            // A URL VirusTotal has never been asked about. Not an error, just no data.
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return new VtResult(VtStatus.Unknown);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                return new VtResult(VtStatus.RateLimited, Error: "VirusTotal rate limit (4/min on the free tier)");

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return new VtResult(VtStatus.Failed, Error: "VirusTotal rejected the API key");

            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return new VtResult(VtStatus.Failed, Error: $"VirusTotal error {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(text);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("attributes", out var attrs) ||
                !attrs.TryGetProperty("last_analysis_stats", out var stats))
            {
                return new VtResult(VtStatus.Unknown);
            }

            var malicious = Count(stats, "malicious");
            var suspicious = Count(stats, "suspicious");
            var engines = malicious + suspicious
                + Count(stats, "harmless") + Count(stats, "undetected") + Count(stats, "timeout");

            return new VtResult(VtStatus.Analyzed, malicious, suspicious, engines);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new VtResult(VtStatus.Failed, Error: "cancelled");
        }
        catch (OperationCanceledException)
        {
            return new VtResult(VtStatus.Failed, Error: "VirusTotal timed out");
        }
        catch (Exception ex)
        {
            return new VtResult(VtStatus.Failed, Error: ex.Message);
        }
    }

    private static int Count(JsonElement stats, string name) =>
        stats.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

    /// <summary>VirusTotal identifies a URL by its unpadded base64url encoding.</summary>
    private static string UrlId(string url) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
