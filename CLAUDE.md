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

Early scaffold. Most of the intended structure exists (Clean Architecture layers, domain
entities, repository/service interfaces) but the wiring is incomplete: `Program.cs` registers
only controllers + OpenAPI, the API project has **no reference to Application or Infra**, DI
for services/repositories is not set up, `ApplicationDbContext` is never registered, ASP.NET
Identity is not configured (despite `app.UseAuthorization()`), `BookController` is empty, and
`BookService` methods throw `NotImplementedException`. Expect to add plumbing, not just
features.

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

EF Core migrations: not set up yet. `dotnet-ef` is not installed and no
`Microsoft.EntityFrameworkCore.Design` reference exists. Before migrations can be generated,
the API project must reference `EReader_API.Infra` and register `ApplicationDbContext`. The
intended commands once that's done:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Name> --project EReader_API.Infra --startup-project EReader_API
dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

## Architecture

Four projects, dependencies point inward toward the domain:

- **EReader_API.Domain** — no dependencies. Entities grouped by bounded context under
  `Entities/` (`Catalog/Book`, `Identity/User`, `Reading/{Bookmark,Highlight,Note}`) and the
  repository contracts in `Interfaces/` (`IBookRepository`, etc.). Every repository interface
  follows the same shape: `Get*Async` / `GetByIdAsync(int?)` / `CreateAsync` / `UpdateAsync` /
  `RemoveAsync`, all returning the entity (or a collection).
- **EReader_API.Application** — references Domain only. `Interfaces/IBookService` +
  `Services/BookService`. Services depend on domain repository interfaces via constructor
  injection.
- **EReader_API.Infra** — references Domain only. `Context/ApplicationDbContext`
  (`IdentityDbContext<ApplicationUser>`, EF Core + Npgsql PostgreSQL), `Repositories/`
  (implementations of the domain interfaces), `Identity/ApplicationUser` (`: IdentityUser`).
  `OnModelCreating` calls `ApplyConfigurationsFromAssembly`, so entity configs go in this
  assembly as `IEntityTypeConfiguration<T>` classes.
- **EReader_API** — the ASP.NET Core host. Controllers in `Controllers/`. Namespace here is
  `EReader.API.*` (note: not `EReader_API.*` like the other projects).

### Two parallel user models

`Domain/Entities/Identity/User` is a plain POCO with `int Id` and a `Password` string, and the
`Reading` entities reference it via `int UserId`. `Infra/Identity/ApplicationUser : IdentityUser`
is the ASP.NET Identity user with a `string` key. These are not reconciled yet — decide which
is authoritative before building auth or anything that links reading data to users.

## Configuration

The database connection string is read from `ConnectionStrings__Default` (set as an env var in
`docker-compose.yml`); there is no connection string in `appsettings*.json`. Local development
secrets use user-secrets (`UserSecretsId` in `EReader.API.csproj`).
