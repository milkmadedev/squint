// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;

namespace Squint;

public enum Verdict { Processing, Verified, Caution, Suspect }

public sealed record CheckResult(Verdict Verdict, string Detail);

/// <summary>
/// Combines the three feeds into one verdict.
///
/// SUSPECT  - any source says this is bad.
/// VERIFIED - the destination is a recognised, operator-controlled site AND nothing flagged it.
/// CAUTION  - everything else, which is most links.
///
/// The rule that matters: absence of evidence is not evidence of absence. Every one of these
/// services is a blocklist, so "no match" means "not listed" and never "safe" — fresh malware
/// routinely isn't listed for hours. Green therefore requires a positive signal, never silence.
/// </summary>
public static class Scanner
{
    /// <summary>One engine out of ~90 is usually a false positive; two is a pattern.</summary>
    private const int EnginesForSuspect = 2;

    public static async Task<CheckResult> ScanAsync(
        RedirectResult redirects, Settings settings, CancellationToken ct)
    {
        // Google takes the whole chain in one request. The other two are per-URL, so they see
        // the ends of the chain — the link you copied and the page you'd actually land on.
        var ends = redirects.WasRedirected
            ? new[] { redirects.Final, redirects.Original }
            : [redirects.Final];

        var gsbTask = SafeBrowsing.LookupAsync(redirects.Chain, settings.ApiKey, ct);
        var vtTask = VirusTotal.LookupAsync(redirects.Final, settings.VirusTotalKey, ct);
        var hausTask = UrlHaus.LookupAsync(ends, settings.UrlHausKey, ct);

        await Task.WhenAll(gsbTask, vtTask, hausTask).ConfigureAwait(false);

        return Combine(
            await gsbTask.ConfigureAwait(false),
            await vtTask.ConfigureAwait(false),
            await hausTask.ConfigureAwait(false),
            redirects,
            settings);
    }

    private static CheckResult Combine(
        GsbResult gsb, VtResult vt, HausResult haus, RedirectResult redirects, Settings settings)
    {
        // ---- SUSPECT: a source with authority says this is bad --------------------------
        var flags = new List<string>();

        if (gsb.Status == GsbStatus.Match)
        {
            var redirected = redirects.WasRedirected
                && gsb.MatchedUrl.Length > 0
                && !string.Equals(gsb.MatchedUrl, redirects.Original, StringComparison.OrdinalIgnoreCase);

            flags.Add(redirected
                ? $"Google flagged the destination: {gsb.Threats}"
                : $"Google flagged this: {gsb.Threats}");
        }

        if (haus.Status == HausStatus.Listed)
        {
            var live = haus.UrlStatus == "online" ? ", currently live" : "";
            flags.Add($"URLhaus lists this as {haus.Threat}{live}");
        }

        if (vt.Malicious >= EnginesForSuspect)
            flags.Add($"VirusTotal: {vt.Malicious} of {vt.Engines} engines call it malicious");

        // Impersonation is the one thing the feeds routinely miss — a fresh typosquat is on
        // nobody's blocklist yet, which is exactly what makes it work.
        var lookalike = TrustedSites.FindLookalike(redirects.Final);
        if (lookalike is not null)
        {
            var host = Uri.TryCreate(redirects.Final, UriKind.Absolute, out var u) ? u.Host : redirects.Final;
            flags.Add($"\"{host}\" is impersonating {lookalike} — check it character by character");
        }

        if (flags.Count > 0)
            return new CheckResult(Verdict.Suspect, string.Join(". ", flags) + ".");

        // ---- VERIFIED: a recognised destination that nothing flagged --------------------
        var trust = TrustedSites.Check(redirects.Final, settings.TrustedDomains);
        var flagged = vt.Malicious + vt.Suspicious;

        // On a known-good domain a lone dissenting engine out of ~90 is noise — youtube.com and
        // google.com pick one up routinely. Two or more malicious already returned SUSPECT above,
        // so this only forgives a single detection, and only for allowlisted destinations.
        if (trust.Trusted && flagged <= 1)
        {
            var via = redirects.WasRedirected
                ? $"Redirects to {trust.Reason}, a known site."
                : $"Known site ({trust.Reason}).";

            var dissent = flagged == 1
                ? $" 1 of {vt.Engines} VirusTotal engines disagrees (treated as noise)."
                : "";

            return new CheckResult(Verdict.Verified, $"{via} {Coverage(gsb, vt, haus)}{dissent}");
        }

        // ---- CAUTION: everything else ---------------------------------------------------
        return new CheckResult(Verdict.Caution, CautionReason(gsb, vt, haus, trust, flagged));
    }

    private static string CautionReason(
        GsbResult gsb, VtResult vt, HausResult haus, TrustedSites.TrustCheck trust, int flagged)
    {
        // On an unrecognised site even one detection is worth saying out loud.
        if (flagged > 0)
        {
            return $"{flagged} of {vt.Engines} VirusTotal engines flagged this — possibly a false "
                 + "positive, but don't treat it as clean.";
        }

        if (gsb.Status == GsbStatus.NotConfigured
            && vt.Status == VtStatus.NotConfigured
            && haus.Status == HausStatus.NotConfigured)
        {
            return "No API keys set — right-click the tray icon to add them.";
        }

        var reason = trust.Reason switch
        {
            "hosts user-uploaded content" =>
                "This host serves user-uploaded files, so the site being well-known means nothing here.",
            "not served over HTTPS" =>
                "Not served over HTTPS.",
            _ =>
                "Not on the known-good list.",
        };

        return $"{reason} {Coverage(gsb, vt, haus)}";
    }

    /// <summary>Says plainly who actually looked, so silence is never mistaken for approval.</summary>
    private static string Coverage(GsbResult gsb, VtResult vt, HausResult haus)
    {
        var checkedBy = new List<string>();
        var missing = new List<string>();

        // Say *why* a source is missing, so a rate limit doesn't look like a missing key and
        // neither one looks like a clean result.
        switch (gsb.Status)
        {
            case GsbStatus.NoMatch: checkedBy.Add("Google"); break;
            case GsbStatus.NotConfigured: missing.Add("Google (no key)"); break;
            default: missing.Add("Google (error)"); break;
        }

        switch (vt.Status)
        {
            // Only claim VirusTotal as "clear" when it actually was. A lone detection is
            // reported by the caller instead, so it never reads as both clear and flagged.
            case VtStatus.Analyzed when vt.Malicious + vt.Suspicious == 0:
                checkedBy.Add($"VirusTotal (0/{vt.Engines})"); break;
            case VtStatus.Analyzed: break;
            case VtStatus.Unknown: missing.Add("VirusTotal (never scanned it)"); break;
            case VtStatus.RateLimited: missing.Add("VirusTotal (rate limited, retry in a minute)"); break;
            case VtStatus.NotConfigured: missing.Add("VirusTotal (no key)"); break;
            default: missing.Add("VirusTotal (error)"); break;
        }

        switch (haus.Status)
        {
            case HausStatus.NoResults: checkedBy.Add("URLhaus"); break;
            case HausStatus.NotConfigured: missing.Add("URLhaus (no key)"); break;
            default: missing.Add("URLhaus (error)"); break;
        }

        var sb = new StringBuilder();

        if (checkedBy.Count > 0)
            sb.Append("Clear on ").Append(Join(checkedBy)).Append('.');
        else
            sb.Append("Nothing could check it.");

        if (missing.Count > 0)
            sb.Append(" No data from ").Append(Join(missing)).Append('.');

        return sb.ToString();
    }

    private static string Join(List<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
