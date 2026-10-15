# Final engineering summary

**Plan & rationale.** Normalise the requirement (REQUIREMENTS.md). Fix a layered design with every dependency injected (ARCHITECTURE.md).
Build in small reviewable tasks with tests alongside. Show three scenarios: greenfield, brownfield and ambiguous (SCENARIOS.md).
Simple mode (in-memory) keeps setup at zero while the repository interface keeps the path to real storage open.

**Artifacts.** .NET 8 Web API (`src/`), xUnit unit + integration tests (`tests/`), Swagger/OpenAPI at `/swagger`,
README with setup, and docs for requirements, architecture/ADRs, scenarios, risks, testing and the AI log.

**Validation.** Warnings-as-errors build. Unit tests on every rule, including security boundaries and concurrency.
Integration tests over the real HTTP pipeline for every status code. Manual Swagger/curl smoke test.

**Key risks / trade-offs.** In-memory data loss. No authentication. Hostname-based SSRF (DNS rebinding) not covered. Per-IP limit
behind proxies. 302 chosen over 301. See RISKS.md.

**Assumptions.** Single instance. UTC everywhere. Anonymous usage acceptable for the prototype. No IP storage.

**Next for production.** SQL store with a unique index. Cache-aside redirect decorator. Async click pipeline (`Channel`/queue → daily rollups).
API keys and ownership. Forwarded headers. Distributed rate limiter. Malicious-domain check. OpenTelemetry. CI pipeline running
build, test, coverage and `dotnet list package --vulnerable`.
