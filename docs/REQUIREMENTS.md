# Requirements (Phase 0)

## Problem statement
Provide an HTTP service that turns long URLs into short codes, redirects visitors from code to target
quickly and safely, and shows the link owner basic click analytics. It must resist obvious abuse
(open-redirect to internal hosts, code guessing, request flooding), and it must be simple to run locally.

## Functional requirements
| ID | Requirement | Acceptance (Given/When/Then) |
|---|---|---|
| FR-1 | Create a short link for an absolute http/https URL | Given a valid URL, when POSTed, then 201 with `code`, `shortUrl`, and a `Location` header |
| FR-2 | Redirect `/{code}` to the target | Given an active link, when GET `/{code}`, then 302 to the target with `Cache-Control: no-store` |
| FR-3 | Optional custom alias | Given an unused, well-formed alias, it becomes the code. If taken: 409. If malformed or reserved: 400 |
| FR-4 | Optional expiry | Given `expiresAt` in the future, redirects after that instant return 410. If `expiresAt` is in the past: 400 |
| FR-5 | Record each click (time, referrer host, user agent) | After N redirects, stats show N |
| FR-6 | Stats: total, by UTC day, top 5 referrers | `GET /api/v1/links/{code}/stats` returns the aggregates |
| FR-7 | Deactivate a link (soft) | After DELETE (204), redirect returns 410, and metadata and stats stay readable |
| FR-8 | Read link metadata | `GET /api/v1/links/{code}` returns 200, or 404 if the code is unknown |

## Non-functional requirements
| ID | Requirement |
|---|---|
| NFR-1 | Redirect path: O(1) lookup, no I/O beyond the store |
| NFR-2 | Thread-safe under concurrent creates and redirects. Codes are unique even under races |
| NFR-3 | Abuse controls: scheme allowlist, private/loopback target block, rate limit on create |
| NFR-4 | Consistent RFC 7807 errors that never leak internals |
| NFR-5 | Configuration validated at startup |
| NFR-6 | Health endpoint. Structured logs for create, collision and deactivate |
| NFR-7 | Privacy: no client IP stored in analytics |

## Ambiguities → decisions taken (engineer defaults)
| Question | Options | Decision | Impact if wrong |
|---|---|---|---|
| Same URL shortened twice? | reuse code / new code | **New code each time** (separate analytics and lifecycle) | Store grows. Easy to add dedupe later |
| 301 or 302? | 301 cacheable / 302 | **302 + no-store** so clicks are counted and deactivation works | Slightly more traffic on repeat visits |
| Authentication / ownership? | API keys / none | **None in prototype** (out of scope) | Anyone can deactivate. Flagged in RISKS |
| Code case sensitivity | sensitive / insensitive | **Case-sensitive Base62** (bigger keyspace) | Users mistyping case get 404 |
| Persistence | SQL / in-memory | **In-memory ("simple mode") behind an interface** | Data lost on restart |
| Delete semantics | hard / soft | **Soft** (keeps stats for audit) | None for prototype |

## Out of scope
Authentication and multi-tenancy, a custom domain per tenant, QR codes, link editing,
geo/IP analytics, and a distributed deployment.
