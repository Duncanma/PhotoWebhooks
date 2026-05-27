# PhotoWebhooks
My Azure Functions for handling the image gallery sales

## Homegrown Analytics Endpoints

The analytics module now exposes read endpoints for a private dashboard:

- `GET /api/stats/timeseries`
- `GET /api/stats/top-pages`
- `GET /api/stats/referrers`
- `GET /api/stats/countries`
- `GET /api/stats/segments`

All stats endpoints require header `X-Analytics-Secret` matching env var:

- `AnalyticsDashboardSecret`

Primary query params:

- `start` and `end` as `YYYY-MM-DD`
- `limit` for ranked lists
- `grain=day` for timeseries (currently day-only)

## Aggregation Jobs

Daily timer functions maintain aggregate containers:

- `ComputeViewsByDay`
- `ComputeViewsByPathByDay`
- `ComputeViewsByReferrerByDay`
- `ComputeViewsByCountryByDay`
