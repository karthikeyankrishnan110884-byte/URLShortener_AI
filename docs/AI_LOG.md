# AI traceability log

Tool: Claude (agentic coding assistant). Engineer/owner: Karthik.
Each entry records the prompt intent, what the AI generated, what was revised and why (by the engineer, or by the AI after a quality gate
or test failed), what was rejected, the gate results, and whether engineer sign-off is required.
**Sign-off boxes are ticked by the engineer only.** All boxes below were approved by Karthik on 2026-10-15, after reviewing the session 2 changes.

Format per master prompt: Prompt intent · AI generated · Edits · Rejected · Quality gates · Sign-off.

---

## Session 1: greenfield build (2026-10-10)

### T0: Requirement normalisation & stack (2026-10-10)
- Prompt intent: turn the assignment PDF into a phased master prompt and normalised requirements.
- AI generated: `URL_Shortener_AI_Prompt.md`, `docs/REQUIREMENTS.md` (ambiguity table with proposed defaults).
- Engineer edits: switched the stack from the Python default to C#/.NET 8, simple mode (in-memory), DI everywhere, meaningful tests, Swagger.
- Rejected: Python/FastAPI default: engineer preference and team stack.
- Quality gates: n/a (docs).
- Sign-off required: Y. [x] defaults in REQUIREMENTS.md accepted, approved by Karthik (2026-10-15)

### T1: Domain, repository, generator, validator, service (2026-10-10)
- Prompt intent: tasks A2-A6.
- AI generated: `Domain/*`, `Repositories/*`, `Services/*`.
- Edits: none. Deliberate choices: atomic `TryAdd` + retry instead of check-then-insert (ADR-002); crypto RNG instead of `System.Random` (ADR-001).
- Rejected: none.
- Quality gates: build 0 warnings ✅ (tests in T4).
- Sign-off required: Y. [x] security rules in `UrlValidator` reviewed (high impact), approved by Karthik (2026-10-15)

### T2: Error handling (2026-10-10)
- Prompt intent: task A7, RFC 7807 error contract.
- AI generated: global `IExceptionHandler`.
- Edits (revised): → MVC `IExceptionFilter`. Reason: the .NET 8 exception-handler middleware logs handled exceptions at Error level, so normal 404/409/410 outcomes polluted error logs (ADR-008).
- Rejected: `Result<T>` types: more ceremony for a prototype.
- Quality gates: build ✅, integration tests per status code ✅.
- Sign-off required: Y. [x] RFC 7807 error contract accepted (public API), approved by Karthik (2026-10-15)

### T3: Rate limiting (2026-10-10)
- Prompt intent: task A9.
- AI generated: limiter with limits read from `IConfiguration` at registration time.
- Edits (revised): → resolved from `IOptions<RateLimitingOptions>` per request. Reason: registration-time reads ignore test/environment overrides, so the limit could not be tested.
- Rejected: global limit: would throttle legitimate redirects.
- Quality gates: build ✅, `RateLimitTests` ✅.
- Sign-off required: N.

### T4: Tests (2026-10-10)
- Prompt intent: unit + integration suites.
- AI generated: 81 tests (unit + `WebApplicationFactory` integration).
- Edits (revised): a C# 12 collection expression inside `Assert.Equal` replaced with an explicit array (overload inference risk).
- Rejected: FluentAssertions: v8 moved to a commercial licence (rule 6). Plain xUnit asserts used.
- Quality gates: build ✅, smoke-tested with curl (201, 302, stats, 400 SSRF, 204 → 410, health, 429) ✅. Full `dotnet test` ran on the engineer's machine (NuGet blocked in the AI workspace at the time).
- Sign-off required: Y. [x] test suite reviewed, approved by Karthik (2026-10-15)

---

## Session 2: review against the assignment, brownfield, ambiguous scope, Phase 5 (2026-10-15)

Engineer direction: *"review this code against the assessment, fix the code issues, fill in the gaps, improve."*
Environment: NuGet.org is blocked in the AI workspace, so packages were restored offline from the engineer's local NuGet cache (read-only access granted for this). The vulnerable-package gate was then run on the engineer's machine against nuget.org: no vulnerable packages (see T16).

### T5: Review & scoring (2026-10-15)
- Prompt intent: score the repo against the master prompt and the assignment PDF.
- AI generated: gap list (missing ADR files, TASKS.md, per-scenario docs, brownfield steps 2-5, scenario C implementation, `/readyz`, request IDs, gate evidence, AI_LOG roll-up).
- Edits: none. Rejected: none.
- Quality gates: baseline run **80/81 passed**: `Validate_RejectsMalformedOrDangerousUrls("/relative/path")` **fails on Linux** (see T6). Coverage 97.2 % line.
- Sign-off required: N.

### T6: Validator defects (B7/C2) (2026-10-15)
- Prompt intent: fix the failing baseline test and probe for bypasses.
- AI generated: probe of `Uri` parsing on edge inputs, then fixes in `UrlValidator`: implicit file paths rejected on every OS; trailing-dot hosts (`localhost.`); IPv6 zone ids (`fe80::1%25eth0`, previously parsed as a hostname and **allowed**); multicast/broadcast/benchmark/IETF ranges; NAT64 and 6to4 embedded IPv4; single-label and `.lan`/`.home.arpa`/`.intranet` hosts; links to the shortener itself (redirect loops). +27 tests.
- Edits: none.
- Rejected: DNS resolution at create time: adds latency and an outbound dependency, and the service never fetches targets; recorded in RISKS instead.
- Quality gates: format ✅ build 0 warnings ✅ validator tests 55/55 ✅.
- Sign-off required: **Y (security logic).** [x] approved by Karthik (2026-10-15)

### T7: Domain thread-safety and bounds (B7/C4) (2026-10-15)
- Prompt intent: fix races and unbounded inputs found in review.
- AI generated: `ShortLink.IsActive` volatile + CAS deactivate (exactly one winner); monotonic `LastAccessedAt`; `MaxExpiryDays`; reserved words skipped for generated codes; 16 KB body limit.
- Edits: none. Rejected: a `lock` per entity: CAS is enough and lock-free on the hot path.
- Quality gates: `ShortLinkTests` 6/6 (incl. 200-way deactivate race) ✅.
- Sign-off required: N.

### T8: Async, split repositories (B2) (2026-10-15)
- Prompt intent: make storage swappable for a real DB without a contract change later.
- AI generated: `ValueTask` + `CancellationToken` on `ILinkRepository`; new `IClickRepository` with pre-aggregated, bounded in-memory store; mechanical async port of all tests.
- Edits: none. Rejected: keeping the sync interface: it would force a breaking change when EF Core/SQL arrives.
- Quality gates: all pre-existing scenarios green after the port ✅ (behaviour-preservation proof).
- Sign-off required: **Y (repository contract / schema-equivalent change).** [x] approved by Karthik (2026-10-15)

### T9: Cache-aside decorator (B3) (2026-10-15)
- Prompt intent: speed up redirects without touching services.
- AI generated: `CachedLinkRepository` (TTL capped by link expiry, request coalescing, negative cache, invalidation on update, size limit), DI composition, 6 unit + 2 integration tests.
- Edits: none.
- Rejected: Redis: new infrastructure dependency for a single-node prototype (rule 7); `IMemoryCache` is in the shared framework, so no new package.
- Quality gates: ✅ (stampede test: 50 concurrent misses → 1 load).
- Sign-off required: N.

### T10: Analytics off the hot path (B4/B4b) (2026-10-15)
- Prompt intent: stop analytics writes blocking redirects, without losing events in the happy path.
- AI generated: `IClickRecorder`, bounded `ChannelClickQueue`, `ClickEventProcessor : BackgroundService`, `ClickQueueHealthCheck` → `/readyz`, Inline mode for comparison.
- Edits (revised after validation): **the load test showed the first version dropping 226,919 of 230,987 events** (one storage round-trip per event, ~250 events/s vs ~14 k redirects/s). Revised to batched ingestion (`IngestBatchAsync`: one counter update per link + one bulk insert per ≤ 1,000 events). Re-run: 0 lost at 30 k rps.
- Rejected: `BoundedChannelFullMode.Wait` with `WriteAsync`: would make redirects wait on analytics, exactly the reported problem.
- Quality gates: `ClickPipelineTests` 7/7 ✅, load test recorded == redirects ✅.
- Sign-off required: **Y (data-flow change; eventual consistency of `clickCount`).** [x] approved by Karthik (2026-10-15)

### T11: Collision bug: failing test, then fix (B1) (2026-10-15)
- Prompt intent: reproduce "two URLs get the same code" before relying on the fix.
- AI generated: `CheckThenInsertLinkRepository` (faithful copy of the bug pattern) + `CollisionBugReproductionTests`.
- Edits (revised after validation): the first reproduction relied on `Task.Delay` timing and **reproduced the bug in only 3 of 5 runs**. Revised to a barrier, so all callers check before any inserts: deterministic, 10/10.
- Rejected: none.
- Quality gates: ✅.
- Sign-off required: N.

### T12: SRP refactor (B5) (2026-10-15)
- Prompt intent: single responsibility without behaviour change.
- AI generated: `AliasPolicy`, `ClickEventFactory`, `LinkAnalyticsService` (read side), `ClickIngestor` (write side).
- Edits: none. Rejected: a generic mediator/CQRS library: new dependency, no benefit at this size.
- Quality gates: all scenario tests green ✅.
- Sign-off required: N.

### T13: Scenario C scope (C1, C3, C5-C10) (2026-10-15)
- Prompt intent: implement the agreed defaults for "safer links, better insights".
- AI generated: `IDestinationScreen` + hot-reloadable `DomainDenylistScreen` (create and redirect time); redirect sliding-window limit; 429 problem + `Retry-After`; UTM capture; device classifier; `/stats/daily`; `Idempotency-Key`; `/healthz` + `/readyz`; `X-Request-ID` middleware with logging scope; JSON console logs; security headers; `AllowedHosts`; Swagger only in Development; trusted forwarded headers; HEAD without counting.
- Edits (revised after validation): a test assumed problem `traceId` would equal `X-Request-ID`; ASP.NET Core correctly uses the W3C trace id. Added a separate `requestId` extension instead of overriding `traceId`.
- Rejected: per-API-key rate limiting: no authentication, so a header key would be trivially spoofable. Storing hashed IPs for unique visitors: reversible for IPv4, needs a privacy decision (ADR-007). Forwarding UTM tags: open PO question.
- Quality gates: format ✅ build 0 warnings ✅ tests ✅.
- Sign-off required: **Y (public API: new endpoint, header, status codes 422/429 bodies; security logic).** [x] approved by Karthik (2026-10-15)

### T14: Load test + smoke test (B6) (2026-10-15)
- Prompt intent: before/after numbers.
- AI generated: dependency-free `tests/UrlShortener.LoadTests` (rps, percentiles, lost-event check, p95 budget), `scripts/load-test.{sh,ps1}`, optional k6 script, `Storage:SimulatedLatencyMs` latency-injection decorator (dev only).
- Edits (revised after validation): the curl smoke test showed the denylist **not applied** when the DLL was launched from the repo root (content root had no `appsettings*.json`). Scripts and CI now pass an absolute `--contentRoot`.
- Rejected: NBomber: new dependency needing licence review (rule 6); the HttpClient tool covers what is needed.
- Quality gates: p95 14.9 → 3.7 ms, 5.2 k → 30.6 k rps, 0 errors, 0 lost events ✅.
- Sign-off required: N.

### T15: Quality-gate automation + CI (D1) (2026-10-15)
- Prompt intent: make rule 5 repeatable.
- AI generated: `scripts/quality-gates.sh` (CI) and `.ps1` (Windows) + `quality.cmd`; `.github/workflows/ci.yml` (gates + load smoke with p95 budget + artifacts).
- Edits: none. Rejected: a third-party coverage-threshold package: the scripts parse Cobertura directly.
- Quality gates: script run end to end ✅.
- Sign-off required: N.

### T16: Documentation (D2) (2026-10-15)
- Prompt intent: fill Phase 1-5 documentation gaps.
- AI generated: `docs/DECISIONS/ADR-001…010`, `TASKS.md`, `SCENARIOS/A|B|C`, rewritten REQUIREMENTS (NFR targets, assumptions, G/W/T for every FR), ARCHITECTURE (control flow, caching, failure modes), RISKS (detection column), TESTING, FINAL_SUMMARY, README.
- Edits: none. Rejected: none.
- Quality gates (final): format ✅ · build 0 warnings ✅ · **212 tests passed** ✅ · **line 97.8 % / branch 94.2 %** ✅ · vulnerable packages: **none**, confirmed against the live nuget.org feed on the engineer's machine (2026-10-15).
- Sign-off required: Y. [x] docs reviewed, approved by Karthik (2026-10-15)

---

## Roll-up

| Outcome | Entries | % | Entries |
|---|---|---|---|
| Accepted as generated | 9 | 53 % | T1, T5, T6, T7, T8, T9, T12, T15, T16 |
| Revised (engineer direction, or AI after a failing gate/test) | 8 | 47 % | T0 (stack), T2, T3, T4, T10, T11, T13, T14 |
| **Total deliverable entries** | **17** | 100 % | |

| Rejected alternatives (with reason) | Count |
|---|---|
| Dependencies for licence/footprint: FluentAssertions, NBomber, Redis, mediator library, coverage-threshold package | 5 |
| Designs: `IExceptionHandler`, `Result<T>`, global rate limit, per-entity lock, blocking channel writes, DNS resolution at create, sync repository contract | 7 |
| Product/privacy: hashed IP storage, per-API-key limits without auth, UTM forwarding (deferred to PO) | 3 |

**Notable revisions caught by validation, not by reading the code:**
1. Linux-only validator failure (`/relative/path` → `file://`): caught by running the suite on Linux.
2. 98 % analytics event loss in the first async design: caught by the load test's lost-event check.
3. A flaky bug reproduction (3/5): caught by repeated runs; made deterministic.
4. Config not loaded when launched outside the content root: caught by the curl smoke test.

Ownership: all code and decisions remain the engineer's. High-impact items (T0, T1, T2, T6, T8, T10, T13) were **signed off by Karthik on 2026-10-15**.
