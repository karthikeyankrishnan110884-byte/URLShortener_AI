# Testing approach

Run: `dotnet test` · Coverage: `dotnet test --collect:"XPlat Code Coverage"`

| Layer | Tests | What they prove |
|---|---|---|
| Domain (`ShortLinkTests`) | expiry boundary is exclusive, idempotent deactivate, 1,000 concurrent clicks counted exactly | Core invariants |
| Generator | length and charset, 10k codes no duplicates, guards | Code quality and keyspace |
| Validator | 5 valid, 9 malformed, 13 private/loopback/metadata (incl. IPv6 and IPv4-mapped), range boundaries (172.15/172.32), max length | Security rules incl. edge cases |
| Repository | duplicate rejected, **200-way race → exactly one winner**, case sensitivity, click isolation | Brownfield regression guard |
| Service | collision retry (scripted generator), retry exhaustion, alias rules, reserved words, past expiry, unsafe URL stores nothing, expired/deactivated → gone and click not counted, referrer normalisation, UA truncation, stats aggregation by day/referrer | Business rules with a fake clock, no sleeps |
| Integration (`WebApplicationFactory`) | 201 + Location, end-to-end create → 302 → stats, 400 problem+json, 409, 404, 204 → 410, health + Swagger doc, **429 after limit while redirects stay unthrottled** | Real HTTP pipeline, DI, filters, routing |

**Design choices:** deterministic time via a `TimeProvider` fake. Collisions forced with a scripted generator instead of probability.
Each integration fixture gets its own app instance (isolated store and limiter). No external services.

**Known gaps:** no load or perf test yet, no mutation testing, no contract test against a published OpenAPI file.
