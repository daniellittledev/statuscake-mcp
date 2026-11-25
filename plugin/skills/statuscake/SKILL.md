---
name: statuscake
description: Check and manage StatusCake uptime monitoring with the StatusCakeMcp CLI. Use whenever the user asks whether a site or service is up or down, what's down right now, the status, uptime percentage or outage history of a check, which SSL certificates are expiring, or wants to pause or resume a StatusCake check (e.g. during a deploy or maintenance window). Trigger on phrasings like "is anything down", "is example.com up", "check StatusCake", "uptime for X", "when did X last go down", "any certs expiring", "pause the monitor for X" — even when StatusCake isn't named.
allowed-tools: Bash(dotnet tool execute StatusCakeMcp@0.3.0 --yes -- list:*), Bash(dotnet tool execute StatusCakeMcp@0.3.0 --yes -- down:*), Bash(dotnet tool execute StatusCakeMcp@0.3.0 --yes -- get:*), Bash(dotnet tool execute StatusCakeMcp@0.3.0 --yes -- history:*), Bash(dotnet tool execute StatusCakeMcp@0.3.0 --yes -- ssl:*), Bash(dotnet tool execute StatusCakeMcp@0.3.0 --yes -- --help:*)
---

# StatusCake

Query StatusCake by running the StatusCakeMcp CLI through Bash. Each call is a short-lived
process that prints a terse plain-text summary and exits, so nothing stays resident for
the session.

## How to run it

Run every command through the signed `dotnet` host with the pinned version, exactly:

```bash
dotnet tool execute StatusCakeMcp@0.3.0 --yes -- <command> [options]
```

For example `dotnet tool execute StatusCakeMcp@0.3.0 --yes -- down`.

- Keep the prefix byte-for-byte as shown (version, `--yes`, and the `--` before the
  command); the read-only commands are pre-approved only in that exact form.
- Don't call a `statuscake-mcp` executable or install a global tool: per-user tool
  launchers are blocked by Defender attack surface reduction rules on managed Windows.
- Don't use `dnx`, and don't run the `mcp` command — that's the MCP server mode, which
  this skill exists to avoid.

## Commands

| `<command>` | What it returns |
| --- | --- |
| `down [--filter S]` | Checks **actively** down, with the account total (or confirms all are up). |
| `list [--filter S] [--status up\|down\|paused] [--page N] [--limit N]` | Roster of checks: glyph, name, status, id. |
| `get <id>` | One check: status, uptime %, check rate, last tested. |
| `history <id> [--limit N]` | Recent up/down periods, newest first, with durations (default 10). |
| `ssl [--days N] [--page N] [--limit N]` | Certificates expiring within N days (default 30), soonest first, incl. expired. |
| `pause <id>` | Pause one check (stops testing and alerting). |
| `resume <id>` | Resume one paused check. |

- `--filter` is a case-insensitive substring of the check name or URL.
- Lists default to `--page 1 --limit 50` (max 100). When output ends with a next-page
  hint, fetch the next page only if the user needs it.
- `get`, `history`, `pause` and `resume` take a numeric **id**, never a name. Find the id
  with `list --filter <name>` (or from `down` output) first.
- If `down` mentions paused checks that last reported down, `list --status paused` shows them.

## Workflow

1. **"Is anything down?"** → `down`. That one call usually answers it.
2. **"Is X up?" / "How's X doing?"** → `list --filter X`, then `get <id>` for detail or
   `history <id>` for recent outages if asked. If the filter matches several checks,
   show them and ask which one is meant.
3. **"Any certs expiring?"** → `ssl` (pass `--days` if the user gave a window).
4. **Pause / resume** changes live monitoring and alerting. Resolve the id first, then
   confirm the exact check (name + id) with the user before running it, unless they
   already named that specific check and clearly asked for the change.

Relay the output as-is or summarised; it's already compact. Don't dump raw JSON or
re-query to "double check" — one call per question is the point.

## Exit codes and errors

Output goes to stdout; errors go to stderr.

- `0` success.
- `1` API/runtime error. The message says which: auth (token missing or invalid),
  not found (bad id), timeout, or connection failure. Report it plainly; don't retry in a loop.
  A `dotnet tool execute` failure (below) also exits non-zero.
- `2` usage error (unknown command or bad flag). Fix the arguments using `--help`.

## Setup problems

- **`dotnet tool execute` isn't a known command / "dotnet: command not found"** → it needs
  the **.NET 10 SDK**. Tell the user to install it; don't install it yourself.
- **"Version 0.3.0 of package statuscakemcp is not found in NuGet feeds"** → `dotnet` checks
  the NuGet feed on every call, even once the package is cached, so nuget.org (or the
  user's configured feed) must be reachable. Report it as a network/feed problem.
- **"No API token configured" / 403** → `STATUSCAKE__APITOKEN` isn't set in the environment
  Claude Code runs commands in. Suggest adding it to the `env` block of
  `~/.claude/settings.json` (or exporting it in their shell profile). Never ask the user to
  paste the token into the chat, and never echo it.
