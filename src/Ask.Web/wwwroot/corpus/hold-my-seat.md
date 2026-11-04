# Hold My Seat: two buyers, one seat, no double-booking

[![Test and deploy](https://github.com/hamzamehmood46/seat-booking-dotnet/actions/workflows/pages.yml/badge.svg)](https://github.com/hamzamehmood46/seat-booking-dotnet/actions/workflows/pages.yml)

**Live demo: [hamzamehmood46.github.io/seat-booking-dotnet](https://hamzamehmood46.github.io/seat-booking-dotnet/)**

A seat-booking system built around the hard part of ticketing: what happens when two people go for the same seat at the same moment. Seats can be held for two minutes, holds expire on their own, purchases can be retried without charging twice, and no seat is ever owned by two people.

The booking rules are plain C# with no UI and no hidden clock, so they can be tested hard. Blazor WebAssembly runs the same code in your browser, so the demo needs no server.

## Try it (30 seconds)

1. As **Ava**, tap seat **A5** and press **Hold seats**. A two-minute countdown starts.
2. Switch to **Ben** and tap A5. It is taken. Ben holds A6 instead.
3. As Ava, tick **Use a card that gets declined** and pay: it fails, but the seats stay held so another card can be tried. Untick it and pay.
4. Press **Skip ahead 1 minute** twice. Ben's hold runs out and his seat is free again.
5. Press **Stampede**: 50 bots all grab the same four seats at once. One wins and 49 are turned away.

## The rules

| Rule | Behavior |
|---|---|
| Holds | A buyer reserves up to 6 seats for 2 minutes. It is all or nothing: if any seat was just taken, none are reserved and the buyer's existing hold is kept. |
| One hold per buyer | Asking again replaces the earlier hold. The buyer's own held seats count as free for the new request. |
| Expiry | There is no background timer. Every operation first frees any hold past its deadline, using an injected clock, so behavior is exact and repeatable. A hold is gone at exactly its deadline. |
| Purchase | Needs an unexpired hold of your own. A declined card keeps the hold so you can try another card. |
| Idempotency | Every purchase carries a key. Pressing Pay twice, or retrying after a dropped connection, returns the original order: one order, one charge, even if the retry arrives after the hold deadline. |
| Best seats together | The best run of N seats side by side: nearest the front, then nearest the middle of the row. A buyer's own held seats count as free. |
| Pricing | Price by row plus a per-ticket fee, in whole cents. |

## How the "no double-booking" guarantee works

Every state change happens under one lock, so concurrent buyers are serialized and exactly one of them can win any given seat. The tests do not just trust that: they fight over seats with 32 real threads.

## How it is built

```
src/Seats.Engine   C#: the venue, holds, purchases and best-seat search. No UI, no hidden clock.
src/Seats.Web      Blazor WebAssembly front end (runs the same engine in the browser)
tests/             xUnit tests for the engine
```

## Tests

```bash
dotnet test
```

42 tests, including:

- holds are all or nothing, replace each other correctly and are capped at six seats;
- expiry at exactly the deadline, an expired hold cannot be bought, and seats free up with no timer running;
- a declined card keeps the hold, a repeated purchase returns the same order, and a retry after the deadline still returns it;
- best-seat search, including skipping taken seats and moving back a row;
- **contention with real threads:** 200 buyers grabbing one seat produce exactly one winner; 400 overlapping multi-seat requests never leave a seat in two holds; 50 simultaneous taps on Pay create one order and one charge; 100 buyers racing for 10 seats sell exactly 10;
- a seeded random run of 4,000 holds, releases, purchases and clock jumps that re-checks after every step that no seat is held and sold at once, no seat is in two holds, a sold seat never comes back, and takings equal the sum of the orders.

I also broke the rules on purpose, five ways, to check the tests notice: removing the lock, skipping the expiry check on purchase, letting sold seats be held, disabling the idempotent replay, and weakening the all-or-nothing check. Each made tests fail.

I drove the deployed build in a real browser as well: a conflict between two buyers, a declined card, hold expiry, and the stampede.

## Run it locally

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Seats.Web
```

## Scope, honestly

- One venue held in memory. There is no database, no accounts and no real payment provider (a card token of `tok_decline` is declined, any other `tok_` token is approved).
- The "two buyers" are two demo identities on one page, standing in for two browser windows. In the browser everything runs on a single thread, so the stampede shows the rules (the first request wins), while the xUnit tests are what exercise real parallel threads.
- A hold cannot be extended, and there is no waiting list.

## Deploying

Every push to `main` runs the tests and, if they pass, publishes the site to GitHub Pages (see `.github/workflows/pages.yml`).

## Related projects

- [Till: offline-first point of sale](https://github.com/hamzamehmood46/point-of-sale-dotnet)
- [Monolith vs Microservices in .NET](https://github.com/hamzamehmood46/monolith-vs-microservices-dotnet)
- [Transactional Outbox and Idempotent Inbox](https://github.com/hamzamehmood46/outbox-pattern-dotnet)

## License

MIT
