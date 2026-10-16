# ADR-009: Abuse controls: SSRF/open-redirect validator, denylist hook, two rate limits

**Status:** Accepted · **Date:** 2026-10-10, extended 2026-10-15 · **Sign-off:** approved by Karthik, 2026-10-15 (security logic)

## Decision
1. **Validator** (create time): http/https only; ≤ `MaxUrlLength`; no credentials; no loopback/private/link-local/CGNAT/benchmark/
   multicast/broadcast ranges (IPv4, IPv6, IPv4-mapped, NAT64, 6to4); no `localhost`, single-label or internal-suffix hosts
   (`.local`, `.internal`, `.lan`, `.home.arpa`); no links back to this service (redirect loops).
2. **`IDestinationScreen`** extension point, run at create **and** redirect time. `DomainDenylistScreen` (config, hot-reloaded via
   `IOptionsMonitor`) blocks a domain and its subdomains. A domain blocked later stops existing links (410).
3. **Rate limits** per client IP: create fixed window (20/min), redirect sliding window (600/min, slows enumeration). Both tunable.
   `ForwardedHeaders:KnownProxies` makes the IP correct behind a proxy without letting clients spoof it.

## Bugs found while hardening (each has a regression test)
* `/relative/path` parsed as `file://` on Linux, so the "absolute URL" rule depended on the OS.
* `http://localhost./` (trailing dot) and `http://[fe80::1%25eth0]/` (zone id) bypassed the private-host check.

## Not covered (RISKS)
DNS rebinding (hostname that resolves to a private IP). The service never fetches targets, which limits the impact to open redirect.
