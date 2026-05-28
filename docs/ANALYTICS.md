# Analytics module

Homegrown page-view analytics: tracking pixel, raw event storage in Cosmos, nightly aggregation jobs, dashboard read API, and manual backfill.

## Background reading

This repo implements the system described in:

- [Homegrown Analytics](https://www.duncanmackenzie.net/blog/homegrown-analytics/) — tracking pixel, queue-based ingest, Cosmos storage, timer aggregations, daily email summary (**primary reference for this code**)

The same Azure Functions + queue pattern was introduced earlier for photo sales in [Order fulfillment with Azure Functions and Stripe](https://www.duncanmackenzie.net/blog/order-fulfillment/).

## Source files

| File | Role |
|------|------|
| `AnalyticsFunctions.cs` | HTTP pixel, queue processor, timers, stats API, backfill |
| `AnalyticsData.cs` | Cosmos queries, aggregate upserts, stats read models |
| `FunctionsHelpers.cs` | Referrer normalization, crawler detection helpers |
| `Charting.cs` | QuickChart line chart for daily summary email |
| `Models.cs` | `RequestRecord`, `ViewsBy*`, stats DTOs |
| `Program.cs` | `ForwardedForHeaderMiddleware` restores client IP for `event` |

External dependency: **UAParser** (`../uap-csharp/UAParser`) for browser/device/spider classification.

## Request flow

```
Browser / bot                     Queues & timers                    Cosmos DB
      │                                  │                              │
      ▼                                  ▼                              ▼
 event (GIF) ──► analytics-event ──► ProcessEvent ──► ViewEvents (raw)
      │                                                                  │
      │                    ComputeViewsBy* (timers) ◄──────────────────┘
      │                                  │
      │                                  ▼
      │                         ViewsByDate / ViewsByPathByDate / …
      │                                  │
      ▼                                  ▼
 stats/* (read API) ◄────────── aggregated containers
```

### Ingestion

1. **`event`** (HTTP GET) — Returns a 1×1 tracking GIF; enqueues a `RequestRecord` unless the request is a known crawler (helper list) or localhost
2. **`ProcessEvent`** (queue) — GeoIP (MaxMind), UA parsing, referrer simplification; writes to **ViewEvents**

### Aggregation (scheduled)

| Function | Schedule (UTC) | Output container |
|----------|----------------|------------------|
| `ComputeViewsByDay` | 00:30 daily | `ViewsByDate` |
| `ComputeViewsByPathByDay` | 04:00 daily | `ViewsByPathByDate` |
| `ComputeViewsByReferrerByDay` | 04:30 daily | `ViewsByReferrerByDate` |
| `ComputeViewsByCountryByDay` | 04:45 daily | `ViewsByCountryByDate` |
| `SendDailySummary` | 05:00 daily | Email report (reads `ViewsByDate`) |

Timers only recompute a **recent window** (last ~2–8 days depending on job). Historical gaps require [backfill](#backfill).

Spider traffic is excluded from aggregates (`isSpider = false` in Cosmos queries). Additional bot UA strings are filtered at ingest in `FunctionsHelpers.RequestIsCrawler`.

## Stats read API

All endpoints require **`X-Analytics-Secret`** (see [Authentication](AUTHENTICATION.md)).

| Endpoint | Description |
|----------|-------------|
| `GET /api/stats/timeseries` | Daily view totals (`grain=day`) |
| `GET /api/stats/top-pages` | Top pages by views |
| `GET /api/stats/referrers` | Top referrers |
| `GET /api/stats/countries` | Top countries |
| `GET /api/stats/segments` | New vs returning, JS/no-JS, browsers, devices |
| `GET` or `POST /api/stats/backfill` | Rebuild aggregates for a date range |

Common query parameters:

- `start`, `end` — dates as `YYYY-MM-DD` (UTC day boundaries in storage use `yyyyMMdd`)
- `limit` — max rows for ranked lists
- `types` — backfill only: `day,path,referrer,country` (comma-separated)

Example:

```bash
curl -H "X-Analytics-Secret: $SECRET" \
  "https://functions.duncanmackenzie.net/api/stats/top-pages?start=2026-05-01&end=2026-05-31&limit=20"
```

## Tracking pixel

Embed on pages (function key required):

```html
<img src="https://functions.duncanmackenzie.net/api/event?code=KEY&page=/path&title=Title" alt="" />
```

Optional query params: `referrer`, `js_enabled` (presence flag). The function sets `visit` / `visitor` cookies for session and new-vs-returning detection.

**Auth:** [Function key](AUTHENTICATION.md#function-key-azure-authorizationlevelfunction) — not the analytics dashboard secret.

## Backfill

Rebuild aggregate tables when timer jobs missed days or after schema/logic changes.

### HTTP API

```bash
curl -H "X-Analytics-Secret: $SECRET" \
  "https://functions.duncanmackenzie.net/api/stats/backfill?start=2026-05-25&end=2026-05-28&types=day,path,referrer,country"
```

Processes one calendar day at a time internally. Path backfills can take several minutes on high-traffic days.

### Shell script

[`scripts/backfill-analytics.sh`](../scripts/backfill-analytics.sh) walks a date range day-by-day:

```bash
# Full history
./scripts/backfill-analytics.sh 2024-01-01 2026-05-28

# Last 7 days (inclusive)
./scripts/backfill-analytics.sh --days 7

# Re-run even if data exists
./scripts/backfill-analytics.sh --force 2026-05-25 2026-05-28
```

By default the script **skips days/types that already have data** (checked via the stats API). Use `--force` to rebuild regardless.

Environment variables:

| Variable | Default | Purpose |
|----------|---------|---------|
| `ANALYTICS_DASHBOARD_SECRET` | *(from `az` if unset)* | Secret header value |
| `ANALYTICS_HOST` | `functions.duncanmackenzie.net` | API hostname |
| `ANALYTICS_TYPES` | all four types | Comma-separated backfill types |
| `CURL_TIMEOUT_SEC` | `600` | Per-day HTTP timeout |

## App settings

| Setting | Purpose |
|---------|---------|
| `AZURE_STORAGE_CONNECTION_STRING` | Analytics event queue |
| `CosmosEndpointUri` | Analytics database |
| `CosmosPrimaryKey` | Analytics database |
| `MaxMindAccountID` | GeoIP lookup in `ProcessEvent` |
| `MaxMindLicenseKey` | GeoIP lookup |
| `AnalyticsDashboardSecret` | Stats/backfill API auth |
| `EmailServiceConnectionString` | Daily summary email |
| `SendAnalyticsReportsTo` | Summary recipient address |
| `SendAnalyticsReportsName` | Summary recipient display name |

## Cosmos layout

Database: **`Analytics`**

| Container | Partition key | Contents |
|-----------|---------------|----------|
| `ViewEvents` | `/day` | Raw `RequestRecord` events |
| `ViewsByDate` | `/dateType` | Daily totals |
| `ViewsByPathByDate` | `/dateType` | Views per page per day |
| `ViewsByReferrerByDate` | `/dateType` | Views per referrer per day |
| `ViewsByCountryByDate` | `/dateType` | Views per country per day |

## Telemetry

Structured log lines (Application Insights):

- `AnalyticsCrawlerFilteredByHelper` — ingest blocked by UA helper list
- `AnalyticsCrawlerDetectedByParser` — UAParser flagged spider in queue processor
- `AnalyticsEventIgnoredLocalRequest` — localhost referrers skipped

## Local development

```bash
func start --dotnet-isolated
# Pixel: http://localhost:7071/api/event?code=<key>&page=/test
# Stats: curl -H "X-Analytics-Secret: …" http://localhost:7071/api/stats/timeseries?start=…
```

Ensure `AnalyticsDashboardSecret` is set in `local.settings.json` for stats endpoints. Copy from [`local.settings.json.example`](../local.settings.json.example) when setting up a new machine.
