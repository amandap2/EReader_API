# Plano de implementação — Spec 02: Catálogo e upload de livros

> Plano de execução para [`../02-catalogo-e-upload.md`](../02-catalogo-e-upload.md). Depende
> das specs 00 e 01 (concluídas). Objetivo: `Book` redesenhado (visibilidade pública vs.
> pessoal), `IBookRepository`/`BookRepository`, `IFileStorage`/`LocalFileStorage`, `IBookService`,
> `BookController` com os 7 endpoints de `/api/books`, streaming com `Range`, seed da
> biblioteca pública, migration `AddBookCatalog`.

## 0. Estado atual (verificado)

- `EReader_API.Domain/Entities/Catalog/Book.cs` ainda é o modelo antigo: `int Id`, `Name`,
  `Author`, `FilePath`, `Format`, `Category`, `LaunchDate`; namespace em bloco (`namespace X {
  }`), sem anotações de nulidade (compila só por warning `CS8618`, não erro). Precisa ser
  reescrito por completo conforme o modelo da spec.
- `EReader_API/Controllers/BookController.cs` é um stub vazio (`[ApiController]` sem rota, sem
  membros) — spec 00 removeu a lógica antiga, nada foi implementado ainda.
- `EReader_API.Domain/Interfaces/` não tem `IBookRepository`; não existem `PagedResult<T>`,
  `BookQuery`, `BookScope`, `BookSource` em lugar nenhum do código.
- `EReader_API.Application/` só tem `Identity/` e `Common/ClaimsPrincipalExtensions.cs`; não
  existe pasta `Catalog/` nem qualquer porta de storage.
- `EReader_API.Infra/` só tem `Identity/` (Auth completo da spec 01) e `Context/`; não existem
  `Repositories/`, `Storage/`, nem `Seed/`.
- `ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` já
  existe com `DbSet<RefreshToken>`; não tem `DbSet<Book>`. `OnModelCreating` já chama
  `ApplyConfigurationsFromAssembly` — uma nova `IEntityTypeConfiguration<Book>` é pega
  automaticamente.
- `appsettings.json` tem `ConnectionStrings`, `Jwt`, `Cors`, `Logging` — sem bloco
  `FileStorage`.
- `EReader_API/docker-compose.yml` só monta volume para o Postgres (`ereader-pgdata`); não há
  volume para arquivos enviados — sem um, todo upload feito em container some no próximo
  `up --build`.
- `EReader_API.Infra/Identity/AuthService.cs` (`DeleteAccountAsync`) hoje só faz
  `userManager.DeleteAsync(user)` — não sabe nada sobre `Book`. A tarefa "encadear no
  `DELETE /api/auth/me`" desta spec exige tocar esse arquivo mesmo ele sendo "dono" da spec 01
  (mesma situação que a spec 01 teve com `Bookmark`/`Highlight`/`Note`).
- Padrão de erro já estabelecido no `AuthController`: exceções específicas (`AuthException`,
  `IdentityValidationException`) capturadas por `try/catch` local mapeando para
  `Unauthorized`/`ValidationProblem` — não existe ainda o `ProblemDetails` padronizado global
  que a spec 04 vai trazer. O `Catalog` deve seguir o mesmo padrão ad-hoc por consistência.
- Não existe `App_Data/`, `docs/seed/`, nem qualquer PDF de domínio público no repositório.

## 1. Pré-condições

- Specs 00 e 01 concluídas: build verde, migration `AddIdentityAndRefreshTokens` aplicada,
  `/api/auth/*` funcional (necessário para obter um Bearer token e testar os endpoints de
  `/api/books`, todos autenticados).
- Acesso de escrita a disco local para `LocalFileStorage` (`App_Data/books` por padrão).
- Para validar o critério "`scope=public` lista os livros do seed": baixar manualmente 2–3
  PDFs de domínio público (ex. Machado de Assis, via `dominiopublico.gov.br` ou Gutenberg) para
  `docs/seed/files/` **antes** de rodar em Dev — este plano não automatiza esse download (ver
  D-02-10).

## 2. Decisões a confirmar antes de começar

A spec original deixa alguns pontos em aberto ou ambíguos (ex. "`IFileStorage` — Domain ou
Application?", `IBookService` sem método para capa, cascata de exclusão de conta não
detalhada). As decisões abaixo resolvem isso antes de codar.

| # | Decisão | Recomendação | Impacto se não seguir |
|---|---|---|---|
| D-02-1 | Onde mora a porta `IFileStorage` | **`Application/Storage`**, não `Domain`. Segue o precedente de D-01-1: capacidades técnicas genéricas implementadas por `Infra` (aqui, storage; lá, Auth) ficam como porta em `Application`; `Domain/Interfaces` fica reservado a contratos de repositório sobre agregados (`IBookRepository`, `IBookmarkRepository`, etc.), que é o padrão já documentado no `CLAUDE.md`. | Colocar em `Domain` funcionaria (não tem dependência de EF/Identity), mas mistura dois tipos de contrato diferentes na mesma pasta e quebra o paralelismo com o Identity. |
| D-02-2 | DTOs de upload não usam `IFormFile` | `UploadBookRequest`/`FileDownload` (em `Application/Catalog`) usam `Stream`/`string`/`long` puros — `Application` não referencia ASP.NET Core (só `Microsoft.Extensions.DependencyInjection.Abstractions`, ver `.csproj` atual). O `BookController` mapeia `IFormFile` → `UploadBookRequest` (`file.OpenReadStream()`, `file.ContentType`, `file.FileName`, `file.Length`). | Colocar `IFormFile` num DTO de `Application` forçaria uma referência a `Microsoft.AspNetCore.Http` nesse projeto — funciona (é só um pacote, não Identity/EF), mas foge do estilo atual do projeto e do texto do `CLAUDE.md` ("Application... nada que carregue dependência de runtime em EF/Identity"), estendido aqui por analogia a "nenhum tipo de framework web". |
| D-02-3 | `IBookService` ganha `OpenCoverAsync` | A assinatura de `IBookService` no corpo da spec (5 métodos + `OpenFileAsync`) não cobre `GET /api/books/{id}/cover`, que está na tabela de endpoints. Adicionar `Task<FileDownload?> OpenCoverAsync(Guid id, Guid requesterId, CancellationToken ct)` — devolve `null` quando `CoverImageKey` é nulo (controller mapeia para `404`); lança a mesma exceção de "não encontrado/não autorizado" que `GetAsync`. | Sem esse método o endpoint de capa não tem como ser implementado sem violar a camada (chamar `IFileStorage` direto do controller). |
| D-02-4 | Cascata de exclusão de conta (`DELETE /api/auth/me`) | `IBookService` ganha `Task DeleteAllOwnedByUserAsync(Guid ownerId, CancellationToken ct)` (busca os livros do usuário via um novo `IBookRepository.GetOwnedByUserAsync(Guid ownerId, ct)`, apaga cada arquivo via `IFileStorage.DeleteAsync` e remove o registro). `AuthService.DeleteAccountAsync` (Infra/Identity) passa a receber `IBookService` no construtor e chama esse método **antes** de `userManager.DeleteAsync(user)`. | Alternativa rejeitada: `AuthService` injetar `IBookRepository`+`IFileStorage` diretamente e reimplementar o loop de exclusão ali — duplica lógica que o `BookService` já precisa ter (excluir 1 livro = apagar arquivo + remover registro) e vaza detalhe de storage para a camada de Identity. |
| D-02-5 | Autorização por dono | Comparação direta `book.OwnerId == requesterId` dentro do `BookService` (não policy/handler ASP.NET). Livro inexistente **e** livro de outro dono lançam a mesma `BookNotFoundException` → `404` uniforme (não revela existência, conforme a spec). | A doc de referência menciona um handler `ResourceOwner` como ideal futuro — introduzir isso agora adiciona infraestrutura (`IAuthorizationHandler`, requirements) sem necessidade para o MVP; fica para a spec 04 (Robustez) se o padrão se repetir em mais contextos. |
| D-02-6 | Resolução do `RootPath` do `LocalFileStorage` | Injetar `IHostEnvironment` (disponível via o `FrameworkReference` já presente em `EReader_API.Infra.csproj`) e resolver `Path.GetFullPath(Path.Combine(env.ContentRootPath, options.RootPath))` uma vez no construtor. Com isso `FileStorage:RootPath = "App_Data/books"` funciona igual local e em container (`ContentRootPath` = `/app` no container). | Caminho relativo sem resolução fica dependente do diretório de trabalho do processo — funciona rodando `dotnet run` na pasta certa, mas quebra silenciosamente sob outro `cwd` (ex. IIS, systemd). |
| D-02-7 | Limite de tamanho de upload | Duas camadas: (a) `services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = ...)` lido de `FileStorage:MaxUploadBytes` em `AddInfrastructure` — rede de segurança no nível de transporte (senão o Kestrel devolve `413` genérico antes de chegar ao controller); (b) validação de negócio em `BookService.UploadAsync` comparando `req.FileSizeBytes` com o limite, para devolver `400` com `ProblemDetails` amigável (é esse `400` que o critério de aceite pede, não um `413`). | Não é possível usar `[RequestSizeLimit(N)]` porque o atributo exige uma constante em tempo de compilação, incompatível com um valor vindo de configuração. |
| D-02-8 | Persistência dos uploads em Docker | Adicionar volume nomeado `ereader-files` montado em `/app/App_Data/books` no serviço `api` de `docker-compose.yml`. | Sem volume, `docker compose up --build` recria o container e perde todos os uploads — o critério de aceite de upload→listar→download só sobrevive dentro da mesma execução de container. |
| D-02-9 | Formato do parâmetro `sort` | String de uma lista fechada: `title` \| `-title` \| `author` \| `-author` \| `createdAt` \| `-createdAt` (prefixo `-` = descendente); default `title` asc; valor fora da lista → `400`. | Um parser de sort genérico (múltiplos campos, sintaxe OData-like) é over-engineering para o MVP e não é pedido por nenhum critério de aceite. |
| D-02-10 | Seed público não baixa arquivos da internet | `PublicLibrarySeeder` lê `docs/seed/public-domain.json`; para cada item cujo `sourceFile` não exista em `docs/seed/files/`, loga um warning e **pula** (não falha o startup). PDFs de seed não entram no controle de versão (`docs/seed/files/*.pdf` no `.gitignore`), com um `docs/seed/README.md` documentando de onde baixar. | Sem essa tolerância, qualquer `dotnet run` em Dev sem os PDFs presentes localmente falharia o startup — inaceitável para quem só quer rodar a API sem mexer no Catalog ainda. |

Este plano assume as recomendações de D-02-1 a D-02-10. Se qualquer uma mudar, os passos e
arquivos finais abaixo mudam de posição/assinatura.

## 3. Ordem de execução

```
P1 baseline
   ──▶ P2 Domain (Book, BookSource, BookScope, BookQuery, PagedResult<T>, IBookRepository)
   ──▶ P3 Application/Catalog (IFileStorage, DTOs, exceções, IBookService)
   ──▶ P4 Infra/Storage (FileStorageOptions, LocalFileStorage)
   ──▶ P5 Infra/Repositories (BookRepository)
   ──▶ P6 Infra/Context (DbSet<Book>, BookConfiguration)
   ──▶ P7 Application/Catalog/BookService
   ──▶ P8 DI (Infra/DependencyInjection + Application/DependencyInjection)
   ──▶ P9 BookController
   ──▶ P10 appsettings (FileStorage) + P11 docker-compose (volume)
   ──▶ P12 AuthService.DeleteAccountAsync (cascata, D-02-4)
   ──▶ P13 migration AddBookCatalog
   ──▶ P14 seed (manifesto + PublicLibrarySeeder + .gitignore + README)
   ──▶ P15 Program.cs (chamar o seeder em Dev)
   ──▶ P16 verificação final
```

Commits pequenos sugeridos: (P2), (P3), (P4+P5), (P6), (P7), (P8+P9), (P10+P11), (P12), (P13),
(P14+P15).

---

## 4. Passo a passo

### P1 — Baseline

`dotnet build EReader.slnx` → deve estar verde (specs 00/01 concluídas). Parar e investigar se
não estiver.

### P2 — `Domain`

1. Reescrever `Entities/Catalog/Book.cs` conforme o modelo da spec (namespace com `;`,
   anotações de nulidade corretas — ver [5.1](#51-domainentitiescatalogbookcs)).
2. Criar `Entities/Catalog/BookSource.cs` (enum `PublicDomain = 0, UserUpload = 1`).
3. Criar `Entities/Catalog/BookScope.cs` (enum `All, Public, Mine`).
4. Criar `Entities/Catalog/BookQuery.cs` (record, ver [5.2](#52-domainentitiescatalogbookquerycs)).
5. Criar `Common/PagedResult.cs` (record genérico, ver [5.3](#53-domaincommonpagedresultcs)).
6. Criar `Interfaces/IBookRepository.cs` — inclui `GetOwnedByUserAsync` (D-02-4) além dos 5
   métodos da spec (ver [5.4](#54-domaininterfacesibookrepositorycs)).
7. `dotnet build EReader.slnx` → só quebra se algo em `Reading` referenciar o `Book` antigo por
   nome de campo (não deveria — `Bookmark`/`Highlight`/`Note` não têm navigation property para
   `Book`, só `int BookId`/`Guid UserId`, fora de escopo aqui).

### P3 — `Application/Catalog`

1. `Storage/IFileStorage.cs` (D-02-1, ver [5.5](#55-applicationstorageifilestoragecs)).
2. `Catalog/Dtos/BookDto.cs`, `UploadBookRequest.cs`, `UpdateBookRequest.cs`, `FileDownload.cs`
   (D-02-2, ver [5.6](#56-applicationcatalogdtos)).
3. `Catalog/BookNotFoundException.cs`, `Catalog/BookValidationException.cs` (mesmo padrão de
   `AuthException`/`IdentityValidationException`, ver [5.7](#57-applicationcatalogexceptions)).
4. `Catalog/IBookService.cs` — 5 métodos da spec + `OpenCoverAsync` (D-02-3) +
   `DeleteAllOwnedByUserAsync` (D-02-4) (ver [5.8](#58-applicationcatalogibookservicecs)).

### P4 — `Infra/Storage`

1. `FileStorageOptions.cs` (`RootPath`, `MaxUploadBytes`).
2. `LocalFileStorage : IFileStorage` (D-02-6): `SaveAsync` gera
   `fileKey = "{yyyy}/{MM}/{Guid:N}.pdf"`, cria diretórios, grava o stream; `OpenReadAsync`
   resolve o caminho absoluto e valida que continua sob a raiz antes de abrir `FileStream`
   só-leitura; `DeleteAsync` idempotente; `ExistsAsync` checa o arquivo físico. Ver
   [5.9](#59-infrastoragelocalfilestoragecs).

### P5 — `Infra/Repositories/BookRepository.cs`

`IBookRepository` real sobre `ApplicationDbContext`:
- `GetByIdAsync`/`GetOwnedByUserAsync`: `Books.FirstOrDefaultAsync`/`Where(...).ToListAsync`.
- `QueryAsync`: monta `IQueryable<Book>` a partir de `BookQuery` — filtro de escopo
  (`Public` ⇒ `Source == PublicDomain`; `Mine` ⇒ `OwnerId == RequesterId`; `All` ⇒
  `Source == PublicDomain || OwnerId == RequesterId`), busca (`Search` com `ILIKE` em
  `Title`/`Author` via `EF.Functions.ILike`), ordenação (whitelist do D-02-9), paginação
  (`Skip`/`Take`), `CountAsync` para `TotalCount`.
- `AddAsync`/`UpdateAsync`/`RemoveAsync`: `Add`/`Update`/`Remove` + `SaveChangesAsync` (sem
  Unit of Work — mesmo estilo simples já usado por `RefreshTokenStore`/`AuthService`).

Ver esqueleto em [5.10](#510-infrarepositoriesbookrepositorycs).

### P6 — `Infra/Context`

1. `ApplicationDbContext`: adicionar `public DbSet<Book> Books => Set<Book>();`.
2. `Context/Configurations/BookConfiguration.cs`: `HasKey(Id)`, `Title` obrigatório
   (`MaxLength(300)`), `Format` default `'pdf'`, índices em `OwnerId` e `Source`, FK
   `OwnerId → AspNetUsers.Id` (nullable, `HasOne<ApplicationUser>().WithMany()
   .HasForeignKey(b => b.OwnerId).OnDelete(DeleteBehavior.Cascade)` — mesmo padrão de
   `RefreshTokenConfiguration`; cascade não dispara para `OwnerId == null`, então livros
   públicos não são afetados), `HasCheckConstraint` para a invariante `Source`/`OwnerId` (ver
   [5.11](#511-infracontextconfigurationsbookconfigurationcs)).

### P7 — `Application/Catalog/BookService.cs`

Casos de uso (algoritmo por método, não código pronto):

- **ListAsync**: valida `sort` contra a whitelist (D-02-9) → `BookValidationException` se
  inválido; monta `BookQuery(requesterId, scope, search, author, page, pageSize, sort)`;
  `repository.QueryAsync`; mapeia `PagedResult<Book>` → `PagedResult<BookDto>`.
- **GetAsync**: `repository.GetByIdAsync`; `null` ou (`Source == UserUpload` e
  `OwnerId != requesterId`) → `BookNotFoundException`; senão mapeia para `BookDto`.
- **UploadAsync**: valida `Title` (1–300 chars) e `PageCount` (`>= 1` quando informado) →
  `BookValidationException` agregando todos os erros de uma vez (mesmo padrão de
  `IdentityValidationException`); lê os 5 primeiros bytes do `req.FileContent` e confere
  `%PDF-`; confere `req.ContentType == "application/pdf"`; confere
  `req.FileSizeBytes <= options.MaxUploadBytes` (D-02-7b); **depois** de validar, faz
  `stream.Seek(0, SeekOrigin.Begin)` (ou usa uma cópia, se o stream de origem não permitir
  voltar) antes de `fileStorage.SaveAsync`; monta `Book` com `Source = UserUpload`,
  `OwnerId = ownerId`, `CreatedAt`/`UpdatedAt = now`; `repository.AddAsync`; mapeia `BookDto`.
- **UpdateAsync**: mesma checagem de dono de `GetAsync`; atualiza só os campos de metadado
  (nunca `FileKey`/`Source`/`OwnerId`); `UpdatedAt = now`; `repository.UpdateAsync`.
- **DeleteAsync**: mesma checagem de dono; `fileStorage.DeleteAsync(book.FileKey)`
  **antes** de `repository.RemoveAsync` (se a remoção do arquivo falhar, preferir abortar a
  operação a deixar um `Book` "fantasma" sem arquivo — decisão de ordem, não crítica para o
  MVP: documentar a escolha ao implementar).
- **OpenFileAsync**: mesma checagem de dono; `fileStorage.OpenReadAsync(book.FileKey)`;
  devolve `FileDownload(stream, "application/pdf", $"{book.Title}.pdf")`.
- **OpenCoverAsync** (D-02-3): mesma checagem de dono; `book.CoverImageKey is null` → `null`
  (controller devolve `404`); senão abre via `fileStorage` (content-type de capa não é
  definido nesta spec — nenhum fluxo desta spec grava `CoverImageKey`, então na prática este
  método sempre devolve `null` até a spec 04 implementar extração de capa; o método já existe
  para não quebrar o endpoint quando isso acontecer).
- **DeleteAllOwnedByUserAsync** (D-02-4): `repository.GetOwnedByUserAsync(ownerId)`; para cada
  livro, `fileStorage.DeleteAsync(book.FileKey)` (idempotente) + `repository.RemoveAsync(book)`.

### P8 — DI

- `Infra/DependencyInjection.cs`: `services.Configure<FileStorageOptions>(configuration.GetSection("FileStorage"));`
  `services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = configuration.GetValue<long>("FileStorage:MaxUploadBytes"));`
  (D-02-7a); `services.AddScoped<IFileStorage, LocalFileStorage>();`
  `services.AddScoped<IBookRepository, BookRepository>();`
- `Application/DependencyInjection.cs`: `services.AddScoped<IBookService, BookService>();`

Ver [5.12](#512-infradependencyinjectioncs-diff).

### P9 — `BookController`

`[ApiController] [Route("api/books")] [Authorize]` (todos os 7 endpoints exigem Bearer, sem
exceção — inclusive a listagem/detalhe de livros públicos). Injeta `IBookService`. `POST`
mapeia `IFormFile` → `UploadBookRequest` (D-02-2). Erros: `BookNotFoundException` → `404`,
`BookValidationException` → `ValidationProblem` (mesmo padrão do `AuthController`). Ver
esqueleto em [5.13](#513-controllersbookcontrollercs-esqueleto).

### P10 — `appsettings.json`

Bloco `FileStorage` (ver [5.14](#514-appsettingsjson-diff)) — nada aqui é secreto, então entra
direto em `appsettings.json` (sem user-secrets).

### P11 — `docker-compose.yml`

Volume `ereader-files` (D-02-8, ver [5.15](#515-docker-composeyml-diff)).

### P12 — `AuthService.DeleteAccountAsync` (D-02-4)

Adicionar `IBookService bookService` ao construtor de `AuthService`; em
`DeleteAccountAsync`, chamar `await bookService.DeleteAllOwnedByUserAsync(userId, ct);` antes
de `await userManager.DeleteAsync(user);`. Ver [5.16](#516-infraidentityauthservicecs-diff).

### P13 — Migration

```powershell
dotnet ef migrations add AddBookCatalog `
  --project EReader_API.Infra `
  --startup-project EReader_API `
  --output-dir Migrations

dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

Conferir no arquivo gerado: tabela `Books` com `Id uuid` PK, `OwnerId uuid NULL` com FK para
`AspNetUsers` (`ON DELETE CASCADE`), índices em `OwnerId` e `Source`, `CHECK` da invariante
`Source`/`OwnerId` presente.

### P14 — Seed da biblioteca pública

1. `docs/seed/public-domain.json` — manifesto com 2–3 títulos (ex. *Dom Casmurro*, *Memórias
   Póstumas de Brás Cubas*, Machado de Assis) — campos `title`, `author`, `language`,
   `publishedDate`, `sourceFile` (ver [5.17](#517-docsseedpublic-domainjson)).
2. `docs/seed/README.md` — instruções de onde baixar os PDFs (ex. `dominiopublico.gov.br`,
   Gutenberg pt-BR) e onde colocá-los (`docs/seed/files/<sourceFile>`).
3. `.gitignore`: adicionar `docs/seed/files/*.pdf` (D-02-10) — o manifesto JSON e o README
   entram no controle de versão, os PDFs não.
4. `Infra/Seed/PublicLibrarySeeder.cs`: lê o manifesto (`System.Text.Json`), para cada item sem
   `Book` correspondente (`Source = PublicDomain`, comparação por `Title`+`Author`) verifica se
   `docs/seed/files/{sourceFile}` existe; se não existir, `logger.LogWarning` e `continue`; se
   existir, `fileStorage.SaveAsync` + `bookRepository.AddAsync` com `OwnerId = null`. Ver
   [5.18](#518-infraseedpubliclibraryseedercs).

### P15 — `Program.cs`

No bloco `if (app.Environment.IsDevelopment())`, depois do `Database.Migrate()` já existente,
chamar o seeder (mesmo `scope`). Ver [5.19](#519-programcs-diff).

### P16 — Verificação (ver seção 6)

---

## 5. Arquivos finais (esqueletos)

### 5.1 `Domain/Entities/Catalog/Book.cs`

```csharp
namespace EReader_API.Domain.Entities.Catalog;

public class Book
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public string Format { get; set; } = "pdf";
    public BookSource Source { get; set; }
    public Guid? OwnerId { get; set; }
    public string FileKey { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int? PageCount { get; set; }
    public string? CoverImageKey { get; set; }
    public DateTime? PublishedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 5.2 `Domain/Entities/Catalog/BookQuery.cs`

```csharp
namespace EReader_API.Domain.Entities.Catalog;

public record BookQuery(
    Guid RequesterId, BookScope Scope, string? Search, string? Author,
    int Page, int PageSize, string? Sort);
```

### 5.3 `Domain/Common/PagedResult.cs`

```csharp
namespace EReader_API.Domain.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
```

### 5.4 `Domain/Interfaces/IBookRepository.cs`

```csharp
using EReader_API.Domain.Common;
using EReader_API.Domain.Entities.Catalog;

namespace EReader_API.Domain.Interfaces;

public interface IBookRepository
{
    Task<Book?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Book>> GetOwnedByUserAsync(Guid ownerId, CancellationToken ct);
    Task<PagedResult<Book>> QueryAsync(BookQuery query, CancellationToken ct);
    Task AddAsync(Book book, CancellationToken ct);
    Task UpdateAsync(Book book, CancellationToken ct);
    Task RemoveAsync(Book book, CancellationToken ct);
}
```

### 5.5 `Application/Storage/IFileStorage.cs`

```csharp
namespace EReader_API.Application.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string contentType, string suggestedName, CancellationToken ct);
    Task<Stream> OpenReadAsync(string fileKey, CancellationToken ct);
    Task DeleteAsync(string fileKey, CancellationToken ct);
    Task<bool> ExistsAsync(string fileKey, CancellationToken ct);
}
```

### 5.6 `Application/Catalog/Dtos`

```csharp
namespace EReader_API.Application.Catalog;

public record BookDto(
    Guid Id, string Title, string? Author, string? Description, string? Language,
    string Format, string Source, Guid? OwnerId, long FileSizeBytes, int? PageCount,
    bool HasCoverImage, DateTime? PublishedDate, DateTime CreatedAt, DateTime UpdatedAt);

public record UploadBookRequest(
    string Title, string? Author, string? Description, string? Language,
    DateTime? PublishedDate, int? PageCount,
    Stream FileContent, string ContentType, string FileName, long FileSizeBytes);

public record UpdateBookRequest(
    string Title, string? Author, string? Description, string? Language,
    DateTime? PublishedDate, int? PageCount);

public record FileDownload(Stream Content, string ContentType, string FileName);
```

> `BookDto` expõe `HasCoverImage` (booleano), não `CoverImageKey`/`FileKey` — chaves internas
> de storage nunca saem da API (D-02-... implícito em D-02-1/D-02-2: o cliente sempre busca
> conteúdo via `/file`/`/cover`, nunca a chave crua).

### 5.7 `Application/Catalog` — exceções

```csharp
namespace EReader_API.Application.Catalog;

public class BookNotFoundException() : Exception("Livro não encontrado.");

public class BookValidationException(IEnumerable<string> errors)
    : Exception(string.Join("; ", errors))
{
    public IReadOnlyCollection<string> Errors { get; } = errors.ToList();
}
```

### 5.8 `Application/Catalog/IBookService.cs`

```csharp
using EReader_API.Domain.Common;

namespace EReader_API.Application.Catalog;

public interface IBookService
{
    Task<PagedResult<BookDto>> ListAsync(string? scope, string? search, string? author,
        int page, int pageSize, string? sort, Guid requesterId, CancellationToken ct);
    Task<BookDto> GetAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<BookDto> UploadAsync(UploadBookRequest req, Guid ownerId, CancellationToken ct);
    Task<BookDto> UpdateAsync(Guid id, UpdateBookRequest req, Guid requesterId, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<FileDownload> OpenFileAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task<FileDownload?> OpenCoverAsync(Guid id, Guid requesterId, CancellationToken ct);
    Task DeleteAllOwnedByUserAsync(Guid ownerId, CancellationToken ct);
}
```

> `ListAsync` recebe `scope`/`sort` como `string?` (não `BookScope`/enum) porque vêm de query
> string — a conversão/validação (D-02-9, whitelist de sort; parse de `scope` case-insensitive
> com fallback para `All`) acontece dentro do `BookService`, não no controller, para manter a
> `BookValidationException` centralizada num só lugar.

### 5.9 `Infra/Storage/LocalFileStorage.cs`

```csharp
using EReader_API.Application.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EReader_API.Infra.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment env)
    {
        _rootPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.RootPath));
    }

    // SaveAsync: fileKey = $"{DateTime.UtcNow:yyyy}/{DateTime.UtcNow:MM}/{Guid.NewGuid():N}.pdf";
    //   Directory.CreateDirectory(Path.GetDirectoryName(caminhoAbsoluto)!);
    //   grava `content` em FileStream (FileMode.CreateNew); devolve fileKey.
    // OpenReadAsync: caminhoAbsoluto = Path.GetFullPath(Path.Combine(_rootPath, fileKey));
    //   se não começar com _rootPath -> lançar (path traversal); abre FileStream read-only.
    // DeleteAsync: File.Delete só se File.Exists (idempotente).
    // ExistsAsync: File.Exists no caminho resolvido (mesma checagem de traversal).
}
```

### 5.10 `Infra/Repositories/BookRepository.cs`

```csharp
using EReader_API.Domain.Common;
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;

namespace EReader_API.Infra.Repositories;

public class BookRepository(ApplicationDbContext db) : IBookRepository
{
    // GetByIdAsync / GetOwnedByUserAsync: consultas diretas em db.Books.
    // QueryAsync: filtro por BookQuery.Scope (Public/Mine/All), EF.Functions.ILike para
    //   Search/Author, whitelist de Sort (D-02-9), Skip/Take, CountAsync separado para
    //   TotalCount (2 queries: contagem + página, aceitável para o volume do MVP).
    // AddAsync/UpdateAsync/RemoveAsync: Add/Update/Remove + SaveChangesAsync.
}
```

### 5.11 `Infra/Context/Configurations/BookConfiguration.cs`

```csharp
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations;

public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Title).IsRequired().HasMaxLength(300);
        builder.Property(b => b.Format).HasMaxLength(20).HasDefaultValue("pdf");
        builder.HasIndex(b => b.OwnerId);
        builder.HasIndex(b => b.Source);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(b => b.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Books_Source_OwnerId",
            "(\"Source\" = 0 AND \"OwnerId\" IS NULL) OR (\"Source\" = 1 AND \"OwnerId\" IS NOT NULL)"));
    }
}
```

> `"Source" = 0` (PublicDomain) / `= 1` (UserUpload) — conferir se o valor `int` do enum
> gravado pelo EF bate com essa ordem antes de fechar o `CHECK` (é a ordem declarada no enum,
> ver [5.1](#51-domainentitiescatalogbookcs) — mas confirmar no SQL gerado pela migration).

### 5.12 `Infra/DependencyInjection.cs` (diff)

```csharp
using EReader_API.Application.Catalog;
using EReader_API.Application.Storage;
using EReader_API.Infra.Repositories;
using EReader_API.Infra.Storage;
using Microsoft.AspNetCore.Http.Features; // FormOptions
// ...

services.Configure<FileStorageOptions>(configuration.GetSection("FileStorage"));
services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = configuration.GetValue<long>("FileStorage:MaxUploadBytes"));

services.AddScoped<IFileStorage, LocalFileStorage>();
services.AddScoped<IBookRepository, BookRepository>();
```

`Application/DependencyInjection.cs` ganha `services.AddScoped<IBookService, BookService>();`.

### 5.13 `Controllers/BookController.cs` (esqueleto)

```csharp
using EReader_API.Application.Catalog;
using EReader_API.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Controllers;

[ApiController]
[Route("api/books")]
[Authorize]
public class BookController(IBookService bookService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? scope, [FromQuery] string? search, [FromQuery] string? author,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? sort = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await bookService.ListAsync(scope, search, author, page, pageSize, sort, User.GetUserId(), ct));
        }
        catch (BookValidationException ex)
        {
            return ValidationProblem(string.Join("; ", ex.Errors));
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        try { return Ok(await bookService.GetAsync(id, User.GetUserId(), ct)); }
        catch (BookNotFoundException) { return NotFound(); }
    }

    [HttpPost]
    [RequestSizeLimit(100_000_000)] // teto absoluto de segurança; limite real vem de FileStorage:MaxUploadBytes (D-02-7)
    public async Task<IActionResult> Upload([FromForm] UploadBookForm form, CancellationToken ct)
    {
        try
        {
            var req = new UploadBookRequest(
                form.Title, form.Author, form.Description, form.Language, form.PublishedDate, form.PageCount,
                form.File.OpenReadStream(), form.File.ContentType, form.File.FileName, form.File.Length);
            var dto = await bookService.UploadAsync(req, User.GetUserId(), ct);
            return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
        }
        catch (BookValidationException ex) { return ValidationProblem(string.Join("; ", ex.Errors)); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateBookRequest req, CancellationToken ct)
    {
        try { return Ok(await bookService.UpdateAsync(id, req, User.GetUserId(), ct)); }
        catch (BookNotFoundException) { return NotFound(); }
        catch (BookValidationException ex) { return ValidationProblem(string.Join("; ", ex.Errors)); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await bookService.DeleteAsync(id, User.GetUserId(), ct); return NoContent(); }
        catch (BookNotFoundException) { return NotFound(); }
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken ct)
    {
        try
        {
            var file = await bookService.OpenFileAsync(id, User.GetUserId(), ct);
            return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
        }
        catch (BookNotFoundException) { return NotFound(); }
    }

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> GetCover(Guid id, CancellationToken ct)
    {
        try
        {
            var cover = await bookService.OpenCoverAsync(id, User.GetUserId(), ct);
            return cover is null ? NotFound() : File(cover.Content, cover.ContentType, cover.FileName, enableRangeProcessing: true);
        }
        catch (BookNotFoundException) { return NotFound(); }
    }
}

public class UploadBookForm
{
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public DateTime? PublishedDate { get; set; }
    public int? PageCount { get; set; }
    public IFormFile File { get; set; } = null!;
}
```

> `UploadBookForm` (com `[FromForm]`) é o tipo de binding do ASP.NET Core para
> `multipart/form-data` — fica no projeto `EReader_API` (host), não em `Application`, porque
> depende de `IFormFile` (D-02-2). É só um adaptador; o `BookService` nunca o vê.

### 5.14 `appsettings.json` (diff)

```json
{
  "FileStorage": {
    "RootPath": "App_Data/books",
    "MaxUploadBytes": 52428800
  }
}
```

### 5.15 `docker-compose.yml` (diff)

```yaml
services:
  api:
    # ...
    volumes:
      - ereader-files:/app/App_Data/books

volumes:
  ereader-pgdata:
  ereader-files:
```

### 5.16 `Infra/Identity/AuthService.cs` (diff)

```csharp
public class AuthService(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenStore refreshTokenStore,
    IEmailSender emailSender,
    IBookService bookService) : IAuthService   // + IBookService
{
    // ...
    public async Task DeleteAccountAsync(Guid userId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
                   ?? throw new AuthException("Usuário não encontrado.");

        await bookService.DeleteAllOwnedByUserAsync(userId, ct);   // novo (D-02-4)
        await userManager.DeleteAsync(user);
    }
}
```

### 5.17 `docs/seed/public-domain.json`

```json
[
  {
    "title": "Dom Casmurro",
    "author": "Machado de Assis",
    "language": "pt-BR",
    "publishedDate": "1899-01-01",
    "sourceFile": "dom-casmurro.pdf"
  },
  {
    "title": "Memórias Póstumas de Brás Cubas",
    "author": "Machado de Assis",
    "language": "pt-BR",
    "publishedDate": "1881-01-01",
    "sourceFile": "memorias-postumas-bras-cubas.pdf"
  }
]
```

### 5.18 `Infra/Seed/PublicLibrarySeeder.cs`

```csharp
namespace EReader_API.Infra.Seed;

public class PublicLibrarySeeder(ApplicationDbContext db, IFileStorage fileStorage, ILogger<PublicLibrarySeeder> logger)
{
    // SeedAsync(CancellationToken ct):
    //   manifesto = JsonSerializer.Deserialize<List<SeedEntry>>(File.ReadAllText(".../docs/seed/public-domain.json"));
    //   foreach entry in manifesto:
    //     if db.Books.Any(b => b.Title == entry.Title && b.Author == entry.Author) -> continue;
    //     caminho = Path.Combine(".../docs/seed/files", entry.SourceFile);
    //     if (!File.Exists(caminho)) { logger.LogWarning(...); continue; }  // D-02-10
    //     usando FileStream de leitura em `caminho`, fileStorage.SaveAsync(...) -> fileKey;
    //     db.Books.Add(new Book { Source = PublicDomain, OwnerId = null, FileKey = fileKey, ... });
    //   db.SaveChangesAsync(ct);
}
```

### 5.19 `Program.cs` (diff)

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<PublicLibrarySeeder>().SeedAsync(default);
}
```

(`PublicLibrarySeeder` precisa estar registrado no DI — `services.AddScoped<PublicLibrarySeeder>();`
em `AddInfrastructure`.)

---

## 6. Verificação final (mapeada aos critérios de aceite da spec)

| Critério de aceite (spec 02) | Como verificar | Passo |
|---|---|---|
| Upload de PDF válido cria `Book` (`UserUpload`) e grava arquivo; `GET .../file` devolve mesmo conteúdo | `curl -F` upload → pegar `id` → `curl .../file -o out.pdf` → comparar hash (`Get-FileHash`) com o original | P7/P9 |
| Upload não-PDF / sem `%PDF-` / acima do limite → `400` | Repetir upload trocando `Content-Type`, corrompendo os magic bytes, e com arquivo > `MaxUploadBytes` | P7 |
| `GET /api/books/{id}` de livro de outro usuário → `404` | Logar com 2 usuários distintos, tentar acessar o livro do outro | P7 |
| `scope=public` lista o seed; `scope=mine` só os do usuário; `scope=all` (padrão) os dois | `curl` com cada valor de `scope`, comparando os `id`s retornados | P7/P14 |
| Paginação: `page`/`pageSize` respeitados, resposta traz `totalCount` | Criar > `pageSize` livros, conferir `items.Count == pageSize` e `totalCount` correto | P7 |
| `Range: bytes=0-1023` → `206 Partial Content` + `Content-Range` | `curl -i -H "Range: bytes=0-1023" .../file` | P9 |
| `DELETE /api/books/{id}` remove registro e arquivo; `GET` seguinte → `404` | `curl -X DELETE` seguido de `curl` no mesmo `id` | P7 |
| `DELETE /api/auth/me` remove também os livros e arquivos do usuário | Upload → `DELETE /api/auth/me` → login (novo usuário) e conferir que o arquivo físico sumiu e `GetOwnedByUserAsync` não retorna mais nada | P12 |
| Migration aplica e reverte sem erro | `dotnet ef database update` e `dotnet ef database update 0` | P13 |

Roteiro de `curl` sugerido (PowerShell):

```powershell
$base = "http://localhost:5035/api"
$token = (...)  # accessToken de /api/auth/login (spec 01)

curl -s -X POST "$base/books" -H "Authorization: Bearer $token" `
  -F "title=Livro de Teste" -F "author=Autor X" -F "file=@C:\caminho\teste.pdf;type=application/pdf"
# guardar o `id` da resposta

curl -s "$base/books?scope=mine" -H "Authorization: Bearer $token"
curl -s -i -H "Range: bytes=0-1023" -H "Authorization: Bearer $token" "$base/books/$id/file"
curl -s -X DELETE -H "Authorization: Bearer $token" "$base/books/$id"
curl -s -i "$base/books/$id" -H "Authorization: Bearer $token"   # -> 404
```

## 7. Riscos e armadilhas

- **`IFormFile.OpenReadStream()` e o peek dos magic bytes**: o ASP.NET Core sempre devolve um
  stream com `CanSeek == true` para `IFormFile` (buffer em memória ou arquivo temporário,
  dependendo do tamanho) — ler 5 bytes e dar `Seek(0, Begin)` antes de `SaveAsync` é seguro,
  mas confirmar isso ao implementar (não assumir para um `Stream` genérico vindo de outro lugar
  no futuro).
- **`enableRangeProcessing: true` exige stream seekable**: `LocalFileStorage.OpenReadAsync`
  precisa devolver um `FileStream` de verdade (seekable), nunca um wrapper que só suporte
  leitura sequencial — senão o `206`/`Content-Range` não funciona.
- **Valor numérico do enum no `CHECK` constraint** ([5.11](#511-infracontextconfigurationsbookconfigurationcs)):
  conferir no SQL gerado pela migration que `PublicDomain = 0` e `UserUpload = 1` realmente
  batem com o que o EF grava (comportamento padrão é a ordem de declaração do enum, mas vale
  conferir).
- **Ordem de exclusão em `DeleteAsync`/`DeleteAllOwnedByUserAsync`**: apagar o arquivo físico
  antes do registro no banco significa que uma falha de storage aborta a operação inteira
  (nada é removido) — é a escolha mais segura para não deixar `Book` órfão sem arquivo; o
  inverso (remover DB primeiro) arrisca deixar arquivo órfão em disco, que é um problema mais
  barato de limpar depois manualmente.
- **Volume novo no `docker-compose.yml`**: só passa a existir depois de um `up --build`; não
  afeta o volume do Postgres (`ereader-pgdata`), então não há risco de perda de dados de
  usuário ao aplicar essa mudança — mas uploads feitos em execuções anteriores (sem o volume)
  já foram perdidos de qualquer forma.
- **Seed em Dev roda a cada `dotnet run`**: a comparação por `Title`+`Author` evita duplicar,
  mas é uma comparação exata de string — variações de acentuação/espaço no manifesto vs. um
  seed anterior podem duplicar; manter o manifesto como única fonte de verdade e não editar
  `Book`s de seed manualmente pelo `PUT`.
- **`FileStorage:MaxUploadBytes` em duas camadas (D-02-7)**: se só a de transporte
  (`FormOptions`) for configurada e a de negócio esquecida (ou vice-versa), o comportamento observado
  diverge do critério de aceite (`413` do Kestrel em vez do `400` esperado, ou o oposto) — testar
  os dois casos limites separadamente.

## 8. Rollback

Cada passo é um commit isolado. Reverter com `git revert <commit>`. Para a migration:
`dotnet ef database update 0 --project EReader_API.Infra --startup-project EReader_API` antes
de `dotnet ef migrations remove --project EReader_API.Infra --startup-project EReader_API`. O
volume `ereader-files` pode ser removido com `docker compose down -v` se for descartável (perde
os uploads feitos em container).

## 9. Ganchos para as próximas specs / follow-ups

- **`CLAUDE.md`**: atualizar a seção "Architecture" — `Domain` ganha `Entities/Catalog/Book`
  redesenhado + `Common/PagedResult`; `Application` ganha `Catalog/` e `Storage/`; `Infra`
  ganha `Repositories/`, `Storage/`, `Seed/`; `EReader_API` ganha `BookController` funcional.
  A frase "todo repositório segue `Get*Async`/`GetByIdAsync(int?)`/..." deixa de valer
  universalmente — `IBookRepository` usa `Guid` e `QueryAsync`/`PagedResult<T>` em vez de
  `Get*Async` simples; vale registrar essa divergência explicitamente.
- **Spec 03 (Leitura)**: `ReadingProgress`/`Bookmark`/`Highlight`/`Note` passam a referenciar
  `Book.Id` (`Guid`) — já compatível a partir desta spec. A regra "remover `Book` `UserUpload`
  remove em cascata as entidades de `Reading`" (doc de referência §4.3) ainda não está
  implementada — a FK `BookId` nessas entidades e seu `OnDelete` são responsabilidade da
  spec 03, que redesenha essas entidades por completo.
- **Spec 04 (Robustez)**: padroniza `ProblemDetails` — as exceções ad-hoc
  (`BookNotFoundException`, `BookValidationException`) devem migrar para esse padrão junto com
  `AuthException`/`IdentityValidationException`; extração automática de `PageCount`/capa
  (D-6 do documento de referência) passa a preencher `CoverImageKey`, momento em que
  `OpenCoverAsync` finalmente devolve algo em runtime; a policy `ResourceOwner` (D-02-5) pode
  ser formalizada aqui se o padrão "dono ou 404" se repetir em mais contextos.
- **Spec 05 (Testes)**: `BookService` é testável via mocks de `IBookRepository`/`IFileStorage`
  (interfaces puras, sem EF/ASP.NET); `LocalFileStorage` pode ser testado com um diretório
  temporário real (sem mock, já que é só `System.IO`); `PublicLibrarySeeder` pode ser testado
  isolado injetando um `IFileStorage` fake e um manifesto de teste.
