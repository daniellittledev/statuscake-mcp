module StatusCakeMcp.Tests.FormatTests

open System
open Xunit
open StatusCakeMcp

let private check name url status paused =
    { Id = "1"; Name = name; WebsiteUrl = url; TestType = "HTTP"; Status = status; Paused = paused }

let private ssl id url validUntil =
    { Id = id; WebsiteUrl = url; ValidUntil = DateTimeOffset.Parse(validUntil: string); CertificateStatus = "CERT_OK" }

[<Fact>]
let ``filterByName matches name or url case-insensitively`` () =
    let checks =
        [ check "EA MCI Malaysia" "https://mcimalaysia.eventsair.com/ping" "down" false
          check "EA Sales" "https://sales.example.com" "up" false
          check "Other" "https://mci-other.com" "up" false ]
    let result = checks |> Format.filterByName "MCI"
    Assert.Equal<string list>([ "EA MCI Malaysia"; "Other" ], result |> List.map (fun c -> c.Name))

[<Fact>]
let ``filterByName with a blank term returns everything`` () =
    let checks = [ check "A" "a" "up" false; check "B" "b" "up" false ]
    Assert.Equal(2, (checks |> Format.filterByName "  ").Length)

[<Fact>]
let ``humanizeDuration shows largest two units`` () =
    Assert.Equal("2h 9m", Format.humanizeDuration 7743000L)   // 2h 9m 3s
    Assert.Equal("154d 18h", Format.humanizeDuration 13373262000L)
    Assert.Equal("<1m", Format.humanizeDuration 4000L)
    Assert.Equal("5m", Format.humanizeDuration 300000L)       // exactly 5 minutes, no second unit

[<Fact>]
let ``expiringWithin keeps certs due within the window, soonest first, dropping the rest`` () =
    let now = DateTimeOffset.Parse "2026-06-19T00:00:00+00:00"
    let ssls =
        [ ssl "far" "far.com" "2026-12-02T00:00:00+00:00"     // 166 days out — excluded
          ssl "soon" "soon.com" "2026-07-10T00:00:00+00:00"   // 21 days out — included
          ssl "expired" "old.com" "2026-06-01T00:00:00+00:00" // already expired — included
          ssl "edge" "edge.com" "2026-07-19T00:00:00+00:00" ] // exactly 30 days — included
    let result = Format.expiringWithin now 30 ssls |> List.map (fun s -> s.Id)
    Assert.Equal<string list>([ "expired"; "soon"; "edge" ], result)

[<Fact>]
let ``formatSiteLine marks up, down, and paused`` () =
    Assert.Equal("✓ EA Sales — up", Format.formatSiteLine (check "EA Sales" "u" "up" false))
    Assert.Equal("✗ EA MCI — down", Format.formatSiteLine (check "EA MCI" "u" "down" false))
    Assert.Equal("⏸ EventsAIR — up (paused)", Format.formatSiteLine (check "EventsAIR" "u" "up" true))

[<Fact>]
let ``formatDownLine shows name and url`` () =
    let c = check "EA MCI Malaysia" "https://mcimalaysia.eventsair.com/ping" "down" false
    Assert.Equal("DOWN: EA MCI Malaysia (https://mcimalaysia.eventsair.com/ping)", Format.formatDownLine c)

[<Fact>]
let ``formatDetail renders a terse multi-line summary`` () =
    let d =
        { Id = "114812"; Name = "EventsAIR"; WebsiteUrl = "eventsair.com"; Status = "up"
          Paused = true; Uptime = 99.9; CheckRate = 300; LastTestedAt = "2023-01-31T16:47:56+00:00" }
    let expected =
        "EventsAIR (eventsair.com)\n"
        + "Status: up (paused)\n"
        + "Uptime: 99.9%\n"
        + "Check rate: 300s\n"
        + "Last tested: 2023-01-31T16:47:56+00:00"
    Assert.Equal(expected, Format.formatDetail d)

[<Fact>]
let ``formatPeriod shows an ongoing span and an ended span with duration`` () =
    let ongoing = { Status = "down"; CreatedAt = "2024-01-21T04:06:41+00:00"; EndedAt = null; Duration = 0L }
    Assert.Equal("down — since 2024-01-21T04:06:41+00:00 (ongoing)", Format.formatPeriod ongoing)
    let ended =
        { Status = "up"; CreatedAt = "2023-08-19T09:18:59+00:00"
          EndedAt = "2024-01-21T04:06:41+00:00"; Duration = 13373262000L }
    Assert.Equal("up — 2023-08-19T09:18:59+00:00 → 2024-01-21T04:06:41+00:00 (154d 18h)", Format.formatPeriod ended)

[<Fact>]
let ``formatSslLine shows days left or EXPIRED`` () =
    let now = DateTimeOffset.Parse "2026-06-19T00:00:00+00:00"
    Assert.Equal("https://soon.com — 21d left (2026-07-10)",
                 Format.formatSslLine now (ssl "soon" "https://soon.com" "2026-07-10T00:00:00+00:00"))
    Assert.Equal("https://old.com — EXPIRED (2026-06-01)",
                 Format.formatSslLine now (ssl "old" "https://old.com" "2026-06-01T00:00:00+00:00"))

[<Fact>]
let ``paginate returns all items and no footer when they fit in one page`` () =
    let items = [ 1; 2; 3 ]
    let window, footer = Format.paginate 1 50 items
    Assert.Equal<int list>([ 1; 2; 3 ], window)
    Assert.Equal(None, footer)

[<Fact>]
let ``paginate first page of many returns a window and a next-page footer`` () =
    let items = [ 1..120 ]
    let window, footer = Format.paginate 1 50 items
    Assert.Equal<int list>([ 1..50 ], window)
    Assert.Equal(Some "Showing 1–50 of 120 matched · request page 2 for more", footer)

[<Fact>]
let ``paginate last page returns the remainder and a footer without a next-page hint`` () =
    let items = [ 1..120 ]
    let window, footer = Format.paginate 3 50 items
    Assert.Equal<int list>([ 101..120 ], window)
    Assert.Equal(Some "Showing 101–120 of 120 matched", footer)

[<Fact>]
let ``paginate past the last page returns an empty window`` () =
    let items = [ 1..120 ]
    let window, _ = Format.paginate 99 50 items
    Assert.Equal<int list>([], window)

[<Fact>]
let ``paginate clamps limit to 100 and page to at least 1`` () =
    let items = [ 1..250 ]
    let window, _ = Format.paginate 0 9999 items
    Assert.Equal<int list>([ 1..100 ], window)

[<Fact>]
let ``pageFooter reports position and next page from a server-side total`` () =
    Assert.Equal(None, Format.pageFooter 1 50 30)                                          // fits one page
    Assert.Equal(Some "Showing 1–50 of 120 matched · request page 2 for more", Format.pageFooter 1 50 120)
    Assert.Equal(Some "Showing 101–120 of 120 matched", Format.pageFooter 3 50 120)        // last page, no next hint
    Assert.Equal(None, Format.pageFooter 99 50 120)                                        // past the end
