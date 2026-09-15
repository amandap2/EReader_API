# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

Specs (pt-BR), derived from the original handwritten notes:
- `docs/ESPECIFICACAO-BACKEND.md` — reference: domain model, bounded contexts, full REST
  surface, phased roadmap, open decisions. Its "Divergências em relação ao código atual"
  section lists what the current scaffold still needs.
- `docs/specs/` — implementation specs, one per work unit, executed in order
  (`00-fundacao` → `01-identidade` → `02-catalogo-e-upload` → `03-leitura` → `04-robustez`,
  with `05-testes` in parallel). Each has a checklist and verifiable acceptance criteria; see
  `docs/specs/README.md`.

Read the relevant spec before implementing.

Specs 00 (Fundação), 01 (Identidade) and 02 (Catálogo e upload) are done. 00/01 wired the four
projects (DI, `ApplicationDbContext`, CORS, ProblemDetails) and ASP.NET Core Identity (`Guid`
keys) + JWT access tokens + rotated refresh tokens behind `/api/auth` (see
`docs/specs/plans/01-identidade.md` for the as-built design). 02 redesigned `Book` (public vs.
personal visibility), added `IBookRepository`/`BookRepository`, `IFileStorage`/
`LocalFileStorage`, `IBookService`/`BookService`, `BookController` (`/api/books`, all 7
endpoints, streaming with `Range`), and a seed for the public-domain library — see
`docs/specs/plans/02-catalogo-e-upload.md` for the as-built design and the decisions it had to
make beyond the spec text (`IFileStorage` living in `Application` rather than `Domain`,
`IBookService` gaining `OpenCoverAsync`/`DeleteAllOwnedByUserAsync` beyond the spec's literal
contract, etc.). No test project exists yet (spec 05).

## Commands

Uses .NET 10 SDK. The solution file is `EReader.slnx` (new XML solution format).

```bash
dotnet build EReader.slnx                 # build all four projects
dotnet run --project EReader_API          # run the API (default profile: http, http://localhost:5035)
dotnet run --project EReader_API --launch-profile https   # https://localhost:7010
```

Run the full stack (API + PostgreSQL 17) in containers:

```bash
docker compose -f EReader_API/docker-compose.yml up --build   # API on :8080, Postgres on :5432
```

Tests: no test project exists yet. When adding one, wire it into `EReader.slnx` and run with
`dotnet test`.

EF Core migrations: `dotnet-ef` is installed globally and `EReader_API.Infra` has the design
package + `ApplicationDbContext` registered. Current migrations: `AddIdentityAndRefreshTokens`
(Identity tables with `Guid` keys + `RefreshTokens`) and `AddBookCatalog` (`Books`, FK
`OwnerId → AspNetUsers` with cascade delete, CHECK on the `Source`/`OwnerId` invariant).

```bash
dotnet ef migrations add <Name> --project EReader_API.Infra --startup-project EReader_API
dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

## Architecture

Four projects, dependencies point inward toward the domain:

- **EReader_API.Domain** — no dependencies (not even on `ApplicationUser`, which is `Infra`).
  Entities grouped by bounded context under `Entities/` (`Catalog/Book` + `BookSource`/
  `BookScope`/`BookQuery`, `Reading/{Bookmark,Highlight,Note}`), `Common/PagedResult<T>`, and
  the repository contracts in `Interfaces/` (`IBookRepository`, `IBookmarkRepository`, etc.).
  `IBookRepository` does **not** follow the older `Get*Async`/`GetByIdAsync(int?)` shape still
  used by the `Reading` repositories — it takes `Guid` ids and exposes `QueryAsync(BookQuery)
  -> PagedResult<Book>` for scope/search/sort/pagination, plus `GetOwnedByUserAsync` for the
  account-deletion cascade (spec 02). `Reading` entities hold a plain `Guid UserId` with no
  navigation property (spec 03 owns their full redesign; spec 01 only changed the FK type so
  the build didn't depend on the now-removed `Domain/Entities/Identity/User`).
- **EReader_API.Application** — references Domain **and nothing else that carries a runtime
  dependency on EF/Identity/ASP.NET Core**. `Identity/` holds the auth *ports* (`IAuthService`,
  `IJwtTokenGenerator`, `IRefreshTokenStore`, `IEmailSender` + their DTOs/exceptions);
  `Storage/` holds `IFileStorage` + `FileStorageOptions` (a plain POCO, shared as-is with
  `Infra` — see below); `Catalog/` holds `IBookService`/`BookService` + DTOs
  (`BookDto`/`UploadBookRequest`/`UpdateBookRequest`/`FileDownload`) + exceptions
  (`BookNotFoundException`, `BookValidationException`). Implementations of the *ports*
  (`IAuthService`, `IFileStorage`) live in `Infra`, which references `Application` to provide
  them; `BookService` itself lives here (it only depends on `IBookRepository`/`IFileStorage`
  abstractions, no EF/Identity). `Common/ClaimsPrincipalExtensions.GetUserId()` reads the `sub`
  claim as a `Guid`. Upload DTOs use `Stream`/`string` rather than `IFormFile` — the mapping
  from `IFormFile` happens in the `EReader_API` host controller.
- **EReader_API.Infra** — references Domain **and Application** (needed so `Infra` can
  implement the ports declared in `Application`, e.g. `AuthService : IAuthService` using
  `UserManager<ApplicationUser>`, `LocalFileStorage : IFileStorage`). `Context/ApplicationDbContext`
  (`IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, EF Core + Npgsql, plus
  `DbSet<RefreshToken>`, `DbSet<Book>`), `Identity/` (`ApplicationUser : IdentityUser<Guid>` with
  `DisplayName`/`CreatedAt`, `RefreshToken`, `JwtOptions`, `JwtTokenGenerator`,
  `RefreshTokenStore`, `LogEmailSender`, `AuthService` — the latter also calls
  `IBookService.DeleteAllOwnedByUserAsync` before deleting the Identity user, so account
  deletion cascades to the user's `UserUpload` books and files), `Repositories/BookRepository`,
  `Storage/LocalFileStorage` (resolves `FileStorage:RootPath` against
  `IHostEnvironment.ContentRootPath`, guards reads against path traversal),
  `Seed/PublicLibrarySeeder` (reads `docs/seed/public-domain.json`, tolerates missing seed PDFs
  by logging and skipping rather than failing startup). `OnModelCreating` calls
  `ApplyConfigurationsFromAssembly`, so entity configs go in this assembly as
  `IEntityTypeConfiguration<T>` classes (`Context/Configurations/`, includes `BookConfiguration`
  with a CHECK constraint enforcing `Source`/`OwnerId`). Also carries an explicit
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — needed because it's a plain
  `Microsoft.NET.Sdk` class library but registers `AddAuthentication`/`AddJwtBearer`, the
  `Microsoft.AspNetCore.RateLimiting` policy (`AddRateLimiter` lives in
  `Microsoft.AspNetCore.Builder`, not `Microsoft.AspNetCore.RateLimiting`), and
  `FormOptions.MultipartBodyLengthLimit` inside `AddInfrastructure`, which only the shared
  framework provides.
- **EReader_API** — the ASP.NET Core host. Controllers in `Controllers/`
  (`AuthController` → `/api/auth/*`, `[EnableRateLimiting("auth")]`, policy applies 10 req/min
  per IP; `BookController` → `/api/books/*`, `[Authorize]` on all 7 endpoints, maps
  `IFormFile` → `UploadBookRequest`). Namespace here is `EReader_API.*`, matching the other
  projects.

### Identity

Single user model: `Infra/Identity/ApplicationUser : IdentityUser<Guid>`. JWT access tokens
(HMAC-SHA256, claims `sub`/`email`/`name`/`jti`) + opaque refresh tokens (SHA-256 hashed at
rest in `RefreshTokens`, rotated on `/refresh`, reuse of an already-rotated token revokes every
active token for that user). `Jwt:SigningKey` is a secret (user-secrets locally / env var
`Jwt__SigningKey` in containers) — never in `appsettings*.json`.

### Catalog

`Book.Source` (`PublicDomain` | `UserUpload`) drives visibility: `PublicDomain` ⇒ `OwnerId ==
null`, readable by any authenticated user; `UserUpload` ⇒ `OwnerId != null`, readable only by
its owner (enforced in `BookService`, not a policy/handler — a book that doesn't exist and one
that exists but belongs to someone else both surface as `404`, so existence is never leaked).
Files live on disk under `FileStorage:RootPath` (default `App_Data/books`, outside `wwwroot`),
keyed by an opaque `fileKey` generated by `LocalFileStorage` (`{yyyy}/{MM}/{Guid:N}.pdf`) —
`Book.FileKey`/`CoverImageKey` never reach the API response (`BookDto` only exposes a
`HasCoverImage` bool). `GET /api/books/{id}/file` streams via `File(..., enableRangeProcessing:
true)`, so `Range` requests get `206`/`Content-Range` from ASP.NET Core directly. Upload
validation (title length, `PageCount >= 1`, `Content-Type: application/pdf`, `%PDF-` magic
bytes, `FileStorage:MaxUploadBytes`) all funnels through `BookValidationException`, mirroring
`IdentityValidationException` from Auth. No spec-02 flow ever sets `CoverImageKey` — cover
extraction is spec 04's job (`GET .../cover` always `404` until then).

## Configuration

The database connection string is read from `ConnectionStrings__Default` (set as an env var in
`docker-compose.yml`); there is no connection string in `appsettings*.json`. Local development
secrets use user-secrets (`UserSecretsId` in `EReader.API.csproj`). `FileStorage:RootPath`/
`MaxUploadBytes` are plain (non-secret) config in `appsettings.json`; the Docker Compose `api`
service mounts a named volume (`ereader-files`) at `/app/App_Data/books` so uploads survive
`docker compose up --build`.
