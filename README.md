# URL Shortener API (.NET 8): AI-assisted, engineer-led

REST Web API that creates short links, redirects them safely and fast, and reports click analytics, built phase by phase with
AI assistance under engineer control (see [`URL_Shortener_AI_Prompt.md`](URL_Shortener_AI_Prompt.md) and [`docs/AI_LOG.md`](docs/AI_LOG.md)).

**Simple mode:** thread-safe in-memory storage behind async `ILinkRepository` / `IClickRepository`, so it runs with no database.
Data resets on restart. Every component is injected against an interface, so SQL Server/Postgres/Redis can be swapped in without touching
controllers or services.

| | |
|---|---|
| Redirect p95 (2 ms store, 64 clients) | **3.7 ms** (was 14.9 ms), 30 k rps, 0 lost click events ([load test](docs/SCENARIOS/B-brownfield.md#step-6-load-test-and-the-bug-it-found)) |
| Tests | **212** unit + integration, line coverage **97.8 %**, branch **94.2 %** |
| Build | nullable on, warnings as errors, `dotnet format` clean, CI on every push |

## Prerequisites
.NET 8 SDK (`dotnet --list-sdks` shows 8.0.x).

## Build, test, run
One step on Windows: double-click **`run.cmd`** (build → test → run → opens Swagger; logs in `logs\`).

Or manually:
```powershell
dotnet build                     # warnings are treated as errors
dotnet test                      # 212 unit + integration tests
dotnet run --project src/UrlShortener.Api --launch-profile http
```
Then open **http://localhost:5104/swagger** (Swagger is enabled in Development only).

## Quality gates
| Gate | Command |
|---|---|
| **All gates in one go** | Windows `quality.cmd` · Linux/macOS `./scripts/quality-gates.sh` |
| Format | `dotnet format --verify-no-changes` |
| Build (warnings as errors) | `dotnet build -c Release` |
| Tests + coverage (≥ 85 % line) | `dotnet test --collect:"XPlat Code Coverage"` |
| Vulnerable packages | `dotnet list package --vulnerable --include-transitive` |
| Load test before/after | `powershell -File scripts\load-test.ps1` · `./scripts/load-test.sh 15 64` |

CI (`.github/workflows/ci.yml`) runs all gates plus a redirect load smoke test (p95 budget, zero lost events).

## API
| Method | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| POST | `/api/v1/links` | Create (optional `customAlias`, `expiresAt`; optional `Idempotency-Key` header) | 201 + `Location` | 400, 409, 422, 429 |
| GET | `/api/v1/links/{code}` | Metadata | 200 | 404 |
| GET | `/api/v1/links/{code}/stats?top=5` | Total, by day, top referrers / UTM sources / mediums / campaigns, devices | 200 | 404 |
| GET | `/api/v1/links/{code}/stats/daily?from=&to=` | Zero-filled daily series (default last 30 days) | 200 | 400, 404 |
| DELETE | `/api/v1/links/{code}` | Soft-deactivate (idempotent) | 204 | 404 |
| GET | `/{code}` (+ optional `utm_source`, `utm_medium`, `utm_campaign`) | Redirect + record click | 302 | 404, 410, 429 |
| HEAD | `/{code}` | Redirect check, **not** counted | 302 | 404, 410 |
| GET | `/healthz` (`/health` alias), `/readyz` | Liveness, readiness (click-queue headroom) | 200 | 503 |

Errors are RFC 7807 `application/problem+json` with `traceId` and `requestId`. Send `X-Request-ID` to correlate; it is echoed and logged.
Click stats are eventually consistent (recorded asynchronously, typically within milliseconds).

> Swagger UI follows redirects, so "Try it out" on `GET /{code}` shows the target page's response.
> To see the raw 302, use a browser tab or `curl -i http://localhost:5104/{code}`.

```powershell
curl -X POST http://localhost:5104/api/v1/links -H "Content-Type: application/json" -H "Idempotency-Key: 7d0c2f" `
     -d '{"url":"https://learn.microsoft.com/dotnet","customAlias":"dotnet8"}'
curl -i "http://localhost:5104/dotnet8?utm_source=newsletter&utm_medium=email&utm_campaign=launch"
curl http://localhost:5104/api/v1/links/dotnet8/stats
curl "http://localhost:5104/api/v1/links/dotnet8/stats/daily?from=2026-10-01"
curl -X DELETE http://localhost:5104/api/v1/links/dotnet8
```

## Configuration (`appsettings.json`, override with env vars such as `Cache__Enabled=false`)
| Key | Default | Meaning |
|---|---|---|
| `Shortener:BaseUrl` | `http://localhost:5104` | Prefix for returned `shortUrl`; links to this host are rejected (loops) |
| `Shortener:CodeLength` | 7 | Generated code length (Base62, 5-16) |
| `Shortener:MaxCodeGenerationAttempts` | 5 | Retries on collision |
| `Shortener:MaxUrlLength` | 2048 | Max target URL length |
| `Shortener:MaxExpiryDays` | 3650 | Max `expiresAt` distance |
| `Shortener:IdempotencyKeyTtlHours` | 24 | How long an `Idempotency-Key` is remembered |
| `RateLimiting:CreatePermitLimit` / `CreateWindowSeconds` | 20 / 60 | Creates per client IP per fixed window |
| `RateLimiting:RedirectPermitLimit` / `RedirectWindowSeconds` | 600 / 60 | Redirects per client IP per sliding window (0 = off) |
| `Analytics:Mode` | `Async` | `Async` (queue + background writer) or `Inline` (synchronous) |
| `Analytics:QueueCapacity` / `MaxBatchSize` | 50000 / 1000 | Bounded queue size; events per storage round-trip |
| `Analytics:MaxRecentClicksPerLink` / `MaxDistinctValuesPerDimension` | 1000 / 500 | Memory bounds for raw events and breakdowns |
| `Analytics:MaxDailyRangeDays` | 366 | Largest `/stats/daily` range |
| `Cache:Enabled` / `LinkTtlSeconds` / `NotFoundTtlSeconds` / `SizeLimit` | true / 60 / 5 / 100000 | Cache-aside for redirects |
| `Safety:BlockedDomains` | `[]` (Dev: `malware.test`, `phishing.test`) | Denylisted domains incl. subdomains; hot-reloaded |
| `ForwardedHeaders:KnownProxies` | `[]` | Proxies trusted for `X-Forwarded-For` |
| `Swagger:Enabled` | Dev only | Expose Swagger outside Development |
| `AllowedHosts` | `localhost;127.0.0.1` | Host-header filter; set your domain in production |
| `Storage:SimulatedLatencyMs` | 0 | **Dev/perf only:** emulate a remote store |

Options are validated at startup: a bad value stops the app instead of failing later.

## Layout
```
src/UrlShortener.Api/
  Controllers/     LinksController, RedirectController (thin; HTTP only)
  Services/        UrlShortenerService, AliasPolicy, UrlValidator, DomainDenylistScreen, idempotency, code generator
    Analytics/     click recorder (Channel), background processor, ingestor, analytics query service, UA classifier
  Repositories/    ILinkRepository, IClickRepository, in-memory stores, CachedLinkRepository, latency decorators
  Domain/          ShortLink, ClickEvent, ClickSummary, domain exceptions
  Contracts/       request/response DTOs
  Infrastructure/  composition root, options, rate limiting, request-context middleware, exception filter
tests/UrlShortener.Tests/      Unit/, Integration/, TestDoubles/
tests/UrlShortener.LoadTests/  dependency-free redirect load generator (+ tests/load/redirect.k6.js)
scripts/                       quality gates, load test (bash + PowerShell)
docs/                          requirements, architecture, ADRs, tasks, scenarios A/B/C, risks, testing, AI log, summary
```

## Documentation map
| Assignment deliverable | Where |
|---|---|
| Requirement understanding | [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) |
| Architecture overview (components, control flow, key decisions) | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/DECISIONS/](docs/DECISIONS) |
| Task decomposition | [docs/TASKS.md](docs/TASKS.md) |
| Three scenarios (greenfield, brownfield, ambiguous) | [docs/SCENARIOS.md](docs/SCENARIOS.md) |
| Testing approach | [docs/TESTING.md](docs/TESTING.md) |
| Risks, trade-offs, limitations | [docs/RISKS.md](docs/RISKS.md), [docs/FINAL_SUMMARY.md](docs/FINAL_SUMMARY.md) |
| AI traceability (generated / edited / rejected) | [docs/AI_LOG.md](docs/AI_LOG.md) |

For questions about this project or the AI-assisted engineering methodology demonstrated, email karthikeyankrishnan110884@gmail.com.

Screenshots of the API running locally :

<img width="1681" height="658" alt="image" src="https://github.com/user-attachments/assets/4788c4b9-6d82-42a1-ab07-b4dd49d31add" />
<img width="1850" height="1076" alt="image" src="https://github.com/user-attachments/assets/cd88a05a-8e4a-47c5-a2d5-fd1fa526fb27" />
<img width="1806" height="982" alt="image" src="https://github.com/user-attachments/assets/43db9754-dfc6-466f-b58b-378288ff3df2" />
<img width="1805" height="997" alt="image" src="https://github.com/user-attachments/assets/d88373a8-7b95-4230-a9c3-53cf13bc3ae7" />


