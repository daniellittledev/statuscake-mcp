namespace StatusCakeMcp

open System
open System.ComponentModel
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

/// MCP tools exposing StatusCake uptime and SSL data.
/// Thin wrappers over Commands.*; output is deliberately terse plain text to keep
/// token usage low.
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
        ToolHelpers.guard "uptime" (fun () -> Commands.listSites client filter status page limit)

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
        ToolHelpers.guard "uptime" (fun () -> Commands.checkSitesDown client filter page limit)

    [<McpServerTool; Description("Get detail for a single uptime check by its ID: status, uptime %, \
        check rate, and when it was last tested. Get the ID from list_sites or check_sites_down.")>]
    member _.GetSite([<Description("The check's numeric ID.")>] id: string) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s" id) (fun () -> Commands.getSite client id)

    [<McpServerTool; Description("Show recent up/down periods for a single uptime check by its ID, \
        most recent first, with how long each lasted. Use this to see when a site went down.")>]
    member _.SiteHistory
        (
            [<Description("The check's numeric ID.")>] id: string,
            [<Description("How many recent periods to return.")>] [<Optional; DefaultParameterValue(10)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s/periods" id) (fun () -> Commands.siteHistory client id limit)

    [<McpServerTool; Description("List SSL certificates expiring soon (within N days, default 30), \
        soonest first, including any already expired. Paged like list_sites.")>]
    member _.CheckSslExpiring
        (
            [<Description("Expiry window in days.")>] [<Optional; DefaultParameterValue(30)>] days: int,
            [<Description("1-based page number.")>] [<Optional; DefaultParameterValue(1)>] page: int,
            [<Description("Page size, max 100.")>] [<Optional; DefaultParameterValue(50)>] limit: int
        ) : Task<string> =
        ToolHelpers.guard "ssl" (fun () -> Commands.checkSslExpiring client days page limit)

    [<McpServerTool; Description("Pause a single uptime check by its ID so it stops testing and alerting. \
        Requires the exact numeric ID (no name lookup) to avoid pausing the wrong check.")>]
    member _.PauseSite([<Description("The check's numeric ID.")>] id: string) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s" id) (fun () -> Commands.pauseSite client id)

    [<McpServerTool; Description("Resume (unpause) a single uptime check by its ID so it starts testing \
        again. Requires the exact numeric ID (no name lookup).")>]
    member _.ResumeSite([<Description("The check's numeric ID.")>] id: string) : Task<string> =
        ToolHelpers.guard (sprintf "uptime/%s" id) (fun () -> Commands.resumeSite client id)
