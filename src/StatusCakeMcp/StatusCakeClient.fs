namespace StatusCakeMcp

open System
open System.Collections.Generic
open System.Net.Http
open System.Net.Http.Json
open System.Threading.Tasks

/// Typed client over the StatusCake v1 API.
/// BaseAddress and the Bearer Authorization header are configured in DI (Program.fs).
/// HTTP failures surface as exceptions; the tool layer turns them into terse messages.
type StatusCakeClient(httpClient: HttpClient) =

    /// Fetch every page of a paged list endpoint (`{ data, metadata }`), e.g. uptime or ssl.
    /// `query` is an optional extra query string fragment starting with "&".
    member private _.FetchAllPages<'T>(resource: string, ?query: string) : Task<'T list> =
        task {
            let q = defaultArg query ""
            let mutable page = 1
            let mutable pageCount = 1
            let results = ResizeArray<'T>()

            while page <= pageCount do
                let url = sprintf "%s?limit=100&page=%d%s" resource page q
                let! resp = httpClient.GetFromJsonAsync<PagedResponse<'T>>(url)
                results.AddRange(resp.Data)
                pageCount <- max 1 resp.Metadata.PageCount
                page <- page + 1

            return List.ofSeq results
        }

    /// Optional `&status=` query fragment (server-side up/down filter).
    static member private statusQuery(status: string) =
        if String.IsNullOrWhiteSpace status then "" else sprintf "&status=%s" (status.Trim().ToLowerInvariant())

    /// All uptime checks across the account.
    member this.ListAllChecks() : Task<UptimeCheck list> = this.FetchAllPages<UptimeCheck>("uptime")

    /// All uptime checks, optionally server-side filtered by status ("up"/"down").
    member this.ListChecks(status: string) : Task<UptimeCheck list> =
        this.FetchAllPages<UptimeCheck>("uptime", StatusCakeClient.statusQuery status)

    /// A single server-side page of uptime checks (optionally status-filtered),
    /// returned with the account-wide total from the response metadata.
    member _.GetChecksPage(status: string, page: int, limit: int) : Task<UptimeCheck list * int> =
        task {
            let url = sprintf "uptime?limit=%d&page=%d%s" limit page (StatusCakeClient.statusQuery status)
            let! resp = httpClient.GetFromJsonAsync<PagedResponse<UptimeCheck>>(url)
            return List.ofArray resp.Data, resp.Metadata.TotalCount
        }

    /// Only checks currently reported as down (server-side filtered).
    member this.ListDownChecks() : Task<UptimeCheck list> =
        this.FetchAllPages<UptimeCheck>("uptime", "&status=down")

    /// Total uptime-check count, read from list metadata with a single 1-item request
    /// (avoids paging the whole account just to count it).
    member _.CountAllChecks() : Task<int> =
        task {
            let! resp = httpClient.GetFromJsonAsync<PagedResponse<UptimeCheck>>("uptime?limit=1&page=1")
            return resp.Metadata.TotalCount
        }

    /// All SSL checks across the account.
    member this.ListSslChecks() : Task<SslCheck list> = this.FetchAllPages<SslCheck>("ssl")

    /// Richer single uptime check.
    member _.GetCheckDetail(id: string) : Task<UptimeCheckDetail> =
        task {
            let! resp = httpClient.GetFromJsonAsync<ItemResponse<UptimeCheckDetail>>(sprintf "uptime/%s" id)
            return resp.Data
        }

    /// Recent up/down periods for a check, most recent first (as the API returns them).
    member _.GetPeriods(id: string, limit: int) : Task<Period list> =
        task {
            let! resp =
                httpClient.GetFromJsonAsync<ItemsResponse<Period>>(sprintf "uptime/%s/periods?limit=%d" id limit)
            return List.ofArray resp.Data
        }

    /// Pause or resume a check (StatusCake mutations are form-urlencoded).
    member _.SetPaused(id: string, paused: bool) : Task<unit> =
        task {
            let form: KeyValuePair<string, string> list =
                [ KeyValuePair("paused", (if paused then "1" else "0")) ]
            use content = new FormUrlEncodedContent(form)
            let! resp = httpClient.PutAsync(sprintf "uptime/%s" id, content)
            resp.EnsureSuccessStatusCode() |> ignore
            return ()
        }
