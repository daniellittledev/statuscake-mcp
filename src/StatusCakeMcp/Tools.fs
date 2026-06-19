namespace StatusCakeMcp

open System
open System.ComponentModel
open System.Net.Http
open System.Runtime.InteropServices
open System.Threading.Tasks
open ModelContextProtocol.Server

module private ToolHelpers =

    /// Run a tool body, turning HTTP/transport failures into a terse one-line message
    /// the model can act on instead of a stack trace.
    let guard (label: string) (work: unit -> Task<string>) : Task<string> =
        task {
            try
                return! work ()
            with
            | :? HttpRequestException as ex ->
                let code =
                    if ex.StatusCode.HasValue then string (int ex.StatusCode.Value) else "network error"
                return sprintf "Error: %s from StatusCake (%s)" code label
            | ex -> return sprintf "Error: %s (%s)" ex.Message label
        }

    /// Render a full (client-side) list to a header + capped page + optional next-page footer.
    let renderList (noun: string) (formatter: 'a -> string) (page: int) (limit: int) (items: 'a list) : string =
        if List.isEmpty items then
            sprintf "No %s." noun
        else
            let window, footer = Format.paginate page limit items
            let lines = window |> List.map formatter |> String.concat "\n"
            match footer with
            | Some f -> sprintf "%s\n%s" lines f
            | None -> sprintf "%d %s:\n%s" (List.length items) noun lines

    /// Render an already-server-paged list, using the account-wide `total` for the footer.
    let renderPage (noun: string) (formatter: 'a -> string) (page: int) (limit: int) (total: int) (items: 'a list) : string =
        if List.isEmpty items then
            sprintf "No %s." noun
        else
            let lines = items |> List.map formatter |> String.concat "\n"
            match Format.pageFooter page limit total with
            | Some f -> sprintf "%s\n%s" lines f
            | None -> sprintf "%d %s:\n%s" total noun lines

/// MCP tools exposing StatusCake uptime and SSL data.
/// Output is deliberately terse plain text to keep token usage low.
[<McpServerToolType>]
type StatusCakeTools(client: StatusCakeClient) =

    [<McpServerTool; Description("List uptime checks with their current status. Optionally filter \
        by a case-insensitive substring of the name or URL, and/or by status ('up' or 'down'). \
        Results are paged: returns one page (default 50) with a footer pointing to the next page \
        when there are more. Use this on large accounts instead of fetching everything.")>]
    member _.ListSites
        (
            [<Description("Case-insensitive substring of the site name or URL. Blank = all sites.")>] [<Optional; DefaultParameterValue("")>] filter: string,
            [<Description("Filter by status: 'up' or 'down'. Blank = any status.")>] [<Optional; DefaultParameterValue("")>] status: string,
            [<Description("1-based page number.")>] [<Optional; DefaultParameterValue(1)>] page: int,
            [<Description("Page size, max 100.")>] [<Optional; DefaultParameterValue(50)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard "uptime" (fun () ->
            task {
                if String.IsNullOrWhiteSpace filter then
                    // No name filter: page server-side (status is a server filter too), one request.
                    let! items, total = client.GetChecksPage(status, page, limit)
                    return ToolHelpers.renderPage "sites" Format.formatSiteLine page limit total items
                else
                    // Name filter has no server-side equivalent: fetch all (status-filtered server-side),
                    // then match and page client-side.
                    let! all = client.ListChecks(status)
                    let matched = all |> Format.filterByName filter
                    return ToolHelpers.renderList "sites" Format.formatSiteLine page limit matched
            })

    [<McpServerTool; Description("Return only the uptime checks that are currently DOWN, with a count \
        of how many of the total are down. Use this to quickly see if anything is broken. Optionally \
        filter by a case-insensitive substring of the name or URL.")>]
    member _.CheckSitesDown
        (
            [<Description("Case-insensitive substring of the site name or URL. Blank = all down sites.")>] [<Optional; DefaultParameterValue("")>] filter: string,
            [<Description("1-based page number.")>] [<Optional; DefaultParameterValue(1)>] page: int,
            [<Description("Page size, max 100.")>] [<Optional; DefaultParameterValue(50)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard "uptime" (fun () ->
            task {
                let! down = client.ListDownChecks()
                // Total comes from list metadata (one request), not a full account page-through.
                let! total = client.CountAllChecks()
                let matched = down |> Format.filterByName filter

                if List.isEmpty matched then
                    return
                        (if String.IsNullOrWhiteSpace filter then sprintf "All %d sites are up." total
                         else "No matching sites are down.")
                else
                    let window, footer = Format.paginate page limit matched
                    let lines = window |> List.map Format.formatDownLine |> String.concat "\n"
                    let body = sprintf "%d of %d sites down:\n%s" (List.length matched) total lines
                    return
                        match footer with
                        | Some f -> sprintf "%s\n%s" body f
                        | None -> body
            })

    [<McpServerTool; Description("Get detail for a single uptime check by its ID: status, uptime %, \
        check rate, and when it was last tested. Get the ID from list_sites or check_sites_down.")>]
    member _.GetSite([<Description("The check's numeric ID.")>] id: string) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s" id) (fun () ->
            task {
                let! d = client.GetCheckDetail(id)
                return Format.formatDetail d
            })

    [<McpServerTool; Description("Show recent up/down periods for a single uptime check by its ID, \
        most recent first, with how long each lasted. Use this to see when a site went down.")>]
    member _.SiteHistory
        (
            [<Description("The check's numeric ID.")>] id: string,
            [<Description("How many recent periods to return.")>] [<Optional; DefaultParameterValue(10)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s/periods" id) (fun () ->
            task {
                let! periods = client.GetPeriods(id, limit)
                return ToolHelpers.renderList "periods" Format.formatPeriod 1 limit periods
            })

    [<McpServerTool; Description("List SSL certificates expiring soon (within N days, default 30), \
        soonest first, including any already expired. Paged like list_sites.")>]
    member _.CheckSslExpiring
        (
            [<Description("Expiry window in days.")>] [<Optional; DefaultParameterValue(30)>] days: int,
            [<Description("1-based page number.")>] [<Optional; DefaultParameterValue(1)>] page: int,
            [<Description("Page size, max 100.")>] [<Optional; DefaultParameterValue(50)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard "ssl" (fun () ->
            task {
                let now = DateTimeOffset.UtcNow
                let! ssls = client.ListSslChecks()
                let expiring = Format.expiringWithin now days ssls

                if List.isEmpty expiring then
                    return sprintf "No certificates expire within %d days." days
                else
                    return ToolHelpers.renderList "expiring certificates" (Format.formatSslLine now) page limit expiring
            })

    [<McpServerTool; Description("Pause a single uptime check by its ID so it stops testing and alerting. \
        Requires the exact numeric ID (no name lookup) to avoid pausing the wrong check.")>]
    member _.PauseSite([<Description("The check's numeric ID.")>] id: string) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s" id) (fun () ->
            task {
                do! client.SetPaused(id, true)
                let! d = client.GetCheckDetail(id)
                return sprintf "Paused %s — now %s." d.Name (if d.Paused then "paused" else "active")
            })

    [<McpServerTool; Description("Resume (unpause) a single uptime check by its ID so it starts testing \
        again. Requires the exact numeric ID (no name lookup).")>]
    member _.ResumeSite([<Description("The check's numeric ID.")>] id: string) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s" id) (fun () ->
            task {
                do! client.SetPaused(id, false)
                let! d = client.GetCheckDetail(id)
                return sprintf "Resumed %s — now %s." d.Name (if d.Paused then "paused" else "active")
            })
