# 02 — Catálogo e upload de livros

**Status:** Concluído
**Depende de:** 00, 01
**Objetivo:** Implementar o contexto `Catalog`: redesenhar a entidade `Book` (visibilidade
pública vs. pessoal), CRUD de metadados, upload de PDF com validação, abstração de
armazenamento de arquivos (`IFileStorage` + `LocalFileStorage`), streaming do arquivo com
suporte a `Range`, e seed da biblioteca pública de domínio público.

## Contexto

Notas originais: "usuário pode logar e adicionar seus livros em formato PDF"; "biblioteca
própria que liste arquivos já existentes do usuário"; "biblioteca pública com livros de
domínio público"; arquitetura com "Arm. de arquivos (Local)". Decisão D-3 (renomear campos,
adicionar `Source`/`OwnerId`).

## Escopo

**Entra:** entidade `Book` redesenhada + migration, `IBookRepository` real, `IFileStorage` +
`LocalFileStorage`, `IBookService` (casos de uso), `BookController` (lista/detalhe/criar/
atualizar/remover/arquivo/capa), validação de upload (PDF apenas, magic bytes, tamanho),
autorização por dono, seed de livros públicos.

**Não entra:** extração automática de `PageCount`/capa (spec 04), busca full-text avançada,
categorias/estantes, formatos além de PDF.

## Modelo

```csharp
// Domain/Entities/Catalog
public enum BookSource { PublicDomain = 0, UserUpload = 1 }

public class Book
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";        // era Name
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }          // ex. "pt-BR"
    public string Format { get; set; } = "pdf";    // fixo no MVP
    public BookSource Source { get; set; }
    public Guid? OwnerId { get; set; }             // null ⇔ PublicDomain
    public string FileKey { get; set; } = "";      // era FilePath; chave no storage
    public long FileSizeBytes { get; set; }
    public int? PageCount { get; set; }            // null até processar/informar
    public string? CoverImageKey { get; set; }
    public DateTime? PublishedDate { get; set; }   // era LaunchDate
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

Invariantes (validar no serviço e, quando possível, no banco):
`Source == UserUpload` ⇒ `OwnerId != null`; `Source == PublicDomain` ⇒ `OwnerId == null`.

```csharp
// Domain/Interfaces
public interface IBookRepository
{
    Task<Book?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PagedResult<Book>> QueryAsync(BookQuery query, CancellationToken ct);
    Task AddAsync(Book book, CancellationToken ct);
    Task UpdateAsync(Book book, CancellationToken ct);
    Task RemoveAsync(Book book, CancellationToken ct);
}

public record BookQuery(
    Guid RequesterId, BookScope Scope, string? Search, string? Author,
    int Page, int PageSize, string? Sort);

public enum BookScope { All, Public, Mine }

// Domain/Interfaces (ou Application) — storage
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string contentType, string suggestedName, CancellationToken ct);
    Task<Stream> OpenReadAsync(string fileKey, CancellationToken ct);
    Task DeleteAsync(string fileKey, CancellationToken ct);
    Task<bool> ExistsAsync(string fileKey, CancellationToken ct);
}
```

```csharp
// Application/Catalog
public interface IBookService
{
    Task<PagedResult<BookDto>> ListAsync(BookScope scope, string? search, string? author,
        int page, int pageSize, string? sort, Guid requesterId, CancellationToken ct);
    Task<BookDto> GetAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<BookDto> UploadAsync(UploadBookRequest req, Guid ownerId, CancellationToken ct);
    Task<BookDto> UpdateAsync(Guid id, UpdateBookRequest req, Guid requesterId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<FileDownload> OpenFileAsync(Guid id, Guid requesterId, CancellationToken ct);
}
```

## Endpoints — `/api/books`

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/api/books?scope=&search=&author=&page=1&pageSize=20&sort=title` | Bearer | lista paginada: livros públicos + do próprio usuário |
| GET | `/api/books/{id}` | Bearer | detalhe (dono ou público) |
| POST | `/api/books` | Bearer | `multipart/form-data`: `title`, `author?`, `description?`, `language?`, `publishedDate?`, `pageCount?`, `file` (PDF) → `201` + `BookDto` |
| PUT | `/api/books/{id}` | Bearer | atualiza metadados (só dono) |
| DELETE | `/api/books/{id}` | Bearer | remove livro + arquivo (só dono) |
| GET | `/api/books/{id}/file` | Bearer | stream do PDF; suporta `Range` → `200`/`206` |
| GET | `/api/books/{id}/cover` | Bearer | imagem de capa ou `404` |

Autorização: `PublicDomain` → qualquer autenticado (somente leitura). `UserUpload` → apenas
`OwnerId`; caso contrário `404` (não revelar existência).

## Validação de upload

- `Content-Type` do arquivo = `application/pdf`.
- Primeiros bytes = `%PDF-` (magic bytes), lidos do stream antes de salvar.
- Tamanho ≤ `FileStorage:MaxUploadBytes` (padrão 52.428.800 = 50 MB); também configurar
  `MultipartBodyLengthLimit` / `RequestSizeLimit` no endpoint.
- `title` obrigatório (1–300 chars). `pageCount`, quando enviado, `>= 1`.
- Rejeições retornam `400` ProblemDetails com detalhe do campo.

## `LocalFileStorage`

- Raiz configurável `FileStorage:RootPath` (padrão `App_Data/books`), resolvida para caminho
  absoluto fora de `wwwroot`.
- `SaveAsync`: gera `fileKey = "{yyyy}/{MM}/{Guid:N}.pdf"`, cria diretórios, grava o stream,
  retorna a `fileKey`.
- `OpenReadAsync`: valida que o caminho resolvido continua sob a raiz (proteção contra path
  traversal), abre `FileStream` só-leitura.
- `DeleteAsync`: idempotente (não falha se o arquivo não existe).

## Streaming com `Range`

- `GET /api/books/{id}/file` retorna `File(stream, "application/pdf", enableRangeProcessing: true)`
  com `fileDownloadName` opcional; deixar o ASP.NET Core tratar `Range`/`206`/`Accept-Ranges`.
- Não bufferizar o arquivo inteiro em memória.

## Seed da biblioteca pública

- `PublicLibrarySeeder` (Infra) executado no startup em Dev (ou comando):
  - lê um manifesto `docs/seed/public-domain.json` (`title`, `author`, `language`,
    `publishedDate`, `sourceFile`) e os PDFs de `docs/seed/files/`;
  - para cada item sem `Book` correspondente (`Source = PublicDomain`, comparação por
    `Title`+`Author`), salva o arquivo via `IFileStorage` e cria o `Book` com `OwnerId = null`.
- Começar com 2–3 títulos de domínio público (ex. Machado de Assis). Os PDFs de seed **não**
  entram no controle de versão se forem grandes — documentar de onde baixar.

## Tarefas

- [ ] Reescrever `Domain/Entities/Catalog/Book.cs` conforme o modelo.
- [ ] `PagedResult<T>`, `BookQuery`, `BookScope`, enums em `Domain`.
- [ ] `IFileStorage` (Domain ou Application) + `LocalFileStorage` (Infra) + opções `FileStorageOptions`.
- [ ] `IBookRepository` novo contrato + `BookRepository` (EF, `QueryAsync` com filtro de
      escopo/busca/paginação/ordenação).
- [ ] `IEntityTypeConfiguration<Book>`: índices (`OwnerId`, `Source`), `Title` obrigatório,
      `Format` default `'pdf'`; opcional `CHECK` das invariantes.
- [ ] `IBookService` + `BookService` (casos de uso, autorização, validação de upload).
- [ ] DTOs: `BookDto`, `UploadBookRequest`, `UpdateBookRequest`, `FileDownload`.
- [ ] `BookController` com os 7 endpoints; limites de tamanho no `POST`.
- [ ] Registrar serviços/repos/storage no DI (`AddApplication`/`AddInfrastructure`).
- [ ] `PublicLibrarySeeder` + `docs/seed/public-domain.json` + instruções dos arquivos.
- [ ] Encadear no `DELETE /api/auth/me` (spec 01): remover livros `UserUpload` do usuário e
      seus arquivos no storage.
- [ ] Migration `AddBookCatalog`.
- [ ] Testes (spec 05): unit de validação/autorização; integração upload→listar (`scope=mine`)
      →download com `Range`→delete; `scope=public` mostra os livros do seed.

## Critérios de aceite

- Upload de um PDF válido cria o `Book` (`Source=UserUpload`, `OwnerId` = usuário) e grava o
  arquivo; `GET /api/books/{id}/file` devolve o mesmo conteúdo (hash igual).
- Upload de arquivo não-PDF (ou `%PDF-` ausente, ou acima do limite) → `400`.
- `GET /api/books/{id}` de livro de outro usuário → `404`.
- `GET /api/books?scope=public` lista os livros do seed; `scope=mine` lista só os do usuário;
  `scope=all` (padrão) lista os dois conjuntos.
- Paginação: `page`/`pageSize` respeitados; resposta traz `totalCount`.
- Requisição com header `Range: bytes=0-1023` → `206 Partial Content` + `Content-Range`.
- `DELETE /api/books/{id}` remove o registro e o arquivo do disco; `GET` seguinte → `404`.
- `DELETE /api/auth/me` remove também os livros e arquivos daquele usuário.
- Migration aplica e reverte sem erro.

## Arquivos afetados

- `EReader_API.Domain/Entities/Catalog/Book.cs`
- `EReader_API.Domain/Common/PagedResult.cs`, `Entities/Catalog/BookSource.cs`, `Catalog/BookScope.cs`
- `EReader_API.Domain/Interfaces/IBookRepository.cs`, `IFileStorage.cs`
- `EReader_API.Infra/Storage/LocalFileStorage.cs`, `FileStorageOptions.cs`
- `EReader_API.Infra/Repositories/BookRepository.cs`
- `EReader_API.Infra/Context/Configurations/BookConfiguration.cs`
- `EReader_API.Infra/Seed/PublicLibrarySeeder.cs`
- `EReader_API.Application/Catalog/*` (interfaces, DTOs, `BookService`)
- `EReader_API/Controllers/BookController.cs`
- `EReader_API/Program.cs`, `appsettings*.json` (`FileStorage`)
- `EReader_API.Infra/Migrations/*`
- `docs/seed/public-domain.json` (novo)
