module StatusCakeMcp.Format

open System
open System.Net.Http
open System.Threading.Tasks

/// Case-insensitive substring match on name or website URL.
/// A blank term matches everything.
let filterByName (term: string) (checks: UptimeCheck list) : UptimeCheck list =
    if String.IsNullOrWhiteSpace term then
        checks
    else
        let t = term.Trim()
        checks
        |> List.filter (fun c ->
            c.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
            || c.WebsiteUrl.Contains(t, StringComparison.OrdinalIgnoreCase))

/// Compact human duration from milliseconds, largest two non-zero units (d/h/m),
/// e.g. "2h 9m", "154d 18h", or "<1m" for anything under a minute.
let humanizeDuration (ms: int64) : string =
    let totalMinutes = ms / 60000L
    if totalMinutes < 1L then
        "<1m"
    else
        let days = totalMinutes / 1440L
        let hours = (totalMinutes % 1440L) / 60L
        let mins = totalMinutes % 60L
        [ days, "d"; hours, "h"; mins, "m" ]
        |> List.skipWhile (fun (v, _) -> v = 0L)
        |> List.truncate 2
        |> List.map (fun (v, s) -> sprintf "%d%s" v s)
        |> String.concat " "

/// SSL checks whose certificate expires within `days` of `now` (including already expired),
/// sorted soonest-first.
let expiringWithin (now: DateTimeOffset) (days: int) (ssls: SslCheck list) : SslCheck list =
    let cutoff = now.AddDays(float days)
    ssls
    |> List.filter (fun s -> s.ValidUntil <= cutoff)
    |> List.sortBy (fun s -> s.ValidUntil)

/// One roster line for an uptime check: glyph, name, status, and a paused marker.
let formatSiteLine (c: UptimeCheck) : string =
    let glyph =
        if c.Paused then "⏸"
        elif c.Status = "up" then "✓"
        else "✗"
    let suffix = if c.Paused then " (paused)" else ""
    sprintf "%s %s — %s%s" glyph c.Name c.Status suffix

/// One line describing a down check: "DOWN: name (url)".
let formatDownLine (c: UptimeCheck) : string =
    sprintf "DOWN: %s (%s)" c.Name c.WebsiteUrl

/// Multi-line terse view of a single check's detail.
let formatDetail (d: UptimeCheckDetail) : string =
    let pausedSuffix = if d.Paused then " (paused)" else ""
    [ sprintf "%s (%s)" d.Name d.WebsiteUrl
      sprintf "Status: %s%s" d.Status pausedSuffix
      sprintf "Uptime: %g%%" d.Uptime
      sprintf "Check rate: %ds" d.CheckRate
      sprintf "Last tested: %s" d.LastTestedAt ]
    |> String.concat "\n"

/// One line for an up/down period, using humanizeDuration for ended spans.
let formatPeriod (p: Period) : string =
    if String.IsNullOrEmpty p.EndedAt then
        sprintf "%s — since %s (ongoing)" p.Status p.CreatedAt
    else
        sprintf "%s — %s → %s (%s)" p.Status p.CreatedAt p.EndedAt (humanizeDuration p.Duration)

/// One line for an SSL check: domain, days until expiry (or EXPIRED), and the date.
let formatSslLine (now: DateTimeOffset) (s: SslCheck) : string =
    let date = s.ValidUntil.ToString("yyyy-MM-dd")
    if s.ValidUntil <= now then
        sprintf "%s — EXPIRED (%s)" s.WebsiteUrl date
    else
        let daysLeft = int (floor (s.ValidUntil - now).TotalDays)
        sprintf "%s — %dd left (%s)" s.WebsiteUrl daysLeft date

/// Footer describing the current page's position within `total` matches, and whether
/// more pages exist. None when it all fits on one page or the page is past the end.
/// `limit` is clamped to 1..100; `page` is clamped to >= 1.
let pageFooter (page: int) (limit: int) (total: int) : string option =
    let limit = max 1 (min 100 limit)
    let page = max 1 page
    let startIdx = (page - 1) * limit
    if total <= limit || startIdx >= total then
        None
    else
        let fromN = startIdx + 1
        let toN = min total (startIdx + limit)
        let baseLine = sprintf "Showing %d–%d of %d matched" fromN toN total
        if toN < total then Some(sprintf "%s · request page %d for more" baseLine (page + 1))
        else Some baseLine

/// Slice an already-filtered list to a single page window.
/// Returns the window plus an optional footer describing position and whether more pages exist.
/// `limit` is clamped to 1..100; `page` is clamped to >= 1.
let paginate (page: int) (limit: int) (items: 'a list) : 'a list * string option =
    let total = List.length items
    let lim = max 1 (min 100 limit)
    let pg = max 1 page
    let startIdx = (pg - 1) * lim
    let window =
        if startIdx >= total then []
        else items |> List.skip startIdx |> List.truncate lim

    window, pageFooter page limit total

/// Turn an exception from a StatusCake call into a terse, actionable one-line message.
/// `label` is the resource being accessed (e.g. "uptime", "uptime/123").
let describeError (label: string) (ex: exn) : string =
    match ex with
    | :? HttpRequestException as e when e.StatusCode.HasValue ->
        let code = int e.StatusCode.Value
        match code with
        | 401
        | 403 ->
            sprintf
                "Error: StatusCake rejected the request (%d) — check your API token is set and valid (env STATUSCAKE__APITOKEN)."
                code
        | 404 -> sprintf "Error: not found (404) — no StatusCake resource at '%s'." label
        | 429 -> sprintf "Error: rate limited by StatusCake (429) — wait a moment and retry."
        | _ -> sprintf "Error: StatusCake returned HTTP %d for '%s'." code label
    | :? HttpRequestException as e ->
        // No status code => the request never completed (DNS, refused, TLS, offline).
        sprintf "Error: could not reach StatusCake (%s) — %s" label e.Message
    | :? TaskCanceledException
    | :? OperationCanceledException -> sprintf "Error: request to StatusCake timed out (%s)." label
    | e -> sprintf "Error: %s (%s)" e.Message label
