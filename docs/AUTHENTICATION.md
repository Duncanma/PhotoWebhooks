# Authentication reference

All HTTP routes are prefixed with `/api` by default (`host.json` does not override `routePrefix`).

Production host is typically `functions.duncanmackenzie.net` (custom domain on the `ProcessPhotoPurchases` Function App).

## Summary table

| Function | Method | Route | Azure auth level | Application auth |
|----------|--------|-------|------------------|------------------|
| `CheckoutComplete` | POST | `/api/checkoutcomplete` | Function | Stripe webhook signature (`Stripe-Signature` + `WebhookSigningSecret`) |
| `event` | GET | `/api/event` | Function | — |
| `StatsTimeSeries` | GET | `/api/stats/timeseries` | Anonymous | `X-Analytics-Secret` |
| `StatsTopPages` | GET | `/api/stats/top-pages` | Anonymous | `X-Analytics-Secret` |
| `StatsReferrers` | GET | `/api/stats/referrers` | Anonymous | `X-Analytics-Secret` |
| `StatsCountries` | GET | `/api/stats/countries` | Anonymous | `X-Analytics-Secret` |
| `StatsSegments` | GET | `/api/stats/segments` | Anonymous | `X-Analytics-Secret` |
| `BackfillAnalytics` | GET, POST | `/api/stats/backfill` | Anonymous | `X-Analytics-Secret` |

Non-HTTP triggers (`ProcessOrder`, `SendLink`, `ProcessEvent`, timer jobs) have no HTTP authentication surface.

## Function key (Azure `AuthorizationLevel.Function`)

Used by:

- **`CheckoutComplete`** — Stripe posts checkout session events here
- **`event`** — tracking pixel loaded by the site (`<img src="…/api/event?code=…">`)

Obtain keys from the Azure portal (**Function App → Functions → {name} → Function keys**) or:

```bash
az functionapp function keys list \
  --resource-group Blog \
  --name ProcessPhotoPurchases \
  --function-name event \
  --query default -o tsv
```

Pass the key as:

- Query string: `?code=<function-key>`
- Header: `x-functions-key: <function-key>`

If the key is missing or wrong, Azure returns **401 Unauthorized** before your function code runs.

## Stripe webhook signature

**`CheckoutComplete`** verifies every request body with Stripe’s signing secret:

1. Read raw request body
2. Read `Stripe-Signature` header
3. Validate via `EventUtility.ConstructEvent(…, WebhookSigningSecret)`

Invalid signatures fail inside the function (bad request / error response). Configure the same signing secret in Stripe’s webhook dashboard and in app setting `WebhookSigningSecret`.

Stripe also needs the function key in the webhook URL if the endpoint uses `AuthorizationLevel.Function`:

```
https://functions.example.net/api/checkoutcomplete?code=<function-key>
```

## Analytics dashboard secret

Stats and backfill endpoints use **`AuthorizationLevel.Anonymous`** at the Azure layer so browsers and scripts are not blocked by function-key requirements. Access control is enforced in code via `TryAuthorizeStatsRequest`:

1. Read expected value from app setting **`AnalyticsDashboardSecret`**
2. Compare to caller-provided secret from:
   - Header: **`X-Analytics-Secret`**
   - Query (fallback): **`?secret=`**

Responses:

- **401** — missing or wrong secret
- **500** — `AnalyticsDashboardSecret` not configured on the app

Example:

```bash
curl -H "X-Analytics-Secret: $SECRET" \
  "https://functions.duncanmackenzie.net/api/stats/timeseries?start=2026-05-01&end=2026-05-31"
```

### Security note

Do not embed `AnalyticsDashboardSecret` in public front-end JavaScript. Call stats APIs from a trusted backend or local tooling. The tracking pixel (`event`) correctly uses a function key because it is not a secret API—it's a public embed with limited side effects (returns a 1×1 GIF).

## Queue and timer triggers

These functions are invoked by the Azure Functions runtime, not by external HTTP clients:

| Function | Trigger | Connection / auth |
|----------|---------|-------------------|
| `ProcessOrder` | Queue `incoming-checkout-complete` | `AZURE_STORAGE_CONNECTION_STRING` |
| `SendLink` | Queue `send-image` | `AZURE_STORAGE_CONNECTION_STRING` |
| `ProcessEvent` | Queue `analytics-event` | `AZURE_STORAGE_CONNECTION_STRING` |
| `ComputeViewsByDay` | Timer (daily) | Host scheduler |
| `ComputeViewsByPathByDay` | Timer (daily) | Host scheduler |
| `ComputeViewsByReferrerByDay` | Timer (daily) | Host scheduler |
| `ComputeViewsByCountryByDay` | Timer (daily) | Host scheduler |
| `SendDailySummary` | Timer (daily) | Host scheduler + email/Cosmos settings |

## Reserved routes

Do **not** use HTTP routes starting with `admin/` — Azure Functions reserves that prefix for the host admin API and the route will return **404**. Analytics backfill lives at **`/api/stats/backfill`**.
