# Requirements (Phase 0)

## 1. Problem statement
Provide an HTTP service that turns long URLs into short codes, redirects visitors from code to target quickly and safely,
and gives link owners click analytics. It must resist obvious abuse (open redirect into internal networks, malicious
destinations, code guessing, request flooding), keep redirects fast when analytics storage is slow, and be runnable locally
with zero setup while staying swappable for real storage.

## 2. Functional requirements
| ID | Requirement |
|---|---|
| FR-1 | Create a short link for an absolute http/https URL |
| FR-2 | Redirect `/{code}` to the target |
| FR-3 | Optional custom alias |
| FR-4 | Optional expiry |
| FR-5 | Record each click: time, referrer host, user-agent summary (device class), UTM tags |
| FR-6 | Stats: total, clicks per UTC day, top referrers, top UTM sources/mediums/campaigns, devices |
| FR-7 | Daily series endpoint (zero-filled, date range) for charting |
| FR-8 | Deactivate a link (soft delete) |
| FR-9 | Read link metadata |
| FR-10 | Block destinations on a configurable denylist, at create time and at redirect time |
| FR-11 | Safe retries of create via `Idempotency-Key` |

## 3. Non-functional requirements
| ID | Requirement | Target / mechanism | Evidence |
|---|---|---|---|
| NFR-1 | Redirect latency | **p95 ≤ 25 ms server-side** at ≥ 1 k rps with a 2 ms remote store; CI smoke budget p95 ≤ 50 ms | Load test: p95 3.7 ms at 30 k rps (B-brownfield.md) |
| NFR-2 | Analytics never block redirects | Bounded queue + batched background writer | ADR-004, load test 0 lost events |
| NFR-3 | Availability | Single instance in prototype; stateless API apart from the store, so N replicas behind an LB; `/healthz` liveness, `/readyz` readiness | Health tests |
| NFR-4 | Concurrency safety | Codes unique under races; counters exact | ADR-002, race tests |
| NFR-5 | Idempotency | `GET`/`HEAD`/`DELETE` idempotent by design; `POST` idempotent with `Idempotency-Key` | ADR-010 |
| NFR-6 | Rate limiting | Per-IP: create 20/min, redirect 600/min; 429 problem+json with `Retry-After` | RateLimitTests |
| NFR-7 | Input validation | Scheme allowlist, length ≤ 2048, no credentials, SSRF/private ranges, alias rules, expiry ≤ 10 y, body ≤ 16 KB | Validator tests (55) |
| NFR-8 | Observability | Structured JSON logs (scopes), `X-Request-ID` correlation in logs + problem details, `/healthz`, `/readyz` (queue depth) | HardeningApiTests |
| NFR-9 | Errors | RFC 7807 everywhere, never leak internals | ADR-008 |
| NFR-10 | Configuration | Options validated at startup (fail fast) | StartupTests |
| NFR-11 | Privacy | No IPs; referrer host only; bounded UA/UTM | ADR-007 |
| NFR-12 | Quality gates | format, warnings-as-errors build, tests, line coverage ≥ 85 %, vulnerable packages | `scripts/quality-gates.*`, CI |

## 4. Ambiguities → decisions (engineer defaults)
| Question | Options | Recommended default (taken) | Impact if wrong |
|---|---|---|---|
| Same URL shortened twice? | reuse code / new code | **New code each time** (separate analytics and lifecycle) | Store grows; dedupe can be added behind a flag |
| 301 or 302? | 301 cacheable / 302 | **302 + no-store** (ADR-003) | Slightly more traffic on repeat visits |
| Authentication / ownership? | API keys / OAuth / none | **None in prototype** (out of scope) | Anyone can deactivate; top risk in RISKS |
| Code case sensitivity | sensitive / insensitive | **Case-sensitive Base62** | Users mistyping case get 404 |
| Persistence | SQL / in-memory | **In-memory behind async interfaces** (ADR-006) | Data lost on restart |
| Delete semantics | hard / soft | **Soft** (keeps stats for audit) | None for prototype |
| Analytics consistency | strong / eventual | **Eventual (ms)** so redirects stay fast (ADR-004) | Dashboards lag slightly; crash can lose queued events |
| UTM tags: record or forward? | record / forward / both | **Record, do not forward** | Marketers expecting tags at the destination must put them on the target URL |
| Max expiry | none / bounded | **≤ 3650 days** (configurable) | Long-lived campaigns need a config change |
| Bots in analytics | count / exclude | **Count, but classify as `bot`** | Dashboards should filter `bot` |

## 5. Assumptions
* UTC everywhere; `TimeProvider` injected.
* Single instance for the prototype; horizontal scale requires shared storage and a distributed rate limiter.
* Anonymous use is acceptable for the prototype.
* The service never fetches target URLs (it only redirects), which limits SSRF impact.
* Clients that need retry safety send `Idempotency-Key`.

## 6. Out of scope
Authentication and multi-tenancy, custom domains, QR codes, link editing, geo/IP analytics, unique visitors, distributed deployment.

## 7. Acceptance criteria (Given / When / Then)
| FR | Given | When | Then |
|---|---|---|---|
| FR-1 | a valid public https URL | `POST /api/v1/links` | 201, body has `code` + `shortUrl`, `Location` → `/api/v1/links/{code}` |
| FR-1 | `http://127.0.0.1/`, `ftp://…`, `/etc/passwd`, `http://localhost./` | POST | 400 problem+json, nothing stored |
| FR-2 | an active link | `GET /{code}` | 302 to target, `Cache-Control: no-store`; a click is recorded |
| FR-2 | an active link | `HEAD /{code}` | 302, no click recorded |
| FR-2 | an unknown code | GET | 404 problem+json |
| FR-3 | an unused well-formed alias | POST with `customAlias` | 201, code == alias |
| FR-3 | a taken alias / malformed / reserved (`swagger`) | POST | 409 / 400 / 400 |
| FR-4 | `expiresAt` in the future (≤ max) | after that instant, GET | 410; past or > max `expiresAt` → 400 at create |
| FR-5 | N redirects with referrer/UA/UTM | stats read | total N; referrer host, device class and tags counted (eventually, ms) |
| FR-6 | clicks over two days | `GET …/stats?top=5` | per-day counts, top-5 lists sorted by count then name |
| FR-7 | clicks on day D | `GET …/stats/daily?from=D-6&to=D` | 7 entries, zeros filled; inverted or > 366-day range → 400 |
| FR-8 | an active link | `DELETE` | 204 (idempotent); redirect → 410 immediately, even if cached; metadata readable |
| FR-9 | a code | `GET /api/v1/links/{code}` | 200 metadata, or 404 |
| FR-10 | `malware.test` on the denylist | POST `https://x.malware.test/` | 400 "Destination not allowed" |
| FR-10 | a link to `d.test`, then `d.test` added to the denylist | GET | 410 "blocked by safety policy" |
| FR-11 | POST with `Idempotency-Key: k` succeeded | same key + same body / different body | 201 same link + `Idempotent-Replayed: true` / 422 |
