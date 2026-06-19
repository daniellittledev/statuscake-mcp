namespace StatusCakeMcp

open System.ComponentModel
open System.Threading.Tasks
open ModelContextProtocol.Server

/// MCP tools exposing StatusCake uptime data.
/// Output is deliberately terse plain text to keep token usage low.
[<McpServerToolType>]
type StatusCakeTools(client: StatusCakeClient) =

    [<McpServerTool; Description("Return only the uptime checks that are currently DOWN. \
        Use this to quickly see if anything is broken. Returns a short plain-text list, \
        or a confirmation that all sites are up.")>]
    member _.CheckSitesDown() : Task<string> =
        task {
            let! down = client.ListDownChecks()
            let! all = client.ListAllChecks()
            let total = List.length all

            if List.isEmpty down then
                return sprintf "All %d sites are up." total
            else
                let lines =
                    down
                    |> List.map (fun c -> sprintf "DOWN: %s (%s)" c.Name c.WebsiteUrl)
                return
                    sprintf "%d of %d sites down:\n%s" (List.length down) total (String.concat "\n" lines)
        }

    [<McpServerTool; Description("List every uptime check with its current status. \
        Returns a compact one-line-per-site summary: a glyph, the site name, and its status.")>]
    member _.ListSites() : Task<string> =
        task {
            let! checks = client.ListAllChecks()

            if List.isEmpty checks then
                return "No uptime checks configured."
            else
                let line (c: UptimeCheck) =
                    let glyph =
                        if c.Paused then "⏸"
                        elif c.Status = "up" then "✓"
                        else "✗"
                    let suffix = if c.Paused then " (paused)" else ""
                    sprintf "%s %s — %s%s" glyph c.Name c.Status suffix

                let lines = checks |> List.map line |> String.concat "\n"
                return sprintf "%d sites:\n%s" (List.length checks) lines
        }
