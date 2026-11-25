---
name: statuscake
description: Check and manage StatusCake uptime monitoring with the `statuscake-mcp` CLI. Use whenever the user asks whether a site or service is up or down, what's down right now, the status, uptime percentage or outage history of a check, which SSL certificates are expiring, or wants to pause or resume a StatusCake check (e.g. during a deploy or maintenance window). Trigger on phrasings like "is anything down", "is example.com up", "check StatusCake", "uptime for X", "when did X last go down", "any certs expiring", "pause the monitor for X" — even when StatusCake isn't named.
allowed-tools: Bash(statuscake-mcp list:*), Bash(statuscake-mcp down:*), Bash(statuscake-mcp get:*), Bash(statuscake-mcp history:*), Bash(statuscake-mcp ssl:*), Bash(statuscake-mcp --help:*)
---

# StatusCake

Query StatusCake through the `statuscake-mcp` command-line tool via Bash. Each call is a
short-lived process that prints a terse plain-text summary and exits, so nothing stays
resident for the session. Don't start `statuscake-mcp mcp` — that's the MCP server mode,
which this skill exists to avoid.

## Commands

| Command | What it returns |
| --- | --- |
| `statuscake-mcp down [--filter S]` | Checks **actively** down, with the account total (or confirms all are up). |
| `statuscake-mcp list [--filter S] [--status up\|down\|paused] [--page N] [--limit N]` | Roster of checks: glyph, name, status, id. |
| `statuscake-mcp get <id>` | One check: status, uptime %, check rate, last tested. |
| `statuscake-mcp history <id> [--limit N]` | Recent up/down periods, newest first, with durations (default 10). |
| `statuscake-mcp ssl [--days N] [--page N] [--limit N]` | Certificates expiring within N days (default 30), soonest first, incl. expired. |
| `statuscake-mcp pause <id>` | Pause one check (stops testing and alerting). |
| `statuscake-mcp resume <id>` | Resume one paused check. |

- `--filter` is a case-insensitive substring of the check name or URL.
- Lists default to `--page 1 --limit 50` (max 100). When output ends with a next-page
  hint, fetch the next page only if the user needs it.
- `get`, `history`, `pause` and `resume` take a numeric **id**, never a name. Find the id
  with `list --filter <name>` (or from `down` output) first.
- If `down` mentions paused checks that last reported down, `list --status paused` shows them.

## Workflow

1. **"Is anything down?"** → `statuscake-mcp down`. That one call usually answers it.
2. **"Is X up?" / "How's X doing?"** → `statuscake-mcp list --filter X`, then
   `get <id>` for detail or `history <id>` for recent outages if asked.
   If the filter matches several checks, show them and ask which one is meant.
3. **"Any certs expiring?"** → `statuscake-mcp ssl` (pass `--days` if the user gave a window).
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
- `2` usage error (unknown command or bad flag). Fix the arguments using `statuscake-mcp --help`.

## Setup problems

- **Version 0.3.0 or later is required.** Older releases are MCP-only: they ignore the
  command and start an MCP server that waits on stdin, so the call hangs or prints JSON-RPC.
  If that happens, stop and tell the user to run `dotnet tool update -g StatusCakeMcp`.
- **`statuscake-mcp: command not found`** → the .NET tool isn't installed. Tell the user to run
  `dotnet tool install -g StatusCakeMcp` (needs the .NET 8+ SDK, and `~/.dotnet/tools` on `PATH`).
  Don't install or update it without asking.
- **"No API token configured" / 403** → `STATUSCAKE__APITOKEN` isn't set in the environment
  Claude Code runs commands in. Suggest adding it to the `env` block of
  `~/.claude/settings.json` (or exporting it in their shell profile). Never ask the user to
  paste the token into the chat, and never echo it.
