# 03 — Leitura (core domain)

**Status:** Não iniciado
**Depende de:** 00, 01, 02
**Objetivo:** Implementar o contexto `Reading` (domínio principal): progresso de leitura por
usuário/livro, marcadores (`Bookmark`), destaques (`Highlight`) e notas (`Note`), mais o
atalho `GET /api/library` para a tela inicial do leitor.

## Contexto

Notas originais: "DDD → subdomínio principal / core domain → READING → sessão de leitura,
progresso, posição atual, anotações, destaques, bookmarks"; requisito de "indicador de
progresso de leitura". Reading Context = `Highlight` + `Bookmark` + `Note` (+ `ReadingProgress`,
que as notas descrevem como "progresso / posição atual").

## Escopo

**Entra:** entidades `ReadingProgress`, `Bookmark`, `Highlight`, `Note` + migration;
repositórios; serviços de caso de uso; controllers; `GET /api/library`; cascata de exclusão
(livro removido, conta removida); autorização por `UserId`.

**Não entra:** sincronização em tempo real, histórico/versionamento de notas, exportação de
anotações, compartilhamento entre usuários.

## Modelo

Todas as entidades: `Id: Guid`, `UserId: Guid` (= `ApplicationUser.Id`), `BookId: Guid`,
datas UTC. Escopo sempre o usuário autenticado.

```csharp
// Domain/Entities/Reading

public class ReadingProgress          // no máx. 1 por (UserId, BookId)
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int CurrentPage { get; set; }        // >= 1
    public int? TotalPages { get; set; }        // espelha Book.PageCount quando conhecido
    public decimal PercentComplete { get; set; }// derivado: TotalPages>0 ? Current/Total*100 : 0
    public DateTime LastReadAt { get; set; }
}

public class Bookmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }         // >= 1
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Highlight
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }         // >= 1
    public string TextContent { get; set; } = "";
    public string? Color { get; set; }          // ex. "#FFEB3B"
    public string? Anchor { get; set; }         // JSON opcional (seleção/coords p/ o leitor)
    public DateTime CreatedAt { get; set; }
}

public class Note
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int? PageNumber { get; set; }        // null = nota geral do livro
    public Guid? HighlightId { get; set; }      // nota ancorada a um destaque (opcional)
    public string Content { get; set; } = "";   // era Description
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

Repositórios (`Domain/Interfaces`): `IReadingProgressRepository` (get por user+book, upsert),
`IBookmarkRepository`, `IHighlightRepository`, `INoteRepository` — cada um com listar por
`(UserId, BookId)`, obter por `Id`, add, update, remove.

Serviços (`Application/Reading`): `IReadingProgressService`, `IBookmarkService`,
`IHighlightService`, `INoteService`. Todo método recebe `Guid requesterId` e valida
propriedade + acesso ao `Book` (dono ou público, via `IBookRepository`).

## Regras

- **Acesso ao livro:** só é possível criar progresso/anotações num livro que o usuário
  poderia ler (`PublicDomain` ou `UserUpload` próprio). Caso contrário `404`.
- **Progresso:** `PUT` é *upsert*. `CurrentPage` deve estar entre `1` e `TotalPages` (quando
  conhecido); fora do intervalo → `400`. Atualiza `LastReadAt = now` e recalcula
  `PercentComplete`. `TotalPages` é preenchido a partir de `Book.PageCount`; se nulo,
  `PercentComplete = 0`.
- **Propriedade:** operar sobre `Bookmark`/`Highlight`/`Note`/`ReadingProgress` de outro
  usuário → `404`.
- **Note.HighlightId:** quando informado, o `Highlight` deve existir, ser do mesmo usuário e
  do mesmo `BookId`; senão `400`. Remover um `Highlight` seta `HighlightId = null` nas notas
  ligadas (não apaga a nota).
- **Cascata:**
  - Remover um `Book` (`UserUpload`, pelo dono) apaga `ReadingProgress`/`Bookmark`/
    `Highlight`/`Note` daquele `BookId` de **todos** os usuários (`ON DELETE CASCADE` por
    `BookId`).
  - `DELETE /api/auth/me` apaga todos os registros de `Reading` com aquele `UserId`
    (`ON DELETE CASCADE` por `UserId`, ou limpeza explícita no `AuthService`).

## Endpoints

| Método | Rota | Corpo → Resposta |
|---|---|---|
| GET | `/api/books/{bookId}/progress` | → `ProgressDto` ou `204` se nunca leu |
| PUT | `/api/books/{bookId}/progress` | `{ currentPage }` → `200` `ProgressDto` (upsert) |
| GET | `/api/books/{bookId}/bookmarks` | → `BookmarkDto[]` |
| POST | `/api/books/{bookId}/bookmarks` | `{ pageNumber, label? }` → `201` |
| DELETE | `/api/bookmarks/{id}` | → `204` |
| GET | `/api/books/{bookId}/highlights` | → `HighlightDto[]` |
| POST | `/api/books/{bookId}/highlights` | `{ pageNumber, textContent, color?, anchor? }` → `201` |
| PATCH | `/api/highlights/{id}` | `{ color?, textContent? }` → `200` |
| DELETE | `/api/highlights/{id}` | → `204` |
| GET | `/api/books/{bookId}/notes` | → `NoteDto[]` |
| POST | `/api/books/{bookId}/notes` | `{ content, pageNumber?, highlightId? }` → `201` |
| PATCH | `/api/notes/{id}` | `{ content }` → `200` |
| DELETE | `/api/notes/{id}` | → `204` |
| GET | `/api/library` | → `LibraryItemDto[]`: livros do usuário (próprios + públicos já iniciados) com `progress` embutido, ordenados por `lastReadAt` desc |

`GET /api/library` = para cada `Book` acessível que tenha `ReadingProgress` do usuário **ou**
seja `UserUpload` do usuário: `{ book: BookDto, progress: ProgressDto? }`.

## Tarefas

- [ ] Criar/ajustar entidades em `Domain/Entities/Reading` (as atuais referenciam `User`
      e `int` — trocar por `Guid UserId`; remover navegações para `Book`/`User` ou mantê-las
      só como `BookId`/`UserId`).
- [ ] Adicionar `ReadingProgress` (nova).
- [ ] `DbSet<>` das quatro entidades no `ApplicationDbContext`.
- [ ] `IEntityTypeConfiguration<>` de cada: índices `(UserId, BookId)`; único
      `ReadingProgress(UserId, BookId)`; FKs para `Books` e `AspNetUsers` com
      `OnDelete(Cascade)`; `Note → Highlight` com `OnDelete(SetNull)`.
- [ ] Repositórios em `Infra/Repositories` + contratos em `Domain/Interfaces`.
- [ ] Serviços em `Application/Reading` + DTOs (`ProgressDto`, `BookmarkDto`, `HighlightDto`,
      `NoteDto`, `LibraryItemDto`) + `create/update` requests.
- [ ] Cálculo de `PercentComplete` centralizado (método puro testável).
- [ ] Controllers: `ReadingProgressController`, `BookmarksController`, `HighlightsController`,
      `NotesController`, `LibraryController` (ou agrupar).
- [ ] DI no `AddApplication`/`AddInfrastructure`.
- [ ] Atualizar cascata no `DELETE /api/auth/me` e no `DELETE /api/books/{id}`.
- [ ] Migration `AddReadingContext`.
- [ ] Testes (spec 05): unit do cálculo de progresso e das regras de propriedade/acesso;
      integração: iniciar leitura → atualizar progresso → criar highlight → nota ancorada →
      `GET /api/library` reflete tudo → remover livro apaga anotações.

## Critérios de aceite

- `PUT /progress` cria na 1ª chamada e atualiza nas seguintes (nunca duplica).
- `currentPage` fora de `[1, TotalPages]` (com `TotalPages` conhecido) → `400`.
- Sem `Book.PageCount`, `percentComplete` volta `0` e o `PUT` ainda funciona.
- Operações em recurso de outro usuário → `404`.
- `POST /notes` com `highlightId` de outro livro/usuário → `400`.
- Remover um `Highlight` mantém as notas ligadas, com `highlightId` nulo.
- `DELETE /api/books/{id}` (dono) remove progresso/bookmarks/highlights/notes de todos.
- `DELETE /api/auth/me` remove todos os dados de `Reading` do usuário.
- `GET /api/library` retorna os livros iniciados + uploads do usuário, com `progress`, na
  ordem de `lastReadAt` desc.
- Migration aplica e reverte sem erro.

## Arquivos afetados

- `EReader_API.Domain/Entities/Reading/{ReadingProgress,Bookmark,Highlight,Note}.cs`
- `EReader_API.Domain/Interfaces/{IReadingProgressRepository,IBookmarkRepository,IHighlightRepository,INoteRepository}.cs`
- `EReader_API.Infra/Repositories/*`
- `EReader_API.Infra/Context/ApplicationDbContext.cs`, `Context/Configurations/*`
- `EReader_API.Application/Reading/*`
- `EReader_API/Controllers/{ReadingProgress,Bookmarks,Highlights,Notes,Library}Controller.cs`
- `EReader_API.Application/Identity/AuthService.cs` (cascata no delete de conta)
- `EReader_API.Application/Catalog/BookService.cs` (cascata no delete de livro)
- `EReader_API.Infra/Migrations/*`
