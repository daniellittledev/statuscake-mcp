namespace StatusCakeMcp

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Threading.Tasks
open Microsoft.Extensions.Configuration

/// Command-line front-end: parses argv into an intent and dispatches to Commands.*.
module Cli =

    /// A parsed CLI intent. Defaults are baked in by `parse` so they match the MCP tools.
    type Command =
        | List of filter: string * status: string * page: int * limit: int
        | Down of filter: string * page: int * limit: int
        | Get of id: string
        | History of id: string * limit: int
        | Ssl of days: int * page: int * limit: int
        | Pause of id: string
        | Resume of id: string
        | Help

    // --- parsing -------------------------------------------------------------

    type private ResultBuilder() =
        member _.Bind(m, f) = Result.bind f m
        member _.Return(x) = Ok x
        member _.ReturnFrom(m) = m

    let private result = ResultBuilder()

    /// Turn "--name value --name2 value2" into a name→value map (names sans "--").
    /// Errors on a token that isn't a "--flag", or a flag with no following value.
    let rec private parseFlags (acc: (string * string) list) (tokens: string list) : Result<Map<string, string>, string> =
        match tokens with
        | [] -> Ok(Map.ofList (List.rev acc))
        | flag :: value :: rest when flag.StartsWith "--" -> parseFlags ((flag.Substring 2, value) :: acc) rest
        | flag :: [] when flag.StartsWith "--" -> Error(sprintf "Flag %s needs a value." flag)
        | tok :: _ -> Error(sprintf "Unexpected argument '%s'." tok)

    let private strFlag (flags: Map<string, string>) name def =
        Map.tryFind name flags |> Option.defaultValue def

    let private intFlag (flags: Map<string, string>) name def : Result<int, string> =
        match Map.tryFind name flags with
        | None -> Ok def
        | Some v ->
            match Int32.TryParse v with
            | true, n -> Ok n
            | _ -> Error(sprintf "--%s must be a number, got '%s'." name v)

    /// Reject any flag not in the allowed set for a command.
    let private checkAllowed (allowed: Set<string>) (flags: Map<string, string>) : Result<unit, string> =
        match flags |> Map.toList |> List.tryFind (fun (k, _) -> not (Set.contains k allowed)) with
        | Some(k, _) -> Error(sprintf "Unknown flag --%s." k)
        | None -> Ok()

    let private withFlags (allowed: string list) (rest: string list) (build: Map<string, string> -> Result<Command, string>) =
        result {
            let! flags = parseFlags [] rest
            let! () = checkAllowed (Set.ofList allowed) flags
            return! build flags
        }

    /// Parse argv (already stripped of the leading "mcp" route) into a Command,
    /// or an Error with a usage message.
    let parse (args: string[]) : Result<Command, string> =
        match List.ofArray args with
        | []
        | [ "--help" ]
        | [ "-h" ]
        | [ "help" ] -> Ok Help
        | cmd :: rest ->
            match cmd with
            | "list" ->
                withFlags [ "filter"; "status"; "page"; "limit" ] rest (fun f ->
                    result {
                        let! page = intFlag f "page" 1
                        let! limit = intFlag f "limit" 50
                        return List(strFlag f "filter" "", strFlag f "status" "", page, limit)
                    })
            | "down" ->
                withFlags [ "filter"; "page"; "limit" ] rest (fun f ->
                    result {
                        let! page = intFlag f "page" 1
                        let! limit = intFlag f "limit" 50
                        return Down(strFlag f "filter" "", page, limit)
                    })
            | "ssl" ->
                withFlags [ "days"; "page"; "limit" ] rest (fun f ->
                    result {
                        let! days = intFlag f "days" 30
                        let! page = intFlag f "page" 1
                        let! limit = intFlag f "limit" 50
                        return Ssl(days, page, limit)
                    })
            | "get" ->
                match rest with
                | [ id ] -> Ok(Get id)
                | [] -> Error "get needs a check id: statuscake-mcp get <id>"
                | _ -> Error "get takes a single id and no flags."
            | "pause" ->
                match rest with
                | [ id ] -> Ok(Pause id)
                | [] -> Error "pause needs a check id: statuscake-mcp pause <id>"
                | _ -> Error "pause takes a single id and no flags."
            | "resume" ->
                match rest with
                | [ id ] -> Ok(Resume id)
                | [] -> Error "resume needs a check id: statuscake-mcp resume <id>"
                | _ -> Error "resume takes a single id and no flags."
            | "history" ->
                match rest with
                | id :: flagTokens when not (id.StartsWith "--") ->
                    withFlags [ "limit" ] flagTokens (fun f ->
                        result {
                            let! limit = intFlag f "limit" 10
                            return History(id, limit)
                        })
                | _ -> Error "history needs a check id: statuscake-mcp history <id> [--limit N]"
            | other -> Error(sprintf "Unknown command '%s'. Run --help for usage." other)

    // --- client / config -----------------------------------------------------

    /// Configure the typed StatusCake HttpClient. Token comes from config key
    /// "StatusCake:ApiToken" (env var STATUSCAKE__APITOKEN). Shared with the MCP host.
    let configureClient (token: string) (c: HttpClient) =
        c.BaseAddress <- Uri "https://api.statuscake.com/v1/"
        if not (String.IsNullOrWhiteSpace token) then
            c.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)

    /// Warn on stderr if no token is set, so the cause is obvious before a 403.
    let warnIfNoToken (token: string) =
        if String.IsNullOrWhiteSpace token then
            eprintfn
                "[StatusCakeMcp] No API token configured. Set STATUSCAKE__APITOKEN (or config key StatusCake:ApiToken); calls will fail with 403 until it is set."

    // --- dispatch / run ------------------------------------------------------

    let private helpText =
        String.concat
            "\n"
            [ "statuscake-mcp — StatusCake uptime monitoring from the command line."
              ""
              "Usage: statuscake-mcp <command> [options]"
              ""
              "Commands:"
              "  list [--filter S] [--status up|down|paused] [--page N] [--limit N]"
              "                          List uptime checks with their state."
              "  down [--filter S] [--page N] [--limit N]"
              "                          Show checks that are actively down."
              "  get <id>                Detail for one check (status, uptime %, last tested)."
              "  history <id> [--limit N]"
              "                          Recent up/down periods for one check (default 10)."
              "  ssl [--days N] [--page N] [--limit N]"
              "                          SSL certificates expiring within N days (default 30)."
              "  pause <id>              Pause one check so it stops testing and alerting."
              "  resume <id>             Resume (unpause) one check."
              "  mcp [--http]            Run as an MCP server (stdio, or Streamable HTTP)."
              ""
              "Defaults: --page 1, --limit 50."
              "Set the API token via STATUSCAKE__APITOKEN (or config key StatusCake:ApiToken)." ]

    /// Map a parsed command to its error label and the work that produces output.
    let private dispatch (client: StatusCakeClient) (cmd: Command) : string * (unit -> Task<string>) =
        match cmd with
        | List(f, s, p, l) -> "uptime", fun () -> Commands.listSites client f s p l
        | Down(f, p, l) -> "uptime", fun () -> Commands.checkSitesDown client f p l
        | Get id -> sprintf "uptime/%s" id, fun () -> Commands.getSite client id
        | History(id, l) -> sprintf "uptime/%s/periods" id, fun () -> Commands.siteHistory client id l
        | Ssl(d, p, l) -> "ssl", fun () -> Commands.checkSslExpiring client d p l
        | Pause id -> sprintf "uptime/%s" id, fun () -> Commands.pauseSite client id
        | Resume id -> sprintf "uptime/%s" id, fun () -> Commands.resumeSite client id
        | Help -> "", fun () -> Task.FromResult ""

    /// Run the CLI: parse, build a client, dispatch, print. Returns the process exit code.
    let run (args: string[]) : int =
        match parse args with
        | Ok Help ->
            printfn "%s" helpText
            0
        | Error msg ->
            eprintfn "%s" msg
            eprintfn ""
            eprintfn "%s" helpText
            2
        | Ok cmd ->
            let config =
                ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional = true)
                    .AddEnvironmentVariables()
                    .Build()

            let token = config.["StatusCake:ApiToken"]
            warnIfNoToken token

            use http = new HttpClient()
            configureClient token http
            let client = StatusCakeClient(http)
            let label, work = dispatch client cmd

            try
                let output = (work ()).GetAwaiter().GetResult()
                printfn "%s" output
                0
            with ex ->
                eprintfn "%s" (Format.describeError label ex)
                1
