# Squint

Squint sits in the Windows tray. Copy a link and a card slides into the bottom-right corner
reading **PROCESSING**, then flips to a verdict once the scanners answer.

The name is what you do at a suspicious URL, and it is also one of the checks:
`steamcomrnunity.com` is not `steamcommunity.com`.

### ⬇ [Download Squint-Setup.exe](https://github.com/milkmadedev/squint/releases/latest/download/Squint-Setup.exe)

One file, about 61 MB. That link gives you the installer only, not the source, so there is
nothing to clone or build. Run it: no admin password, no prerequisites, nothing to unzip.

Windows shows *"Windows protected your PC"*, so click **More info → Run anyway**. That warning is
expected, and the next section explains why and how to check the file yourself.

All [releases](https://github.com/milkmadedev/squint/releases) · licensed
[GPL-3.0-or-later](LICENSE)

## Why Windows warns, and how to verify the download

Squint is not code-signed. A certificate costs a few hundred dollars a year, the kind that
actually clears SmartScreen straight away (EV) costs more, and since 2023 they have to live on a
hardware token. I am one person writing a free tool for myself and my girlfriend, so I am not
paying that. SmartScreen warns about anything it has not seen before regardless, so the warning
tells you nothing either way.

Instead of asking you to trust the file, here is how to check it.

**1. Get the hash of what you downloaded.** In PowerShell, in the folder holding the installer:

```powershell
Get-FileHash .\Squint-Setup.exe
```

**2. Compare it to
[SHA256SUMS.txt](https://github.com/milkmadedev/squint/releases/latest/download/SHA256SUMS.txt).**
That file is written by GitHub Actions during the release, not by me. If the two match, your copy
is exactly what the workflow published.

### The build is reproducible

The checksum only proves your download is intact. What makes it mean something is that anyone can
regenerate the same file from source:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.0.1
Get-FileHash .\dist\Squint-Setup.exe
```

Build the tagged commit yourself and you get **byte-for-byte the same installer**, so the same
hash. Nothing time-based or machine-specific goes into it: source-file timestamps are excluded
from the package, the .NET build is deterministic, and paths are fixed.

Every release enforces this. The workflow builds twice and refuses to publish unless both builds
are identical, so a release that cannot be reproduced never ships. You can read
[`release.yml`](.github/workflows/release.yml) and see the run that produced your file under
[Actions](https://github.com/milkmadedev/squint/actions).

Said plainly, because it matters: reproducible means *same toolchain, same bytes*. A different
.NET SDK or Inno Setup version legitimately produces a different file, so both are pinned:
**.NET SDK 10.0.201** (in `global.json`) and **Inno Setup 6.7.1**. Every release also ships a
`BUILDINFO.txt` naming the exact versions and commit it was built from. Match those two and you
get the published hash; use anything else and you will not, which is expected rather than a
warning sign.

**Or just read it.** The source is all here under GPLv3. If you would rather scan the binary,
upload it to [VirusTotal](https://www.virustotal.com/) — which is, after all, one of the three
services Squint uses.

## Building it yourself

```bash
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
```

The script produces `dist\Squint-Setup.exe`, one file of about 61 MB, and that file is everything
you send.

You need .NET SDK 10.0.201 (pinned in `global.json`) and Inno Setup 6.7.1
(`choco install innosetup --version=6.7.1`) to reproduce a release exactly; any recent pair builds
a working installer, it just will not match the published hash.
The recipient needs nothing: the build publishes self-contained and single-file, so .NET rides
inside `Squint.exe` and there is no runtime to install.

### Installing it

Run the .exe. No admin password, no prerequisites, no unzipping.

The wizard runs three screens: **how it works** (the three verdicts, with icons), **your API keys**
(three fields, each with a "Get key" button that opens the right signup page), then it installs and
starts. Setup writes any keys you type into `settings.json`, so nothing is left to configure.

Windows shows *"Windows protected your PC"* on first run, so click **More info → Run anyway**.
See [why Windows warns](#why-windows-warns-and-how-to-verify-the-download) if you'd rather check
the file first.

**Uninstall:** Settings → Apps → Installed apps → Squint. It asks whether to keep your API keys,
and defaults to keeping them.

## Two switches, and they mean different things

- **Is the app running?** The tray icon answers that. Icon present means running. Squint starts
  with Windows and promotes itself out of the `^` overflow onto the taskbar. Explorer caches the
  overflow list, so the icon lands from your next sign-in.
- **Is it checking links?** That starts **off** at every launch. Left-click the tray icon to turn
  it on; the icon greys out when off. Squint sends nothing anywhere until you switch it on.

Right-click the icon for the menu: check clipboard now, preview toast styles, API keys, settings
folder, exit.

> Windows 11's Quick Settings panel (Win+A) is closed to third-party apps, with no API for adding
> a tile there. The tray icon is the closest thing Windows allows.

## Verdicts

| | |
|---|---|
| **SUSPECT** | Any source flags it (Google, VirusTotal with 2+ engines, URLhaus), or the domain impersonates a known site. |
| **VERIFIED** | The destination is on the known-good list *and* nothing flagged it. |
| **CAUTION** | Everything else, which is where most links land. |

All three sources are blocklists. "Not listed" means nobody has reported the URL yet, and new
malware goes unlisted for hours or days, so a clean scan alone never earns green. Green needs a
*positive* signal: a destination on a recognised, operator-controlled site.

The allowlist therefore ranks operators, not popularity. `bit.ly`, `blogspot.com`,
`githubusercontent.com` and `cdn.discordapp.com` all sit near the top of the web and all serve
malware, because anyone can put a file on them. A domain earns green when its operator controls
what it serves.

The same rule applies inside a trusted domain. `google.com` is trusted and `drive.google.com` is
not, since a Drive link carries whatever the uploader put there. Docs, Sites, Discord's CDN,
`raw.githubusercontent.com`, S3 buckets and `*.github.io` work the same way.

Add your own under **Your own trusted domains** in settings (subdomains included). Don't add
anything that hosts other people's uploads.

### Impersonation

A fresh typosquat sits on no blocklist yet, which is what makes it work. Squint also compares each
domain against the allowlist for visual impersonation and flags a match **SUSPECT**:

- `steamcomrnunity.com` → impersonating `steamcommunity.com` (`rn` reads as `m`)
- `paypa1.com` → `paypal.com`, `arnazon.com` → `amazon.com`, `googie.com` → `google.com`

Squint compares only names of five characters or more, so short domains don't collide by accident.

### steam:// links

Google, VirusTotal and URLhaus handle http(s) only, so a `steam://` link never reaches them. The
protocol itself resists faking, since `steam://` opens Steam and nothing else, but an attacker
controls the *command* it carries. Squint reads that command locally:

| | |
|---|---|
| **SUSPECT** | `run/<id>//<options>` (launch-option injection, the classic Steam exploit), `open/console`, `openurl/` pointing at a non-web target |
| **CAUTION** | `connect`, `joinlobby`, `install`, `uninstall`, `flushconfig`, `resetgamestats`, a plain `run`, or any unrecognised command |
| **VERIFIED** | `store`, `library`, `friends`, `settings` and other navigation-only commands |

For `steam://openurl/https://…`, Squint unwraps the web address and runs it through all three
scanners. The result caps at CAUTION and never goes green, because Steam's built-in browser
sandboxes less than a real one.

## API keys

All three are free, and the installer asks for them up front. To add them later, open Start Menu →
**Squint Settings** or right-click the tray icon. **Test keys** confirms they work. A correct Safe
Browsing key reports SUSPECT on Google's test URL, and that report is the pass condition rather
than a failure.

| Source | Adds | Sign-up |
|---|---|---|
| **Google Safe Browsing** | Confirmed-bad URLs. 10,000 lookups/day. | console.cloud.google.com → enable Safe Browsing API → Credentials → API key |
| **VirusTotal** | ~90 engines instead of one list. Free tier is personal, non-commercial: 4/min, 500/day. | virustotal.com → sign in → profile menu → API key |
| **URLhaus** (abuse.ch) | Live feed of URLs actively serving malware; often lists a payload within minutes. | auth.abuse.ch → free Auth-Key |

Without keys, Squint still catches lookalike domains and unsafe `steam://` links, and every other
link reports CAUTION. Keys live in `%APPDATA%\Squint\settings.json` in plain text.

When a source is unavailable, the toast names it and gives the reason: `(rate limited, retry in a
minute)`, `(no key)`, `(never scanned it)`, `(error)`. A missing source never makes a link look
clean, and it never blocks or retries.

## Behaviour notes

- **Redirects.** Squint follows the chain before scanning, so a shortener is judged by where it
  lands, and the toast shows `bit.ly → wherever.com`. It requests headers only and downloads no
  page content. Resolving does contact the server, which tells whoever runs it that the link
  arrived; turn resolution off in settings if you'd rather not.
- **What counts as a link.** Any `http(s)://` or `steam://` anywhere in the copied text, so
  highlighting a sentence works. A bare `example.com/page` counts when it is the whole clipboard.
  Squint ignores filenames (`report.docx`, `setup.exe`).
- **One VirusTotal detection is not red.** A single engine out of ~90 usually means a false
  positive. On an unrecognised site that gives CAUTION with the score shown; on a known-good site
  the toast reports the score and treats it as noise. Two or more detections give SUSPECT.
- **Caching.** Squint caches results for 10 minutes and never caches failures.
- The toast never takes focus. Hover to keep it up, click to dismiss.

## Layout

```
installer/Squint.iss    the Inno Setup wizard, including the tutorial and key-entry pages
tools/build-installer.ps1      publishes single-file, renders wizard art, compiles the installer
tools/make-assets.ps1          regenerates the PNGs and .ico - edit and re-run to restyle

src/Squint/
  App.xaml(.cs)          tray icon, wiring, result cache, taskbar pinning
  ClipboardWatcher.cs    WM_CLIPBOARDUPDATE listener + URL extraction
  RedirectResolver.cs    follows the chain, headers only
  SafeBrowsing.cs        Google - checks every hop in one request
  VirusTotal.cs          ~90 engines, checks the destination
  UrlHaus.cs             abuse.ch live malware feed
  SteamLinks.cs          reads steam:// commands the web scanners can't see
  TrustedSites.cs        the allowlist behind green, plus impersonation detection
  Scanner.cs             combines everything into one verdict
  ToastWindow.xaml(.cs)  the corner toast (fixed size for every state)
  ApiKeyWindow.xaml(.cs) keys, trusted domains, redirect toggle
  Settings.cs            JSON in %APPDATA%
```

Swap the look by dropping your own `verified.png` / `caution.png` / `suspect.png` into `Assets/`
(256x256, transparent) and rebuilding.

`tools/add-license-headers.ps1` re-stamps the GPL notice on every source file. It is idempotent,
so run it after adding new files.

## License

Copyright (C) 2026 milkmade.

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version. Full text in [LICENSE](LICENSE).

It is distributed in the hope that it will be useful, but **without any warranty**, without even
the implied warranty of merchantability or fitness for a particular purpose.

One practical consequence: the GPL is copyleft, so anyone you hand the built installer to is
entitled to the source. Point them at this repository and you have satisfied that.

### Third-party services

Squint talks to Google Safe Browsing, VirusTotal and URLhaus with **your own** API keys. Those
services set their own terms; VirusTotal's free tier covers personal, non-commercial use only.
Nothing here grants you rights to them, and the repository includes none of their code.

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php), which places no
licensing conditions on the installers it produces.
