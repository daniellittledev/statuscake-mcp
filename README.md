# statuscake-mcp

A minimal, token-efficient [Model Context Protocol](https://modelcontextprotocol.io)
server for [StatusCake](https://www.statuscake.com), written in F# on .NET and
served over Streamable HTTP.

It exposes two tools that return terse plain-text summaries (not raw JSON) so an
LLM client spends as few tokens as possible:

| Tool | Description |
| --- | --- |
| `check_sites_down` | Returns only the uptime checks that are currently **down** (or confirms all are up). |
| `list_sites` | Returns a compact one-line-per-site roster of every uptime check and its status. |

## Prerequisites

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
- A StatusCake API token ([My Account → API Keys](https://app.statuscake.com/User/Account.php))

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

## Build & run

```bash
dotnet build
STATUSCAKE__APITOKEN="your-token" dotnet run --project src/StatusCakeMcp
```

The MCP endpoint is then served over Streamable HTTP at the root path, e.g.
`http://localhost:5250/` (the dev port from `launchSettings.json`; override with
`ASPNETCORE_URLS`).

## Connecting an MCP client

Point any Streamable-HTTP-capable MCP client at the server URL. Example client
config:

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

## Verifying without a client

You can exercise the protocol with `curl`. `initialize` returns an
`Mcp-Session-Id` header that subsequent requests must echo back:

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
  Models.fs            # JSON record types for the /v1/uptime response
  StatusCakeClient.fs  # typed HttpClient wrapper (paginates the uptime endpoint)
  Tools.fs             # the two MCP tools
  Program.fs           # host, DI, and MapMcp wiring
```

## Scope

This is a deliberately minimal starting point covering uptime status only.
Creating/editing checks, SSL/PageSpeed/server tests, alerts and contact groups
are intentionally out of scope; the client + tool pattern is structured so they
can be added later.
