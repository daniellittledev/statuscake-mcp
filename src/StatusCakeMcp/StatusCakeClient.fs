namespace StatusCakeMcp

open System.Net.Http
open System.Net.Http.Json
open System.Threading.Tasks

/// Typed client over the StatusCake v1 API.
/// BaseAddress and the Bearer Authorization header are configured in DI (Program.fs).
type StatusCakeClient(httpClient: HttpClient) =

    /// Fetch every page of `uptime`, optionally filtered by status ("up"/"down").
    member private _.FetchAll(?status: string) : Task<UptimeCheck list> =
        task {
            let filter =
                match status with
                | Some s -> sprintf "&status=%s" s
                | None -> ""

            let mutable page = 1
            let mutable pageCount = 1
            let results = ResizeArray<UptimeCheck>()

            while page <= pageCount do
                let url = sprintf "uptime?limit=100&page=%d%s" page filter
                let! resp = httpClient.GetFromJsonAsync<UptimeListResponse>(url)
                results.AddRange(resp.Data)
                pageCount <- max 1 resp.Metadata.PageCount
                page <- page + 1

            return List.ofSeq results
        }

    /// All uptime checks across the account.
    member this.ListAllChecks() : Task<UptimeCheck list> = this.FetchAll()

    /// Only checks currently reported as down (server-side filtered).
    member this.ListDownChecks() : Task<UptimeCheck list> = this.FetchAll(status = "down")
