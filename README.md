# URL Shortener API (.NET 8)

REST Web API that creates short links, redirects them, and reports click analytics.


**Simple mode:** thread-safe in-memory storage behind `ILinkRepository`, so it runs with no database.
Data resets on restart. Every component is wired through dependency injection against an interface,
so you can swap in SQL Server or Redis without touching controllers or services.

## Prerequisites
- .NET 8 SDK (`dotnet --list-sdks` should show 8.0.x)

## Build, test, run
One step on Windows: double-click **`run.cmd`** (build → test → run → opens Swagger; logs in `logs\`).

Or manually:
```powershell
dotnet build                     # warnings are treated as errors
dotnet test                      # unit + integration tests
dotnet run --project src/UrlShortener.Api --launch-profile http
```
Then open **http://localhost:5104/swagger**.

Coverage: `dotnet test --collect:"XPlat Code Coverage"`

## API
| Method | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| POST | `/api/v1/links` | Create (optional `customAlias`, `expiresAt`) | 201 + `Location` | 400, 409, 429 |
| GET | `/api/v1/links/{code}` | Metadata | 200 | 404 |
| GET | `/api/v1/links/{code}/stats` | Clicks total / by day / top referrers | 200 | 404 |
| DELETE | `/api/v1/links/{code}` | Soft-deactivate | 204 | 404 |
| GET | `/{code}` | Redirect + record click | 302 | 404, 410 |
| GET | `/health` | Liveness | 200 | |

Errors use RFC 7807 `application/problem+json`.

> Swagger UI follows redirects, so "Try it out" on `GET /{code}` shows the target page's response.
> To see the raw 302, use a browser tab or `curl -i http://localhost:5104/{code}`.

```powershell
curl -X POST http://localhost:5104/api/v1/links -H "Content-Type: application/json" `
     -d '{"url":"https://learn.microsoft.com/dotnet","customAlias":"dotnet8"}'
curl -i http://localhost:5104/dotnet8
curl http://localhost:5104/api/v1/links/dotnet8/stats
```

## Configuration (`appsettings.json`)
| Key | Default | Meaning |
|---|---|---|
| `Shortener:BaseUrl` | `http://localhost:5104` | Prefix for returned `shortUrl` |
| `Shortener:CodeLength` | 7 | Generated code length (Base62) |
| `Shortener:MaxCodeGenerationAttempts` | 5 | Retries on collision |
| `Shortener:MaxUrlLength` | 2048 | Max target URL length |
| `RateLimiting:CreatePermitLimit` / `CreateWindowSeconds` | 20 / 60 | Create requests allowed per client IP per window |

Options are validated at startup: a bad value stops the app from starting instead of failing later.

## Layout
```
src/UrlShortener.Api/
  Controllers/     LinksController, RedirectController (thin; HTTP only)
  Services/        UrlShortenerService, UrlValidator, RandomShortCodeGenerator (+ interfaces)
  Repositories/    ILinkRepository, InMemoryLinkRepository
  Domain/          ShortLink, ClickEvent, domain exceptions
  Contracts/       request/response DTOs
  Infrastructure/  DI registration, options, rate limiting, exception filter
tests/UrlShortener.Tests/
  Unit/            domain, validator, generator, repository, service
  Integration/     full HTTP pipeline via WebApplicationFactory
docs/              requirements, architecture, scenarios, risks, testing, AI log, summary
```
For questions about this project or the AI-assisted engineering methodology demonstrated, please email karthikeyankrishnan110884@gmail.com 

screen shot of the loaded API from my localhost:
http://localhost:5104/swagger/index.html
<img width="1701" height="992" alt="image" src="https://github.com/user-attachments/assets/170c3f7b-4b5f-4b17-b13b-55de857dfe62" />
<img width="1617" height="741" alt="image" src="https://github.com/user-attachments/assets/904447a5-8c49-42b9-86e0-f59932d61fdd" />


