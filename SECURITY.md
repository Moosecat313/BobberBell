# Security Policy

## Supported versions
| Version | Supported |
|---|---|
| 1.0.6 | Yes |
| Older 1.0.x (no longer published) | No |
| Pre-release (WeatherBug) | No |

## Reporting a vulnerability
Please report security issues **privately**. Do not open a public issue.

- Use GitHub's **private vulnerability reporting** (Security tab → *Report a vulnerability*) where it is enabled for this repository, or
- contact the maintainer, Chris Monoxide, through GitHub ([@Moosecat313](https://github.com/Moosecat313)).

Include the version, Windows build, steps to reproduce and the impact you expect. Never include license keys or the contents
of `license.dat` in a report.

What to expect: an acknowledgement within 7 days, an assessment within 14 days, and a fix or mitigation plan for confirmed
issues. Credit is given in the changelog unless you ask otherwise.

## Scope
In scope: the BobberBell app and its build/release pipeline (this repository, CI workflows, installer).
Out of scope: game Terms-of-Service questions, issues that need an already-compromised Windows account or administrator
access, and bypasses of the client-side license or anti-tamper checks (these are deterrents by design; see the threat model).

## Threat model summary
BobberBell is an offline, single-user desktop app. It has no server, makes no network calls, and runs with the user's normal
rights (the installer is per-user and needs no admin).

| Asset | Main threats | Key mitigations |
|---|---|---|
| User's PC and account | Malicious `settings.json` or sound file; DLL planting; NTLM leak via UNC path | Size-capped strict JSON parsing and value allow-lists; sound-file type, size and path checks (no UNC/device paths/ADS); System32-only P/Invoke |
| Keyboard input stream | Sending keys to the wrong window; runaway automation | Send inputs off at every launch; inputs only while the captured game window is active; allow-listed keys; auto-stop (default 0.25 h, max 168 h); global F8/Esc stop |
| Privacy (audio, user name) | Recording or leaking audio; user name in logs | Audio processed in memory only, nothing stored or sent; profile paths redacted in every log line |
| License codes | Recovering codes from the binary; copying a license to another PC; timing attacks | Only salted PBKDF2-SHA256 hashes shipped; HMAC binding to MachineGuid; constant-time comparisons |
| Trial | Resetting or extending the 5-minute trial | Signed, PC-bound record in a file and a registry mirror; max of both; monotonic clock; fails closed. Client-side by design (see threat model 5a) |
| Build and supply chain | Vulnerable or swapped dependencies; leaked secrets; tampered Actions | Lock files + locked restore; vulnerability gate, NuGet audit, Dependabot; SHA-pinned Actions; least-privilege tokens; gitleaks; CodeQL |

The full STRIDE analysis, trust boundaries and known limitations are in [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md).
