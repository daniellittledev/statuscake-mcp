module StatusCakeMcp.Program

open System
open System.Net.Http.Headers
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open StatusCakeMcp

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    // API token from config key "StatusCake:ApiToken" (env var STATUSCAKE__APITOKEN).
    let token = builder.Configuration.["StatusCake:ApiToken"]

    builder.Services
        .AddHttpClient<StatusCakeClient>(fun c ->
            c.BaseAddress <- Uri "https://api.statuscake.com/v1/"
            if not (String.IsNullOrWhiteSpace token) then
                c.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token))
    |> ignore

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<StatusCakeTools>()
    |> ignore

    let app = builder.Build()
    app.MapMcp() |> ignore
    app.Run()
    0
