# OrderFlow

[![CI](https://github.com/hamzamehmood46/orderflow-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/hamzamehmood46/orderflow-dotnet/actions/workflows/ci.yml)

**Live demo: [orderflow-dotnet.onrender.com/swagger](https://orderflow-dotnet.onrender.com/swagger)** (Swagger UI). Try POST /api/orders with an Idempotency-Key header and send it twice: you get the same order back. Hosted on Render's free tier, so the first load after idle can take about a minute; demo data resets on restart.

An event-driven order-processing service in **.NET 8 / ASP.NET Core**. It demonstrates the reliability patterns that keep integration platforms healthy in production: **idempotent APIs, asynchronous processing, exponential-backoff retries, and dead-letter handling.**

```
POST /api/orders ──► validate ──► persist (EF Core) ──► 202 Accepted
   Idempotency-Key                        │
                                          ▼ OrderPlaced event
                                   ┌──────────────┐
                                   │ message bus  │  (in-process channel; swappable
                                   └──────┬───────┘   for Azure Service Bus)
                                          ▼
                                   OrderWorker (BackgroundService)
                                   retry 250ms → 500ms → 1s …
                                     │                    │
                              success ▼                   ▼ retries exhausted
                          Order = Completed        Order = Failed + dead-letter entry
```

## Why it's built this way

| Concern | Approach |
|---|---|
| **Duplicate requests** | `Idempotency-Key` header + unique index. Replays return the original order (`200`); concurrent requests with one key produce exactly one order (tested with 8 parallel calls). |
| **Duplicate deliveries** | The handler is idempotent: an order that is no longer `Pending` is skipped, so at-least-once delivery is safe. |
| **Transient failures** | Exponential-backoff retries (`Worker:MaxAttempts`, `Worker:BaseDelayMs`). |
| **Poison messages** | After the final attempt the order is marked `Failed` and recorded at `GET /api/dead-letters` instead of blocking the queue. |
| **Broker lock-in** | `IMessageBus` / `IPaymentGateway` abstractions live in `Core`; infrastructure is registered in one place. |
| **Operability** | Structured logging on every attempt, `/health` endpoint backed by a DB check, Swagger UI. |

## Project layout

```
src/
  OrderFlow.Core            Domain model (Order) and messaging abstractions - no dependencies
  OrderFlow.Infrastructure  EF Core (SQLite), channel bus, worker, simulated payment gateway
  OrderFlow.Api             ASP.NET Core controllers, validation, Swagger, health checks
tests/
  OrderFlow.Tests           xUnit: domain unit tests + end-to-end tests via WebApplicationFactory
```

## Run it

```bash
dotnet run --project src/OrderFlow.Api
```

Open the Swagger UI at the URL printed on startup (`/swagger`). Or with curl:

```bash
curl -X POST http://localhost:5110/api/orders \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-001" \
  -d '{"customerId":"cust-1","lines":[{"sku":"SKU-1","quantity":2,"unitPrice":19.99}]}'
```

Poll `GET /api/orders/{id}` and watch it move from `Pending` to `Completed`.

### Try the failure modes

The simulated payment gateway has two special SKUs so you can see the resilience behaviour without any external service:

| SKU | Behaviour |
|---|---|
| `FLAKY` | Fails the first two attempts, then succeeds - the order finishes `Completed` with `attempts: 3` |
| `POISON` | Always fails - the order ends `Failed` and appears in `GET /api/dead-letters` |

### Docker

```bash
docker build -t orderflow .
docker run -p 8080:8080 orderflow
```

## Tests

```bash
dotnet test
```

Covers domain rules, the happy path, retry-until-success, dead-lettering after max attempts, idempotent replays, concurrent duplicate requests, validation, 404s and health.

## Production notes

This is a reference implementation. To take it further:

- Replace `ChannelMessageBus` with Azure Service Bus (the `IMessageBus` seam is already there) and move the dead-letter store to the broker's DLQ.
- Switch SQLite to SQL Server or PostgreSQL by changing the provider in `DependencyInjection.cs`.
- Adopt the outbox pattern so the DB write and the publish are atomic.
- Add OpenTelemetry tracing across the API and worker.

## License

MIT

