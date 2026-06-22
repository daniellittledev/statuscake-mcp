namespace StatusCakeMcp

open System
open System.Threading.Tasks

/// Shared command core used by both front-ends (MCP tools and the CLI).
/// Each operation does the work and returns terse plain text; failures surface as
/// exceptions so each front-end can handle them its own way (MCP turns them into
/// text, the CLI sets an exit code).
module Commands =

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

    /// List uptime checks, optionally filtered by name/URL substring and/or state.
    let listSites (client: StatusCakeClient) (filter: string) (status: string) (page: int) (limit: int) : Task<string> =
        task {
            let state = if isNull status then "" else status.Trim().ToLowerInvariant()

            if String.IsNullOrWhiteSpace filter && state = "" then
                // No filter at all: page server-side, one request.
                let! items, total = client.GetChecksPage("", page, limit)
                return renderPage "sites" Format.formatSiteLine page limit total items
            else
                // 'up'/'down' narrow server-side; 'paused' has no server filter (fetch all).
                // The state filter is then applied client-side so paused is excluded from up/down.
                let serverStatus = if state = "up" || state = "down" then state else ""
                let! all = client.ListChecks(serverStatus)
                let matched = all |> Format.filterByState state |> Format.filterByName filter
                return renderList "sites" Format.formatSiteLine page limit matched
        }

    /// Uptime checks that are ACTIVELY down (running and last check failed).
    let checkSitesDown (client: StatusCakeClient) (filter: string) (page: int) (limit: int) : Task<string> =
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
        }

    /// Detail for a single uptime check by ID.
    let getSite (client: StatusCakeClient) (id: string) : Task<string> =
        task {
            let! d = client.GetCheckDetail(id)
            return Format.formatDetail d
        }

    /// Recent up/down periods for a single uptime check by ID.
    let siteHistory (client: StatusCakeClient) (id: string) (limit: int) : Task<string> =
        task {
            let! periods = client.GetPeriods(id, limit)
            return renderList "periods" Format.formatPeriod 1 limit periods
        }

    /// SSL certificates expiring within `days` (default window applied by the caller).
    let checkSslExpiring (client: StatusCakeClient) (days: int) (page: int) (limit: int) : Task<string> =
        task {
            let now = DateTimeOffset.UtcNow
            let! ssls = client.ListSslChecks()
            let expiring = Format.expiringWithin now days ssls

            if List.isEmpty expiring then
                return sprintf "No certificates expire within %d days." days
            else
                return renderList "expiring certificates" (Format.formatSslLine now) page limit expiring
        }

    /// Pause a single uptime check by ID.
    let pauseSite (client: StatusCakeClient) (id: string) : Task<string> =
        task {
            do! client.SetPaused(id, true)
            let! d = client.GetCheckDetail(id)
            return sprintf "Paused %s — now %s." d.Name (if d.Paused then "paused" else "active")
        }

    /// Resume (unpause) a single uptime check by ID.
    let resumeSite (client: StatusCakeClient) (id: string) : Task<string> =
        task {
            do! client.SetPaused(id, false)
            let! d = client.GetCheckDetail(id)
            return sprintf "Resumed %s — now %s." d.Name (if d.Paused then "paused" else "active")
        }
