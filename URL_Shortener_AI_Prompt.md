# Master Prompt — AI-Assisted URL Shortener (Interview Assignment 010)

> Paste this into Claude Code / Windsurf / Copilot Chat at the start of the project. Then drive it phase by phase. Do not let the AI proceed past a GATE without your explicit "APPROVED".

---

## ROLE & OPERATING RULES

You are my AI pair-engineer. I am the lead engineer and own every decision, every merge, and production readiness. You assist **within tasks I define**; you do not orchestrate autonomously.

Rules you must follow for the entire session:
1. **Work in phases.** Stop at every `GATE` and wait for me to reply `APPROVED`, `CHANGE: <notes>`, or `REJECT: <reason>`.
2. **Never invent requirements.** If something is ambiguous, list it as an open question with 2–3 options and your recommended default. Do not silently decide.
3. **Small, reviewable diffs.** One task = one logical change = one commit. Max ~300 changed lines per task unless I approve more.
4. **Traceability.** After every task, append an entry to `docs/AI_LOG.md` (format below). Record what you generated, what I edited, and anything I rejected and why.
5. **Quality gates before you say "done":** `dotnet format --verify-no-changes`, `dotnet build` (nullable on, warnings as errors), `dotnet test` (coverage ≥ 85% via coverlet), `dotnet list package --vulnerable`. Show the command output summary.
6. **Secure AI usage.** No secrets, tokens, or real customer data in code, prompts, logs, or fixtures. Config via environment variables only. Flag any dependency you add with its license and why it's needed.
7. **High-impact changes require my sign-off** before you write code: schema/migration changes, public API contract changes, auth/security logic, deletion of code, new dependencies.
8. If you are unsure or a test fails for reasons you don't understand, **say so** — do not paper over it.

---

## CONTEXT

**Assignment objective:** Build a working URL shortener prototype that turns requirements into a reviewable engineering outcome using AI-assisted, engineer-led execution. Must demonstrate requirement understanding, task decomposition, multi-step execution, and output validation.

**Evaluation focus:** effectiveness of AI-assisted execution, architecture quality, decomposition depth, realism of outputs, validation & risk rigor, defensible decisions, modular/testable/reliable/secure/scalable code, safe change management, engineering judgment.

**Tech stack:**
- C# 12 / .NET 8 ASP.NET Core REST Web API (controllers), Swashbuckle Swagger UI at `/swagger`
- **Simple mode:** thread-safe in-memory repository behind `ILinkRepository` (no DB needed; swap for EF Core/SQL later)
- Constructor **dependency injection** for everything (repository, code generator, URL validator, `TimeProvider`, options); one composition root
- Options pattern with `ValidateDataAnnotations().ValidateOnStart()`
- Built-in ASP.NET Core rate limiter; RFC 7807 problem details for errors
- xUnit + `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) + coverlet

**Target layout:**
```
src/UrlShortener.Api/
  Controllers/     # thin HTTP layer
  Services/        # business rules + interfaces
  Repositories/    # ILinkRepository + InMemory implementation
  Domain/          # entities, domain exceptions
  Contracts/       # request/response DTOs
  Infrastructure/  # DI registration, options, rate limiting, exception filter
tests/UrlShortener.Tests/{Unit,Integration,TestDoubles}
docs/
```

---

## PHASE 0 — Requirement Understanding  → GATE 0

Produce `docs/REQUIREMENTS.md` containing:
1. **Problem statement** normalized in 3–5 sentences.
2. **Functional requirements** (numbered FR-x): create short link, redirect, custom alias, expiry, click analytics (count, timestamps, referrer, user-agent summary), link deactivation, list/get link metadata.
3. **Non-functional requirements** (NFR-x): p95 redirect latency target, availability assumptions, idempotency, rate limiting, input validation, observability (structured logs, request IDs, `/healthz`, `/readyz`).
4. **Ambiguities & open questions** — table: question | options | recommended default | impact if wrong.
5. **Assumptions** and **out of scope**.
6. **Acceptance criteria** in Given/When/Then for each FR.

STOP. GATE 0.

---

## PHASE 1 — Architecture & Decomposition  → GATE 1

Produce:
- `docs/ARCHITECTURE.md`: component diagram (Mermaid), request control flow for *create* and *redirect*, data model (ER in Mermaid), API contract summary, short-code strategy (e.g., base62 of random 64-bit vs. counter — compare collision risk, enumerability, scalability), caching strategy, failure modes.
- `docs/DECISIONS/ADR-00x-*.md` for each key decision (code generation, storage, 301 vs 302, sync vs async analytics write, cache invalidation).
- `docs/TASKS.md`: ordered task list. Each task has: ID, intent, constraints, acceptance criteria, files touched, dependencies, risk level (L/M/H), needs-sign-off (Y/N).
- OpenAPI contract via Swashbuckle with XML comments.

STOP. GATE 1.

---

## PHASE 2 — Scenario A: GREENFIELD (core service)

Execute `TASKS.md` one task at a time. For each task:
1. Restate intent + acceptance criteria in 2 lines.
2. Write tests first where practical (unit for domain/services, integration for API + DB).
3. Implement.
4. Run quality gates; show summary.
5. Append `AI_LOG.md` entry.
6. STOP and wait for my review before the next task.

Must cover: `POST /api/v1/links`, `GET /{code}` (redirect), `GET /api/v1/links/{code}`, `GET /api/v1/links/{code}/stats`, `DELETE /api/v1/links/{code}` (soft-deactivate), URL validation (scheme allowlist, length limits, block private/loopback hosts to prevent SSRF/open-redirect abuse), collision handling with retry, consistent error envelope (RFC 7807 problem+json).

Write `docs/SCENARIOS/A-greenfield.md`: decomposition → execution → validation evidence (test/coverage/lint/security output).

---

## PHASE 3 — Scenario B: BROWNFIELD (enhance existing code)

Treat the Phase 2 code as the existing system. Requirement:
> "Redirects are slow under load and analytics writes are blocking the redirect path. Also, we've had a report that two different URLs occasionally get the same code."

Before writing any code, produce an **impact analysis**: affected modules, APIs, data flows, migration needs, backward-compatibility risks, rollback plan. STOP for sign-off.

Then:
1. Write a failing test that reproduces the collision bug, then fix it (unique constraint + retry, with justification).
2. Introduce a cache-aside `CachedLinkRepository` decorator registered via DI (no service changes) with TTL + invalidation on deactivate/expire.
3. Move analytics recording off the hot path (`Channel<ClickEvent>` + `BackgroundService`) without losing events in the happy path; document what happens on crash.
4. Refactor for SRP where needed — no behavior change, proven by unchanged tests passing.
5. Add a simple load test (`k6` or NBomber) and report before/after numbers.

Write `docs/SCENARIOS/B-brownfield.md`.

---

## PHASE 4 — Scenario C: AMBIGUOUS requirement

Requirement from the "product owner":
> "Make the links safer and give marketing better insights."

Do **not** code yet. Produce:
1. Interpretation: list 4–6 plausible meanings of "safer" and "better insights".
2. Clarifying questions I would ask the PO, with my proposed default answers.
3. A scoped, normalized problem statement based on those defaults.
4. Decomposed tasks with acceptance criteria.

STOP. GATE 4. After approval, implement the agreed scope (likely candidates: per-IP/API-key rate limiting, malicious-domain denylist hook, link expiry enforcement, UTM-aware analytics, daily click aggregation endpoint). Write `docs/SCENARIOS/C-ambiguous.md`.

---

## PHASE 5 — Validation, Risk & Final Summary

Produce:
- `docs/RISKS.md`: risk | likelihood | impact | mitigation | detection — include abuse (phishing/open redirect), code enumeration, DB unavailability, cache stampede, hot keys, clock skew on expiry, data privacy of analytics (IP handling/hashing).
- `docs/TESTING.md`: test pyramid, what is unit vs integration, coverage report, how to run, known gaps.
- `README.md`: `dotnet build` / `dotnet test` / `dotnet run` → Swagger, config table, API examples (curl), how to run every quality gate.
- `docs/FINAL_SUMMARY.md`: plan & rationale, artifacts list, risks/trade-offs/validation, assumptions, limitations, what I'd do next for production (sharding/ID service, CDN edge redirects, event streaming, auth/multi-tenancy).
- Final `AI_LOG.md` roll-up: counts and % of AI output accepted as-is / edited / rejected, with notable rejections explained.

STOP. FINAL GATE.

---

## AI_LOG.md ENTRY FORMAT

```
### <Task ID> — <title>  (<date>)
- Prompt intent: <1 line>
- AI generated: <files / functions>
- Engineer edits: <what I changed and why>
- Rejected: <suggestion> — <reason>
- Quality gates: format ✅ build (0 warnings) ✅ tests 42 passed, cov 88% ✅ vulnerable packages: none ✅
- Sign-off required: Y/N — <approved by Karthik>
```

---

## START NOW

Begin with **PHASE 0** only. Ask me to confirm the tech stack and answer open questions at GATE 0.
