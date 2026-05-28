# Photo sales module

Handles Stripe checkout completion, order fulfillment, and delivery of purchased images by email.

## Background reading

This repo implements the pipeline described in Duncan’s blog posts:

1. [Adding some e-commerce to my galleries](https://www.duncanmackenzie.net/blog/adding-e-commerce-to-my-galleries/) — Stripe Payment Links, products/prices, storing originals in blob storage
2. [Order fulfillment with Azure Functions and Stripe](https://www.duncanmackenzie.net/blog/order-fulfillment/) — three-function webhook → queue → SAS URL → email design (**primary reference for this code**)
3. [Handling duplicate Stripe events](https://www.duncanmackenzie.net/blog/handling-duplicate-stripe-events/) — Cosmos-backed idempotency in `OrderData`

Related gallery infrastructure: [Adding photo galleries to my site](https://www.duncanmackenzie.net/blog/adding-photo-galleries/)

## Source files

| File | Role |
|------|------|
| `Functions.cs` | `PhotoFunctions` — HTTP and queue entry points |
| `OrderData.cs` | Cosmos DB access for order deduplication / logging |
| `Messages.cs` | Queue message payloads for the photo pipeline |

Shared dependencies: `Models.cs`, Azure Storage queues/blobs, Azure Communication Email, Stripe.net, Cosmos (order log).

## Request flow

```
Stripe webhook                Azure Storage queues              Cosmos / Blob / Email
      │                                │                                │
      ▼                                ▼                                ▼
 CheckoutComplete ──► incoming-checkout-complete ──► ProcessOrder ──► blob SAS + send-image queue
                                                                               │
                                                                               ▼
                                                                          SendLink ──► email with link
```

1. **`CheckoutComplete`** (HTTP POST) — Stripe `checkout.session.completed` webhook; validates signature; enqueues session payload to `incoming-checkout-complete`
2. **`ProcessOrder`** (queue) — Loads order from Stripe session; writes Cosmos log; generates blob read SAS; enqueues email job on `send-image`
3. **`SendLink`** (queue) — Sends purchase link email via Azure Communication Services

## Functions

| Function | Type | Route / trigger | Auth |
|----------|------|-----------------|------|
| `CheckoutComplete` | HTTP POST | `/api/checkoutcomplete` | [Function key + Stripe signature](../docs/AUTHENTICATION.md) |
| `ProcessOrder` | Queue | `incoming-checkout-complete` | Storage connection string |
| `SendLink` | Queue | `send-image` | Storage connection string |

## App settings

| Setting | Purpose |
|---------|---------|
| `StripeKey` | Stripe API key for session retrieval |
| `WebhookSigningSecret` | Stripe webhook signing secret |
| `AZURE_STORAGE_CONNECTION_STRING` | Queues and blob access |
| `CosmosEndpointUri` | Order log database |
| `CosmosPrimaryKey` | Order log database |
| `EmailServiceConnectionString` | Azure Communication Email |

## Stripe configuration

1. Create a webhook in the Stripe dashboard pointing at your function URL (include function key if required):
   ```
   https://<host>/api/checkoutcomplete?code=<function-key>
   ```
2. Subscribe to **`checkout.session.completed`**
3. Copy the signing secret into `WebhookSigningSecret`

## Local testing

Use [Stripe CLI](https://stripe.com/docs/stripe-cli) to forward signed webhooks:

```bash
stripe listen --forward-to "http://localhost:7071/api/checkoutcomplete?code=<local-function-key>"
```

Use test mode keys in `local.settings.json` unless you intentionally exercise live mode.

## Operational notes

- **`OrderData`** maintains a Cosmos container (`FunctionLog`) for idempotency — duplicate checkout/event IDs are detected before side effects
- Queue clients use **Base64** message encoding
- Photo processing shares the same Function App and storage account as analytics but uses separate queues and Cosmos containers/databases from the analytics pipeline
