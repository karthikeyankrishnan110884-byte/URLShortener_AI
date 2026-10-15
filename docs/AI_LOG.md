# AI traceability log

Tool: Claude (agentic coding assistant). Engineer/owner: Karthik.
Entries record what the AI produced and what was revised during the session. **Sign-off** boxes are for the engineer to tick after review.

### T0: Requirement normalisation & stack
- Intent: turn the assignment PDF into a phased master prompt, then build it as C#/.NET 8 Web API in simple mode with DI and Swagger.
- AI generated: `URL_Shortener_AI_Prompt.md`, `docs/REQUIREMENTS.md` (ambiguity table with proposed defaults).
- Engineer direction: switch from the Python default to .NET 8. Simple mode. DI. Meaningful tests. Swagger.
- Sign-off: [ ] defaults in REQUIREMENTS.md accepted

### T1: Domain, repository, generator, validator, service
- AI generated: `Domain/*`, `Repositories/*`, `Services/*`.
- Design choices made deliberately (see ADRs): atomic `TryAdd` + retry instead of check-then-insert (ADR-2). Crypto RNG
  (`RandomNumberGenerator.GetString`) instead of `System.Random`.
- Sign-off: [ ] security rules in `UrlValidator` reviewed (high impact)

### T2: Error handling (revised)
- First version: global `IExceptionHandler`.
- **Revised** → MVC `IExceptionFilter`. Reason: the .NET 8 exception-handler middleware logs handled exceptions at Error level,
  so normal 404/409/410 outcomes would pollute error logs.
- Sign-off: [ ] RFC 7807 error contract accepted (public API change)

### T3: Rate limiting (revised)
- First version: limits read from `IConfiguration` at registration time.
- **Revised** → resolved from `IOptions<RateLimitingOptions>` at request time. Reason: registration-time reads ignore test/environment
  overrides, so the limit couldn't be tested reliably.

### T4: Tests
- AI generated: unit + integration suites (see `TESTING.md`).
- Dependency rejected: FluentAssertions (v8 moved to a commercial licence). Plain xUnit asserts used instead.
- Revised: a C# 12 collection expression inside `Assert.Equal` replaced with an explicit array (overload inference risk).
- Verified: API compiled with warnings-as-errors and smoke-tested with curl (201, 302, stats, 400 SSRF block, 204→410, health, 429).
- Sign-off: [ ] test suite reviewed

### Environment note
NuGet was blocked in the AI's cloud workspace, so the full build + `dotnet test` + Swagger run happened on the engineer's machine.
