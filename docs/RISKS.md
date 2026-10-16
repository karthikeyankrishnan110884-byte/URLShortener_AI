# Risks, trade-offs, failure scenarios

L/M/H = likelihood/impact. "Detection" says how we would notice in production.

| Risk | L | I | Mitigation in prototype | Detection | Production follow-up |
|---|---|---|---|---|---|
| Data lost on restart (in-memory) | Certain | H | Documented; async interfaces isolate storage (ADR-006) | n/a (by design) | SQL/Postgres with `UNIQUE(code)` |
| No auth: anyone can deactivate any link | H | M | Out of scope, flagged | Audit log of DELETEs | API keys / OAuth, owner on link, per-key limits |
| Phishing / malware destinations | M | H | Denylist screen at create **and** redirect (410 when blocked later), hot reload | Blocked-redirect warning logs | Safe Browsing / threat-intel feed behind `IDestinationScreen`, report-abuse flow |
| Open redirect / SSRF into private networks | M | H | Validator: private/loopback/link-local/CGNAT/multicast, IPv6 + NAT64/6to4, encoded IPv4, trailing dot, zone id, internal suffixes; 55 tests | 400 rate in logs | Resolve DNS at create and re-check (rebinding) |
| DNS rebinding (public name → private IP) | L | M | Service never fetches targets, so impact is limited to an open redirect | – | Resolve + pin at create; periodic re-check |
| Code enumeration | L | M | Crypto-random 7-char Base62 (3.5×10¹²); redirect limit 600/min/IP; 5 s negative cache | 404 rate per IP | Longer codes for sensitive links, CAPTCHA/WAF on 404 bursts |
| Create flood | M | M | Fixed window 20/min/IP, 16 KB body limit | 429 rate | Distributed limiter (Redis), WAF |
| Rate limit keyed on proxy IP | M | M | `ForwardedHeaders:KnownProxies` (trusted only, no spoofing) | All traffic from one IP | Configure per environment |
| Redirect latency under load | M | H | Cache-aside + batched async analytics: p95 14.9 → 3.7 ms | Load test in CI (p95 budget) | CDN edge redirects, Redis cache |
| Cache stampede on hot key | M | M | Request coalescing (1 load per key in flight), tested 50-way | Store QPS spikes | Same + jittered TTL |
| Stale cache after deactivate (multi-node) | M | M | Local eviction; staleness ≤ `LinkTtlSeconds` (60 s) | – | Redis pub/sub invalidation |
| Analytics queue overflow / store down | M | M | Bounded queue, drop + count, never blocks redirects | `/readyz` Degraded at 90 %, `dropped` counter | Durable broker, alerting |
| Click events lost on crash | L | L | ≤ queue capacity; graceful shutdown drains | `drainedOnStop` log | Outbox / Kafka |
| Unbounded analytics memory | M | M | Raw events capped per link; per-dimension cardinality cap → `(other)` | Memory metrics | Partitioned tables + retention |
| Analytics privacy (IPs, referrer paths) | M | H | No IPs (raw or hashed), referrer host only, UA truncated, UTM sanitised (ADR-007) | Data review | DPIA before any geo/unique-visitor feature; salted rotating HMAC |
| Duplicate creates on client retry | M | L | `Idempotency-Key` (ADR-010) | Replayed header count | Redis/SQL idempotency store |
| Clock skew on expiry | L | L | Single node, `TimeProvider`, UTC; expiry exclusive | – | NTP, server-side time only |
| Host-header / clickjacking | L | M | `AllowedHosts`, `X-Frame-Options: DENY`, `nosniff` | 400s on host filter | CSP on any HTML |
| Swagger exposure | – | L | Off outside Development unless `Swagger:Enabled=true` | – | Behind auth if needed |
| Vulnerable dependencies | L | H | Only Swashbuckle (MIT) at runtime; `dotnet list package --vulnerable` gate in CI | CI failure | Dependabot |
| AI-generated code defects | M | M | Tests first where practical, quality gates, engineer review of high-impact changes, AI_LOG traceability | Gate failures | Same in PR review |

**Trade-offs accepted:** 302 over 301 (accuracy over bandwidth). Eventual consistency of stats (redirect speed over immediacy).
Drop analytics under overload (availability over completeness). Exceptions for domain flow (clarity over micro-performance).
