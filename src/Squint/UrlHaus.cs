// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Squint;

public enum HausStatus
{
    /// <summary>URLhaus has no record of this URL.</summary>
    NoResults,

    /// <summary>Listed as a malware distribution URL.</summary>
    Listed,

    NotConfigured,
    Failed,
}

public sealed record HausResult(
    HausStatus Status,
    string Threat = "",
    string UrlStatus = "",
    string MatchedUrl = "",
    string Error = "");

/// <summary>
/// abuse.ch URLhaus — a live feed of URLs actively distributing malware. Complements Safe
/// Browsing: URLhaus often lists a payload URL within minutes, well before Google does.
/// Free, but requires an Auth-Key from auth.abuse.ch since 2025.
/// </summary>
public static class UrlHaus
{
    private const string Endpoint = "https://urlhaus-api.abuse.ch/v1/url/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Checks the given URLs concurrently and returns the first hit, if any.</summary>
    public static async Task<HausResult> LookupAsync(
        IReadOnlyList<string> urls, string authKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authKey))
            return new HausResult(HausStatus.NotConfigured);

        // The API is one URL per request, so keep it to the ends of the chain.
        var targets = urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        var results = await Task.WhenAll(targets.Select(u => LookupOneAsync(u, authKey, ct)))
            .ConfigureAwait(false);

        foreach (var result in results)
        {
            if (result.Status == HausStatus.Listed) return result;
        }

        // No hits: report a real failure over a clean result, so errors never read as "safe".
        return results.FirstOrDefault(r => r.Status == HausStatus.Failed)
            ?? new HausResult(HausStatus.NoResults);
    }

    private static async Task<HausResult> LookupOneAsync(string url, string authKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("Auth-Key", authKey);
            request.Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("url", url)]);

            using var resp = await Http.SendAsync(request, ct).ConfigureAwait(false);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new HausResult(HausStatus.Failed, Error: "URLhaus rejected the Auth-Key");

            if (!resp.IsSuccessStatusCode)
                return new HausResult(HausStatus.Failed, Error: $"URLhaus error {(int)resp.StatusCode}");

            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var status = root.TryGetProperty("query_status", out var q) ? q.GetString() : null;

            if (status == "no_results") return new HausResult(HausStatus.NoResults);

            if (status != "ok")
                return new HausResult(HausStatus.Failed, Error: $"URLhaus: {status ?? "unexpected response"}");

            var threat = root.TryGetProperty("threat", out var t) ? t.GetString() ?? "" : "";
            var urlStatus = root.TryGetProperty("url_status", out var s) ? s.GetString() ?? "" : "";

            return new HausResult(HausStatus.Listed, Friendly(threat), urlStatus, url);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new HausResult(HausStatus.Failed, Error: "cancelled");
        }
        catch (OperationCanceledException)
        {
            return new HausResult(HausStatus.Failed, Error: "URLhaus timed out");
        }
        catch (Exception ex)
        {
            return new HausResult(HausStatus.Failed, Error: ex.Message);
        }
    }

    private static string Friendly(string threat) => threat switch
    {
        "malware_download" => "malware download",
        "" => "malware",
        _ => threat.Replace('_', ' '),
    };
}
