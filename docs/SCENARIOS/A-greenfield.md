# Scenario A: Greenfield (core service)

## Requirement
Build the core shortener: create, redirect, custom alias, expiry, click analytics, soft-deactivate, metadata, with
URL validation (scheme allowlist, length, private/loopback block), collision handling and RFC 7807 errors.

## Decomposition
Tasks A1-A9 in [TASKS.md](../TASKS.md), executed in dependency order: skeleton → domain → repository → generator →
validator (high risk, security sign-off) → service → controllers + error contract → composition root → rate limiting.
Each task was one reviewable change with its tests.

## Execution highlights
* **Atomic uniqueness from day one** (ADR-002): `TryAdd` + retry, not check-then-insert.
* **Crypto-random Base62** (ADR-001) instead of `System.Random` or a counter.
* **Thin controllers**, business rules in the service, `TimeProvider` injected for deterministic time.
* **Error contract** revised mid-task: global `IExceptionHandler` → MVC filter, because the former logged normal 4xx at Error level (ADR-008).
* **Rate-limit options** revised: read at request time from `IOptions`, because registration-time reads ignored test overrides.

## Validation evidence
| Gate | Result |
|---|---|
| Build (Release, nullable, warnings as errors) | 0 warnings, 0 errors |
| `dotnet format --verify-no-changes` | clean |
| Tests | Domain, repository, generator, validator, service and full-pipeline integration tests per status code (now part of the 212-test suite) |
| Coverage | ≥ 85 % gate; current 97.8 % line / 94.2 % branch |
| Manual smoke (curl) | 201 + Location, 302 no-store, stats, 400 SSRF problem, 204 → 410, 429 |

Example (captured from the running API):
```
POST /api/v1/links {"url":"https://learn.microsoft.com/dotnet","customAlias":"dotnet8"}
HTTP/1.1 201 Created
Location: http://localhost:5104/api/v1/links/dotnet8
{"code":"dotnet8","shortUrl":"http://localhost:5104/dotnet8","originalUrl":"https://learn.microsoft.com/dotnet","isActive":true,"clickCount":0,...}

GET /dotnet8
HTTP/1.1 302 Found
Cache-Control: no-store
Location: https://learn.microsoft.com/dotnet
```
