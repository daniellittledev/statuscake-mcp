# statuscake-mcp

A minimal, token-efficient tool for [StatusCake](https://www.statuscake.com) uptime
monitoring, written in F# on .NET. It works two ways from one binary:

- a **command-line tool** (the default) — `statuscake-mcp down`, `statuscake-mcp get 123`;
- a **[Model Context Protocol](https://modelcontextprotocol.io) server** — `statuscake-mcp mcp`
  — for Claude Desktop and other LLM clients (stdio, or Streamable HTTP).

Both front-ends share the same logic and return terse plain-text summaries (not raw JSON)
so an LLM client spends as few tokens as possible. List commands are **filtered and paged**
so they stay cheap even on accounts with thousands of checks.

| Command | MCP tool | Description |
| --- | --- | --- |
| `list` | `list_sites` | Roster of uptime checks (glyph, name, status). Optional `--filter` (name/URL substring), `--status` (`up`/`down`/`paused`), and `--page`/`--limit` (default 50, max 100). |
| `down` | `check_sites_down` | Only the checks currently **down**, with a count of the total (or confirms all are up). Optional `--filter`. |
| `get <id>` | `get_site` | Detail for one check: status, uptime %, check rate, last tested. |
| `history <id>` | `site_history` | Recent up/down periods for one check, most recent first, with durations (`--limit`, default 10). |
| `ssl` | `check_ssl_expiring` | SSL certificates expiring within `--days` (default 30), soonest first, including already expired. Paged. |
| `pause <id>` | `pause_site` | Pause one check. **ID-only** (no name lookup) to avoid pausing the wrong check. |
| `resume <id>` | `resume_site` | Resume (unpause) one check. **ID-only**. |

Commands that act on one check take the `id` shown in `list` / `down`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) for `dotnet tool execute`
  (the tool itself targets .NET 8 and runs on any newer runtime)
- A StatusCake API token ([My Account → API Keys](https://app.statuscake.com/User/Account.php))

## Install

Nothing to install. Run it through the signed `dotnet` host with a pinned version; the
package is downloaded to the NuGet cache on first use:

```bash
dotnet tool execute StatusCakeMcp@0.3.0 --yes -- down
```

The CLI (and the Claude Code skill) needs **0.3.0 or later**; earlier versions are MCP-only.
To upgrade, change the version you pass. Note that `dotnet tool execute` checks the NuGet
feed on every run, even when the package is cached, so nuget.org must be reachable.

> **Managed Windows:** don't install it as a global tool (`dotnet tool install -g`) or
> point anything at the `.exe` in `%USERPROFILE%\.dotnet\tools`. Corporate Defender attack
> surface reduction rules block those unsigned per-user launchers. Use `dotnet tool execute`
> as above, and use `dotnet` rather than `dnx`: `dnx` is a `.cmd` file, which Claude Code
> can't start directly on Windows.

Where launchers aren't blocked you can still install a global tool and get a shorter
command: `dotnet tool install -g StatusCakeMcp` gives you `statuscake-mcp`.

## CLI usage

Set the API token in your environment (see [Configuration](#configuration)), then run a
command. The examples use the global-tool name `statuscake-mcp`; with `dotnet tool execute`,
replace it with `dotnet tool execute StatusCakeMcp@0.3.0 --yes --`.

```bash
statuscake-mcp down                       # anything actively down?
statuscake-mcp list --status down         # the roster, down checks only
statuscake-mcp list --filter mci --limit 20
statuscake-mcp get 218518                 # detail for one check
statuscake-mcp history 218518 --limit 5   # recent up/down periods
statuscake-mcp ssl --days 14              # certs expiring within 14 days
statuscake-mcp pause 218518               # pause / resume one check
statuscake-mcp resume 218518
statuscake-mcp --help                     # full command list
```

Exit codes: `0` success · `1` API/runtime error (auth, not found, timeout) · `2` usage
error (unknown command or bad arguments). Errors print to stderr; output prints to stdout.

## Use from Claude Code (skill, no MCP server)

An MCP server stays resident for the whole Claude Code session. If you'd rather not
keep a process around, install the **`statuscake` plugin** from this repo instead. It
adds a skill that teaches Claude to run the CLI via Bash, so each lookup is a
short-lived process that exits once it prints its answer.

```text
/plugin install statuscake --marketplace daniellittledev/statuscake-mcp
```

On older Claude Code versions, add the marketplace first:

```text
/plugin marketplace add daniellittledev/statuscake-mcp
/plugin install statuscake@statuscake
```

The skill runs `dotnet tool execute StatusCakeMcp@<version> --yes -- <command>`, pinned to
the plugin's version, so it needs the .NET 10 SDK but no tool install, and updating the
plugin updates the tool. It also needs the token in the environment Claude Code runs
commands in. The simplest place is the `env` block of `~/.claude/settings.json`:

```json
{
  "env": { "STATUSCAKE__APITOKEN": "your-statuscake-api-token" }
}
```

Then just ask — "is anything down?", "uptime for the billing site", "any certs expiring
this month?". The read-only commands are pre-approved by the skill; `pause`/`resume`
still prompt, and Claude confirms the check with you first. Remove any `statuscake`
entry from your MCP config so you're not running both.

## Use as an MCP server

The same package runs as an MCP server under the **`mcp`** subcommand. For Claude Code,
launch it through the signed `dotnet` host with a pinned version (see [Install](#install)
for why), passing the token via its environment:

```bash
claude mcp add statuscake -s user -e STATUSCAKE__APITOKEN=your-statuscake-api-token -- dotnet tool execute StatusCakeMcp@0.3.0 --yes -- mcp
```

Keep the `--` before `mcp`: it separates the tool's arguments from `dotnet tool execute`'s
own. To upgrade, change the version in the config rather than running `dotnet tool update`.

For other clients, the equivalent JSON config is:

```json
{
  "mcpServers": {
    "statuscake": {
      "command": "dotnet",
      "args": ["tool", "execute", "StatusCakeMcp@0.3.0", "--yes", "--", "mcp"],
      "env": { "STATUSCAKE__APITOKEN": "your-statuscake-api-token" }
    }
  }
}
```

The `mcp` subcommand speaks **stdio** so the client launches it on demand. See
[Transports](#transports) below to run it as an HTTP server instead.

## Claude Desktop extension (.mcpb)

You can install the server into Claude Desktop as a one-click extension instead of
installing the .NET tool. The bundle ships a self-contained binary, so the host needs
neither .NET nor anything else on `PATH`.

Each tagged release attaches a prebuilt `statuscake-mcp.mcpb` you can download directly
from the [Releases](https://github.com/daniellittledev/statuscake-mcp/releases) page, so
most users can skip the build below.

To build it yourself (requires the .NET SDK; uses the official `mcpb` packer via `npx`
when available, otherwise zips with PowerShell):

```pwsh
pwsh mcpb/build.ps1
```

This produces `dist/statuscake-mcp.mcpb`. The packaging files live under [`mcpb/`](mcpb/)
([`manifest.json`](mcpb/manifest.json), [`build.ps1`](mcpb/build.ps1)). The current
binary bundle targets **Windows x64**.

To install it: open Claude Desktop → **Settings → Extensions → Advanced settings →
Install Extension**, choose `dist/statuscake-mcp.mcpb`, and when prompted paste your
**StatusCake API Token**. The token is stored securely by Claude Desktop and passed to
the server as `STATUSCAKE__APITOKEN`; it is never written into the manifest.

## Configuration

The server reads the API token from configuration key `StatusCake:ApiToken`.
Set it via an environment variable (double-underscore separator):

```bash
export STATUSCAKE__APITOKEN="your-statuscake-api-token"
```

The environment variable is the supported way to configure the installed tool. As of
0.3.0 the CLI no longer reads an `appsettings.json` from your current directory, only the
one shipped next to the binary, so a token kept in a local `appsettings.json` must move
to the environment variable (or user-secrets, below).

In local development you can instead use [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets).
The CLI always reads them. The MCP host only reads them in the Development environment:
`dotnet run` sets `ASPNETCORE_ENVIRONMENT=Development`, which covers `mcp --http`; for
stdio also set `DOTNET_ENVIRONMENT=Development`. Environment variables take precedence.

```bash
dotnet user-secrets set "StatusCake:ApiToken" "your-statuscake-api-token" --project src/StatusCakeMcp
```

Never commit the token. `.env` is already git-ignored.

## Transports

The `mcp` subcommand selects its transport at startup:

| Transport | When | How to select |
| --- | --- | --- |
| **stdio** | local use; the MCP client launches the process | `statuscake-mcp mcp` |
| **Streamable HTTP** | remote, shared, or web-based clients | `statuscake-mcp mcp --http`, or `STATUSCAKE__TRANSPORT=http` |

### stdio

Nothing to start manually — the client spawns `statuscake-mcp mcp` and talks to it over
stdin/stdout. Logs go to stderr so they never corrupt the protocol stream. Use the
client config shown under [Use as an MCP server](#use-as-an-mcp-server).

### HTTP

```bash
STATUSCAKE__APITOKEN="your-token" ASPNETCORE_URLS="http://localhost:5250" statuscake-mcp mcp --http
```

The installed tool does not read `launchSettings.json`, so without `ASPNETCORE_URLS`
it binds to the ASP.NET Core default (`http://localhost:5000`). Set `ASPNETCORE_URLS`
to pin the port your client expects. Then point a Streamable-HTTP-capable client at it:

```json
{
  "mcpServers": {
    "statuscake": {
      "type": "http",
      "url": "http://localhost:5250/"
    }
  }
}
```

## Build & run

```bash
dotnet build
# CLI (default)
STATUSCAKE__APITOKEN="your-token" dotnet run --project src/StatusCakeMcp -- down
# MCP over stdio
STATUSCAKE__APITOKEN="your-token" dotnet run --project src/StatusCakeMcp -- mcp
# MCP over HTTP
STATUSCAKE__APITOKEN="your-token" dotnet run --project src/StatusCakeMcp -- mcp --http
```

## Verifying the HTTP transport with curl

When running with `mcp --http` you can exercise the protocol with `curl`. `initialize`
returns an `Mcp-Session-Id` header that subsequent requests must echo back:

```bash
# 1. initialize (note the Mcp-Session-Id response header)
curl -s -D - http://localhost:5250/ \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"curl","version":"0"}}}'

# 2. list tools (substitute the session id)
curl -s http://localhost:5250/ \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -H 'Mcp-Session-Id: <session-id>' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

## Project layout

```
src/StatusCakeMcp/
  Models.fs            # JSON record types for the uptime/ssl/periods responses
  Format.fs            # pure functions: paging, filtering, SSL expiry, terse formatters
  StatusCakeClient.fs  # typed HttpClient wrapper (paginates uptime/ssl; detail/periods/pause)
  Commands.fs          # shared command core: one operation per command, client + Format
  Tools.fs             # the MCP tools (thin: guard + Commands)
  Cli.fs               # the CLI: arg parsing, dispatch to Commands, exit codes
  Program.fs           # routes mcp vs cli; MCP host, DI, and transport selection
tests/StatusCakeMcp.Tests/
  FormatTests.fs       # xUnit tests for the pure functions in Format.fs
  CliTests.fs          # xUnit tests for the CLI argument parser
.claude-plugin/
  marketplace.json     # makes this repo a Claude Code plugin marketplace
plugin/
  .claude-plugin/plugin.json
  skills/statuscake/SKILL.md  # the Claude Code skill that drives the CLI
scripts/
  check-versions.sh    # CI guard: every pinned/declared version matches the .fsproj
```

The project turns off the Web SDK's server defaults (Server GC, concurrent GC, dynamic
PGO, ICU) because it only ever runs as a short CLI call or a single-user MCP process.
That keeps both the per-call and the idle-server footprint small.

## Releasing

Publishing is automated with GitHub Actions ([`.github/workflows/publish.yml`](.github/workflows/publish.yml))
using NuGet [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
(OIDC) — there is **no long-lived NuGet API key** stored in the repo.

One-time setup:

1. On nuget.org, create a Trusted Publishing policy bound to repo
   `daniellittledev/statuscake-mcp`, workflow file `publish.yml`
   (and environment `nuget` if you use one).
2. In the repo, add an Actions **variable** `NUGET_USER` = your nuget.org username
   (Settings → Secrets and variables → Actions → *Variables*).

To cut a release, push a version tag — the workflow derives the package version
from the tag, packs, and pushes:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The same tag also triggers [`Release`](.github/workflows/release.yml), which builds the
Claude Desktop extension (`statuscake-mcp.mcpb`) at that version and attaches it to a
GitHub Release of the same name. It needs no secrets beyond the automatic `GITHUB_TOKEN`.

When you bump the version, update it everywhere it's named: `<Version>` in the `.fsproj`,
`version` in [`plugin/.claude-plugin/plugin.json`](plugin/.claude-plugin/plugin.json) and
[`mcpb/manifest.json`](mcpb/manifest.json), and every pinned `StatusCakeMcp@<version>` in
the [skill](plugin/skills/statuscake/SKILL.md) and this README. The skill runs exactly that
pinned version, so it must exist on NuGet. [`scripts/check-versions.sh`](scripts/check-versions.sh)
checks they all agree; CI runs it on every push, and `Publish` runs it against the tag
before packing, so a mismatched tag never reaches NuGet.

`CI` ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds every push and
PR to `main`.

## Scope

This is a deliberately minimal starting point covering uptime status only.
Creating/editing checks, SSL/PageSpeed/server tests, alerts and contact groups
are intentionally out of scope; the client + tool pattern is structured so they
can be added later.
