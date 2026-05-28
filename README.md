# PhotoWebhooks

Azure Functions (**.NET 8 isolated worker**) for two related but separate workloads:

1. **Photo sales** — Stripe checkout webhooks, order processing, and customer email delivery
2. **Analytics** — site tracking pixel, event ingestion, Cosmos aggregates, and a private stats API

Both live in one Function App (`ProcessPhotoPurchases`) and share storage/Cosmos configuration, but the code is split by file and documented separately:

- [Photo sales module](docs/PHOTO-SALES.md)
- [Analytics module](docs/ANALYTICS.md)

Background on [duncanmackenzie.net](https://www.duncanmackenzie.net/blog/):

- Photo sales: [Order fulfillment with Azure Functions and Stripe](https://www.duncanmackenzie.net/blog/order-fulfillment/) (series also covers [e-commerce setup](https://www.duncanmackenzie.net/blog/adding-e-commerce-to-my-galleries/) and [duplicate webhook handling](https://www.duncanmackenzie.net/blog/handling-duplicate-stripe-events/))
- Analytics: [Homegrown Analytics](https://www.duncanmackenzie.net/blog/homegrown-analytics/)

## Requirements

- .NET 8 SDK
- Azure Functions Core Tools v4 (`func`)
- Azure CLI (`az`) for deploy scripts and optional secret lookup
- Sibling repo: `../uap-csharp/UAParser` (User-Agent parsing for analytics)

## Local development

```bash
cp local.settings.json.example local.settings.json
# Fill in real values (file is gitignored)
func start --dotnet-isolated
```

`local.settings.json` is gitignored and must include `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` plus the app settings listed in the module docs.

## Deploy

```bash
./scripts/deploy-function.sh
```

Publishes to the `ProcessPhotoPurchases` Function App using `func azure functionapp publish … --dotnet-isolated`.

## Scripts

| Script | Purpose |
|--------|---------|
| [`scripts/deploy-function.sh`](scripts/deploy-function.sh) | Deploy the Function App to Azure |
| [`scripts/backfill-analytics.sh`](scripts/backfill-analytics.sh) | Rebuild analytics aggregate tables one day at a time |

See [Analytics module — Backfill](docs/ANALYTICS.md#backfill) for backfill script details.

## Authentication overview

HTTP-triggered functions use one of three patterns:

| Pattern | Used by | How callers authenticate |
|---------|---------|---------------------------|
| **Function key** | `event`, `CheckoutComplete` | Azure Functions key: `?code=…` query param or `x-functions-key` header |
| **Stripe webhook signature** | `CheckoutComplete` (in addition to function key if URL requires it) | Valid `Stripe-Signature` header verified with `WebhookSigningSecret` |
| **Analytics dashboard secret** | All `/api/stats/*` endpoints including backfill | Header `X-Analytics-Secret` (or query `?secret=`) matching `AnalyticsDashboardSecret` |

Queue- and timer-triggered functions are not called over HTTP; they use Azure Storage connection strings and the Functions host scheduler.

Details, endpoint lists, and security notes are in the module docs and [Authentication reference](docs/AUTHENTICATION.md).

## Project layout

```
PhotoWebhooks/
├── Functions.cs           # Photo sales HTTP/queue functions
├── OrderData.cs           # Photo order logging (Cosmos)
├── Messages.cs            # Photo email/queue message types
├── AnalyticsFunctions.cs  # Analytics HTTP/queue/timer functions
├── AnalyticsData.cs       # Analytics Cosmos queries and aggregates
├── FunctionsHelpers.cs    # Referrer simplification, bot detection
├── Charting.cs            # QuickChart URLs for daily summary email
├── Models.cs              # Shared DTOs (analytics + general)
├── Program.cs             # Isolated worker host + middleware
├── host.json
├── scripts/
│   ├── deploy-function.sh
│   └── backfill-analytics.sh
└── docs/
    ├── PHOTO-SALES.md
    ├── ANALYTICS.md
    └── AUTHENTICATION.md
```

## Tests

```bash
dotnet test
```

Unit tests live in `PhotoWebhooksTests/` (currently focused on `FunctionsHelpers`).
