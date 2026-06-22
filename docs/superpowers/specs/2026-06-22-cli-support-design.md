# CLI support for statuscake-mcp — design

**Date:** 2026-06-22
**Status:** approved (pending spec review)

## Goal

Make `statuscake-mcp` usable as a normal command-line tool (the **default**
behaviour), while still serving as an MCP server for Claude Desktop / chat clients.
A human running `statuscake-mcp down` should get the same terse output the MCP
`check_sites_down` tool returns today.

## Decisions

- **Arg parsing:** hand-rolled functional parser in F#, no new dependencies (fits the
  project's "minimal" ethos; only ~8 commands with simple flags).
- **MCP invocation:** an explicit `mcp` subcommand. `statuscake-mcp mcp` runs stdio;
  `statuscake-mcp mcp --http` runs Streamable HTTP. Everything else is CLI.
  This is a **breaking change** to Claude Desktop config (must add `args: ["mcp"]`).
- **Mutations (pause/resume):** act immediately, no confirmation prompt — consistent
  with the MCP tools.

## Architecture

Today all real logic lives in `StatusCakeClient` (HTTP) and `Format` (pure
formatters); `Tools.fs` adds pagination helpers (`renderList`/`renderPage`), the
down-count logic, and MCP attributes. To avoid duplicating the pagination and
down-count logic in the CLI, that middle layer is extracted into a shared module.

```
src/StatusCakeMcp/
  Models.fs            # unchanged
  Format.fs            # unchanged
  StatusCakeClient.fs  # unchanged
  Commands.fs          # NEW — shared command core: 7 operations, client -> ... -> Task<string>
  Tools.fs             # thin MCP wrapper: each tool guards + calls Commands.X
  Cli.fs               # NEW — arg parsing, help, dispatch to Commands.X, exit codes
  Program.fs           # routes: first arg "mcp" -> MCP server, else -> Cli.run
```

Compile order in `.fsproj`: `Models → Format → StatusCakeClient → Commands → Tools → Cli → Program`.

### Commands.fs (shared core)

Holds the rendering helpers currently in `Tools.ToolHelpers` (`renderList`,
`renderPage`) and one function per operation. These functions do the work and **may
throw**; they do not catch (each front-end handles errors its own way):

```
listSites      : StatusCakeClient -> filter:string -> status:string -> page:int -> limit:int -> Task<string>
checkSitesDown : StatusCakeClient -> filter:string -> page:int -> limit:int -> Task<string>
getSite        : StatusCakeClient -> id:string -> Task<string>
siteHistory    : StatusCakeClient -> id:string -> limit:int -> Task<string>
checkSslExpiring : StatusCakeClient -> days:int -> page:int -> limit:int -> Task<string>
pauseSite      : StatusCakeClient -> id:string -> Task<string>
resumeSite     : StatusCakeClient -> id:string -> Task<string>
```

Bodies are moved verbatim from the current `Tools.fs` members (minus the `guard`
wrapper). `DateTimeOffset.UtcNow` for SSL stays inside `checkSslExpiring`.

### Tools.fs (MCP front-end)

Each `[<McpServerTool>]` member keeps its `Description` attributes and signature, and
its body becomes `ToolHelpers.guard label (fun () -> Commands.X client …)`. `guard`
(returns `Format.describeError` text — MCP wants text, not exit codes) stays here.

### Cli.fs (CLI front-end)

- **Token / client:** build an `IConfiguration` via `ConfigurationBuilder`
  (`AddJsonFile "appsettings.json" optional`, `AddEnvironmentVariables`), read
  `StatusCake:ApiToken`, and construct an `HttpClient` with the existing
  `configureClient` helper (moved to a shared spot so both Program and Cli use it).
  Same `warnIfNoToken` behaviour.
- **Parser:** a discriminated union of intents, e.g.
  `type Command = List of … | Down of … | Get of id | History of id*limit | Ssl of … | Pause of id | Resume of id | Help | Unknown of string`.
  A pure `parse : string[] -> Result<Command, string>` function maps argv to an intent
  (this is the unit-tested surface). Flags are `--name value`; `<id>` is positional.
  Defaults match the MCP tools: `page 1`, `limit 50`, ssl `days 30`, history `limit 10`,
  blank `filter`/`status`.
- **Dispatch:** map the parsed `Command` to the matching `Commands.X` call inside a
  `try/with`. Success → print result to **stdout**, exit `0`. Exception →
  `eprintfn "%s" (Format.describeError label ex)`, exit `1`. Usage error (bad/unknown
  command, missing id) → print message + help to **stderr**, exit `2`.
  `--help`/`-h`/no args → help to stdout, exit `0`.

### Program.fs (router)

```
main args =
  match args with
  | first :: rest when first = "mcp" -> run MCP (stdio, or http if rest has --http / env)  -> 0
  | _ -> Cli.run args   (returns exit code)
```

The existing `STATUSCAKE__TRANSPORT=http` env opt-in is honoured within the `mcp`
branch. `runHttp`/`runStdio` are unchanged apart from receiving the args after `mcp`.

## Command surface

| Command | Maps to | Args (defaults) |
| --- | --- | --- |
| `list` | listSites | `--filter ""` `--status ""` `--page 1` `--limit 50` |
| `down` | checkSitesDown | `--filter ""` `--page 1` `--limit 50` |
| `get <id>` | getSite | — |
| `history <id>` | siteHistory | `--limit 10` |
| `ssl` | checkSslExpiring | `--days 30` `--page 1` `--limit 50` |
| `pause <id>` | pauseSite | — |
| `resume <id>` | resumeSite | — |
| `mcp [--http]` | MCP server | — |
| `--help` / `-h` / (none) | help | — |

`--status` accepts `up`/`down`/`paused` (same semantics as the MCP tool).

## Error handling & exit codes

| Outcome | Channel | Exit |
| --- | --- | --- |
| Success | stdout | 0 |
| API/runtime error (auth, 404, timeout, network) | stderr (`describeError`) | 1 |
| Usage error (unknown command, missing `<id>`, bad flag) | stderr + help | 2 |
| Help requested / no args | stdout (help) | 0 |

## Testing

Add `CliTests.fs` to the existing xUnit project, covering the pure `parse` function:

- each command with no flags → correct intent with defaults
- flags parsed and override defaults (`list --status down --limit 10 --page 2`)
- positional id captured (`get 123`, `pause 456`)
- missing id → usage error
- unknown command → `Unknown`
- `--help` / `-h` / `[||]` → `Help`

The `Commands` functions are the same code paths exercised today through `Format`
tests; no new live-API tests.

## Docs

README updates:
- Reframe so **CLI is the primary usage**: a "Usage" section with the command table
  and examples (`statuscake-mcp down`, `statuscake-mcp get 123`).
- Change the Claude Desktop config snippet to `"args": ["mcp"]` and call out the
  breaking change.
- Keep the Transports section, noting HTTP is now `statuscake-mcp mcp --http`.
- Update the project-layout block to list `Commands.fs` and `Cli.fs`.

## Out of scope (YAGNI)

- `--json` output (text only for now)
- confirmation prompts / `--yes`
- shell completion
- name→id resolution for mutations (still id-only)
