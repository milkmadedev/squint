// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net;
using System.Net.Http;

namespace Squint;

/// <summary>Every URL in the redirect chain, first to last. Always has at least one entry.</summary>
public sealed record RedirectResult(IReadOnlyList<string> Chain, string? Error)
{
    public string Original => Chain[0];
    public string Final => Chain[^1];
    public bool WasRedirected => Chain.Count > 1;
    public int Hops => Chain.Count - 1;
}

/// <summary>
/// Follows redirects by hand so every hop can be handed to the scanners — a shortener is
/// only as safe as wherever it lands. Headers only; no response body is ever read.
/// </summary>
public static class RedirectResolver
{
    private const int MaxHops = 10;
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(7);

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(4),
    })
    {
        Timeout = TimeSpan.FromSeconds(6),
    };

    static RedirectResolver()
    {
        // Shorteners and cloaked pages behave differently for non-browser agents.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        Http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
    }

    public static async Task<RedirectResult> ResolveAsync(string url, CancellationToken ct)
    {
        var chain = new List<string> { url };

        if (!Uri.TryCreate(url, UriKind.Absolute, out var current))
            return new RedirectResult(chain, "unparseable URL");

        if (current.Scheme is not ("http" or "https"))
            return new RedirectResult(chain, null);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current.AbsoluteUri };

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalBudget);

        try
        {
            for (var hop = 0; hop < MaxHops; hop++)
            {
                using var response = await SendAsync(current, budget.Token).ConfigureAwait(false);

                if ((int)response.StatusCode is < 300 or > 399) break;

                var location = response.Headers.Location;
                if (location is null) break;

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (next.Scheme is not ("http" or "https")) break;
                if (!seen.Add(next.AbsoluteUri)) break;      // redirect loop

                chain.Add(next.AbsoluteUri);
                current = next;

                if (hop == MaxHops - 1)
                    return new RedirectResult(chain, $"stopped after {MaxHops} redirects");
            }

            return new RedirectResult(chain, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new RedirectResult(chain, "cancelled");
        }
        catch (OperationCanceledException)
        {
            // Our own budget ran out — the partial chain is still worth scanning.
            return new RedirectResult(chain, "redirect check timed out");
        }
        catch (HttpRequestException ex)
        {
            return new RedirectResult(chain, Describe(ex));
        }
        catch (Exception ex)
        {
            return new RedirectResult(chain, ex.Message);
        }
    }

    /// <summary>HEAD is cheapest, but plenty of servers reject it — fall back to a body-less GET.</summary>
    private static async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken ct)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, uri);
        var response = await Http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode is not (HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.NotImplemented
            or HttpStatusCode.Forbidden
            or HttpStatusCode.BadRequest))
        {
            return response;
        }

        response.Dispose();
        using var get = new HttpRequestMessage(HttpMethod.Get, uri);
        return await Http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private static string Describe(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "domain doesn't resolve",
        HttpRequestError.ConnectionError => "couldn't connect",
        HttpRequestError.SecureConnectionError => "TLS handshake failed",
        _ => ex.Message,
    };
}
