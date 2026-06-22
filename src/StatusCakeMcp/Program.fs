module StatusCakeMcp.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StatusCakeMcp

/// Run as a long-lived Streamable HTTP server (for remote/shared/web-client use).
let private runHttp (args: string[]) =
    let builder = WebApplication.CreateBuilder(args)
    let token = builder.Configuration.["StatusCake:ApiToken"]
    Cli.warnIfNoToken token

    builder.Services.AddHttpClient<StatusCakeClient>(Cli.configureClient token) |> ignore

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<StatusCakeTools>()
    |> ignore

    let app = builder.Build()
    app.MapMcp() |> ignore
    app.Run()

/// Run over stdio: the MCP client launches and owns the process.
let private runStdio (args: string[]) =
    let builder = Host.CreateApplicationBuilder(args)
    let token = builder.Configuration.["StatusCake:ApiToken"]
    Cli.warnIfNoToken token

    // stdout carries the JSON-RPC protocol, so all logs must go to stderr or they corrupt it.
    builder.Logging.AddConsole(fun o -> o.LogToStandardErrorThreshold <- LogLevel.Trace) |> ignore

    builder.Services.AddHttpClient<StatusCakeClient>(Cli.configureClient token) |> ignore

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<StatusCakeTools>()
    |> ignore

    builder.Build().Run()

[<EntryPoint>]
let main args =
    // CLI is the default. The `mcp` subcommand runs the MCP server instead:
    //   statuscake-mcp mcp           -> stdio (the client launches the process)
    //   statuscake-mcp mcp --http    -> Streamable HTTP (or STATUSCAKE__TRANSPORT=http)
    match Array.toList args with
    | "mcp" :: rest ->
        let restArr = List.toArray rest
        let wantsHttp =
            Array.contains "--http" restArr
            || String.Equals(
                Environment.GetEnvironmentVariable "STATUSCAKE__TRANSPORT",
                "http",
                StringComparison.OrdinalIgnoreCase)

        // Strip our own flag so it isn't fed to the configuration command-line parser.
        let hostArgs = restArr |> Array.filter (fun a -> a <> "--http")

        if wantsHttp then runHttp hostArgs else runStdio hostArgs
        0
    | _ -> Cli.run args
