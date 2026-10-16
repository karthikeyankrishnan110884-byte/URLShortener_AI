# Task list (Phase 1 output, executed in Phases 2-4)

Risk: L/M/H. Sign-off: Y = high-impact per master-prompt rule 7 (security, public contract, data flow, dependencies).
"Done" = acceptance tests green + all quality gates + AI_LOG entry.

## Scenario A: greenfield
| ID | Intent | Constraints | Acceptance criteria | Files | Depends | Risk | Sign-off |
|---|---|---|---|---|---|---|---|
| A1 | Solution skeleton | net8.0, nullable, warnings as errors | `dotnet build` clean | `Directory.Build.props`, `*.sln`, `*.csproj` | – | L | N |
| A2 | Domain entity + exceptions | Thread-safe counters, exclusive expiry | ShortLink tests (expiry boundary, idempotent deactivate, 1 k concurrent clicks) | `Domain/*` | A1 | L | N |
| A3 | Link repository | Atomic uniqueness | Duplicate rejected; 200-way race → 1 winner; case-sensitive | `Repositories/*` | A2 | M | Y |
| A4 | Code generator | Crypto RNG | Length/charset; 10 k unique | `Services/RandomShortCodeGenerator.cs` | A1 | L | N |
| A5 | URL validator | No DNS lookups | Scheme, length, credentials, private ranges incl. boundaries | `Services/UrlValidator.cs` | A1 | **H** | Y |
| A6 | Shortener service | Constructor DI, `TimeProvider` | Create/alias/expiry/resolve/deactivate; collision retry | `Services/UrlShortenerService.cs` | A2-A5 | M | N |
| A7 | Controllers + RFC 7807 + Swagger | Thin controllers | Integration test per status code | `Controllers/*`, `Infrastructure/DomainExceptionFilter.cs` | A6 | M | Y |
| A8 | Composition root + options | `ValidateOnStart` | Bad config stops startup | `Infrastructure/ServiceCollectionExtensions.cs` | A7 | L | N |
| A9 | Create rate limit | Built-in limiter | 429 after limit, redirects unaffected | same | A8 | M | N |

## Scenario B: brownfield
| ID | Intent | Constraints | Acceptance criteria | Files | Depends | Risk | Sign-off |
|---|---|---|---|---|---|---|---|
| B0 | Impact analysis | Before any code | Modules, APIs, data flows, migration, compatibility, rollback documented | `SCENARIOS/B-brownfield.md` | – | L | Y |
| B1 | Collision bug: failing test, then fix | Atomic insert, no lock | Concurrent-create test fails on check-then-insert, passes on `TryAdd` | repo + service + tests | B0 | M | Y |
| B2 | Async, split repositories | No behaviour change | All prior tests pass after mechanical async port | `Repositories/*`, services, tests | B1 | M | Y |
| B3 | Cache-aside decorator | DI-only wiring, no service changes | Hit/TTL/expiry cap/invalidation/negative/stampede tests | `Repositories/CachedLinkRepository.cs` | B2 | M | N |
| B4 | Analytics off hot path | Bounded, never blocks | Queue drop test, drain-on-stop, failing-write resilience, readiness | `Services/Analytics/*` | B2 | **H** | Y |
| B4b | Batch writes (found by B6) | One round-trip per batch | Load test: recorded == redirects | `ClickIngestor`, `ClickEventProcessor` | B4, B6 | M | Y |
| B5 | SRP refactor | Unchanged behaviour | `AliasPolicy`, `LinkAnalyticsService`, `ClickEventFactory` extracted; tests green | services | B2 | L | N |
| B6 | Load test before/after | No new dependencies | Table of rps/p50/p95/p99 + lost-event check | `tests/UrlShortener.LoadTests`, `scripts/load-test.*` | B3, B4 | L | N |
| B7 | Defect fixes from review | Each with a regression test | Linux file-URI, trailing-dot host, IPv6 zone id, deactivate race, unbounded clicks, expiry cap | various | B1 | **H** | Y |

## Scenario C: ambiguous ("safer links, better insights")
| ID | Intent | Constraints | Acceptance criteria | Files | Depends | Risk | Sign-off |
|---|---|---|---|---|---|---|---|
| C0 | Interpretation + PO questions | No code | Meanings, questions, defaults, scoped statement | `SCENARIOS/C-ambiguous.md` | – | L | Y |
| C1 | Destination denylist hook | Extension point, hot reload | Create 400; existing link 410 after block; subdomains | `Services/IDestinationScreen.cs`, `DomainDenylistScreen.cs` | C0 | M | Y |
| C2 | Hardened validator | No false positives on public ranges | 18 new bypass cases rejected, 7 boundary cases allowed | `UrlValidator.cs` | C0 | **H** | Y |
| C3 | Redirect rate limit | High default, tunable, 0 = off | 429 after budget, separate from create | `ServiceCollectionExtensions.cs` | C0 | M | N |
| C4 | Expiry cap | Configurable | > max → 400 | service, options | C0 | L | N |
| C5 | UTM capture | Sanitised, not forwarded | Tags recorded and aggregated | `ClickContext.cs`, `RedirectController.cs` | B4 | M | Y |
| C6 | Device summary | No dependency | Classifier table test | `UserAgentClassifier.cs` | B4 | L | N |
| C7 | Daily series endpoint | Zero-filled, bounded range | 400 on bad range | `LinkAnalyticsService.cs`, controller | B4 | L | Y |
| C8 | Idempotent create | Additive header | Replay/mismatch/in-progress/abandon | `InMemoryIdempotencyStore.cs`, service | A6 | M | Y |
| C9 | Observability | Built-in only | `/healthz`, `/readyz`, `X-Request-ID` in logs + problems, JSON logs | `Program.cs`, `RequestContextMiddleware.cs` | A8 | L | N |
| C10 | Production hygiene | – | Swagger off outside Dev, `AllowedHosts`, body limit, security headers, forwarded headers | `Program.cs`, config | A8 | M | Y |

## Phase 5
| ID | Intent | Acceptance |
|---|---|---|
| D1 | Quality-gate scripts + CI | `scripts/quality-gates.{sh,ps1}`, `quality.cmd`, `.github/workflows/ci.yml` all green |
| D2 | Docs | REQUIREMENTS, ARCHITECTURE, ADRs, TASKS, SCENARIOS A/B/C, RISKS, TESTING, README, FINAL_SUMMARY, AI_LOG roll-up |
