// Squint - copied-link safety checker for Windows.
// Copyright (C) 2026 milkmade
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Text.Json;

namespace Squint;

public sealed class Settings
{
    /// <summary>Google Safe Browsing key. Named "ApiKey" so existing settings.json files still load.</summary>
    public string ApiKey { get; set; } = "";

    public string VirusTotalKey { get; set; } = "";

    /// <summary>abuse.ch Auth-Key, free from auth.abuse.ch.</summary>
    public string UrlHausKey { get; set; } = "";

    /// <summary>Follow redirects so shorteners are judged by where they actually land.</summary>
    public bool FollowRedirects { get; set; } = true;

    /// <summary>
    /// Extra domains you want treated as known-good, on top of the built-in list.
    /// Subdomains are covered, so "example.com" also trusts "shop.example.com".
    /// </summary>
    public List<string> TrustedDomains { get; set; } = [];

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Squint");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt or unreadable — start fresh rather than blocking startup */ }

        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
