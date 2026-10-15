# Scenarios (Phases 2–4)

## A. Greenfield: core service
**Decomposition (executed in order, each its own reviewable change):**
| Task | Intent | Acceptance | Risk |
|---|---|---|---|
| A1 | Solution skeleton, `Directory.Build.props` (nullable, warnings as errors) | `dotnet build` clean | L |
| A2 | Domain `ShortLink`, `ClickEvent`, exceptions | Unit tests for expiry boundary, idempotent deactivate, concurrent clicks | L |
| A3 | `ILinkRepository` + in-memory implementation | Duplicate insert rejected. Case-sensitive | M |
| A4 | `IShortCodeGenerator` (crypto Base62) | Length and charset. No duplicates in 10k | L |
| A5 | `IUrlValidator` | Scheme, length, credentials, private ranges (with boundary cases) | **H** (security) |
| A6 | `UrlShortenerService` | Create, alias, expiry, resolve, stats, deactivate | M |
| A7 | Controllers + RFC 7807 filter + Swagger | Integration tests for every status code | M |
| A8 | DI composition root + options validation | App fails fast on bad config | L |

**Validation:** 70+ unit and integration tests, a clean warnings-as-errors build, and a manual Swagger and curl smoke test.

## B. Brownfield: concurrency bug + hardening of the existing code
**Incoming report:** "Two different URLs occasionally get the same code" and "redirects should honour retirement."

**Impact analysis (done before code):**
* Affected: `UrlShortenerService.Create*`, `ILinkRepository` contract, `RedirectController`.
* Root cause pattern: *check-then-insert* (`Exists(code)` followed by `Add`). Two requests can both see "free" and both insert.
* API contract: unchanged (additive 410 response only). No data migration. Rollback = revert the commit.

**Execution:**
1. Regression test for the race: `TryAdd_UnderConcurrentWritesOfSameCode_ExactlyOneWins` (200 parallel inserts of one code must yield exactly one success).
2. Fix: the repository contract became `bool TryAdd(...)` (atomic `ConcurrentDictionary.TryAdd`). The service retries generation on `false`.
   Equivalent in SQL: a unique index + catch the duplicate-key error.
3. Deterministic tests with a scripted `SequenceCodeGenerator`: collide twice, then succeed. Always collide, then a 503 after N attempts.
4. Added soft-deactivate (`DELETE` → 410) without breaking existing callers.

**Planned next (not done in simple mode):** a cache-aside decorator `CachedLinkRepository : ILinkRepository`
registered via DI (no service changes), and moving click writes to a `Channel<ClickEvent>` background consumer.

## C. Ambiguous: "Make the links safer and give marketing better insights"
**Interpretations considered:** (1) block malicious or phishing targets, (2) stop the service being used to reach
internal hosts (SSRF / open redirect), (3) stop code guessing, (4) throttle abuse, (5) expiring links;
and for insights: (a) clicks over time, (b) traffic sources, (c) device/geo, (d) campaign (UTM) tracking.

**Questions for the PO (defaults taken):**
| Question | Default |
|---|---|
| Is "safer" about our infrastructure or about end users? | Both, starting with infrastructure (cheap and high value) |
| Do we have a threat-intel feed for malicious domains? | No → leave an extension point, don't build it |
| May we store visitor IPs for geo analytics? | No (privacy) → referrer host + UA only |
| Which insights matter first? | Volume over time + top sources |

**Normalized scope delivered:** private/loopback/metadata IP block, credential-in-URL block, scheme allowlist, non-enumerable
codes, reserved aliases, a per-IP create rate limit (429), expiry (410), and stats with clicks by day + top referrers.
**Deferred:** malicious-domain feed, UTM breakdown, geo/device analytics.
