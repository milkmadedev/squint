// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Squint;

/// <summary>
/// The allowlist behind a green VERIFIED.
///
/// Membership is deliberately *not* based on popularity. Popular-domain lists (Tranco, Umbrella)
/// are full of sites that host arbitrary user content — bit.ly, blogspot.com, githubusercontent.com
/// are all top-ranked and all routinely serve malware. A domain earns a place here only when the
/// domain itself is the brand and the operator controls what it serves.
/// </summary>
public static class TrustedSites
{
    /// <summary>
    /// Trusted registrable domains. A match covers subdomains (mail.google.com) unless the host
    /// hits <see cref="UserContentHosts"/> below.
    /// </summary>
    private static readonly HashSet<string> Trusted = new(StringComparer.OrdinalIgnoreCase)
    {
        // search / platforms
        "google.com", "google.co.uk", "google.ca", "google.com.au", "google.de", "google.fr",
        "youtube.com", "youtu.be", "bing.com", "duckduckgo.com", "ecosia.org", "startpage.com",
        "apple.com", "icloud.com", "microsoft.com", "live.com", "office.com", "office365.com",
        "outlook.com", "msn.com", "windows.com", "xbox.com", "bing.net", "azure.com",

        // social
        "facebook.com", "fb.com", "messenger.com", "instagram.com", "threads.net",
        "x.com", "twitter.com", "reddit.com", "redd.it", "linkedin.com", "tiktok.com",
        "pinterest.com", "snapchat.com", "tumblr.com", "bsky.app", "quora.com", "nextdoor.com",

        // commerce
        "amazon.com", "amazon.co.uk", "amazon.ca", "amazon.de", "amazon.fr", "amazon.com.au",
        "amazon.co.jp", "amazon.in", "ebay.com", "ebay.co.uk", "etsy.com", "walmart.com",
        "target.com", "bestbuy.com", "costco.com", "ikea.com", "aliexpress.com", "alibaba.com",
        "newegg.com", "argos.co.uk", "johnlewis.com", "asos.com", "shein.com", "temu.com",

        // gaming
        "steampowered.com", "steamcommunity.com", "steamstatic.com", "epicgames.com",
        "unrealengine.com", "gog.com", "ea.com", "origin.com", "ubisoft.com", "ubi.com",
        "battle.net", "blizzard.com", "riotgames.com", "leagueoflegends.com", "valorant.com",
        "playstation.com", "nintendo.com", "roblox.com", "minecraft.net", "mojang.com",
        "twitch.tv", "humblebundle.com", "itch.io", "gamebanana.com", "speedrun.com",
        "faceit.com", "esea.net", "hltv.org", "op.gg", "tracker.gg",

        // developer
        "github.com", "gitlab.com", "bitbucket.org", "stackoverflow.com", "stackexchange.com",
        "npmjs.com", "pypi.org", "nuget.org", "crates.io", "rubygems.org", "packagist.org",
        "docker.com", "kubernetes.io", "python.org", "nodejs.org", "rust-lang.org", "go.dev",
        "golang.org", "java.com", "oracle.com", "mozilla.org", "w3.org", "apache.org",
        "kernel.org", "debian.org", "ubuntu.com", "archlinux.org", "fedoraproject.org",
        "gnu.org", "sourceforge.net", "jetbrains.com", "visualstudio.com", "unity.com",
        "godotengine.org", "developer.mozilla.org", "readthedocs.io", "mdn.dev",

        // reference / news
        "wikipedia.org", "wikimedia.org", "wiktionary.org", "archive.org", "bbc.com", "bbc.co.uk",
        "cnn.com", "nytimes.com", "washingtonpost.com", "theguardian.com", "reuters.com",
        "apnews.com", "npr.org", "economist.com", "ft.com", "bloomberg.com", "wsj.com",
        "sky.com", "independent.co.uk", "telegraph.co.uk", "arstechnica.com", "theverge.com",
        "wired.com", "techcrunch.com", "engadget.com", "tomshardware.com", "anandtech.com",

        // streaming / media
        "netflix.com", "spotify.com", "hulu.com", "disneyplus.com", "primevideo.com", "max.com",
        "paramountplus.com", "peacocktv.com", "crunchyroll.com", "soundcloud.com", "bandcamp.com",
        "vimeo.com", "last.fm", "genius.com", "imdb.com", "letterboxd.com", "goodreads.com",

        // communication
        "discord.com", "slack.com", "zoom.us", "telegram.org", "signal.org", "whatsapp.com",
        "protonmail.com", "proton.me", "tutanota.com", "fastmail.com", "gmail.com",

        // productivity / cloud
        "dropbox.com", "notion.so", "figma.com", "canva.com", "adobe.com", "atlassian.com",
        "trello.com", "asana.com", "monday.com", "airtable.com", "miro.com", "linear.app",
        "cloudflare.com", "digitalocean.com", "heroku.com", "linode.com", "vultr.com",

        // finance — the real domains, so lookalikes stand out by contrast
        "paypal.com", "stripe.com", "wise.com", "revolut.com", "monzo.com", "starlingbank.com",
        "chase.com", "bankofamerica.com", "wellsfargo.com", "citi.com", "capitalone.com",
        "hsbc.com", "hsbc.co.uk", "barclays.co.uk", "lloydsbank.com", "nationwide.co.uk",
        "santander.co.uk", "natwest.com", "amex.com", "americanexpress.com", "visa.com",
        "mastercard.com", "coinbase.com", "kraken.com", "binance.com",

        // AI
        "anthropic.com", "claude.ai", "openai.com", "chatgpt.com", "huggingface.co",
        "perplexity.ai", "midjourney.com", "stability.ai",

        // government / education
        "gov.uk", "nhs.uk", "usa.gov", "irs.gov", "ssa.gov", "cdc.gov", "nih.gov", "nasa.gov",
        "europa.eu", "who.int", "un.org", "mit.edu", "stanford.edu", "harvard.edu",
        "ox.ac.uk", "cam.ac.uk",

        // misc utilities
        "speedtest.net", "virustotal.com", "haveibeenpwned.com", "urlhaus.abuse.ch",
        "letsencrypt.org", "1password.com", "bitwarden.com", "lastpass.com", "keepass.info",
    };

    /// <summary>
    /// Hosts under an otherwise-trusted domain that serve whatever a stranger uploaded.
    /// These can never be VERIFIED — a Google Drive link is only as trustworthy as its uploader.
    /// Matched against the host itself and any subdomain of it.
    /// </summary>
    private static readonly string[] UserContentHosts =
    [
        // Google
        "drive.google.com", "docs.google.com", "sites.google.com", "script.google.com",
        "groups.google.com", "storage.googleapis.com", "googleusercontent.com", "appspot.com",
        "firebasestorage.googleapis.com", "web.app", "firebaseapp.com", "page.link", "goo.gl",
        "blogspot.com",

        // Microsoft / Apple
        "1drv.ms", "onedrive.live.com", "sharepoint.com", "azurewebsites.net",
        "blob.core.windows.net", "icloud.com.cn",

        // code hosting raw content
        "githubusercontent.com", "github.io", "gist.github.com", "gitlab.io", "pages.dev",
        "workers.dev", "netlify.app", "vercel.app", "surge.sh", "glitch.me", "repl.co",
        "replit.dev", "herokuapp.com", "onrender.com", "fly.dev",

        // chat / file CDNs
        "cdn.discordapp.com", "media.discordapp.net", "discord.gg", "files.slack.com",
        "t.me", "telegra.ph",

        // generic hosting and shorteners
        "s3.amazonaws.com", "amazonaws.com", "cloudfront.net", "wordpress.com", "weebly.com",
        "wixsite.com", "squarespace.com", "webflow.io", "carrd.co", "notion.site",
        "bit.ly", "tinyurl.com", "t.co", "ow.ly", "buff.ly", "is.gd", "cutt.ly", "rebrand.ly",
        "shorturl.at", "rb.gy", "t.ly", "lnkd.in", "shorte.st", "adf.ly",
    ];

    /// <summary>Two-part public suffixes, so bbc.co.uk resolves to bbc.co.uk and not co.uk.</summary>
    private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "net.uk", "sch.uk", "nhs.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au", "co.nz", "net.nz", "org.nz",
        "co.za", "org.za", "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp", "com.br", "net.br",
        "com.mx", "com.ar", "com.co", "com.pe", "com.tr", "com.cn", "net.cn", "org.cn",
        "gov.cn", "edu.cn", "com.hk", "com.sg", "com.my", "com.tw", "co.kr", "or.kr",
        "co.in", "net.in", "org.in", "com.ph", "com.vn", "co.il", "com.pl", "com.ua",
        "com.ru", "com.es", "com.pt", "com.gr", "co.th", "or.th", "com.sa", "com.eg",
        "co.id", "com.ng", "com.pk", "com.bd", "co.ke",
    };

    public sealed record TrustCheck(bool Trusted, string Reason);

    /// <summary>
    /// Decides whether a destination is well-known enough to earn a green light. Everything that
    /// isn't explicitly recognised falls through to caution — that's the intended default.
    /// </summary>
    public static TrustCheck Check(string url, IEnumerable<string>? extraTrusted = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new TrustCheck(false, "unparseable URL");

        // A trusted brand served over plain HTTP isn't a trusted connection.
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return new TrustCheck(false, "not served over HTTPS");

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();

        if (MatchesAny(host, UserContentHosts))
            return new TrustCheck(false, "hosts user-uploaded content");

        var registrable = RegistrableDomain(host);

        if (Trusted.Contains(registrable) || Trusted.Contains(host))
            return new TrustCheck(true, registrable);

        if (extraTrusted is not null)
        {
            foreach (var entry in extraTrusted)
            {
                var clean = entry.Trim().TrimStart('*', '.').ToLowerInvariant();
                if (clean.Length == 0) continue;
                if (registrable == clean || host == clean || host.EndsWith("." + clean, StringComparison.Ordinal))
                    return new TrustCheck(true, clean + " (your list)");
            }
        }

        return new TrustCheck(false, "not a recognised site");
    }

    /// <summary>
    /// Looks for a domain that is pretending to be one on the allowlist — steamcomrnunity.com
    /// for steamcommunity.com, paypa1.com for paypal.com. Returns the impersonated domain, or
    /// null. Only ever called for domains that failed the trust check.
    /// </summary>
    public static string? FindLookalike(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var registrable = RegistrableDomain(uri.IdnHost.TrimEnd('.').ToLowerInvariant());
        if (Trusted.Contains(registrable)) return null;

        // Short names collide by accident (x.com vs y.com), so only judge substantial ones.
        var label = registrable.Split('.')[0];
        if (label.Length < 5) return null;

        var skeleton = Skeleton(label);

        foreach (var trusted in Trusted)
        {
            var trustedLabel = trusted.Split('.')[0];
            if (trustedLabel.Length < 5) continue;
            if (label == trustedLabel) continue;   // same brand, different TLD — handled elsewhere

            // Visual substitution: rn->m, 1->l, 0->o. Catches the deliberate fakes.
            if (skeleton == Skeleton(trustedLabel)) return trusted;

            // One-character typo. Catches googie.com, amazom.com.
            if (Math.Abs(label.Length - trustedLabel.Length) <= 1 && Distance(label, trustedLabel) == 1)
                return trusted;
        }

        return null;
    }

    /// <summary>Collapses characters that look alike on screen into one canonical form.</summary>
    private static string Skeleton(string label)
    {
        var s = label.Replace("-", "")
            .Replace("rn", "m")
            .Replace("vv", "w")
            .Replace("cl", "d");

        return new string(s.Select(c => c switch
        {
            '0' => 'o',
            '1' or 'l' or '|' => 'i',
            '3' => 'e',
            '4' => 'a',
            '5' => 's',
            '7' => 't',
            '8' => 'b',
            '9' => 'g',
            _ => c,
        }).ToArray());
    }

    /// <summary>Levenshtein, bailing out as soon as it exceeds 1 — that's all we care about.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var best = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                best = Math.Min(best, current[j]);
            }

            if (best > 1) return 2;   // already past anything we'd act on
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static bool MatchesAny(string host, string[] entries)
    {
        foreach (var entry in entries)
        {
            if (host == entry || host.EndsWith("." + entry, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>example.co.uk from www.example.co.uk. Good enough without shipping the full PSL.</summary>
    public static string RegistrableDomain(string host)
    {
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2) return host;

        var lastTwo = labels[^2] + "." + labels[^1];
        return MultiLabelSuffixes.Contains(lastTwo) && labels.Length >= 3
            ? labels[^3] + "." + lastTwo
            : lastTwo;
    }
}
