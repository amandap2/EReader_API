# Plano de implementação — Spec 03: Leitura (core domain)

> Plano de execução para [`../03-leitura.md`](../03-leitura.md). Depende das specs 00, 01 e 02
> (concluídas). Objetivo: contexto `Reading` completo — `ReadingProgress`/`Bookmark`/
> `Highlight`/`Note` redesenhados (`Guid`, sem navigation properties), repositórios, serviços de
> caso de uso, 5 controllers (`ReadingProgress`, `Bookmarks`, `Highlights`, `Notes`, `Library`),
> `GET /api/library`, cascata de exclusão via FK do banco, migration `AddReadingContext`.

## 0. Estado atual (verificado)

- `EReader_API.Domain/Entities/Reading/{Bookmark,Highlight,Note}.cs` existem mas estão no
  formato antigo pré-spec-01: namespace em bloco (`namespace X { }`), `int Id`, navigation
  property `Book Book` (hoje aponta para o `Book` já redesenhado da spec 02, então compila, mas
  não é o modelo que esta spec pede), sem `CreatedAt`/`UpdatedAt`. `Note.Content` ainda se chama
  `Description`. Não existe `ReadingProgress` em lugar nenhum.
- `EReader_API.Domain/Interfaces/{IBookmarkRepository,IHighlightRepository,INoteRepository}.cs`
  existem no formato antigo citado no `CLAUDE.md`: `GetXAsync()` sem parâmetros,
  `GetByIdAsync(int? id)`, `Create/Update/RemoveAsync` devolvendo `Task<T>`. Não têm
  implementação em `Infra` — `EReader_API.Infra/Repositories/` só tem `BookRepository.cs`.
  Nenhum dos três é usado em nenhum lugar do código (não há DI, não há controller).
- `EReader_API.Application/` não tem pasta `Reading/`. `Application/Catalog/BookService.cs` tem
  um `GetAuthorizedAsync` **privado** que já implementa exatamente a checagem de acesso que esta
  spec precisa reaplicar ("dono ou público → OK, senão 404") — ver [D-03-4](#d-03-4).
  `Application/Catalog/Dtos/BookDto.cs` expõe um mapeamento `Book → BookDto` hoje só acessível
  como método privado estático dentro de `BookService` — ver [D-03-5](#d-03-5).
- `EReader_API.Infra/Context/ApplicationDbContext.cs` tem `DbSet<RefreshToken>` e
  `DbSet<Book>`; nenhum `DbSet` de `Reading`. `OnModelCreating` já chama
  `ApplyConfigurationsFromAssembly` — as 4 novas `IEntityTypeConfiguration<>` são pegas
  automaticamente sem registro manual.
- `Book` (spec 02) não tem navigation property para `Reading` nem vice-versa — ambos os lados
  continuam sem nav property depois desta spec (ver [D-03-2](#d-03-2)).
- `BookService.DeleteAsync`/`DeleteAllOwnedByUserAsync` e `AuthService.DeleteAccountAsync`
  (Infra/Identity) **não precisam de nenhuma mudança** nesta spec — a cascata de `Reading` é
  inteiramente responsabilidade de FK no banco, diferente da cascata de `Book`↔arquivo em disco
  que a spec 02 precisou orquestrar em código (D-02-4). Ver [D-03-3](#d-03-3) — isso diverge do
  texto literal da spec ("Arquivos afetados" lista esses dois arquivos); este plano não os toca.
- Migrations atuais: `AddIdentityAndRefreshTokens`, `AddBookCatalog`. Nenhuma migration de
  `Reading` existe.
- Nenhum projeto de teste existe ainda (spec 05) — os itens de teste do checklist da spec ficam
  fora deste plano (mesmo tratamento dado pela spec 02).

## 1. Pré-condições

- Specs 00, 01 e 02 concluídas: build verde, migrations `AddIdentityAndRefreshTokens` e
  `AddBookCatalog` aplicadas, `/api/auth/*` e `/api/books/*` funcionais (necessário para ter um
  Bearer token e pelo menos um `Book` — público ou próprio — para exercitar `/api/books/{id}/...`
  desta spec).
- Banco Postgres acessível (mesmo `docker compose` ou instância local já usada nas specs
  anteriores).

## 2. Decisões a confirmar antes de começar

A spec deixa a forma exata dos repositórios/serviços em aberto ("cada um com listar por
`(UserId, BookId)`, obter por `Id`, add, update, remove" — sem assinatura). As decisões abaixo
fixam isso antes de codar.

| # | Decisão | Recomendação | Impacto se não seguir |
|---|---|---|---|
| D-03-1 | Forma dos repositórios de `Reading` | Reescrever `IBookmarkRepository`/`IHighlightRepository`/`INoteRepository` no formato moderno já estabelecido por `IBookRepository` (spec 02): `Guid`, `CancellationToken ct` em todo método, `AddAsync`/`UpdateAsync`/`RemoveAsync` devolvendo `Task` (não `Task<T>`). `IReadingProgressRepository` novo, mesmo estilo. `Bookmark` não ganha `UpdateAsync` (não há `PATCH /bookmarks/{id}` na tabela de endpoints). | Manter o formato antigo (`Task<T>` de volta, `GetByIdAsync(int?)`) reintroduz a inconsistência que o `CLAUDE.md` já documenta como pendência — pior, mistura com `Guid` gera erro de compilação, não só inconsistência de estilo. |
| D-03-2 | Sem navigation properties | `Bookmark`/`Highlight`/`Note`/`ReadingProgress` mantêm `Guid BookId`/`Guid UserId` como campos simples, sem `Book Book` nem nav para `ApplicationUser` — remove o `Book Book` hoje presente em `Bookmark`/`Highlight`. Consistente com a frase já no `CLAUDE.md` ("`Reading` entities hold a plain `Guid UserId` with no navigation property") e com o próprio `Book`, que também não tem nav para `Reading`. | Nav para `Book` funcionaria (mesmo assembly `Domain`), mas convida a `Include()` acidental e xtorna o EF Core responsável por lazy-loading que este projeto não usa em nenhum outro lugar — inconsistente com o resto do código. |
| D-03-3 | Cascata de exclusão é 100% FK de banco | `OnDelete(Cascade)` em `BookId → Books` e `UserId → AspNetUsers` nas 4 configurações; `OnDelete(SetNull)` em `Note.HighlightId → Highlights`. **Nenhuma** mudança em `BookService`/`AuthService` — ao contrário da cascata de `Book`↔arquivo (D-02-4), aqui não há recurso externo (arquivo em disco) para orquestrar, só linhas de tabela; o Postgres resolve isso sozinho no `DELETE`. | Reimplementar a cascata em código (como D-02-4 fez) seria trabalho redundante e arrisca esquecer uma das 4 entidades — a spec pede exatamente o comportamento que `ON DELETE CASCADE` já dá de graça. |
| D-03-4 | Checagem de acesso ao `Book` compartilhada | `internal static class BookAccessGuard` em `Application/Reading`, com `EnsureAccessibleAsync(IBookRepository, bookId, requesterId, ct) → Book` reaplicando a mesma regra que `BookService.GetAuthorizedAsync` usa (`book is null \|\| (Source == UserUpload && OwnerId != requesterId)` → `ReadingNotFoundException`). Usado pelos 4 serviços de `Reading`. | Alternativa rejeitada: expor esse método publicamente em `IBookService` — polui o contrato de `Catalog` com algo que o próprio `Catalog` nunca chama, só para servir a outro bounded context. Duplicar a lógica em 4 lugares sem extrair funcionaria, mas qualquer ajuste futuro na regra (ex. livros compartilhados) exigiria editar 4 arquivos. |
| D-03-5 | Mapeamento `Book → BookDto` compartilhado | Extrair o `ToDto(Book)` hoje privado em `BookService` para `public static class BookMapper` em `Application/Catalog` (mesmo namespace). `BookService` e o novo `LibraryService` (`Application/Reading`) chamam `BookMapper.ToDto(book)`. | Duplicar o mapeamento de 6 campos em `LibraryService` funciona, mas diverge silenciosamente do original se `BookDto` ganhar um campo novo em outra spec e só um dos dois lugares for atualizado. |
| D-03-6 | Busca em lote de `Book` para `GET /api/library` | `IBookRepository` ganha `Task<IReadOnlyList<Book>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)` — usado para buscar livros públicos com progresso que não estão entre os `GetOwnedByUserAsync` do usuário. | Sem isso, `LibraryService` faria um `GetByIdAsync` por item (N+1) — funciona para o volume do MVP, mas é o tipo de N+1 óbvio e barato de evitar com um método a mais no mesmo repositório. |
| D-03-7 | Rotas mistas de `Bookmarks`/`Highlights`/`Notes` | Sem `[Route]` no nível do controller — cada `[Http*]` leva o caminho completo (`[HttpGet("api/books/{bookId:guid}/bookmarks")]`, `[HttpDelete("api/bookmarks/{id:guid}")]`, etc.), porque cada um desses 3 recursos mistura 2 endpoints aninhados (`list`/`create` sob `/api/books/{bookId}/...`) com 1-2 endpoints "soltos" (`delete`/`patch` sob `/api/{recurso}/{id}`) — não há prefixo único que sirva para os dois. `ReadingProgressController` (100% aninhado) e `LibraryController` (rota única) continuam usando `[Route]` normalmente. | Forçar um prefixo comum (ex. só `api/`) e repetir o resto em cada ação dá no mesmo resultado, mas com mais ruído por ação; o formato acima é o que ASP.NET Core recomenda para controllers com rotas heterogêneas. |
| D-03-8 | Exceções de `Reading` | `ReadingNotFoundException` (sem parâmetro, mensagem fixa) cobre tanto "livro inacessível" quanto "recurso de `Reading` inexistente/de outro usuário" — mesmo raciocínio de "não vazar existência" da D-02-5. `ReadingValidationException(IEnumerable<string> errors)` espelha `BookValidationException`. | Ter uma exceção por caso (ex. `BookNotAccessibleException` separada de `BookmarkNotFoundException`) não muda o `404` observável e só multiplica tipos sem necessidade. |
| D-03-9 | Ordenação de `GET /api/library` | `OrderByDescending(x => x.Progress?.LastReadAt ?? DateTime.MinValue)` — itens sem progresso (upload próprio nunca aberto) ficam por último; sem critério de desempate documentado além desse, porque nenhum critério de aceite pede um segundo campo de ordenação. | Inventar um desempate (ex. título) não é errado, mas é escopo não pedido; se algum critério de aceite futuro exigir, ajusta-se então. |
| D-03-10 | Validação de `PageNumber` em `Bookmark`/`Highlight` | Só `>= 1` (mesmo texto do modelo: `// >= 1`). Sem limite superior contra `Book.PageCount` — a seção "Regras" da spec só define esse limite para `ReadingProgress.CurrentPage`, e `PageCount` pode ser `null`. | Adicionar essa checagem para bookmarks/highlights não é pedido por nenhum critério de aceite e criaria um comportamento a mais para justificar/testar sem necessidade. |

Este plano assume as recomendações de D-03-1 a D-03-10.

## 3. Ordem de execução

```
P1 baseline
   ──▶ P2 Domain (entidades Reading reescritas + ReadingProgress + 4 interfaces de repositório)
   ──▶ P3 Application/Catalog (BookMapper extraído, D-03-5) + IBookRepository.GetByIdsAsync (D-03-6)
   ──▶ P4 Application/Reading — base (exceções, BookAccessGuard, ReadingProgressCalculator, DTOs)
   ──▶ P5 Application/Reading — serviços (Progress/Bookmark/Highlight/Note/Library)
   ──▶ P6 Infra/Repositories (4 repositórios novos + BookRepository.GetByIdsAsync)
   ──▶ P7 Infra/Context (DbSets + 4 IEntityTypeConfiguration<>)
   ──▶ P8 DI (Application/DependencyInjection + Infra/DependencyInjection)
   ──▶ P9 Controllers (ReadingProgress, Bookmarks, Highlights, Notes, Library)
   ──▶ P10 migration AddReadingContext
   ──▶ P11 verificação final
```

Commits pequenos sugeridos: (P2), (P3+P4), (P5), (P6+P7), (P8+P9), (P10).

---

## 4. Passo a passo

### P1 — Baseline

`dotnet build EReader.slnx` → deve estar verde (specs 00/01/02 concluídas). Parar e investigar
se não estiver.

### P2 — `Domain`

1. Reescrever `Entities/Reading/Bookmark.cs` e `Highlight.cs` (namespace com `;`, `Guid Id`, sem
   nav `Book Book`, campos novos — ver [5.1](#51-domainentitiesreading)).
2. Reescrever `Entities/Reading/Note.cs` (`Content` no lugar de `Description`, `HighlightId`,
   `UpdatedAt`).
3. Criar `Entities/Reading/ReadingProgress.cs`.
4. Reescrever as 3 interfaces existentes + criar `IReadingProgressRepository.cs` em
   `Domain/Interfaces` (D-03-1, ver [5.2](#52-domaininterfaces)).
5. `dotnet build EReader.slnx` → só o próprio `Domain` deve compilar aqui (nada mais referencia
   essas interfaces/entidades ainda).

### P3 — `Application/Catalog` (extensões para D-03-5/D-03-6)

1. Criar `Application/Catalog/BookMapper.cs` com `public static BookDto ToDto(Book book)`
   (corpo idêntico ao `ToDto` hoje privado em `BookService`).
2. `BookService.cs`: remover o `ToDto` privado, trocar as 3 chamadas internas por
   `BookMapper.ToDto(...)`.
3. `Domain/Interfaces/IBookRepository.cs`: adicionar
   `Task<IReadOnlyList<Book>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct);`
   (D-03-6).
4. `Infra/Repositories/BookRepository.cs`: implementar `GetByIdsAsync`
   (`db.Books.Where(b => ids.Contains(b.Id)).ToListAsync(ct)`).
5. `dotnet build EReader.slnx` → verde (nada além de `BookService`/`BookRepository` muda de
   assinatura pública).

### P4 — `Application/Reading` — base

1. `Reading/ReadingNotFoundException.cs`, `Reading/ReadingValidationException.cs` (D-03-8, ver
   [5.3](#53-applicationreading--exceções)).
2. `Reading/BookAccessGuard.cs` (D-03-4, ver [5.4](#54-applicationreadingbookaccessguardcs)).
3. `Reading/ReadingProgressCalculator.cs` (método puro, ver
   [5.5](#55-applicationreadingreadingprogresscalculatorcs)).
4. `Reading/Dtos/{ProgressDto,UpdateProgressRequest,BookmarkDto,CreateBookmarkRequest,
   HighlightDto,CreateHighlightRequest,UpdateHighlightRequest,NoteDto,CreateNoteRequest,
   UpdateNoteRequest,LibraryItemDto}.cs` — um record por arquivo, namespace
   `EReader_API.Application.Reading` (mesmo padrão de `Application/Catalog/Dtos`, ver
   [5.6](#56-applicationreadingdtos)).

### P5 — `Application/Reading` — serviços

1. `IReadingProgressService`/`ReadingProgressService` (ver
   [5.7](#57-applicationreadingreadingprogressservicecs)).
2. `IBookmarkService`/`BookmarkService`.
3. `IHighlightService`/`HighlightService`.
4. `INoteService`/`NoteService` (valida `HighlightId` — mesmo usuário e `BookId`, D-03-8).
5. `ILibraryService`/`LibraryService` (D-03-6/D-03-9, ver
   [5.8](#58-applicationreadinglibraryservicecs)).

Algoritmos por serviço — ver [5.7](#57-applicationreadingreadingprogressservicecs) em diante
para o detalhe; resumo:

- **Progress.UpsertAsync**: `BookAccessGuard.EnsureAccessibleAsync` → valida `CurrentPage` em
  `[1, book.PageCount]` quando `PageCount != null` (senão só `>= 1`) → busca
  `ReadingProgress` existente por `(userId, bookId)` → reusa ou cria `Id = Guid.NewGuid()` →
  seta `TotalPages = book.PageCount`, `LastReadAt = now`,
  `PercentComplete = ReadingProgressCalculator.PercentComplete(...)` → `UpsertAsync`.
- **Bookmark/Highlight/Note.CreateAsync**: `BookAccessGuard.EnsureAccessibleAsync` → valida
  `PageNumber >= 1` (D-03-10) → monta entidade com `Id = Guid.NewGuid()`, `CreatedAt = now` →
  `AddAsync`.
- **Bookmark.DeleteAsync / Highlight.DeleteAsync/UpdateAsync / Note.DeleteAsync/UpdateAsync**:
  busca por `Id` → `null` ou `UserId != requesterId` → `ReadingNotFoundException`; senão aplica
  a operação.
- **Note.CreateAsync com `HighlightId`**: além do acima, busca o `Highlight` por `HighlightId`;
  `null`, `UserId != requesterId` ou `BookId` diferente → `ReadingValidationException`.
- **Library.GetAsync**: `progressRepository.ListByUserAsync(requesterId)` (dict por `BookId`) +
  `bookRepository.GetOwnedByUserAsync(requesterId)` + `bookRepository.GetByIdsAsync` para os
  `BookId` com progresso que não estão entre os próprios (D-03-6) → monta
  `LibraryItemDto(BookMapper.ToDto(book), progresso ou null)` para a união dos dois conjuntos →
  ordena por D-03-9.

### P6 — `Infra/Repositories`

`ReadingProgressRepository.cs`, `BookmarkRepository.cs`, `HighlightRepository.cs`,
`NoteRepository.cs` — mesmo estilo de `BookRepository.cs` (classe com construtor primário
`(ApplicationDbContext db)`, sem Unit of Work, `SaveChangesAsync` a cada operação). Ver
[5.9](#59-infrarepositories).

### P7 — `Infra/Context`

1. `ApplicationDbContext`: adicionar os 4 `DbSet<>`.
2. `Context/Configurations/{ReadingProgress,Bookmark,Highlight,Note}Configuration.cs` — índices,
   FKs com `OnDelete` (D-03-3), único em `ReadingProgress(UserId, BookId)`. Ver
   [5.10](#510-infracontextconfigurations).

### P8 — DI

- `Application/DependencyInjection.cs`: registrar os 5 serviços novos
  (`IReadingProgressService`, `IBookmarkService`, `IHighlightService`, `INoteService`,
  `ILibraryService`).
- `Infra/DependencyInjection.cs`: registrar os 4 repositórios novos.

Ver [5.11](#511-dependencyinjection-diffs).

### P9 — Controllers

`ReadingProgressController`, `BookmarksController`, `HighlightsController`, `NotesController`,
`LibraryController` em `EReader_API/Controllers/`, todos `[Authorize]`. Rotas mistas conforme
D-03-7. Ver [5.12](#512-controllers).

### P10 — Migration

```powershell
dotnet ef migrations add AddReadingContext `
  --project EReader_API.Infra `
  --startup-project EReader_API `
  --output-dir Migrations

dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

Conferir no arquivo gerado: 4 tabelas novas com `Id uuid` PK; FKs `BookId → Books` e
`UserId → AspNetUsers` com `ON DELETE CASCADE`; FK `Notes.HighlightId → Highlights` com
`ON DELETE SET NULL` e coluna nullable; índice único em `ReadingProgresses(UserId, BookId)`.

### P11 — Verificação (ver seção 6)

---

## 5. Arquivos finais (esqueletos)

### 5.1 `Domain/Entities/Reading`

```csharp
// Bookmark.cs
namespace EReader_API.Domain.Entities.Reading;

public class Bookmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Highlight.cs
namespace EReader_API.Domain.Entities.Reading;

public class Highlight
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string TextContent { get; set; } = "";
    public string? Color { get; set; }
    public string? Anchor { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Note.cs
namespace EReader_API.Domain.Entities.Reading;

public class Note
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int? PageNumber { get; set; }
    public Guid? HighlightId { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ReadingProgress.cs
namespace EReader_API.Domain.Entities.Reading;

public class ReadingProgress
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int CurrentPage { get; set; }
    public int? TotalPages { get; set; }
    public decimal PercentComplete { get; set; }
    public DateTime LastReadAt { get; set; }
}
```

### 5.2 `Domain/Interfaces`

```csharp
// IReadingProgressRepository.cs
using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface IReadingProgressRepository
{
    Task<ReadingProgress?> GetAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<IReadOnlyList<ReadingProgress>> ListByUserAsync(Guid userId, CancellationToken ct);
    Task UpsertAsync(ReadingProgress progress, CancellationToken ct);
}

// IBookmarkRepository.cs
using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface IBookmarkRepository
{
    Task<IReadOnlyList<Bookmark>> ListAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<Bookmark?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Bookmark bookmark, CancellationToken ct);
    Task RemoveAsync(Bookmark bookmark, CancellationToken ct);
}

// IHighlightRepository.cs
using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface IHighlightRepository
{
    Task<IReadOnlyList<Highlight>> ListAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<Highlight?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Highlight highlight, CancellationToken ct);
    Task UpdateAsync(Highlight highlight, CancellationToken ct);
    Task RemoveAsync(Highlight highlight, CancellationToken ct);
}

// INoteRepository.cs
using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Domain.Interfaces;

public interface INoteRepository
{
    Task<IReadOnlyList<Note>> ListAsync(Guid userId, Guid bookId, CancellationToken ct);
    Task<Note?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Note note, CancellationToken ct);
    Task UpdateAsync(Note note, CancellationToken ct);
    Task RemoveAsync(Note note, CancellationToken ct);
}
```

> `INoteRepository` não precisa de método para "zerar `HighlightId` das notas ligadas" — isso é
> `ON DELETE SET NULL` no banco (D-03-3), invisível para o EF/repositório.

### 5.3 `Application/Reading` — exceções

```csharp
namespace EReader_API.Application.Reading;

public class ReadingNotFoundException() : Exception("Recurso não encontrado.");

public class ReadingValidationException(IEnumerable<string> errors)
    : Exception(string.Join("; ", errors))
{
    public IReadOnlyCollection<string> Errors { get; } = errors.ToList();
}
```

### 5.4 `Application/Reading/BookAccessGuard.cs`

```csharp
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

internal static class BookAccessGuard
{
    public static async Task<Book> EnsureAccessibleAsync(
        IBookRepository bookRepository, Guid bookId, Guid requesterId, CancellationToken ct)
    {
        var book = await bookRepository.GetByIdAsync(bookId, ct);
        if (book is null || (book.Source == BookSource.UserUpload && book.OwnerId != requesterId))
            throw new ReadingNotFoundException();

        return book;
    }
}
```

### 5.5 `Application/Reading/ReadingProgressCalculator.cs`

```csharp
namespace EReader_API.Application.Reading;

public static class ReadingProgressCalculator
{
    public static decimal PercentComplete(int currentPage, int? totalPages) =>
        totalPages is > 0 ? Math.Round(100m * currentPage / totalPages.Value, 2) : 0m;
}
```

### 5.6 `Application/Reading/Dtos`

```csharp
namespace EReader_API.Application.Reading;

public record ProgressDto(
    Guid BookId, int CurrentPage, int? TotalPages, decimal PercentComplete, DateTime LastReadAt);

public record UpdateProgressRequest(int CurrentPage);

public record BookmarkDto(Guid Id, Guid BookId, int PageNumber, string? Label, DateTime CreatedAt);

public record CreateBookmarkRequest(int PageNumber, string? Label);

public record HighlightDto(
    Guid Id, Guid BookId, int PageNumber, string TextContent, string? Color, string? Anchor,
    DateTime CreatedAt);

public record CreateHighlightRequest(int PageNumber, string TextContent, string? Color, string? Anchor);

public record UpdateHighlightRequest(string? Color, string? TextContent);

public record NoteDto(
    Guid Id, Guid BookId, int? PageNumber, Guid? HighlightId, string Content,
    DateTime CreatedAt, DateTime UpdatedAt);

public record CreateNoteRequest(string Content, int? PageNumber, Guid? HighlightId);

public record UpdateNoteRequest(string Content);

public record LibraryItemDto(EReader_API.Application.Catalog.BookDto Book, ProgressDto? Progress);
```

> Um record por arquivo na prática (mesmo padrão de `Application/Catalog/Dtos`); agrupados aqui
> só para leitura do plano.

### 5.7 `Application/Reading/ReadingProgressService.cs`

```csharp
using EReader_API.Domain.Interfaces;
using EReader_API.Domain.Entities.Reading;

namespace EReader_API.Application.Reading;

public interface IReadingProgressService
{
    Task<ProgressDto?> GetAsync(Guid bookId, Guid requesterId, CancellationToken ct);
    Task<ProgressDto> UpsertAsync(Guid bookId, UpdateProgressRequest req, Guid requesterId, CancellationToken ct);
}

public class ReadingProgressService(IReadingProgressRepository repository, IBookRepository bookRepository)
    : IReadingProgressService
{
    public async Task<ProgressDto?> GetAsync(Guid bookId, Guid requesterId, CancellationToken ct)
    {
        await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);
        var progress = await repository.GetAsync(requesterId, bookId, ct);
        return progress is null ? null : ToDto(progress);
    }

    public async Task<ProgressDto> UpsertAsync(
        Guid bookId, UpdateProgressRequest req, Guid requesterId, CancellationToken ct)
    {
        var book = await BookAccessGuard.EnsureAccessibleAsync(bookRepository, bookId, requesterId, ct);

        if (req.CurrentPage < 1 || (book.PageCount is int total && req.CurrentPage > total))
            throw new ReadingValidationException(["currentPage fora do intervalo permitido."]);

        var progress = await repository.GetAsync(requesterId, bookId, ct)
                        ?? new ReadingProgress { Id = Guid.NewGuid(), UserId = requesterId, BookId = bookId };

        progress.CurrentPage = req.CurrentPage;
        progress.TotalPages = book.PageCount;
        progress.LastReadAt = DateTime.UtcNow;
        progress.PercentComplete = ReadingProgressCalculator.PercentComplete(req.CurrentPage, book.PageCount);

        await repository.UpsertAsync(progress, ct);
        return ToDto(progress);
    }

    private static ProgressDto ToDto(ReadingProgress p) =>
        new(p.BookId, p.CurrentPage, p.TotalPages, p.PercentComplete, p.LastReadAt);
}
```

`BookmarkService`/`HighlightService`/`NoteService` seguem o mesmo esqueleto (injeta o
repositório próprio + `IBookRepository`; `CreateAsync` chama `BookAccessGuard`, valida
`PageNumber >= 1`; `Delete`/`Update` buscam por `Id` e checam `UserId == requesterId`).
`NoteService.CreateAsync` adicionalmente valida `HighlightId` (D-03-8):

```csharp
if (req.HighlightId is Guid hId)
{
    var highlight = await highlightRepository.GetByIdAsync(hId, ct);
    if (highlight is null || highlight.UserId != requesterId || highlight.BookId != bookId)
        throw new ReadingValidationException(["highlightId inválido para este livro/usuário."]);
}
```

### 5.8 `Application/Reading/LibraryService.cs`

```csharp
using EReader_API.Application.Catalog;
using EReader_API.Domain.Interfaces;

namespace EReader_API.Application.Reading;

public interface ILibraryService
{
    Task<IReadOnlyList<LibraryItemDto>> GetAsync(Guid requesterId, CancellationToken ct);
}

public class LibraryService(IBookRepository bookRepository, IReadingProgressRepository progressRepository)
    : ILibraryService
{
    public async Task<IReadOnlyList<LibraryItemDto>> GetAsync(Guid requesterId, CancellationToken ct)
    {
        var progressList = await progressRepository.ListByUserAsync(requesterId, ct);
        var progressByBook = progressList.ToDictionary(p => p.BookId);

        var owned = await bookRepository.GetOwnedByUserAsync(requesterId, ct);
        var ownedIds = owned.Select(b => b.Id).ToHashSet();

        var missingIds = progressByBook.Keys.Where(id => !ownedIds.Contains(id)).ToList();
        var fetched = missingIds.Count > 0
            ? await bookRepository.GetByIdsAsync(missingIds, ct)
            : [];

        var items = owned.Concat(fetched)
            .Select(book => new LibraryItemDto(
                BookMapper.ToDto(book),
                progressByBook.TryGetValue(book.Id, out var p)
                    ? new ProgressDto(p.BookId, p.CurrentPage, p.TotalPages, p.PercentComplete, p.LastReadAt)
                    : null))
            .OrderByDescending(i => i.Progress?.LastReadAt ?? DateTime.MinValue)
            .ToList();

        return items;
    }
}
```

### 5.9 `Infra/Repositories`

```csharp
// ReadingProgressRepository.cs
using EReader_API.Domain.Entities.Reading;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;

namespace EReader_API.Infra.Repositories;

public class ReadingProgressRepository(ApplicationDbContext db) : IReadingProgressRepository
{
    public Task<ReadingProgress?> GetAsync(Guid userId, Guid bookId, CancellationToken ct) =>
        db.Set<ReadingProgress>().FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, ct);

    public async Task<IReadOnlyList<ReadingProgress>> ListByUserAsync(Guid userId, CancellationToken ct) =>
        await db.Set<ReadingProgress>().Where(p => p.UserId == userId).ToListAsync(ct);

    public async Task UpsertAsync(ReadingProgress progress, CancellationToken ct)
    {
        var existing = await db.Set<ReadingProgress>().FirstOrDefaultAsync(p => p.Id == progress.Id, ct);
        if (existing is null)
            db.Set<ReadingProgress>().Add(progress);
        else
            db.Entry(existing).CurrentValues.SetValues(progress);

        await db.SaveChangesAsync(ct);
    }
}
```

`BookmarkRepository`/`HighlightRepository`/`NoteRepository`: `ListAsync` filtra
`Where(x => x.UserId == userId && x.BookId == bookId)`; `GetByIdAsync`/`AddAsync`/
`UpdateAsync`/`RemoveAsync` seguem exatamente o padrão de `BookRepository`
(`Add`/`Update`/`Remove` + `SaveChangesAsync`).

### 5.10 `Infra/Context/Configurations`

```csharp
// ReadingProgressConfiguration.cs
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations;

public class ReadingProgressConfiguration : IEntityTypeConfiguration<ReadingProgress>
{
    public void Configure(EntityTypeBuilder<ReadingProgress> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.UserId, p.BookId }).IsUnique();

        builder.HasOne<Book>().WithMany().HasForeignKey(p => p.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

`BookmarkConfiguration`/`HighlightConfiguration`: mesmo par de `HasOne<Book>()`/
`HasOne<ApplicationUser>()` com `OnDelete(Cascade)`, mais `HasIndex(x => new { x.UserId, x.BookId })`
(não único).

```csharp
// NoteConfiguration.cs — igual às anteriores, mais:
builder.HasOne<Highlight>()
    .WithMany()
    .HasForeignKey(n => n.HighlightId)
    .OnDelete(DeleteBehavior.SetNull);
```

### 5.11 DependencyInjection (diffs)

```csharp
// Application/DependencyInjection.cs
services.AddScoped<IReadingProgressService, ReadingProgressService>();
services.AddScoped<IBookmarkService, BookmarkService>();
services.AddScoped<IHighlightService, HighlightService>();
services.AddScoped<INoteService, NoteService>();
services.AddScoped<ILibraryService, LibraryService>();

// Infra/DependencyInjection.cs
services.AddScoped<IReadingProgressRepository, ReadingProgressRepository>();
services.AddScoped<IBookmarkRepository, BookmarkRepository>();
services.AddScoped<IHighlightRepository, HighlightRepository>();
services.AddScoped<INoteRepository, NoteRepository>();
```

### 5.12 Controllers

```csharp
// ReadingProgressController.cs
[ApiController]
[Route("api/books/{bookId:guid}/progress")]
[Authorize]
public class ReadingProgressController(IReadingProgressService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid bookId, CancellationToken ct)
    {
        try
        {
            var dto = await service.GetAsync(bookId, User.GetUserId(), ct);
            return dto is null ? NoContent() : Ok(dto);
        }
        catch (ReadingNotFoundException) { return NotFound(); }
    }

    [HttpPut]
    public async Task<IActionResult> Upsert(Guid bookId, UpdateProgressRequest req, CancellationToken ct)
    {
        try { return Ok(await service.UpsertAsync(bookId, req, User.GetUserId(), ct)); }
        catch (ReadingNotFoundException) { return NotFound(); }
        catch (ReadingValidationException ex) { return ValidationProblem(string.Join("; ", ex.Errors)); }
    }
}

// BookmarksController.cs (D-03-7 — sem [Route] no controller)
[ApiController]
[Authorize]
public class BookmarksController(IBookmarkService service) : ControllerBase
{
    [HttpGet("api/books/{bookId:guid}/bookmarks")]
    public async Task<IActionResult> List(Guid bookId, CancellationToken ct)
    {
        try { return Ok(await service.ListAsync(bookId, User.GetUserId(), ct)); }
        catch (ReadingNotFoundException) { return NotFound(); }
    }

    [HttpPost("api/books/{bookId:guid}/bookmarks")]
    public async Task<IActionResult> Create(Guid bookId, CreateBookmarkRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await service.CreateAsync(bookId, req, User.GetUserId(), ct);
            return CreatedAtAction(nameof(List), new { bookId }, dto);
        }
        catch (ReadingNotFoundException) { return NotFound(); }
        catch (ReadingValidationException ex) { return ValidationProblem(string.Join("; ", ex.Errors)); }
    }

    [HttpDelete("api/bookmarks/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await service.DeleteAsync(id, User.GetUserId(), ct); return NoContent(); }
        catch (ReadingNotFoundException) { return NotFound(); }
    }
}

// LibraryController.cs
[ApiController]
[Route("api/library")]
[Authorize]
public class LibraryController(ILibraryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await service.GetAsync(User.GetUserId(), ct));
}
```

`HighlightsController`/`NotesController`: mesmo formato de `BookmarksController`, mais
`[HttpPatch("api/highlights/{id:guid}")]`/`[HttpPatch("api/notes/{id:guid}")]` chamando
`UpdateAsync`.

---

## 6. Verificação final (mapeada aos critérios de aceite da spec)

| Critério de aceite (spec 03) | Como verificar | Passo |
|---|---|---|
| `PUT /progress` cria na 1ª chamada e atualiza nas seguintes (nunca duplica) | `PUT` duas vezes com `currentPage` diferente; `GET` mostra o valor mais recente; índice único garante 1 linha por `(UserId, BookId)` | P5/P7 |
| `currentPage` fora de `[1, TotalPages]` → `400` | `PUT` com `currentPage` = `0` e = `TotalPages + 1` num livro com `PageCount` conhecido | P5 |
| Sem `Book.PageCount`, `percentComplete` volta `0` e o `PUT` ainda funciona | Upload com `pageCount` nulo (ou público sem `PageCount`) → `PUT /progress` → `200` com `percentComplete: 0` | P5 |
| Operações em recurso de outro usuário → `404` | Logar com 2 usuários, criar bookmark/highlight/note com um, tentar `DELETE`/`PATCH` com o outro | P5 |
| `POST /notes` com `highlightId` de outro livro/usuário → `400` | Criar `Highlight` no livro A, tentar `POST /books/{B}/notes` referenciando esse `highlightId` | P5 |
| Remover um `Highlight` mantém as notas ligadas, com `highlightId` nulo | Criar nota com `highlightId`, `DELETE` o highlight, `GET /notes` mostra a nota com `highlightId: null` | P7/P10 |
| `DELETE /api/books/{id}` (dono) remove progresso/bookmarks/highlights/notes de todos | Dois usuários interagem com o mesmo livro (`UserUpload` do dono não é possível para outro user; testar com público OU o próprio dono deletando seu upload após só ele mesmo ter interagido) → dono deleta o livro → `GET`s seguintes em qualquer entidade daquele `BookId` não retornam nada | P7/P10 |
| `DELETE /api/auth/me` remove todos os dados de `Reading` do usuário | Criar progresso/bookmark/highlight/note → `DELETE /api/auth/me` → consultar direto no banco (`SELECT ... WHERE "UserId" = ...`) e confirmar 0 linhas nas 4 tabelas | P7/P10 |
| `GET /api/library` retorna os livros iniciados + uploads do usuário, com `progress`, ordem `lastReadAt` desc | Upload de um livro (nunca aberto) + progresso num livro público → `GET /api/library` traz os dois, o com progresso mais recente primeiro | P5/P9 |
| Migration aplica e reverte sem erro | `dotnet ef database update` e `dotnet ef database update 0` | P10 |

Roteiro de `curl` sugerido (PowerShell), assumindo `$token`/`$bookId` já obtidos como nas specs
anteriores:

```powershell
$base = "http://localhost:5035/api"

curl -s -X PUT "$base/books/$bookId/progress" -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" -d '{"currentPage": 5}'
curl -s "$base/books/$bookId/progress" -H "Authorization: Bearer $token"

curl -s -X POST "$base/books/$bookId/bookmarks" -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" -d '{"pageNumber": 10, "label": "cap. 2"}'

curl -s -X POST "$base/books/$bookId/highlights" -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" -d '{"pageNumber": 10, "textContent": "trecho"}'
# guardar $highlightId da resposta

curl -s -X POST "$base/books/$bookId/notes" -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" -d "{`"content`": `"nota`", `"highlightId`": `"$highlightId`"}"

curl -s "$base/library" -H "Authorization: Bearer $token"
```

## 7. Riscos e armadilhas

- **Duas trajetórias de cascata até a mesma linha**: ao deletar um `ApplicationUser`, uma linha
  de `Bookmark`/`Highlight`/`Note`/`ReadingProgress` sobre um livro **próprio** desse usuário é
  alcançável tanto via `User → Book → Reading` (`BookId`) quanto via `User → Reading` direto
  (`UserId`). SQL Server proíbe isso ("multiple cascade paths"); Postgres não tem essa
  restrição e resolve normalmente — mas vale confirmar isso explicitamente rodando a migration
  e testando o `DELETE /api/auth/me` de verdade (P10/P11), já que é uma suposição sobre o motor
  de banco, não algo garantido só por este plano.
- **`BookService.DeleteAsync` não impede deletar livro `PublicDomain` de outro "dono"**:
  `GetAuthorizedAsync` (spec 02) só lança `BookNotFoundException` quando `Source == UserUpload`
  e o dono é outro — para `PublicDomain` (`OwnerId == null`), qualquer usuário autenticado passa
  no `DeleteAsync`. Isso é comportamento pré-existente da spec 02 (fora do escopo desta spec),
  mas ganha peso aqui porque deletar um livro público agora também apaga em cascata o
  progresso/anotações de **todos** os usuários que o leram — vale flagar para a spec 04
  (Robustez) e não é algo que este plano corrige.
- **`ReadingProgressRepository.UpsertAsync` e rastreamento do EF**: como o `ReadingProgress`
  passado pelo serviço pode ser uma instância nova (não rastreada) mesmo quando já existe no
  banco (o serviço recria o objeto a partir do que `GetAsync` devolveu, não há tracking entre
  chamadas), o `UpsertAsync` precisa buscar o `existing` por `Id` de novo dentro do repositório
  antes de decidir `Add` vs. `SetValues` — não dá para assumir que o `progress` recebido já está
  anexado ao `DbContext`.
- **`GetByIdsAsync` com lista vazia**: `LibraryService` já evita chamar o repositório com
  `missingIds` vazio (`? await ... : []`), mas se essa checagem for removida num refactor futuro,
  `Where(b => ids.Contains(b.Id))` com lista vazia ainda funciona (retorna vazio), só evita uma
  query desnecessária — não é um bug, é só uma otimização a preservar.

## 8. Rollback

Cada passo é um commit isolado. Reverter com `git revert <commit>`. Para a migration:
`dotnet ef database update 0 --project EReader_API.Infra --startup-project EReader_API` antes de
`dotnet ef migrations remove --project EReader_API.Infra --startup-project EReader_API`.

## 9. Ganchos para as próximas specs / follow-ups

- **`CLAUDE.md`**: atualizar a seção "Architecture" — `Domain` ganha `Entities/Reading`
  redesenhado (sem nav properties, `Guid`) + `IReadingProgressRepository`/etc. no formato
  moderno (a frase atual sobre o formato "antigo" das interfaces de `Reading` deixa de valer);
  `Application` ganha `Reading/` (+ `Catalog/BookMapper.cs` novo); `Infra` ganha os 4
  repositórios e as 4 configurações; `EReader_API` ganha os 5 controllers novos.
- **Spec 04 (Robustez)**: as exceções ad-hoc `ReadingNotFoundException`/
  `ReadingValidationException` devem migrar para o `ProblemDetails` padronizado junto com as de
  `Identity`/`Catalog`; o gap de autorização em `BookService.DeleteAsync` para livros
  `PublicDomain` (ver seção 7) é candidato a correção ali, já que agora tem efeito colateral
  maior (cascata de `Reading` de terceiros).
- **Spec 05 (Testes)**: `ReadingProgressCalculator.PercentComplete` é a unidade mais óbvia para
  testar (função pura); os 4 serviços de `Reading` são testáveis via mocks das interfaces de
  repositório (sem EF); `BookAccessGuard` pode ser testado isolado com um `IBookRepository` fake
  cobrindo os 3 casos (`null`, `UserUpload` de outro, `PublicDomain`); o roteiro de integração
  descrito no checklist da spec (progresso → highlight → nota ancorada → `/library` → deletar
  livro) mapeia diretamente para os testes de integração da seção 6 acima.
