# Testing approach

## How to run
| What | Command |
|---|---|
| All tests | `dotnet test` |
| Coverage | `dotnet test --collect:"XPlat Code Coverage"` |
| **All quality gates** (format, build, tests, coverage ≥ 85 %, vulnerable packages) | Windows `quality.cmd` · Linux/macOS/CI `./scripts/quality-gates.sh` |
| Load test before/after | `scripts\load-test.ps1` or `./scripts/load-test.sh 15 64` |
| One load run against a running API | `dotnet run -c Release --project tests/UrlShortener.LoadTests -- --base-url http://localhost:5104` |

## Test pyramid (212 tests)
| Layer | Class | # | What it proves |
|---|---|---|---|
| Unit: domain | `ShortLinkTests` | 6 | Exclusive expiry, idempotent and race-free deactivate, 1 k concurrent clicks exact, out-of-order timestamps |
| Unit: security | `UrlValidatorTests` | 55 | Valid URLs; malformed/dangerous schemes; 30 private/loopback/encoded/IPv6/NAT64/6to4/suffix bypasses; boundaries just outside private ranges; length; self-loop; Linux file-path regression |
| Unit: security | `DomainDenylistScreenTests` | 7 | Domain + subdomain match only, empty list, hot reload |
| Unit: generator | `RandomShortCodeGeneratorTests` | 5 | Length/charset, 10 k unique, guards |
| Unit: storage | `InMemoryLinkRepositoryTests` | 4 | Duplicate rejected, **200-way race → one winner**, case sensitivity, update |
| Unit: storage | `InMemoryClickRepositoryTests` | 5 | Per-code isolation, UTC-day/referrer/UTM/device aggregates, bounded raw events, cardinality cap, 1 k concurrent writes |
| Unit: cache | `CachedLinkRepositoryTests` | 6 | Hits, TTL, link-expiry cap, invalidation, negative cache, **50-way stampede → 1 load** |
| Unit: service | `UrlShortenerServiceTests` | 33 | Collision retry/exhaustion, reserved generated codes, concurrent creates, aliases, expiry (past/max), unsafe URL stores nothing, denylist create + later-block, idempotency (replay/mismatch/release), resolve/peek/gone, input bounding |
| Unit: bug reproduction | `CollisionBugReproductionTests` | 2 | Old check-then-insert hands one code to 20 URLs (deterministic barrier); atomic `TryAdd` hands it to one |
| Unit: analytics | `LinkAnalyticsServiceTests` | 7 | Aggregation, top-N stability, empty stats, daily zero-fill, defaults, range validation |
| Unit: analytics | `ClickEventFactoryTests` | 26 | UA classifier table, UTM sanitising/truncation, referrer host only |
| Unit: pipeline | `ClickPipelineTests` | 7 | Drop-when-full never blocks, batch drain to storage, drain on graceful stop, failing batch isolated, one update per link per batch, readiness Degraded, inline mode |
| Unit: idempotency | `InMemoryIdempotencyStoreTests` | 5 | Lifecycle, abandon, TTL, 100-way concurrent begin, sweep |
| Unit: errors | `DomainExceptionFilterTests` | 11 | Every domain exception → its status code |
| Integration | `LinksApiTests` | 13 | 201 + Location, create → 302 → stats e2e, 400/404/409/410 problem+json, health + Swagger |
| Integration | `HardeningApiTests` | 14 | Idempotency over HTTP, denylist 400, expiry cap, body limit, UTM/device/daily e2e, HEAD not counted, deactivate beats cache, negative cache + later alias, `X-Request-ID` echo + problem `requestId`, unsafe id replaced, security headers, readiness |
| Integration | `RateLimitTests` | 1 | 429 problem + `Retry-After` on create; separate redirect budget |
| Integration | `StartupTests` | 5 | Invalid config stops startup, no Swagger in Production, host filtering, Inline mode same contract, latency + no-cache pipeline |
| Performance | `UrlShortener.LoadTests` | – | rps, p50/p95/p99, **lost-event check**; CI smoke with p95 budget |

**Unit vs integration:** unit tests construct real classes with in-memory stores, a `FakeTimeProvider` and scripted generators,
with no sleeps and no HTTP. Integration tests boot the real app (`WebApplicationFactory`) with production defaults (async analytics, cache on),
one isolated app per fixture, and poll eventually-consistent reads (`Eventually`) instead of sleeping.

## Coverage (`scripts/quality-gates.sh`, Release)
**Line 97.8 % · Branch 94.2 %** (gate: line ≥ 85 %). Remaining uncovered lines are mostly generated logging/regex code
and the defensive `_ =>` branch of the error mapping.

## Quality gate output (last run)
```
=== 2/5 format (dotnet format --verify-no-changes) ===        clean
=== 3/5 build (Release, nullable on, TreatWarningsAsErrors) === Build succeeded. 0 Warning(s) 0 Error(s)
=== 4/5 test + coverage ===  Passed! Failed: 0, Passed: 212, Skipped: 0, Total: 212
Line coverage: 97.8%  Branch coverage: 94.2%  (gate: line >= 85%)
=== 5/5 vulnerable packages === no vulnerable packages (see note)
```
Note: the AI's workspace has no nuget.org access, so step 5 ran against a local package mirror. Re-run `quality.cmd` (or let CI run) to
check against the live advisory feed.

## Determinism
The full suite was run repeatedly with no flakes. The collision reproduction uses a barrier rather than timing (it was flaky, 2/5, before that change).

## Known gaps
* No mutation testing (Stryker.NET would be the next step).
* No contract test against a committed OpenAPI file.
* Load test runs on one host; no distributed or soak test.
* Multi-node behaviour (cache invalidation, distributed rate limits) is reasoned about in ADRs, not tested.
