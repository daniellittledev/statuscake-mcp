namespace StatusCakeMcp

open System
open System.ComponentModel
open System.Net.Http
open System.Runtime.InteropServices
open System.Threading.Tasks
open ModelContextProtocol.Server

module private ToolHelpers =

    /// Run a tool body, turning any failure into a terse, actionable one-line message
    /// (see Format.describeError) instead of a stack trace.
    let guard (label: string) (work: unit -> Task<string>) : Task<string> =
        task {
            try
                return! work ()
            with ex ->
                return Format.describeError label ex
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

    [<McpServerTool; Description("List uptime checks with their current state. Optionally filter by \
        a case-insensitive substring of the name or URL, and/or by state. State is one of: 'up' \
        (running, last check passed), 'down' (running, last check FAILED), or 'paused' (suspended — \
        a paused check is never 'up' or 'down' even if it last reported down). Results are paged \
        (default 50) with a footer pointing to the next page. Use this on large accounts instead of \
        fetching everything.")>]
    member _.ListSites
        (
            [<Description("Case-insensitive substring of the site name or URL. Blank = all sites.")>] [<Optional; DefaultParameterValue("")>] filter: string,
            [<Description("Filter by state: 'up', 'down', or 'paused'. Blank = any state.")>] [<Optional; DefaultParameterValue("")>] status: string,
            [<Description("1-based page number.")>] [<Optional; DefaultParameterValue(1)>] page: int,
            [<Description("Page size, max 100.")>] [<Optional; DefaultParameterValue(50)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard "uptime" (fun () ->
            task {
                let state = if isNull status then "" else status.Trim().ToLowerInvariant()

                if String.IsNullOrWhiteSpace filter && state = "" then
                    // No filter at all: page server-side, one request.
                    let! items, total = client.GetChecksPage("", page, limit)
                    return ToolHelpers.renderPage "sites" Format.formatSiteLine page limit total items
                else
                    // 'up'/'down' narrow server-side; 'paused' has no server filter (fetch all).
                    // The state filter is then applied client-side so paused is excluded from up/down.
                    let serverStatus = if state = "up" || state = "down" then state else ""
                    let! all = client.ListChecks(serverStatus)
                    let matched = all |> Format.filterByState state |> Format.filterByName filter
                    return ToolHelpers.renderList "sites" Format.formatSiteLine page limit matched
            })

    [<McpServerTool; Description("Return uptime checks that are ACTIVELY down (running AND last check \
        failed), with how many of the total that is. Use this to see if anything is actually broken. \
        Paused checks are NOT counted as down even if they last reported down — they are suspended and \
        not alerting; their count is noted separately (use list_sites status=paused to see them). \
        Optionally filter by a case-insensitive substring of the name or URL.")>]
    member _.CheckSitesDown
        (
            [<Description("Case-insensitive substring of the site name or URL. Blank = all down sites.")>] [<Optional; DefaultParameterValue("")>] filter: string,
            [<Description("1-based page number.")>] [<Optional; DefaultParameterValue(1)>] page: int,
            [<Description("Page size, max 100.")>] [<Optional; DefaultParameterValue(50)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard "uptime" (fun () ->
            task {
                let! downStatus = client.ListDownChecks()
                // Total comes from list metadata (one request), not a full account page-through.
                let! total = client.CountAllChecks()
                let matched = downStatus |> Format.filterByName filter
                let active = matched |> List.filter (fun c -> not c.Paused)
                let pausedDown = List.length matched - List.length active

                let note =
                    if pausedDown > 0 then
                        sprintf
                            "\n(%d paused check%s last reported down — use list_sites status=paused to see them.)"
                            pausedDown
                            (if pausedDown = 1 then "" else "s")
                    else
                        ""

                if List.isEmpty active then
                    let head =
                        if String.IsNullOrWhiteSpace filter then sprintf "0 of %d sites actively down." total
                        else "No matching sites are actively down."
                    return head + note
                else
                    let window, footer = Format.paginate page limit active
                    let lines = window |> List.map Format.formatDownLine |> String.concat "\n"
                    let body = sprintf "%d of %d sites actively down:\n%s" (List.length active) total lines
                    let withFooter =
                        match footer with
                        | Some f -> sprintf "%s\n%s" body f
                        | None -> body
                    return withFooter + note
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
