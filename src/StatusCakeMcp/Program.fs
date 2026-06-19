module StatusCakeMcp.Program

open System
open System.Net.Http
open System.Net.Http.Headers
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StatusCakeMcp

/// Configure the typed StatusCake HttpClient. Token comes from config key
/// "StatusCake:ApiToken" (env var STATUSCAKE__APITOKEN).
let private configureClient (token: string) (c: HttpClient) =
    c.BaseAddress <- Uri "https://api.statuscake.com/v1/"
    if not (String.IsNullOrWhiteSpace token) then
        c.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)

/// Run as a long-lived Streamable HTTP server (for remote/shared/web-client use).
let private runHttp (args: string[]) =
    let builder = WebApplication.CreateBuilder(args)
    let token = builder.Configuration.["StatusCake:ApiToken"]

    builder.Services.AddHttpClient<StatusCakeClient>(configureClient token) |> ignore

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<StatusCakeTools>()
    |> ignore

    let app = builder.Build()
    app.MapMcp() |> ignore
    app.Run()

/// Run over stdio (the default): the MCP client launches and owns the process.
let private runStdio (args: string[]) =
    let builder = Host.CreateApplicationBuilder(args)
    let token = builder.Configuration.["StatusCake:ApiToken"]

    // stdout carries the JSON-RPC protocol, so all logs must go to stderr or they corrupt it.
    builder.Logging.AddConsole(fun o -> o.LogToStandardErrorThreshold <- LogLevel.Trace) |> ignore

    builder.Services.AddHttpClient<StatusCakeClient>(configureClient token) |> ignore

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<StatusCakeTools>()
    |> ignore

    builder.Build().Run()

[<EntryPoint>]
let main args =
    // Default to stdio; opt into HTTP with `--http` or STATUSCAKE__TRANSPORT=http.
    let wantsHttp =
        Array.contains "--http" args
        || String.Equals(
            Environment.GetEnvironmentVariable "STATUSCAKE__TRANSPORT",
            "http",
            StringComparison.OrdinalIgnoreCase)

    // Strip our own flag so it isn't fed to the configuration command-line parser.
    let hostArgs = args |> Array.filter (fun a -> a <> "--http")

    if wantsHttp then runHttp hostArgs else runStdio hostArgs
    0
