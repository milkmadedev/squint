// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Squint;

/// <summary>
/// What a steam:// link actually asks Steam to do.
/// <paramref name="EmbeddedUrl"/> is set when the link wraps a web URL that the normal
/// scanners should look at instead.
/// </summary>
public sealed record SteamAnalysis(
    Verdict Verdict,
    string Detail,
    string Headline,
    string? EmbeddedUrl = null);

/// <summary>
/// steam:// links can't be checked by Google, VirusTotal or URLhaus — those only handle http(s).
/// The protocol itself can't be faked (steam:// always opens Steam), but the *command* it carries
/// is attacker-controlled, and some commands are genuinely dangerous. So this reads the command.
/// </summary>
public static class SteamLinks
{
    private const string Scheme = "steam://";

    public static bool IsSteamLink(string url) =>
        url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Commands that only ever open a page or panel in the client.</summary>
    private static readonly HashSet<string> Benign = new(StringComparer.OrdinalIgnoreCase)
    {
        "store", "library", "friends", "settings", "nav", "appnews", "advertise",
        "support", "news", "music", "screenshots", "gamehub", "broadcast", "checksysreqs",
        "guides", "publisher", "search", "takesurvey", "updatenews", "backup",
    };

    /// <summary>Disruptive or attack-adjacent, but not a code-execution path on their own.</summary>
    private static readonly Dictionary<string, string> Risky = new(StringComparer.OrdinalIgnoreCase)
    {
        ["connect"] = "connects you straight to a game server chosen by whoever sent it",
        ["joinlobby"] = "joins a lobby chosen by whoever sent it",
        ["install"] = "starts installing a game",
        ["subscriptioninstall"] = "adds a game to your account and installs it",
        ["uninstall"] = "uninstalls a game",
        ["flushconfig"] = "wipes your Steam configuration",
        ["resetgamestats"] = "resets your stats and achievements for a game",
        ["forceinputappid"] = "redirects controller input to another app",
        ["validate"] = "forces a file validation",
        ["defrag"] = "forces a defragment",
    };

    public static SteamAnalysis Analyse(string url)
    {
        var rest = url[Scheme.Length..];
        var slash = rest.IndexOf('/');
        var command = (slash < 0 ? rest : rest[..slash]).Trim().ToLowerInvariant();
        var args = slash < 0 ? "" : rest[(slash + 1)..];
        var headline = "steam://" + (command.Length > 0 ? command : "?");

        // ---- launch-option injection: the classic remote-code-execution vector -------------
        // steam://run/<appid>//<launch options> hands arbitrary arguments to the game binary.
        if (command is "run" or "rungameid" or "launch")
        {
            var injected = ExtractLaunchOptions(args);
            if (injected.Length > 0)
            {
                return new SteamAnalysis(Verdict.Suspect,
                    $"Launches a game with attacker-supplied command-line options: \"{Clip(injected, 60)}\". "
                    + "This is the classic Steam link exploit — don't run it.",
                    headline);
            }

            return new SteamAnalysis(Verdict.Caution,
                "Launches a game. No injected launch options, but only run it if you expected it.",
                headline);
        }

        // ---- Steam's built-in browser -----------------------------------------------------
        if (command is "openurl" or "openurl_external")
        {
            var target = args.Trim();
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Hand the wrapped address to the real scanners.
                return new SteamAnalysis(Verdict.Caution,
                    "Opens a web page inside Steam's browser.", headline, target);
            }

            return new SteamAnalysis(Verdict.Suspect,
                $"Opens a non-web target inside Steam's browser: \"{Clip(target, 60)}\".", headline);
        }

        // ---- console access is a staple of Steam scams ------------------------------------
        if (command == "open" && args.Trim().Equals("console", StringComparison.OrdinalIgnoreCase))
        {
            return new SteamAnalysis(Verdict.Suspect,
                "Opens the hidden Steam console. Scammers use this to get you to paste commands. "
                + "There is no legitimate reason to send this to someone.",
                headline);
        }

        if (Risky.TryGetValue(command, out var what))
        {
            return new SteamAnalysis(Verdict.Caution,
                $"This {what}. Safe only if you're expecting it from someone you trust.", headline);
        }

        if (Benign.Contains(command))
        {
            return new SteamAnalysis(Verdict.Verified,
                $"Opens the {command} page in Steam. This command can't run anything or change settings.",
                headline);
        }

        return new SteamAnalysis(Verdict.Caution,
            $"Unrecognised Steam command \"{command}\". Nothing known against it, but it isn't a "
            + "command this tool can vouch for.",
            headline);
    }

    /// <summary>
    /// Pulls the launch-option payload out of run/<![CDATA[<appid>]]>//<![CDATA[<options>]]>.
    /// Returns empty when the link is just an app id.
    /// </summary>
    private static string ExtractLaunchOptions(string args)
    {
        var decoded = Uri.UnescapeDataString(args);

        // Everything after the "//" separator is passed to the game.
        var separator = decoded.IndexOf("//", StringComparison.Ordinal);
        if (separator >= 0)
        {
            var tail = decoded[(separator + 2)..].Trim();
            if (tail.Length > 0) return tail;
        }

        // Some variants append options directly, without the separator.
        var appId = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var extra = decoded[appId.Length..].Trim(' ', '/');

        return extra.Length > 0 && extra.IndexOfAny([' ', '-', '+', '"']) >= 0 ? extra : "";
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
