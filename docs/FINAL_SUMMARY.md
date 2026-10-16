# Final engineering summary

**Plan & rationale.** Normalise the requirement into FRs, measurable NFRs and explicit defaults (REQUIREMENTS). Fix a layered design
with every dependency injected and one composition root (ARCHITECTURE, ten ADRs). Decompose into tasks with acceptance criteria,
risk and sign-off flags (TASKS). Execute one reviewable change at a time with tests, run the quality gates, and log every AI contribution
(AI_LOG). Demonstrate three scenarios: greenfield (core service), brownfield (collision bug, slow redirects, blocking analytics) and
ambiguous ("safer links, better insights"). Simple mode (in-memory) keeps setup at zero; async repository interfaces keep the path to
real storage open.

**Artifacts.**
* Runnable .NET 8 Web API (`src/`), Swagger/OpenAPI at `/swagger` (Development), `run.cmd` one-click build → test → run.
* 212 xUnit tests (unit + `WebApplicationFactory` integration), dependency-free load-test tool (+ k6 script).
* Quality gates: `quality.cmd` / `scripts/quality-gates.sh`; GitHub Actions CI with a redirect p95 budget and lost-event check.
* Docs: REQUIREMENTS, ARCHITECTURE, `DECISIONS/ADR-001…010`, TASKS, `SCENARIOS/A|B|C`, RISKS, TESTING, AI_LOG, this summary.

**Validation.** Warnings-as-errors build; `dotnet format` clean; 212/212 tests; line 97.8 % / branch 94.2 % (gate 85 %); no vulnerable packages (live nuget.org feed); security boundary
tests (55 validator cases incl. encoded and IPv6 bypasses); concurrency tests (unique codes, exact counters, race-free deactivate,
cache stampede); a deterministic reproduction of the reported collision bug; load test before/after (p95 14.9 → 3.7 ms, 5.9× throughput,
0 lost events); curl smoke test of the running app.

**Key risks / trade-offs.** In-memory data loss; no authentication (anyone can deactivate); DNS rebinding not covered; analytics are
eventually consistent and dropped under overload by design (availability over completeness); multi-node cache staleness ≤ 60 s after
deactivate; 302 over 301. See RISKS.md (with detection signals).

**Assumptions.** Single instance; UTC everywhere; anonymous use acceptable; the service never fetches targets; no IP storage.

**Limitations.** Load numbers come from a 2-vCPU
container with the load generator on the same host (ratios are meaningful, absolutes are not). No mutation, soak or multi-node tests.

**Next for production.**
1. **Storage:** Postgres (`UNIQUE(code)`, `click_daily` aggregates, partitioned raw clicks with retention) behind the existing interfaces.
2. **ID service at scale:** range-allocated or Snowflake-style IDs when the random space gets crowded (> ~1 B links).
3. **Edge:** CDN/edge redirects for hot links; Redis as the shared cache with pub/sub invalidation.
4. **Event streaming:** Kafka/Event Hubs (or an outbox) instead of the in-process channel; retries + dead-letter; rollups as a stream job.
5. **Auth & multi-tenancy:** API keys/OAuth, link ownership, per-tenant quotas and per-key rate limits (distributed limiter).
6. **Safety:** Safe Browsing/threat-intel `IDestinationScreen`, DNS re-check, abuse-report workflow.
7. **Observability:** OpenTelemetry traces/metrics (queue depth, dropped, cache hit ratio, p95), SLO alerts.
8. **Delivery:** containerise, blue/green with config-flag rollback (`Analytics:Mode`, `Cache:Enabled`), Dependabot.
