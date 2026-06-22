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

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
- A StatusCake API token ([My Account → API Keys](https://app.statuscake.com/User/Account.php))

## Install

Install it as a .NET global tool:

```bash
dotnet tool install -g StatusCakeMcp
```

## CLI usage

Running `statuscake-mcp` with a command is the default. Set the API token in your
environment (see [Configuration](#configuration)), then:

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

## Use as an MCP server

The same binary runs as an MCP server under the **`mcp`** subcommand — point the client
at the command with `args: ["mcp"]` and pass the token via its environment:

```json
{
  "mcpServers": {
    "statuscake": {
      "command": "statuscake-mcp",
      "args": ["mcp"],
      "env": { "STATUSCAKE__APITOKEN": "your-statuscake-api-token" }
    }
  }
}
```

`statuscake-mcp mcp` speaks **stdio** so the client launches it on demand. See
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

In local development you can instead use [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

```bash
dotnet user-secrets init --project src/StatusCakeMcp
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
```

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

`CI` ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds every push and
PR to `main`.

## Scope

This is a deliberately minimal starting point covering uptime status only.
Creating/editing checks, SSL/PageSpeed/server tests, alerts and contact groups
are intentionally out of scope; the client + tool pattern is structured so they
can be added later.
