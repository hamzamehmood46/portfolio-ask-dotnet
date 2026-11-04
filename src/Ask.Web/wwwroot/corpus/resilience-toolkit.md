# Resilience Toolkit

[![Test and deploy](https://github.com/hamzamehmood46/resilience-toolkit-dotnet/actions/workflows/pages.yml/badge.svg)](https://github.com/hamzamehmood46/resilience-toolkit-dotnet/actions/workflows/pages.yml)

**Live playground: [hamzamehmood46.github.io/resilience-toolkit-dotnet](https://hamzamehmood46.github.io/resilience-toolkit-dotnet/)**. Break a fake service on purpose and watch the real library protect the caller, running in your browser in real time (Blazor WebAssembly).
 it: set the dependency to Down and send a request (three attempts with growing waits, then the circuit opens), send again (rejected instantly, the dependency is not called), set it back to Healthy and send after the break (one probe, circuit closes), then set it to Slow and press the burst button (timeouts, then the bulkhead turns callers away).
 small [Playground](src/ResilienceToolkit.Playground) project holds the page; the library itself has no UI code.
A small, dependency-free **.NET 8** library for calling unreliable services safely: **retry with exponential backoff and jitter, circuit breaker, timeout, and bulkhead**, composable into one pipeline. It's a from-scratch implementation of the patterns behind libraries like Polly, written to be easy to read and fully testable without ever sleeping.

## Usage

```csharp
var pipeline = ResiliencePipeline.Create()
    .AddRetry(o => o.MaxAttempts = 4)                       // outermost: retries failed attempts
    .AddCircuitBreaker(o => { o.FailureThreshold = 5;       // sees every attempt; opens when a dependency is down
                              o.BreakDuration = TimeSpan.FromSeconds(30); })
    .AddTimeout(TimeSpan.FromSeconds(2))                    // bounds each individual attempt
    .Build();

var response = await pipeline.ExecuteAsync(ct => httpClient.GetStringAsync(url, ct), cancellationToken);
```

The first strategy added is the outermost. This order matters: with retry on the outside, the breaker counts every attempt, so it can open *in the middle of* a retry sequence and stop hammering a dead dependency.

## The strategies

| Strategy | What it does | Design notes |
|---|---|---|
| **Retry** | Re-runs failed attempts with exponential backoff | **Full jitter** spreads retries so clients that failed together don't all return at the same instant. Capped by `MaxDelay`. Caller cancellation, open circuits and full bulkheads are never retried. |
| **Circuit breaker** | Fails fast after N consecutive failures, then probes for recovery | `Closed -> Open -> HalfOpen -> Closed`. **Exactly one probe** is allowed in half-open state. A failed probe re-opens for a full break. Caller cancellation is not counted as a failure. Thread-safe. |
| **Timeout** | Cancels an attempt that runs too long | Throws a distinct `TimeoutRejectedException`, so a hung call can be retried or counted. It's distinguishable from the *caller* cancelling. |
| **Bulkhead** | Caps concurrent calls (plus a bounded wait queue) | Stops one slow dependency from exhausting every thread or connection. Releases its slot even when the call throws. |

## Testable by design

Time and randomness are injected, so the whole suite runs in milliseconds with no real delays:

- `TimeProvider` (.NET 8) drives the breaker and timeout, tested with `FakeTimeProvider`.
- `RetryOptions.Delay` and `RetryOptions.Random` are replaceable, so backoff values and jitter are asserted exactly.

## Tests

```bash
dotnet test
```

23 tests, including: backoff sequence `[100ms, 200ms]`, delay capping, jitter bounds, non-retryable exceptions, cancellation, breaker open/half-open/closed transitions on a fake clock, single-probe enforcement, timeout vs. caller cancellation, bulkhead rejection when the queue is full, and pipeline composition (a retry loop that stops when the circuit opens; a hung attempt that a timeout converts into a successful retry).

## Project layout


```
src/ResilienceToolkit/              IResilienceStrategy + Retry, CircuitBreaker, Timeout, Bulkhead, ResiliencePipeline
src/ResilienceToolkit.Playground/   Blazor WebAssembly page that drives the real library
tests/                   xUnit tests
```

## Deploying

Every push to `main` runs the tests and, if they pass, publishes the playground to GitHub Pages (see `.github/workflows/pages.yml`).

## License

MIT
