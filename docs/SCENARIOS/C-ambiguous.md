# Scenario C: Ambiguous requirement

> "Make the links safer and give marketing better insights."

## 1. Interpretation (plausible meanings)
**"Safer"**
1. Don't let the shortener be used to reach internal systems (SSRF / open redirect into private networks).
2. Don't send end users to phishing or malware destinations, including links that turn bad after creation.
3. Make codes impossible to guess or enumerate (privacy of other people's links and stats).
4. Throttle abuse: bulk link creation, redirect floods and enumeration.
5. Limit link lifetime (expiry, bounded maximum) and allow takedown.
6. Safe API behaviour: idempotent retries, no internal details in errors, no host-header or clickjacking issues.

**"Better insights"**
- a. Clicks over time, chartable (daily series with zero-filled gaps).
- b. Where traffic comes from (referrers).
- c. Which campaign or channel drove it (UTM source/medium/campaign).
- d. What devices people use; separate bots from humans.
- e. Unique visitors and geography (needs personal data).

## 2. Clarifying questions for the PO (with proposed defaults)
| # | Question | Proposed default (taken) |
|---|---|---|
| 1 | Is "safer" about our infrastructure, our end users, or both? | Both: infrastructure first (cheap, high value), then destination screening |
| 2 | Do we have a threat-intel feed (Safe Browsing, internal list)? | Not yet → build an **extension point** + config denylist; plug the feed in later |
| 3 | May we store visitor IPs (geo, unique visitors)? | **No** (privacy/legal review needed) → referrer host, device class, UTM only |
| 4 | Should a link whose domain becomes malicious stop working? | Yes → screen again on every redirect (410) |
| 5 | Should UTM tags on the short URL be forwarded to the destination? | No, record only (avoids double-tagging; revisit with marketing) |
| 6 | Which insights first? | Volume over time, sources/campaigns, device mix |
| 7 | Are bots counted? | Counted but labelled `bot` so dashboards can exclude them |
| 8 | What is an acceptable abuse threshold? | Create 20/min/IP, redirect 600/min/IP (tunable) |

## 3. Normalised problem statement (based on the defaults)
Reject destinations that are internal, loop back to us or are denylisted, at create time and at redirect time; bound link lifetime;
rate-limit creation and redirects per client; make create retries safe. Record, without personal data, each click's day, referrer host,
device class and UTM tags, and expose top-N breakdowns plus a zero-filled daily series.

## 4. Decomposed tasks
C1-C10 in [TASKS.md](../TASKS.md), each with acceptance criteria. Summary:

| Task | Delivered | Acceptance evidence |
|---|---|---|
| C1 denylist hook | `IDestinationScreen` + `DomainDenylistScreen` (subdomains, hot reload via `IOptionsMonitor`) | `DomainDenylistScreenTests` (7), service + API tests: 400 at create, 410 after a later block |
| C2 validator hardening | +18 bypass cases rejected (trailing dot, IPv6 zone id, encoded IPv4, CGNAT, benchmark, multicast, NAT64, 6to4, single-label, `.home.arpa`), self-loop block | `UrlValidatorTests` (55) |
| C3 redirect rate limit | Sliding window 600/min/IP, problem+json with `Retry-After` | `RateLimitTests` |
| C4 expiry cap | `MaxExpiryDays` (3650) | service + API tests |
| C5 UTM capture | Sanitised, aggregated, not forwarded | `ClickEventFactoryTests`, API campaign test |
| C6 device summary | bot/mobile/tablet/desktop/unknown | 13-row classifier table |
| C7 daily endpoint | `GET …/stats/daily?from&to`, zero-filled, ≤ 366 days | analytics + API tests |
| C8 idempotent create | `Idempotency-Key` (ADR-010) | store + service + API tests |
| C9 observability | `/healthz`, `/readyz`, `X-Request-ID` → logs + problem details, JSON logs | `HardeningApiTests` |
| C10 hygiene | Swagger only in Dev, `AllowedHosts`, 16 KB body limit, security headers, trusted forwarded headers | `StartupTests` |

**Deferred (needs a PO/privacy decision):** threat-intel feed integration, unique visitors and geo (question 3), per-API-key limits
(needs authentication), UTM forwarding (question 5).

## 5. Validation
Captured from the running API (Development config with `phishing.test` denylisted):
```
POST {"url":"https://login.phishing.test/"}
→ 400 {"title":"Destination not allowed","detail":"The destination domain 'phishing.test' is blocked by policy.",...,"requestId":"…"}

POST {"url":"http://[fe80::1%25eth0]/"}
→ 400 {"title":"Invalid request","detail":"URLs pointing to local or private network addresses are not allowed.",...}

GET /dotnet8?utm_source=newsletter&utm_medium=email&utm_campaign=launch   (iPhone UA, LinkedIn referrer)
GET /api/v1/links/dotnet8/stats
→ {"totalClicks":1,"clicksByDay":[{"date":"2026-10-16","clicks":1}],"topReferrers":[{"referrer":"www.linkedin.com","clicks":1}],
   "topSources":[{"value":"newsletter","clicks":1}],"topMediums":[{"value":"email","clicks":1}],
   "topCampaigns":[{"value":"launch","clicks":1}],"devices":[{"value":"mobile","clicks":1}]}

GET /api/v1/links/dotnet8/stats/daily?from=2026-10-14
→ {"from":"2026-10-14","to":"2026-10-16","totalClicks":1,"days":[{"date":"2026-10-14","clicks":0},{"date":"2026-10-15","clicks":0},{"date":"2026-10-16","clicks":1}]}
```
