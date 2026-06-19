namespace StatusCakeMcp

open System.Text.Json.Serialization

/// A single uptime check as returned by GET /v1/uptime.
/// Only the fields we actually use are mapped; the rest are ignored on deserialize.
type UptimeCheck =
    { [<JsonPropertyName("id")>] Id: string
      [<JsonPropertyName("name")>] Name: string
      [<JsonPropertyName("website_url")>] WebsiteUrl: string
      [<JsonPropertyName("test_type")>] TestType: string
      [<JsonPropertyName("status")>] Status: string
      [<JsonPropertyName("paused")>] Paused: bool }

/// Pagination metadata block returned alongside the data array.
type Pagination =
    { [<JsonPropertyName("page")>] Page: int
      [<JsonPropertyName("per_page")>] PerPage: int
      [<JsonPropertyName("page_count")>] PageCount: int
      [<JsonPropertyName("total_count")>] TotalCount: int }

/// Envelope for the list endpoint: { "data": [...], "metadata": {...} }.
type UptimeListResponse =
    { [<JsonPropertyName("data")>] Data: UptimeCheck[]
      [<JsonPropertyName("metadata")>] Metadata: Pagination }
