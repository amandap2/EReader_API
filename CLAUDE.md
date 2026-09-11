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

Specs 00 (Fundação) and 01 (Identidade) are done: the four projects are wired (DI,
`ApplicationDbContext`, CORS, ProblemDetails), and ASP.NET Core Identity (`Guid` keys) + JWT
access tokens + rotated refresh tokens are implemented behind `/api/auth` (see
`docs/specs/plans/01-identidade.md` for the as-built design and the decisions it had to make
beyond the spec text). Still open: `Book`/`BookController` don't exist yet — spec 00 removed
the non-compiling stubs, spec 02 reintroduces `Book` with the redesigned schema. No test
project exists yet (spec 05).

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
(Identity tables with `Guid` keys + `RefreshTokens`).

```bash
dotnet ef migrations add <Name> --project EReader_API.Infra --startup-project EReader_API
dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

## Architecture

Four projects, dependencies point inward toward the domain:

- **EReader_API.Domain** — no dependencies (not even on `ApplicationUser`, which is `Infra`).
  Entities grouped by bounded context under `Entities/` (`Catalog/Book`,
  `Reading/{Bookmark,Highlight,Note}`) and the repository contracts in `Interfaces/`
  (`IBookmarkRepository`, etc.). Every repository interface follows the same shape:
  `Get*Async` / `GetByIdAsync(int?)` / `CreateAsync` / `UpdateAsync` / `RemoveAsync`, all
  returning the entity (or a collection). `Reading` entities hold a plain `Guid UserId` with no
  navigation property (spec 03 owns their full redesign; spec 01 only changed the FK type so
  the build didn't depend on the now-removed `Domain/Entities/Identity/User`).
- **EReader_API.Application** — references Domain **and nothing else that carries a runtime
  dependency on EF/Identity**. `Identity/` holds the auth *ports* (`IAuthService`,
  `IJwtTokenGenerator`, `IRefreshTokenStore`, `IEmailSender` + their DTOs/exceptions) —
  implementations live in `Infra`, which references `Application` to provide them (see below).
  `Common/ClaimsPrincipalExtensions.GetUserId()` reads the `sub` claim as a `Guid`. No `Book`
  service exists yet (spec 02).
- **EReader_API.Infra** — references Domain **and Application** (needed so `Infra` can
  implement the ports declared in `Application`, e.g. `AuthService : IAuthService` using
  `UserManager<ApplicationUser>`). `Context/ApplicationDbContext`
  (`IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, EF Core + Npgsql, plus
  `DbSet<RefreshToken>`), `Identity/` (`ApplicationUser : IdentityUser<Guid>` with
  `DisplayName`/`CreatedAt`, `RefreshToken`, `JwtOptions`, `JwtTokenGenerator`,
  `RefreshTokenStore`, `LogEmailSender`, `AuthService`). `OnModelCreating` calls
  `ApplyConfigurationsFromAssembly`, so entity configs go in this assembly as
  `IEntityTypeConfiguration<T>` classes (`Context/Configurations/`). Also carries an explicit
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — needed because it's a plain
  `Microsoft.NET.Sdk` class library but registers `AddAuthentication`/`AddJwtBearer` and the
  `Microsoft.AspNetCore.RateLimiting` policy (`AddRateLimiter` lives in
  `Microsoft.AspNetCore.Builder`, not `Microsoft.AspNetCore.RateLimiting`) inside
  `AddInfrastructure`, which only the shared framework provides.
- **EReader_API** — the ASP.NET Core host. Controllers in `Controllers/`
  (`AuthController` → `/api/auth/*`, `[EnableRateLimiting("auth")]`, policy applies 10 req/min
  per IP). Namespace here is `EReader_API.*`, matching the other projects.

### Identity

Single user model: `Infra/Identity/ApplicationUser : IdentityUser<Guid>`. JWT access tokens
(HMAC-SHA256, claims `sub`/`email`/`name`/`jti`) + opaque refresh tokens (SHA-256 hashed at
rest in `RefreshTokens`, rotated on `/refresh`, reuse of an already-rotated token revokes every
active token for that user). `Jwt:SigningKey` is a secret (user-secrets locally / env var
`Jwt__SigningKey` in containers) — never in `appsettings*.json`.

## Configuration

The database connection string is read from `ConnectionStrings__Default` (set as an env var in
`docker-compose.yml`); there is no connection string in `appsettings*.json`. Local development
secrets use user-secrets (`UserSecretsId` in `EReader.API.csproj`).
