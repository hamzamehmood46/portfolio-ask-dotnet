# Claims API

[![CI](https://github.com/hamzamehmood46/claims-api-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/hamzamehmood46/claims-api-dotnet/actions/workflows/ci.yml)

**Live demo: [claims-api-dotnet.onrender.com/swagger](https://claims-api-dotnet.onrender.com/swagger)** (Swagger UI). Get a token from POST /api/auth/token with `biller` / `demo-biller-pass` (or `reviewer` / `demo-reviewer-pass`), press Authorize, and walk a claim through its workflow. These are public demo accounts. Hosted on Render's free tier, so the first load after idle can take about a minute; demo data resets on restart.

A medical-billing REST API in **.NET 8 / ASP.NET Core** built with **Clean Architecture**. It manages patients and insurance claims through a strict approval workflow, with **JWT authentication and role-based authorization**.

## The domain

```
   Draft ──submit──► Submitted ──approve──► Approved ──pay──► Paid
   (Biller)          (Biller)      (Reviewer)                 (Biller)
                          └──deny(reason)──► Denied
                                (Reviewer)
```

The state machine lives in the domain model (`Claim`), so no controller or service can put a claim into an invalid state. Illegal transitions return `409 Conflict`; bad input returns `400`; unknown IDs return `404` - all as RFC 7807 problem details from one exception handler.

## Architecture

```
src/
  ClaimsApi.Domain          Entities + business rules. No dependencies.
  ClaimsApi.Application     Use-case services, DTOs, IClaimsDbContext abstraction
  ClaimsApi.Infrastructure  EF Core (SQLite) implementation and DI wiring
  ClaimsApi.Api             Controllers, JWT auth, Swagger, exception handling
tests/
  ClaimsApi.Tests           xUnit: domain rules + end-to-end API tests (WebApplicationFactory)
```

Dependencies point inward: Api -> Application -> Domain, with Infrastructure implementing Application's abstractions.

## Security

| Concern | Approach |
|---|---|
| Authentication | JWT bearer tokens from `POST /api/auth/token` (issuer, audience, signature and lifetime validated) |
| Authorization | Roles: **Biller** creates patients/claims, submits, marks paid. **Reviewer** approves/denies. Both can read. |
| Passwords | PBKDF2-SHA256 (100k iterations, per-user salt), constant-time comparison; unknown users take comparable time |
| Secrets | No signing key in source. `Jwt:Key` (32+ chars) must be supplied via config/env or the app refuses to start |
| Error handling | Internals never leak: unexpected errors return a generic 500 and are logged server-side |
| Abuse limits | Page size is capped at 100 |

> The demo users in `appsettings.Development.json` exist only so the sample runs locally. Replace `UserStore` with ASP.NET Core Identity or your IdP for real use.

## Run it

```bash
dotnet run --project src/ClaimsApi.Api --environment Development
```

Open `/swagger`, call **POST /api/auth/token** with the demo user `biller` (password `demo-biller-pass`) or `reviewer` (`demo-reviewer-pass`), then click **Authorize** and paste the token.

Outside Development, provide your own settings:

```bash
export Jwt__Key="<a random string of at least 32 characters>"
export Auth__Users__0__Username=biller Auth__Users__0__Password=... Auth__Users__0__Role=Biller
```

### Endpoints

| Method | Route | Role |
|---|---|---|
| POST | `/api/auth/token` | anonymous |
| GET | `/api/patients?q=&page=&pageSize=` | any |
| GET | `/api/patients/{id}` | any |
| POST | `/api/patients` | Biller |
| GET | `/api/claims?status=&patientId=&page=&pageSize=` | any |
| GET | `/api/claims/{id}` | any |
| POST | `/api/claims` | Biller |
| POST | `/api/claims/{id}/submit` | Biller |
| POST | `/api/claims/{id}/approve` | Reviewer |
| POST | `/api/claims/{id}/deny` | Reviewer |
| POST | `/api/claims/{id}/pay` | Biller |

## Tests

```bash
dotnet test
```

22 tests: every legal and illegal state transition, validation rules, auth failures, role enforcement, the full multi-role lifecycle, filtering, paging, page-size caps, CORS, and UTC timestamps.

There's an Angular front end for this API: [claims-portal-angular](https://github.com/hamzamehmood46/claims-portal-angular).

## Production notes

- Swap SQLite for SQL Server/PostgreSQL in `ClaimsDbContext` registration and add EF Core migrations.
- Add optimistic concurrency (row version) on `Claim` for concurrent reviewers.
- Add audit logging of status transitions.

## License

MIT

