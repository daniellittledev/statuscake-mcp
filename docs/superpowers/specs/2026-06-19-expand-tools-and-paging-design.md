# Expand StatusCake MCP tools + effective paging

Date: 2026-06-19

## Goal

Grow the StatusCake MCP server from 2 read tools to a useful uptime + SSL toolset,
and make every list-style tool stay token-cheap on a large tenant (this account has
1356 uptime checks and 70 SSL checks; an unfiltered `list_sites` currently returns ~62 KB).

## Constraints / decisions

- **Paging model:** filter + capped page. Tools fetch all pages internally (`limit=100`
  per request, loop to `page_count`) but return only one capped window to the model,
  with a footer pointing to the next page.
- **Write safety:** `pause_site`/`resume_site` are **ID-only** (no fuzzy name match) and
  always enabled (no env gate). `get_site`/`site_history` are also ID-only for consistency
  with the IDs `list_sites` returns.
- Output stays terse plain text (existing house style), not raw JSON.
- Functional style: all filtering/slicing/formatting are pure functions; the HTTP client
  stays thin.

## Architecture

Unchanged file roles:

- `Models.fs` — JSON records.
- `StatusCakeClient.fs` — typed `HttpClient` wrapper.
- `Tools.fs` — MCP tool surface.
- `Program.fs` — host/DI wiring (no change expected).

New: a `Format.fs` (or a `Formatting` module) holding the pure functions so they can be
unit-tested without HTTP.

### Paging engine

`FetchAll` is generalised to a private generic paged fetch over any `{data:[],metadata:{}}`
list endpoint (`uptime`, `ssl`), reusing one loop with `limit=100`.

A pure `paginate` function takes the full matched list + `(page, limit)` and returns the
window plus a footer string:
`Showing 1–50 of 412 matched · request page 2 for more` (footer omitted when it all fits).
Defaults: `limit=50`, hard cap `100`, `page=1`.

Name/URL `filter` is a case-insensitive substring match applied **client-side** (after the
internal fetch). `status` (`up`/`down`) uses the server-side `?status=` filter.

## Tools (7)

| Tool | Args | Endpoint | Output |
| --- | --- | --- | --- |
| `list_sites` | `filter?`, `status?`, `page?`, `limit?` | `GET /uptime` | one line per site (glyph, name, status), capped + footer |
| `check_sites_down` | `filter?` | `GET /uptime?status=down` (+ all for total) | down list, capped + footer if large |
| `get_site` | `id` | `GET /uptime/{id}` | name, url, status, uptime %, last tested, check rate, paused |
| `site_history` | `id`, `limit?`=10 | `GET /uptime/{id}/periods` | recent up/down periods: `status — started … (duration)` |
| `check_ssl_expiring` | `days?`=30, `page?`, `limit?` | `GET /ssl` | certs with `valid_until` within N days, soonest first, capped + footer |
| `pause_site` | `id` | `PUT /uptime/{id}` `paused=true` | confirmation of new state |
| `resume_site` | `id` | `PUT /uptime/{id}` `paused=false` | confirmation of new state |

## Models

Verified against the live API on 2026-06-19:

- `UptimeCheck` (existing) — list item: `id, name, website_url, test_type, status, paused`.
- `UptimeCheckDetail` (new) — `id, name, website_url, status, paused, uptime` (float %),
  `check_rate` (int), `last_tested_at` (string).
- `Period` (new) — `status, created_at`, optional `ended_at`, optional `duration` (ms).
- `SslCheck` (new) — `id, website_url, valid_until` (ISO), `certificate_status`,
  `issuer_common_name`, plus `flags.is_expired`.
- Generic `ListResponse<'T>` envelope (`data`, `metadata`) replacing the uptime-specific one.

Detail endpoint returns `{ "data": { ... } }` (object). `periods`/`alerts` return
`{ "data": [...], "links": {...} }` (no `metadata`).

Note: `/uptime/{id}/history` is empty on this tenant; `/periods` is the populated, more
useful source (gives durations), so `site_history` uses `/periods`. `duration` is in
**milliseconds**.

## Writes

StatusCake v1 mutations use `application/x-www-form-urlencoded` (e.g. body `paused=1`).
The client gets a small form-PUT helper. Live verification of the write path will use a
no-op (re-assert `paused=true` on an already-paused check) to confirm the request format
and a 200 without changing any production state.

## Error handling

The client currently lets a non-2xx throw raw. Wrap calls so 401/404/etc. return a terse
`Error: <status> from StatusCake (<resource>)` string the LLM can act on.

## Testing

- TDD on pure functions: `paginate` window/footer math, the SSL days-to-expiry filter+sort,
  the duration humaniser, and each terse formatter against sample records.
- Client stays thin (HTTP), exercised via a final manual end-to-end pass against the live
  API like the initial verification.

## Out of scope

Create/edit checks, PageSpeed, server tests, contact-group management — unchanged from the
project's stated scope.
