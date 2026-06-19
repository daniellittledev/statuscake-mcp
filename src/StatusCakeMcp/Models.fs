namespace StatusCakeMcp

open System
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

/// Richer single-check view from GET /v1/uptime/{id}.
type UptimeCheckDetail =
    { [<JsonPropertyName("id")>] Id: string
      [<JsonPropertyName("name")>] Name: string
      [<JsonPropertyName("website_url")>] WebsiteUrl: string
      [<JsonPropertyName("status")>] Status: string
      [<JsonPropertyName("paused")>] Paused: bool
      [<JsonPropertyName("uptime")>] Uptime: float
      [<JsonPropertyName("check_rate")>] CheckRate: int
      [<JsonPropertyName("last_tested_at")>] LastTestedAt: string }

/// An up/down span from GET /v1/uptime/{id}/periods.
/// `EndedAt` is null and `Duration` is 0 for an ongoing (most recent) period.
type Period =
    { [<JsonPropertyName("status")>] Status: string
      [<JsonPropertyName("created_at")>] CreatedAt: string
      [<JsonPropertyName("ended_at")>] EndedAt: string
      [<JsonPropertyName("duration")>] Duration: int64 }

/// An SSL check from GET /v1/ssl.
type SslCheck =
    { [<JsonPropertyName("id")>] Id: string
      [<JsonPropertyName("website_url")>] WebsiteUrl: string
      [<JsonPropertyName("valid_until")>] ValidUntil: DateTimeOffset
      [<JsonPropertyName("certificate_status")>] CertificateStatus: string }

/// Pagination metadata block returned alongside the data array.
type Pagination =
    { [<JsonPropertyName("page")>] Page: int
      [<JsonPropertyName("per_page")>] PerPage: int
      [<JsonPropertyName("page_count")>] PageCount: int
      [<JsonPropertyName("total_count")>] TotalCount: int }

/// Paged list envelope: { "data": [...], "metadata": {...} } (uptime, ssl).
type PagedResponse<'T> =
    { [<JsonPropertyName("data")>] Data: 'T[]
      [<JsonPropertyName("metadata")>] Metadata: Pagination }

/// List envelope without pagination metadata: { "data": [...], "links": {...} } (periods).
type ItemsResponse<'T> =
    { [<JsonPropertyName("data")>] Data: 'T[] }

/// Single-item envelope: { "data": { ... } } (detail endpoints).
type ItemResponse<'T> =
    { [<JsonPropertyName("data")>] Data: 'T }
