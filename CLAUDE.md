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

Specs 00 (Fundação), 01 (Identidade), 02 (Catálogo e upload) and 03 (Leitura) are done. 00/01
wired the four projects (DI, `ApplicationDbContext`, CORS, ProblemDetails) and ASP.NET Core
Identity (`Guid` keys) + JWT access tokens + rotated refresh tokens behind `/api/auth` (see
`docs/specs/plans/01-identidade.md` for the as-built design). 02 redesigned `Book` (public vs.
personal visibility), added `IBookRepository`/`BookRepository`, `IFileStorage`/
`LocalFileStorage`, `IBookService`/`BookService`, `BookController` (`/api/books`, all 7
endpoints, streaming with `Range`), and a seed for the public-domain library — see
`docs/specs/plans/02-catalogo-e-upload.md` for the as-built design and the decisions it had to
make beyond the spec text (`IFileStorage` living in `Application` rather than `Domain`,
`IBookService` gaining `OpenCoverAsync`/`DeleteAllOwnedByUserAsync` beyond the spec's literal
contract, etc.). 03 redesigned the `Reading` entities (`Bookmark`/`Highlight`/`Note`, plus new
`ReadingProgress`) to `Guid` with no navigation properties, added the matching repositories,
`IReadingProgressService`/`IBookmarkService`/`IHighlightService`/`INoteService`/`ILibraryService`,
5 controllers (`/api/books/{bookId}/progress`, `/bookmarks`, `/highlights`, `/notes`,
`/api/library`), and made all `Reading` cascade deletion (on `Book` or account removal) a pure
database-FK concern — no application code orchestrates it, unlike the `Book`↔file cascade from
02 — see `docs/specs/plans/03-leitura.md` for the as-built design and decisions (D-03-1 to
D-03-10). 04 (Robustez) is in progress: a common exception hierarchy
(`Application/Common/Exceptions`) now backs a global `IExceptionHandler`, request DTOs carry
`DataAnnotations`, rate limiting gained an `upload` policy + a global per-IP limiter +
`Retry-After`, `GET /health/live`/`GET /health/ready` exist, PDF upload now extracts
`PageCount`/a cover image via `Docnet.Core` and propagates `PageCount` changes to existing
`ReadingProgress` rows, `Book(Title)` has an index, and `Jwt:SigningKey` fails fast at startup —
see `docs/specs/plans/04-robustez.md` for the as-built design, decisions (D-04-1 to D-04-11) and
what's still open (DB-side projection in `BookRepository.QueryAsync`, structured
request-scoped logging, `EXPLAIN`-verified index usage). No test project exists yet (spec 05).

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
(Identity tables with `Guid` keys + `RefreshTokens`), `AddBookCatalog` (`Books`, FK
`OwnerId → AspNetUsers` with cascade delete, CHECK on the `Source`/`OwnerId` invariant),
`AddReadingContext` (`Bookmarks`/`Highlights`/`Notes`/`ReadingProgresses`, FK `BookId → Books`
and `UserId → AspNetUsers` with cascade delete on all four, FK `Notes.HighlightId → Highlights`
with `SET NULL`, unique index on `ReadingProgresses(UserId, BookId)`), and
`AddPerformanceIndexes` (just `CREATE INDEX` on `Books(Title)` — `PageCount`/`CoverImageKey`
already existed from `AddBookCatalog`, so spec 04 needed no new columns despite its checklist
anticipating one).

```bash
dotnet ef migrations add <Name> --project EReader_API.Infra --startup-project EReader_API
dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

## Architecture

Four projects, dependencies point inward toward the domain:

- **EReader_API.Domain** — no dependencies (not even on `ApplicationUser`, which is `Infra`).
  Entities grouped by bounded context under `Entities/` (`Catalog/Book` + `BookSource`/
  `BookScope`/`BookQuery`, `Reading/{Bookmark,Highlight,Note,ReadingProgress}`),
  `Common/PagedResult<T>`, and the repository contracts in `Interfaces/` (`IBookRepository`,
  `IBookmarkRepository`, `IHighlightRepository`, `INoteRepository`,
  `IReadingProgressRepository`). All five repository interfaces now share the same shape:
  `Guid` ids, `CancellationToken` on every method, `AddAsync`/`UpdateAsync`/`RemoveAsync`
  returning `Task` (not `Task<T>`) — `IBookRepository` additionally exposes
  `QueryAsync(BookQuery) -> PagedResult<Book>` for scope/search/sort/pagination and
  `GetByIdsAsync`/`GetOwnedByUserAsync` for batch/owner lookups (spec 02/03);
  `IReadingProgressRepository` additionally exposes `ListByBookAsync` (spec 04, used to
  propagate a `Book.PageCount` change to every existing `ReadingProgress` row for that book).
  `Reading` entities
  hold plain `Guid BookId`/`UserId` fields with **no navigation properties** to `Book` or
  `ApplicationUser` (spec 03) — same reasoning as `Book` itself not navigating to `Reading`.
- **EReader_API.Application** — references Domain **and nothing else that carries a runtime
  dependency on EF/Identity/ASP.NET Core** (spec 04 added `Microsoft.Extensions.Logging
  .Abstractions`/`Microsoft.Extensions.DependencyInjection.Abstractions` package references,
  both dependency-free of EF/Identity/ASP.NET Core, so this still holds). `Common/` holds
  `ClaimsPrincipalExtensions.GetUserId()` (`sub` claim as `Guid`), `ReadingProgressCalculator`
  (pure `PercentComplete`, moved here from `Reading/` in spec 04 so `Catalog/BookService` can
  reuse it too — mirrors `BookMapper`'s reverse move in spec 03) and `Exceptions/`
  (`NotFoundException`/`ForbiddenException`/`ValidationException`/`ConflictException`/
  `UnauthorizedException`, spec 04) — abstract base types that every bounded context's own
  exceptions (`BookNotFoundException`, `ReadingValidationException`, `AuthException`, etc.) now
  inherit from, so `EReader_API/Infrastructure/GlobalExceptionHandler.cs` can map them to HTTP
  status by pattern-matching the base type instead of each controller doing its own
  `try/catch`. `Identity/` holds the auth *ports* (`IAuthService`, `IJwtTokenGenerator`,
  `IRefreshTokenStore`, `IEmailSender` + their DTOs/exceptions); `Storage/` holds `IFileStorage`
  (gained `SaveCoverAsync` in spec 04, mirroring `SaveAsync` but for the generated cover JPEG) +
  `FileStorageOptions` (a plain POCO, shared as-is with `Infra` — see below); `Catalog/` holds
  `IBookService`/`BookService` + `BookMapper` (public `Book -> BookDto` mapping, extracted in
  spec 03 so `Reading/LibraryService` can reuse it) + `IPdfInspector` (spec 04 — `InspectAsync`
  returns page count + an optional rendered-cover JPEG, implemented in `Infra` with
  `Docnet.Core`) + DTOs (`BookDto`/`UploadBookRequest`/`UpdateBookRequest`/`FileDownload`) +
  exceptions (`BookNotFoundException`, `BookValidationException`, `BookForbiddenException` —
  spec 04, thrown by `DeleteAsync` when the target is a `PublicDomain` book); `Reading/` holds
  the four Reading services (`IReadingProgressService`, `IBookmarkService`, `IHighlightService`,
  `INoteService`) + `ILibraryService`/`LibraryService` + `Dtos/` + `ReadingNotFoundException`/
  `ReadingValidationException` (mirror `Catalog`'s exceptions) + `BookAccessGuard` (`internal`,
  shared "book exists and requester can read it" check reused by all four Reading services —
  same rule as `BookService`'s private `GetAuthorizedAsync`, duplicated rather than exposed
  publicly on `IBookService` since only `Reading` needs it). Implementations of the *ports*
  (`IAuthService`, `IFileStorage`, `IPdfInspector`) live in `Infra`, which references
  `Application` to provide them; `BookService`/`Reading` services themselves live here (they
  only depend on `IBookRepository`/`IFileStorage`/`IPdfInspector`/the `Reading` repository
  abstractions, no EF/Identity). Upload DTOs use `Stream`/`string` rather than `IFormFile` — the
  mapping from `IFormFile` happens in the `EReader_API` host controller.
- **EReader_API.Infra** — references Domain **and Application** (needed so `Infra` can
  implement the ports declared in `Application`, e.g. `AuthService : IAuthService` using
  `UserManager<ApplicationUser>`, `LocalFileStorage : IFileStorage`,
  `DocnetPdfInspector : IPdfInspector`). `Context/ApplicationDbContext`
  (`IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, EF Core + Npgsql, plus
  `DbSet<RefreshToken>`, `DbSet<Book>`), `Identity/` (`ApplicationUser : IdentityUser<Guid>` with
  `DisplayName`/`CreatedAt`, `RefreshToken`, `JwtOptions`, `JwtTokenGenerator`,
  `RefreshTokenStore`, `LogEmailSender`, `AuthService` — the latter also calls
  `IBookService.DeleteAllOwnedByUserAsync` before deleting the Identity user, so account
  deletion cascades to the user's `UserUpload` books and files; the `Reading` rows cascade on
  their own via FK, `AuthService` doesn't need to know about them), `Repositories/BookRepository`
  + `ReadingProgressRepository`/`BookmarkRepository`/`HighlightRepository`/`NoteRepository`
  (same no-unit-of-work style: `SaveChangesAsync` per operation; every `Get`/`List` read uses
  `AsNoTracking()` since spec 04 — safe because every write path re-attaches explicitly via
  `Update()`/`Remove()`, none of them relied on the entity already being tracked from a prior
  read), `Storage/LocalFileStorage` (resolves `FileStorage:RootPath` against
  `IHostEnvironment.ContentRootPath`, guards reads against path traversal; `SaveCoverAsync`
  added in spec 04, same shape as `SaveAsync` but keyed `...-cover.jpg`) +
  `Storage/FileStorageHealthCheck` (spec 04 — probes a write+delete under `FileStorage:RootPath`
  for `GET /health/ready`), `Pdf/DocnetPdfInspector` (spec 04 — wraps `Docnet.Core`/PDFium:
  `GetPageCount()` + renders page 0 to a raw bitmap that `SixLabors.ImageSharp` encodes as
  JPEG; note ImageSharp 4.x nags a build-time license warning — see "Configuration" below),
  `Seed/PublicLibrarySeeder` (reads `docs/seed/public-domain.json`, tolerates missing seed PDFs
  by logging and skipping rather than failing startup).
  `OnModelCreating` calls `ApplyConfigurationsFromAssembly`, so entity configs go in this
  assembly as `IEntityTypeConfiguration<T>` classes (`Context/Configurations/`, includes
  `BookConfiguration` with a CHECK constraint enforcing `Source`/`OwnerId` plus indexes on
  `OwnerId`/`Source`/`Title` (the last added in spec 04 — note a plain B-tree index doesn't
  accelerate the existing `ILIKE '%term%'` search; that would need `pg_trgm`, deliberately not
  added yet), and `ReadingProgressConfiguration`/`BookmarkConfiguration`/`HighlightConfiguration`/
  `NoteConfiguration` — all four FK `BookId -> Books` and `UserId -> AspNetUsers` with
  `OnDelete(Cascade)`, `ReadingProgress` additionally unique on `(UserId, BookId)`, `Note`
  additionally FK `HighlightId -> Highlights` with `OnDelete(SetNull)` so removing a `Highlight`
  detaches linked notes instead of deleting them — verified against a live Postgres instance
  that a row reachable via two cascade paths, e.g. `User -> Book -> Reading` and
  `User -> Reading` directly, deletes cleanly; this is disallowed at migration time on SQL
  Server but fine on Postgres). `DependencyInjection.AddInfrastructure` also registers
  `AddHealthChecks().AddDbContextCheck<ApplicationDbContext>().AddCheck<FileStorageHealthCheck>
  ("file_storage")` (spec 04) and binds `JwtOptions` via `AddOptions<JwtOptions>()
  .Validate(...).ValidateOnStart()` instead of plain `Configure<JwtOptions>` (spec 04 — a
  missing `Jwt:SigningKey` now fails the host at startup instead of only on the first JWT
  operation). Also carries an explicit
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — needed because it's a plain
  `Microsoft.NET.Sdk` class library but registers `AddAuthentication`/`AddJwtBearer`, the
  `Microsoft.AspNetCore.RateLimiting` policy (`AddRateLimiter` lives in
  `Microsoft.AspNetCore.Builder`, not `Microsoft.AspNetCore.RateLimiting`), and
  `FormOptions.MultipartBodyLengthLimit` inside `AddInfrastructure`, which only the shared
  framework provides.
- **EReader_API** — the ASP.NET Core host. `Infrastructure/GlobalExceptionHandler` (spec 04)
  implements `IExceptionHandler`, pattern-matching the `Application/Common/Exceptions` base
  types to `404`/`403`/`401`/`409`/`400` (with a field-error dictionary for
  `ValidationException`) and writing a `ProblemDetails` via `IProblemDetailsService`; unmapped
  exceptions return `false` and fall through to the default ASP.NET Core handler (`500`, no
  stack, since there's no `UseDeveloperExceptionPage()` in the pipeline in any environment).
  `Program.cs`'s `AddProblemDetails(options => options.CustomizeProblemDetails = ...)` stamps a
  `traceId` on every `ProblemDetails` response process-wide — exceptions, status code pages, and
  the automatic `[ApiController]` `400 ValidationProblemDetails` alike. Controllers in
  `Controllers/` no longer `try/catch` domain exceptions (removed in spec 04 — they let them
  propagate to the global handler): `AuthController` → `/api/auth/*`,
  `[EnableRateLimiting("auth")]`, policy applies 10 req/min per IP; `BookController` →
  `/api/books/*`, `[Authorize]` on all 7 endpoints, `Upload` additionally
  `[EnableRateLimiting("upload")]` (20/hour per user, spec 04), maps `IFormFile` →
  `UploadBookRequest`; `ReadingProgressController` → `/api/books/{bookId}/progress`;
  `BookmarksController`/`HighlightsController`/`NotesController` — no controller-level
  `[Route]`, each `[Http*]` carries its full path instead, because each resource mixes endpoints
  nested under `/api/books/{bookId}/...` with flat ones under `/api/{resource}/{id}` (spec 03,
  D-03-7); `LibraryController` → `/api/library`). All `Reading` controllers `[Authorize]`.
  Request DTOs across all controllers carry `DataAnnotations` (spec 04) — `[ApiController]`'s
  built-in automatic model validation turns a failed attribute into `400`
  `ValidationProblemDetails` with per-field `errors`, so no custom validation filter was needed;
  note record constructor parameters take the attributes **without** a `[property: ...]` target
  (`record Foo([Required] string X)`) — with the target, ASP.NET Core throws
  `InvalidOperationException` at request time instead of silently ignoring it, since it can't
  associate property-targeted metadata with the record's parameter-based `ModelMetadata`.
  `Program.cs`'s middleware order matters: `UseRateLimiter()` runs **after**
  `UseAuthentication()`/`UseAuthorization()`, not before — the `"upload"` policy partitions by
  `httpContext.User.GetUserId()`, which needs `User` already populated with JWT claims.
  `GET /health/live` (`Predicate = _ => false`, no checks — pure liveness) and
  `GET /health/ready` (runs every registered check) are mapped alongside `MapControllers()`.
  Namespace here is `EReader_API.*`, matching the other projects.

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
bytes, `FileStorage:MaxUploadBytes`) all funnels through `BookValidationException` (now a
`ValidationException` subclass, `Application/Common/Exceptions`), mirroring
`IdentityValidationException` from Auth. `UploadAsync` (spec 04) extracts `PageCount` and a
cover image from the saved PDF via `IPdfInspector` right after `fileStorage.SaveAsync` — wrapped
in a broad `try/catch` that logs and falls back to the form-supplied `PageCount` (or `null`) on
failure, never blocking the upload; on success the extracted `PageCount` **overrides** the form
value (more authoritative than what the uploader typed) and `CoverImageKey` gets set via the new
`IFileStorage.SaveCoverAsync`. Both `UploadAsync` and `UpdateAsync` then propagate any
`PageCount` change to every existing `ReadingProgress` row for that book
(`IReadingProgressRepository.ListByBookAsync`, recalculating `TotalPages`/`PercentComplete`
through `Common/ReadingProgressCalculator`) — otherwise a book's progress rows would keep a
stale `PageCount` from before the extraction ran or an edit changed it. `DeleteAsync` also
rejects deleting a `PublicDomain` book with `BookForbiddenException` (`403`, spec 04) — only a
`UserUpload`'s own owner can delete their book; deleting a public-catalog book used to be open
to any authenticated user and would cascade away every other user's progress/annotations on it
(this was the CLAUDE.md-flagged gap from spec 02/03, closed here).

### Reading

Progress (`ReadingProgress`), bookmarks, highlights and notes, all scoped to `(UserId, BookId)`
with no navigation properties. Every operation first calls `BookAccessGuard.EnsureAccessibleAsync`
(same "public, or own `UserUpload`" rule as `Catalog`) — a book the requester can't read, and a
`Reading` row that exists but belongs to someone else, both surface as `404`
(`ReadingNotFoundException`), same "don't leak existence" pattern as `Catalog`. `PUT
/api/books/{id}/progress` is an upsert on the unique `(UserId, BookId)` index —
`ReadingProgressRepository.UpsertAsync` re-fetches by `Id` before deciding `Add` vs.
`CurrentValues.SetValues`, since the entity the service passes in is never already tracked by
the `DbContext`. `CurrentPage` must be in `[1, Book.PageCount]` when `PageCount` is known
(`400` otherwise); `PercentComplete` is `0` when `PageCount` is `null`, via the pure
`ReadingProgressCalculator.PercentComplete`. `Note.HighlightId` is validated against the same
user + `BookId` at create time; deleting the `Highlight` doesn't delete the note — the FK
`OnDelete(SetNull)` detaches it. Deleting a `Book` or an `ApplicationUser` cascades all four
`Reading` tables purely via FK `ON DELETE CASCADE` — no code in `BookService`/`AuthService`
orchestrates it (contrast with the `Book`↔file cascade from spec 02, which needs application
code because a file on disk has no FK). `GET /api/library` (`LibraryService`) unions the
requester's owned books with any book (owned or public) that has `ReadingProgress`, batching the
public ones via `IBookRepository.GetByIdsAsync` to avoid N+1, sorted by `progress.LastReadAt`
desc (books never opened sort last). The `BookService.DeleteAsync` gap this section used to
flag (any authenticated user could delete a `PublicDomain` book) is fixed as of spec 04 — see
"Catalog" above.

## Configuration

The database connection string is read from `ConnectionStrings__Default` (set as an env var in
`docker-compose.yml`); there is no connection string in `appsettings*.json`. Local development
secrets use user-secrets (`UserSecretsId` in `EReader.API.csproj`). `FileStorage:RootPath`/
`MaxUploadBytes` are plain (non-secret) config in `appsettings.json`; the Docker Compose `api`
service mounts a named volume (`ereader-files`) at `/app/App_Data/books` so uploads survive
`docker compose up --build`. `Jwt:SigningKey` fails the app at startup if missing/blank (spec
04, `ValidateOnStart()` — see "Infra" above); `ConnectionStrings:Default` already did (spec 00).
`FileStorage:RootPath` itself is never "missing" (has a code default) — its *writability* is a
`GET /health/ready` concern (`FileStorageHealthCheck`), not a startup-config one.

Rate limiting (`Infra/DependencyInjection.AddInfrastructure`, spec 01/04): named policies
`"auth"` (10/min per IP) and `"upload"` (20/hour per user), plus an unnamed `GlobalLimiter`
(100/min per IP) that runs in addition to whichever named policy applies — so an upload request
is checked against both `"upload"` and the global limiter. `OnRejected` explicitly copies the
rejected lease's `RetryAfter` metadata into the `Retry-After` response header; this is **not**
automatic from `RejectionStatusCode` alone. `app.UseRateLimiter()` runs after
`UseAuthentication()`/`UseAuthorization()` in `Program.cs` (needed so the `"upload"` policy's
`User.GetUserId()` partition key sees populated JWT claims).

`GET /health/live` and `GET /health/ready` (spec 04) are unauthenticated, mapped in `Program.cs`
next to `MapControllers()`.

PDF extraction (spec 04, `Infra/Pdf/DocnetPdfInspector`) depends on `Docnet.Core` (wraps
PDFium; nupkg ships native binaries under a generic `runtimes/linux/native/pdfium.so` — the RID
graph resolves that for `linux-x64` at publish, matching the Debian-based
`mcr.microsoft.com/dotnet/aspnet:10.0` image the `Dockerfile` builds on, but this has only been
confirmed by inspecting the nupkg's RID folder, not by an actual `docker compose up --build` +
upload run) and `SixLabors.ImageSharp` (encodes the raw bitmap `Docnet.Core` returns to JPEG —
needed because `System.Drawing.Common` is Windows-only). **ImageSharp 4.x prints a build-time
"No Six Labors license found" warning** (it doesn't fail the build) — the Six Labors Split
License is free for most individual/small-revenue use but technically requires registering a
community license key to silence the nag; worth a conscious decision before shipping, not
something this session resolved.
