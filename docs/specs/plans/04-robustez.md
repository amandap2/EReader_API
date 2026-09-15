# Plano de implementação — Spec 04: Robustez e operação

> Plano de execução para [`../04-robustez.md`](../04-robustez.md). Depende das specs 01, 02 e 03
> (concluídas). Objetivo: tratamento de erros consistente (ProblemDetails), validação
> centralizada, rate limiting (`auth` formalizada + `upload` nova + limite global por IP), health
> checks, extração de `PageCount`/capa do PDF no upload, revisão de índices/performance, migration
> `AddPerformanceIndexes`, validação de config na inicialização.

## 0. Estado atual (verificado)

- **Erros**: `Program.cs` já chama `AddProblemDetails()` + `UseExceptionHandler()` (sem
  `IExceptionHandler` customizado — usa o handler default, que devolve `application/problem+json`
  genérico, sem mapear por tipo de exceção) e `UseStatusCodePages()`. Não há
  `Infrastructure/GlobalExceptionHandler.cs`. Cada bounded context tem seu **próprio par de
  exceções não relacionadas** (`Exception` direto, sem hierarquia comum):
  `EReader_API.Application.Identity.{IdentityValidationException, AuthException}`,
  `EReader_API.Application.Catalog.{BookNotFoundException, BookValidationException}`,
  `EReader_API.Application.Reading.{ReadingNotFoundException, ReadingValidationException}`. Todo
  controller (`AuthController`, `BookController`, `ReadingProgressController`,
  `BookmarksController`, `HighlightsController`, `NotesController`) tem `try/catch` manual por
  ação convertendo essas exceções em `NotFound()`/`ValidationProblem(...)`/`Unauthorized(...)` —
  exatamente a duplicação que 4.1 pede para centralizar. `LibraryController` não lança nada
  (sem `try/catch`). Nenhum `NotImplementedException` remanescente no código (`grep` já limpo).
- **Validação**: 100% manual dentro dos serviços (listas de `string` viram `*ValidationException`).
  Nenhuma DTO de request usa `System.ComponentModel.DataAnnotations` hoje — `RegisterRequest`,
  `LoginRequest`, `UpdateHighlightRequest`, `UploadBookForm` etc. são records/classes "nuas".
  `BookController.List` recebe `page`/`pageSize` como parâmetros soltos (`[FromQuery] int page = 1,
  pageSize = 20`) sem nenhum limite — hoje dá pra pedir `pageSize=999999`.
- **Rate limiting**: `Infra/DependencyInjection.cs` já registra `AddRateLimiter` com a policy
  `"auth"` (fixed window, 10/min por IP, `RejectionStatusCode = 429`) — falta formalizar isso como
  já "pronto" (item já satisfeito), mais a policy `"upload"` (não existe) e um limiter global por
  IP (não existe — hoje só a policy `auth` está registrada, o resto do tráfego não passa por
  nenhum limiter mesmo com `app.UseRateLimiter()` já no pipeline).
- **Health checks**: não existem. Nenhuma referência a
  `Microsoft.Extensions.Diagnostics.HealthChecks*` em nenhum `.csproj`.
- **`PageCount`/capa**: `Book.PageCount` (`int?`) e `Book.CoverImageKey` (`string?`) **já existem**
  no schema desde a spec 02 — não é preciso migration para adicionar colunas, só para os novos
  índices (ver abaixo). Hoje `PageCount` só é gravado se o cliente mandar no `multipart/form-data`
  (`UploadBookForm.PageCount`); `CoverImageKey` nunca é setado em lugar nenhum —
  `GET /api/books/{id}/cover` sempre `404` (`BookService.OpenCoverAsync` já trata `CoverImageKey ==
  null` retornando `null` → controller devolve `404`, isso já está pronto e não muda). Nenhuma
  biblioteca de PDF referenciada em nenhum `.csproj` (nem PdfPig nem Docnet.Core).
- **Índices já existentes** (conferidos nas `IEntityTypeConfiguration<>` de `Infra/Context/
  Configurations/`): `Book(OwnerId)`, `Book(Source)` (`BookConfiguration`), `ReadingProgress
  (UserId, BookId)` único, `Bookmark(UserId, BookId)`, `Highlight(UserId, BookId)`,
  `Note(UserId, BookId)` (não únicos), `RefreshToken(TokenHash)` único + `RefreshToken(UserId)`
  (`RefreshTokenConfiguration`). **Falta só** `Book(Title)`, que é o único item da lista da spec
  que meu diff efetivamente precisa acrescentar — os outros cinco já estão feitos pelas specs
  01–03. `BookRepository.QueryAsync` já usa `AsNoTracking()` e `Skip/Take` + `CountAsync`; busca já
  usa `EF.Functions.ILike`. Os demais repositórios (`Reading`/`BookRepository.GetByIdAsync` etc.)
  **não** usam `AsNoTracking()` nas leituras que alimentam `Update` (ver [D-04-8](#d-04-8)).
- **Config na inicialização**: `Infra/DependencyInjection.cs` já lança
  `InvalidOperationException` se `ConnectionStrings:Default` estiver vazio (fail-fast já
  implementado, spec 00). `Jwt:SigningKey` **não** é validado no startup — só falha tarde, na
  primeira chamada a `Convert.FromBase64String` dentro do `AddJwtBearer` (avaliado quando o
  pipeline de auth processa a 1ª request, não no `app.Build()`). `FileStorage:RootPath` tem
  default `"App_Data/books"` em `FileStorageOptions` — nunca está "ausente" tecnicamente, então
  "falhar por ausência" não se aplica a ele; a preocupação real é gravabilidade em runtime, que
  é papel do health check de storage (4.4), não de validação de config no startup.
- **Gap conhecido, fora do texto literal da spec mas mencionado no `CLAUDE.md`**:
  `BookService.DeleteAsync`/`GetAuthorizedAsync` (spec 02) só bloqueiam exclusão de `UserUpload`
  de outro dono — qualquer usuário autenticado consegue `DELETE` um livro `PublicDomain`, e isso
  agora also apaga em cascata o progresso/anotações de **todos** os usuários que o leram (spec 03).
  Ver [D-04-9](#d-04-9) — decisão a confirmar com você antes de incluir, já que não está no
  checklist literal de `04-robustez.md`.
- Nenhum projeto de teste existe ainda (spec 05) — itens de teste do checklist ficam fora deste
  plano (mesmo tratamento das specs 02/03).

## 1. Pré-condições

- Specs 01, 02 e 03 concluídas: build verde, migrations `AddIdentityAndRefreshTokens`,
  `AddBookCatalog`, `AddReadingContext` aplicadas, todas as rotas atuais funcionais.
- Banco Postgres acessível.
- Dockerfile builda a partir de `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian, `linux-x64`) —
  relevante para a escolha de biblioteca de PDF em [D-04-6](#d-04-6) (precisa funcionar dentro do
  container, não só localmente no Windows).

## 2. Decisões a confirmar antes de começar

| # | Decisão | Recomendação | Impacto se não seguir |
|---|---|---|---|
| D-04-1 | Hierarquia comum de exceções | Criar `Application/Common/Exceptions/{NotFoundException,ForbiddenException,ValidationException,ConflictException,UnauthorizedException}.cs` como classes-base **abstratas**. Fazer as exceções existentes **herdarem** delas em vez de `Exception` direto: `BookNotFoundException`/`ReadingNotFoundException` → `NotFoundException`; `BookValidationException`/`ReadingValidationException`/`IdentityValidationException` → `ValidationException(errors)`; `AuthException` → `UnauthorizedException`. Nomes/namespaces atuais não mudam (zero call-site diff fora dos `throw`/`class` declarations e da remoção dos `try/catch` nos controllers). | Reescrever tudo do zero como uma exceção genérica só (`DomainException` com um enum de tipo) forçaria editar todo `throw new XyzException(...)` existente — herdar preserva 100% do código que já lança essas exceções. |
| D-04-2 | `UnauthorizedException` além da lista literal da spec | A spec 4.1 lista só `NotFoundException/ForbiddenException/ValidationException/ConflictException → demais 500`. Mas `AuthException` (login/refresh inválidos) **precisa** continuar virando `401`, não `500` — é comportamento já existente (spec 01) que a spec 04 não pode regredir. Adicionar `UnauthorizedException → 401` ao mapeamento do `GlobalExceptionHandler`, além dos 4 da spec. | Seguir o texto literal faria login/refresh inválidos virarem `500 Internal Server Error` em vez de `401` — regressão de comportamento visível e quebra o critério de aceite implícito "credenciais inválidas não é erro de servidor". |
| D-04-3 | `IExceptionHandler` global | `EReader_API/Infrastructure/GlobalExceptionHandler.cs`, registrado com `builder.Services.AddExceptionHandler<GlobalExceptionHandler>()` **antes** de `AddProblemDetails()`. Faz `switch` por tipo (`is ValidationException ve` extrai `ve.Errors` para o dicionário `errors`; os demais só status+title) e escreve via `IProblemDetailsService.WriteAsync`. Retorna `false` para exceções não mapeadas (deixa o handler default do ASP.NET Core cuidar — que, com `AddProblemDetails()` já registrado, produz `500` `application/problem+json` sem stack trace em nenhum ambiente, já que não há `UseDeveloperExceptionPage()` no pipeline hoje). Depois disso, **remover os `try/catch` de todos os 6 controllers** — eles passam a deixar a exceção subir. | Manter os `try/catch` nos controllers ao lado do handler global duplica a lógica de mapeamento em dois lugares (controller decide o status, handler global também decide) — se divergirem, o comportamento observado depende de qual `catch` pega primeiro, virando fonte de bug sutil. |
| D-04-4 | `traceId`/`title`/`type`/`detail` padronizados | Em vez de escrever esses 4 campos à mão dentro do `GlobalExceptionHandler` E de novo em algum filtro de validação, usar `AddProblemDetails(options => options.CustomizeProblemDetails = ctx => { ... traceId = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier ... })` — esse `CustomizeProblemDetails` já é aplicado pelo ASP.NET Core em **todo** `ProblemDetails` gerado no processo: pelo `UseExceptionHandler`, pelo `UseStatusCodePages`, e pelo `ValidationProblemDetails` automático do `[ApiController]` (ver D-04-5) — um único lugar cobre os 3 caminhos. | Escrever `traceId` manualmente em cada `return` de cada controller (como hoje faz `ValidationProblem(string.Join(...))`, que nem seta `traceId`) é o padrão atual e é exatamente o que a spec pede para eliminar. |
| D-04-5 | Validação de forma (shape) via `DataAnnotations` nativas do `[ApiController]`, não um filtro customizado | Todos os controllers já têm `[ApiController]`, que **já** intercepta `ModelState` inválido e devolve `400` `ValidationProblemDetails` com `errors` por campo automaticamente — sem escrever nenhum filtro. Só falta anotar as DTOs com `[Required]`/`[EmailAddress]`/`[StringLength]`/`[Range]`/`[RegularExpression]` (ver [5.5](#55-dataannotations-nas-dtos-exemplos)). `pageSize`/`page` do `BookController.List` (hoje parâmetros soltos, não uma DTO) recebem `[Range(1,100)]`/`[Range(1,int.MaxValue)]` diretamente no parâmetro do método — o `[ApiController]` valida atributos em parâmetros de ação do mesmo jeito que em propriedades de DTO. Regras que dependem de outro registro no banco (unicidade de e-mail — já tratada pelo Identity —, `sort`/`scope` com valores permitidos, `highlightId` de outro livro/usuário) continuam como validação manual nos serviços, gerando `ValidationException` (D-04-1), porque `DataAnnotations` não alcança essas checagens. | Escrever um `IAsyncActionFilter`/`IEndpointFilter` customizado para reimplementar o que o `[ApiController]` já faz de graça é retrabalho — só faz sentido se a spec exigisse um formato de erro diferente do `ValidationProblemDetails` padrão, o que não é o caso (o critério de aceite só pede `400` com `errors` por campo, que é exatamente a forma default). |
| D-04-6 | Biblioteca de extração de PDF: **Docnet.Core**, não PdfPig | A spec pede `GetPageCount` **e** `RenderFirstPagePng`. PdfPig lê metadados/texto mas não rasteriza página em bitmap sem uma segunda lib; `Docnet.Core` (wrapper do PDFium, nupkg com binários nativos para `linux-x64`/`win-x64`/`osx-x64`) faz as duas coisas com uma única dependência e já é usado em produção .NET para esse propósito. Também adicionar `SixLabors.ImageSharp` só para codificar o buffer de pixels cru que o Docnet devolve em JPEG (não existe encoder de imagem builtin no .NET fora do Windows desde que `System.Drawing.Common` foi restrito a Windows-only — relevante porque o container roda Linux, ver Dockerfile). Classe `Infra/Pdf/DocnetPdfInspector.cs` (nome diverge do `PdfPigInspector.cs` da seção "Arquivos afetados" da spec — deliberado, ver justificativa acima). | Ficar só com PdfPig cobre `GetPageCount` mas deixa `RenderFirstPagePng` sem implementação (a spec pede os dois no mesmo item do checklist) — teria que adicionar uma segunda lib de rasterização de qualquer forma, então começar direto com Docnet.Core (que cobre ambos) é estritamente menos dependências. |
| D-04-7 | Extração síncrona no upload, PageCount extraído tem prioridade sobre o campo do formulário | Confirma a "Estratégia" já sugerida no texto da spec (síncrono no MVP). Em `BookService.UploadAsync`, depois de `fileStorage.SaveAsync`, chama `pdfInspector.GetPageCount`/`RenderFirstPagePng` dentro de `try/catch` amplo — falha loga (`ILogger`) e segue com `PageCount = req.PageCount` (o que o usuário mandou, ou `null`) e `CoverImageKey = null`, sem bloquear o upload (exigência explícita do critério de aceite). Em caso de sucesso, `PageCount` extraído **sobrescreve** o valor do formulário (fonte mais confiável que o número digitado por quem fez upload) — `CoverImageKey` é setado só se a extração de página deu certo. | Preferir o `PageCount` do formulário sobre o extraído economiza uma linha, mas contraria o espírito do item — a extração automática existe justamente para não depender do usuário digitar certo; e um valor de formulário desatualizado nunca seria corrigido depois. |
| D-04-8 | Propagação de `PageCount` para `ReadingProgress` existentes | Novo método `Task<IReadOnlyList<ReadingProgress>> ListByBookAsync(Guid bookId, CancellationToken ct)` em `IReadingProgressRepository`/`ReadingProgressRepository` (mesmo padrão dos outros `ListXAsync`). `BookService` ganha uma dependência a mais, `IReadingProgressRepository` (permitido — está em `Domain.Interfaces`, `Application/Catalog` já só depende de `Domain`, nenhuma violação de camada). Ao definir/alterar `Book.PageCount` (tanto em `UploadAsync` quanto em `UpdateAsync`, os dois lugares que hoje escrevem esse campo), buscar os `ReadingProgress` do livro e, para cada um, `TotalPages = book.PageCount` +
`PercentComplete = ReadingProgressCalculator.PercentComplete(p.CurrentPage, book.PageCount)`, salvando via `UpsertAsync`. `ReadingProgressCalculator` (hoje `internal`-por-convenção em `Application/Reading`, mas já `public`) muda de pasta para `Application/Common/ReadingProgressCalculator.cs` — mesmo raciocínio de `BookMapper` (D-03-5): função pura compartilhada por dois bounded contexts, não faz sentido um "possuir" o outro. `Reading/ReadingProgressService.cs` passa a referenciar `EReader_API.Application.Common.ReadingProgressCalculator`. | Deixar `ReadingProgressCalculator` dentro de `Application/Reading` e `Catalog` referenciá-lo de lá funcionaria (mesmo assembly), mas inverte a dependência conceitual — `Catalog` não deveria "importar de dentro" de `Reading` para uma função que não tem nada de específico de `Reading`. Não propagar (deixar o valor desatualizado até o próximo `PUT /progress`) contraria o critério de aceite explícito da spec ("Após o PageCount ser definido, `GET .../progress` passa a retornar `percentComplete` > 0"). |
| D-04-9 | Corrigir o gap "qualquer um deleta livro público" | **A confirmar com você** — não está no checklist literal de `04-robustez.md`, mas o `CLAUDE.md` já o lista como "flagged for spec 04" e a spec 04 é o lugar natural (é exatamente o tipo de item de "robustez"). Se aprovado: `BookService.DeleteAsync` ganha uma checagem antes de deletar — `book.Source == BookSource.PublicDomain` → `throw new ForbiddenException()` (novo tipo de D-04-1, primeiro uso real dele no código) — só o dono de um `UserUpload` pode deletar seu próprio livro; nenhum usuário comum deleta um livro do catálogo público (isso fica implicitamente reservado a um fluxo administrativo que não existe ainda, fora de escopo). Se **não** aprovado, este item sai do plano e o gap permanece documentado no `CLAUDE.md` como estava. | Incluir sem confirmar seria expandir o escopo da spec por conta própria; não incluir deixa uma escrita destrutiva (`DELETE`) sem controle de autorização adequado, que é exatamente o tipo de coisa que "robustez" deveria fechar — por isso está marcado para sua decisão explícita, não assumido. |
| D-04-10 | `/health/live` vs `/health/ready` | `/health/live`: `MapHealthChecks("/health/live", new() { Predicate = _ => false })` — sem rodar nenhum check, só confirma que o processo aceita requisições (liveness "puro", não depende de banco/disco, para não derrubar o pod/container por causa de uma dependência externa temporariamente fora). `/health/ready`: `MapHealthChecks("/health/ready")` sem predicate — roda todos os checks registrados (`AddDbContextCheck<ApplicationDbContext>()` + `FileStorageHealthCheck` customizado). Ambos endpoints sem `[Authorize]` (padrão para health checks — orquestradores/load balancers não mandam Bearer token). | Um único endpoint `/health` que roda tudo mistura os dois conceitos — um Kubernetes/orquestrador usaria liveness pra decidir "reiniciar o processo" e isso reiniciaria o container toda vez que o Postgres piscar, o que é o oposto do que liveness deveria fazer. |
| D-04-11 | Migration só de índice, nome `AddPerformanceIndexes` (não `AddCoverAndIndexes`) | Como `PageCount`/`CoverImageKey` já existem no schema desde a spec 02 (ver [seção 0](#0-estado-atual-verificado)), a única mudança de schema desta spec é o índice novo em `Book(Title)` — não há coluna de capa para adicionar. Nome da migration reflete o conteúdo real em vez do nome sugerido no texto da spec (que previa possivelmente precisar de coluna nova para capa). | Manter o nome literal `AddCoverAndIndexes` seria enganoso no histórico de migrations — alguém lendo `dotnet ef migrations list` no futuro esperaria uma coluna de capa que não existe ali (ela já foi adicionada por `AddBookCatalog`, spec 02). |

Este plano assume as recomendações de D-04-1 a D-04-8 e D-04-10/D-04-11. **D-04-9 aguarda sua
confirmação explícita** antes da implementação — sinalize sim/não junto com a aprovação do plano.

## 3. Ordem de execução

```
P1 baseline
   ──▶ P2 Application/Common/Exceptions (hierarquia D-04-1) + mover ReadingProgressCalculator (D-04-8)
   ──▶ P3 Fazer exceções existentes herdarem da hierarquia (Identity/Catalog/Reading)
   ──▶ P4 EReader_API/Infrastructure/GlobalExceptionHandler.cs + Program.cs (AddProblemDetails
        customizado, AddExceptionHandler) + remover try/catch dos 6 controllers
   ──▶ P5 DataAnnotations nas DTOs de request (D-04-5) + Range em page/pageSize
   ──▶ P6 Rate limiting: formalizar "auth", policy "upload" (por usuário), limiter global por IP
   ──▶ P7 Health checks (pacote + AddDbContextCheck + FileStorageHealthCheck + endpoints)
   ──▶ P8 IPdfInspector (Application/Catalog) + DocnetPdfInspector (Infra/Pdf) (D-04-6)
   ──▶ P9 BookService: extração no upload (D-04-7) + propagação de PageCount (D-04-8) +
        (se D-04-9 aprovado) bloqueio de DELETE em PublicDomain
   ──▶ P10 Infra/Context: índice Book(Title) + ReadingProgressRepository.ListByBookAsync
   ──▶ P11 Validação de Jwt:SigningKey no startup (ValidateOnStart)
   ──▶ P12 migration AddPerformanceIndexes
   ──▶ P13 verificação final
```

Commits pequenos sugeridos: (P2+P3), (P4), (P5), (P6), (P7), (P8+P9+P10), (P11), (P12).

---

## 4. Passo a passo

### P1 — Baseline

`dotnet build EReader.slnx` → deve estar verde. Parar e investigar se não estiver.

### P2 — `Application/Common`

1. `Common/Exceptions/NotFoundException.cs`, `ForbiddenException.cs`, `ValidationException.cs`
   (com `Errors`), `ConflictException.cs`, `UnauthorizedException.cs` — ver
   [5.1](#51-applicationcommonexceptions).
2. Mover `Application/Reading/ReadingProgressCalculator.cs` →
   `Application/Common/ReadingProgressCalculator.cs` (D-04-8), atualizar o `using` em
   `ReadingProgressService.cs`.
3. `dotnet build EReader.slnx` → verde (nada além do `namespace`/`using` do calculator muda).

### P3 — Exceções existentes herdam da hierarquia comum

1. `Catalog/BookNotFoundException.cs` → `: NotFoundException`; `Catalog/BookValidationException.cs`
   → `: ValidationException`.
2. `Reading/ReadingNotFoundException.cs` → `: NotFoundException`;
   `Reading/ReadingValidationException.cs` → `: ValidationException`.
3. `Identity/IdentityValidationException.cs` → `: ValidationException`; `Identity/AuthException.cs`
   → `: UnauthorizedException` (D-04-2).
4. `dotnet build EReader.slnx` → verde (assinaturas públicas não mudam, só a base class).

### P4 — `GlobalExceptionHandler` + `Program.cs` + limpeza dos controllers

1. `EReader_API/Infrastructure/GlobalExceptionHandler.cs` — ver
   [5.2](#52-ereader_apiinfrastructureglobalexceptionhandlercs).
2. `Program.cs`: `AddExceptionHandler<GlobalExceptionHandler>()` antes de
   `AddProblemDetails(options => options.CustomizeProblemDetails = ...)` (D-04-4) — ver
   [5.3](#53-programcs-diff).
3. Remover todo `try/catch` de `AuthController`, `BookController`, `ReadingProgressController`,
   `BookmarksController`, `HighlightsController`, `NotesController` — as ações passam a só chamar
   o serviço e deixar a exceção subir (ver [5.4](#54-controller-depois-exemplo)).
4. `dotnet build EReader.slnx` → verde. Testar manualmente: `GET /api/books/{id-inexistente}` →
   `404` `application/problem+json` com `traceId`; `POST /api/auth/login` com senha errada → `401`.

### P5 — `DataAnnotations`

Anotar `RegisterRequest`, `LoginRequest`, `ResetPasswordRequest`, `ForgotPasswordRequest`,
`UpdateHighlightRequest`, `CreateHighlightRequest`, `UploadBookForm`, `UpdateBookRequest`,
`CreateBookmarkRequest`, `CreateNoteRequest` conforme [5.5](#55-dataannotations-nas-dtos-exemplos).
`BookController.List`: `[Range(1, int.MaxValue)] int page = 1, [Range(1, 100)] int pageSize = 20`.
Testar: `POST /api/auth/register` com e-mail inválido → `400` com `errors.email`.

### P6 — Rate limiting

`Infra/DependencyInjection.cs`, dentro de `AddRateLimiter`:
1. Policy `"upload"` — partição por `userId` (não IP, já que é `[Authorize]`), 20/hora, ver
   [5.6](#56-ratelimiter-diff).
2. `options.GlobalLimiter` — partição por IP, ex. 100/min, brando (cobre tudo que não tem policy
   nomeada).
3. `BookController`: `[HttpPost] [EnableRateLimiting("upload")]` só na ação `Upload` (as outras
   ações do controller continuam só sob o limiter global).
4. Testar: 21 uploads em menos de 1h do mesmo usuário → 21ª dá `429` com header `Retry-After`.

### P7 — Health checks

1. `EReader_API.Infra.csproj`: `PackageReference
   Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`.
2. `Infra/Storage/FileStorageHealthCheck.cs` — ver
   [5.7](#57-infrastoragefilestoragehealthcheckcs).
3. `Infra/DependencyInjection.cs`: `services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>().AddCheck<FileStorageHealthCheck>("file_storage")`.
4. `Program.cs`: `app.MapHealthChecks("/health/live", ...)` / `app.MapHealthChecks("/health/ready")`
   (D-04-10).
5. Testar: `GET /health/ready` com Postgres no ar → `200 {"status":"Healthy"}`; derrubar o
   container do banco (`docker compose stop db`) → `GET /health/ready` → `503 Unhealthy`;
   `GET /health/live` continua `200` mesmo com o banco fora.

### P8 — `IPdfInspector`

1. `Application/Catalog/IPdfInspector.cs` — ver [5.8](#58-applicationcatalogipdfinspectorcs).
2. `EReader_API.Infra.csproj`: `PackageReference Docnet.Core` + `PackageReference
   SixLabors.ImageSharp` (D-04-6).
3. `Infra/Pdf/DocnetPdfInspector.cs` — ver [5.9](#59-infrapdfdocnetpdfinspectorcs).
4. `Infra/DependencyInjection.cs`: `services.AddScoped<IPdfInspector, DocnetPdfInspector>();`.

### P9 — `BookService`: extração + propagação + (D-04-9) bloqueio de delete público

1. Injeta `IPdfInspector`, `IReadingProgressRepository`, `ILogger<BookService>` no construtor.
2. `UploadAsync`: depois de `SaveAsync`, extrai `PageCount`/gera capa dentro de `try/catch`
   (D-04-7) — ver [5.10](#510-bookservice-diff-uploadasync).
3. Método privado `PropagateProgressAsync(Guid bookId, int? pageCount, CancellationToken ct)`
   (D-04-8), chamado ao fim de `UploadAsync` e `UpdateAsync` quando `PageCount` muda.
4. **Se D-04-9 aprovado**: `DeleteAsync` ganha a checagem
   `if (book.Source == BookSource.PublicDomain) throw new ForbiddenException();` logo após
   `GetAuthorizedAsync`.
5. `dotnet build EReader.slnx` → verde.

### P10 — `Infra/Context` + `ReadingProgressRepository`

1. `BookConfiguration.cs`: `builder.HasIndex(b => b.Title);`.
2. `IReadingProgressRepository`/`ReadingProgressRepository`: `ListByBookAsync(Guid bookId,
   CancellationToken ct)` (D-04-8) — `Where(p => p.BookId == bookId)`.

### P11 — Validação de `Jwt:SigningKey` no startup

`Infra/DependencyInjection.cs`:
```csharp
services.AddOptions<JwtOptions>()
    .Bind(configuration.GetSection("Jwt"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey não configurado.")
    .ValidateOnStart();
```
(substitui o `services.Configure<JwtOptions>(...)` atual — `ValidateOnStart()` faz o
`IHostedService` de validação de opções rodar no `app.Build()`/primeira resolução, falhando
rápido com mensagem clara em vez de só quebrar na 1ª request de auth). Testar: rodar sem
user-secret/env var `Jwt__SigningKey` configurado → app falha ao subir com a mensagem, não só na
1ª chamada.

### P12 — Migration

```powershell
dotnet ef migrations add AddPerformanceIndexes `
  --project EReader_API.Infra `
  --startup-project EReader_API `
  --output-dir Migrations

dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

Conferir no arquivo gerado: só `CREATE INDEX` em `Books(Title)`, nenhuma coluna nova (D-04-11).

### P13 — Verificação (ver seção 6)

---

## 5. Arquivos finais (esqueletos)

### 5.1 `Application/Common/Exceptions`

```csharp
// NotFoundException.cs
namespace EReader_API.Application.Common.Exceptions;

public abstract class NotFoundException(string message) : Exception(message)
{
    protected NotFoundException() : this("Recurso não encontrado.") { }
}

// ForbiddenException.cs
namespace EReader_API.Application.Common.Exceptions;

public abstract class ForbiddenException(string message) : Exception(message)
{
    protected ForbiddenException() : this("Operação não permitida.") { }
}

// ValidationException.cs
namespace EReader_API.Application.Common.Exceptions;

public abstract class ValidationException(IEnumerable<string> errors)
    : Exception(string.Join("; ", errors))
{
    public IReadOnlyCollection<string> Errors { get; } = errors.ToList();
}

// ConflictException.cs
namespace EReader_API.Application.Common.Exceptions;

public abstract class ConflictException(string message) : Exception(message);

// UnauthorizedException.cs
namespace EReader_API.Application.Common.Exceptions;

public abstract class UnauthorizedException(string message) : Exception(message);
```

`BookNotFoundException`/`ReadingNotFoundException` passam a `class BookNotFoundException()
: NotFoundException("Livro não encontrado.");` (idem para as outras — só troca o `: Exception`
por `: NotFoundException`/`: ValidationException`/`: UnauthorizedException`, mantendo a mensagem
já usada hoje).

### 5.2 `EReader_API/Infrastructure/GlobalExceptionHandler.cs`

```csharp
using EReader_API.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EReader_API.Infrastructure;

public class GlobalExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, errors) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Recurso não encontrado", null),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Operação não permitida", null),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Não autorizado", null),
            ConflictException => (StatusCodes.Status409Conflict, "Conflito", null),
            ValidationException ve => (StatusCodes.Status400BadRequest, "Erro de validação",
                (IReadOnlyCollection<string>?)ve.Errors),
            _ => (0, null, null),
        };

        if (status == 0)
            return false; // deixa o handler default (500 problem+json, sem stack) tratar

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
        };
        if (errors is not null)
            problem.Extensions["errors"] = errors.Select((e, i) => (i, e))
                .GroupBy(_ => "", x => x.e)
                .ToDictionary(g => g.Key, g => g.ToArray());

        return await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
```

> O agrupamento de `errors` acima é só um placeholder simples (lista única sob uma chave vazia);
> como nenhuma das exceções de validação hoje carrega o **nome do campo**, só a mensagem (`List
> <string>`), o formato exato de "`errors` por campo" que sai automático do `[ApiController]`
> (D-04-5) é quem cobre esse critério de aceite de verdade — o `errors` aqui é best-effort para as
> validações de regra de negócio (que não têm um "campo" único, ex. "`sort` inválido"). Ajustar o
> formato exato na implementação, se `ValidationProblemDetails` (em vez de `ProblemDetails` +
> extension manual) ficar mais simples de popular a partir de `IReadOnlyCollection<string>`.

### 5.3 `Program.cs` (diff)

```csharp
using System.Diagnostics;
using EReader_API.Infrastructure;

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
```

(substitui o `builder.Services.AddProblemDetails();` de hoje; `app.UseExceptionHandler();` no
pipeline não muda — passa a resolver o `GlobalExceptionHandler` registrado). Health checks (P7):

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
```

### 5.4 Controller depois (exemplo)

```csharp
// BookController.cs — depois de P4, sem try/catch
[HttpGet("{id:guid}")]
public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
    Ok(await bookService.GetAsync(id, User.GetUserId(), ct));
```

`ValidationProblem(...)` e os `catch` somem de todo controller — a exceção sobe até o
`GlobalExceptionHandler`.

### 5.5 `DataAnnotations` nas DTOs (exemplos)

```csharp
// RegisterRequest.cs
using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Identity;

public record RegisterRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(8)] string Password,
    [property: Required, StringLength(100)] string DisplayName);

// UpdateHighlightRequest.cs
using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record UpdateHighlightRequest(
    [property: RegularExpression("^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$")] string? Color,
    string? TextContent);
```

`UploadBookForm` (classe, não record — `BookController.cs`): `[Required, StringLength(300)]` em
`Title`, `[RegularExpression("^[a-z]{2}(-[A-Z]{2})?$")]` em `Language`,
`[Range(1, int.MaxValue)]` em `PageCount`. Mesmo padrão em `UpdateBookRequest`,
`CreateBookmarkRequest.PageNumber`/`CreateHighlightRequest.PageNumber`
(`[Range(1, int.MaxValue)]` — mantém a checagem de negócio "`pageNumber >= 1`" hoje manual no
serviço redundante com o atributo; **decisão**: manter as duas (atributo pega o `400` mais cedo,
sem round-trip ao banco; checagem manual no serviço fica como defesa em profundidade e não sai —
é barata e já existe).

### 5.6 `RateLimiter` diff (`Infra/DependencyInjection.cs`)

```csharp
services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        }));

    options.AddPolicy("upload", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.User.GetUserId().ToString(),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromHours(1), QueueLimit = 0,
        }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
```

> `options.GlobalLimiter` roda **além** de qualquer policy nomeada aplicada via
> `[EnableRateLimiting]` (não substitui) — uma requisição de upload passa pelos dois: `"upload"`
> (20/h por usuário) e o global (100/min por IP). Isso é o comportamento desejado (upload já é
> naturalmente mais restrito que o limite global, então o global só entra em jogo nas rotas sem
> policy nomeada).

### 5.7 `Infra/Storage/FileStorageHealthCheck.cs`

```csharp
using EReader_API.Application.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace EReader_API.Infra.Storage;

public class FileStorageHealthCheck(FileStorageOptions options, IHostEnvironment env) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.RootPath));
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".health-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Diretório de storage não é gravável.", ex);
        }
    }
}
```

### 5.8 `Application/Catalog/IPdfInspector.cs`

```csharp
namespace EReader_API.Application.Catalog;

public record PdfInspectionResult(int PageCount, byte[]? CoverJpegBytes);

public interface IPdfInspector
{
    Task<PdfInspectionResult> InspectAsync(Stream pdfContent, CancellationToken ct);
}
```

### 5.9 `Infra/Pdf/DocnetPdfInspector.cs`

```csharp
using Docnet.Core;
using Docnet.Core.Models;
using EReader_API.Application.Catalog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EReader_API.Infra.Pdf;

public class DocnetPdfInspector : IPdfInspector
{
    public Task<PdfInspectionResult> InspectAsync(Stream pdfContent, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        pdfContent.CopyTo(ms);
        var bytes = ms.ToArray();

        using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(1080, 1920));
        var pageCount = docReader.GetPageCount();

        byte[]? coverBytes = null;
        if (pageCount > 0)
        {
            using var pageReader = docReader.GetPageReader(0);
            var rawBytes = pageReader.GetImage();
            var (width, height) = (pageReader.GetPageWidth(), pageReader.GetPageHeight());

            using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);
            using var outStream = new MemoryStream();
            image.SaveAsJpeg(outStream);
            coverBytes = outStream.ToArray();
        }

        return Task.FromResult(new PdfInspectionResult(pageCount, coverBytes));
    }
}
```

> Esqueleto — API exata do `Docnet.Core` (nomes de `PageDimensions`, `GetImage`, formato de pixel
> devolvido) deve ser conferida contra a versão do pacote resolvida no `dotnet restore` antes de
> codar (não testado neste plano, só levantado por conhecimento da lib). `PageDimensions(1080,
> 1920)` é só a resolução-alvo do render — ajustar conforme necessidade real da capa.

### 5.10 `BookService` diff — `UploadAsync`

```csharp
var fileKey = await fileStorage.SaveAsync(req.FileContent, req.ContentType, req.FileName, ct);

int? pageCount = req.PageCount;
string? coverImageKey = null;
try
{
    if (req.FileContent.CanSeek) req.FileContent.Seek(0, SeekOrigin.Begin);
    var inspection = await pdfInspector.InspectAsync(req.FileContent, ct);
    pageCount = inspection.PageCount;
    if (inspection.CoverJpegBytes is { } jpeg)
    {
        using var coverStream = new MemoryStream(jpeg);
        coverImageKey = await fileStorage.SaveCoverAsync(coverStream, ct); // novo método em IFileStorage
    }
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Falha ao extrair PageCount/capa do PDF recém-enviado.");
}

var book = new Book { /* ... */ PageCount = pageCount, CoverImageKey = coverImageKey, /* ... */ };
await repository.AddAsync(book, ct);
await PropagateProgressAsync(book.Id, book.PageCount, ct); // livro novo: sem ReadingProgress ainda, no-op, mas mantém 1 caminho de código com UpdateAsync
return BookMapper.ToDto(book);
```

> `IFileStorage.SaveCoverAsync` é um método novo (mesma forma de `SaveAsync`, mas gerando uma
> `fileKey` sob um prefixo diferente, ex. `{yyyy}/{MM}/{Guid:N}-cover.jpg`) — não estava no
> escopo da spec 02 porque não havia nada que gerasse capas ainda; adicionar aqui é o mínimo
> necessário para persistir o JPEG gerado. Alternativa rejeitada: reaproveitar `SaveAsync` (que
> hoje sempre gera `.pdf` no nome) — precisaria de um parâmetro de extensão, mudando a assinatura
> pública existente; um método novo é aditivo e não quebra nada em `Infra`/testes futuros.

## 6. Verificação final (mapeada aos critérios de aceite da spec)

| Critério de aceite (spec 04) | Como verificar | Passo |
|---|---|---|
| Toda rota de erro retorna `application/problem+json` com `traceId`; nenhum `500` vaza stack em `Production` | `GET /api/books/{guid-inexistente}` (autenticado) → inspecionar corpo `404` tem `traceId`; forçar uma exceção não mapeada (ex. desconectar o banco) → `500` sem `StackTrace` no corpo, em qualquer `ASPNETCORE_ENVIRONMENT` | P4 |
| Payload inválido → `400` com `errors` por campo | `POST /api/auth/register` com `email: "not-an-email"` → `400`, corpo tem `errors.email` (formato automático do `[ApiController]`) | P5 |
| Estourar a policy de `auth`/`upload` → `429` com `Retry-After` | 11 `POST /api/auth/login` em <1min do mesmo IP → 11ª `429` + header `Retry-After`; 21 uploads em <1h do mesmo usuário → 21ª `429` | P6 |
| `GET /health/ready` fica `Unhealthy` quando o banco está fora ou storage não gravável | `docker compose stop db` → `/health/ready` `503`; `chmod -w` (ou renomear) a pasta de `FileStorage:RootPath` → `/health/ready` `503` com o check `file_storage` `Unhealthy` | P7 |
| Upload de PDF de N páginas → `Book.PageCount == N` e `CoverImageKey` preenchido; PDF corrompido ainda cria o `Book` (sem `PageCount`) e loga erro | Upload de um PDF real de N páginas conhecido → `GET /api/books/{id}` mostra `pageCount: N`; `GET /api/books/{id}/cover` devolve a imagem (não mais `404`); upload de um arquivo com magic bytes `%PDF-` mas conteúdo corrompido depois → `Book` é criado (`201`), log de warning aparece, `pageCount` é o que veio do formulário (ou `null`) | P9 |
| Após `PageCount` ser definido, `GET .../progress` passa a retornar `percentComplete` > 0 para quem já tinha progresso | Criar `ReadingProgress` num livro sem `PageCount` (`percentComplete: 0`) → fazer `PUT /api/books/{id}` com um `PageCount` novo (ou re-upload que dispare a extração, se aplicável ao fluxo de update) → `GET .../progress` do mesmo usuário já mostra `percentComplete` recalculado | P9/P10 |
| `EXPLAIN` da listagem de livros usa índice (sem seq scan em tabela grande) | `EXPLAIN ANALYZE SELECT * FROM "Books" WHERE "Source" = 0 ORDER BY "Title" LIMIT 20;` no Postgres após popular a tabela com massa de teste → plano usa `Index Scan`/`Bitmap Index Scan` em `Source`/`Title`, não `Seq Scan` | P10/P12 |
| Migration aplica e reverte sem erro | `dotnet ef database update` e `dotnet ef database update 0` | P12 |

Itens de configuração cobertos fora da tabela de critérios de aceite explícitos, mas no checklist:
subir a API sem `Jwt__SigningKey` configurado → falha imediata no `app.Build()`/startup com
mensagem clara (P11), não só na primeira chamada de login.

## 7. Riscos e armadilhas

- **`Docnet.Core` dentro do container Linux**: o pacote traz binários nativos do PDFium por RID;
  confirmar que o `dotnet publish` do `Dockerfile` (que roda dentro da imagem `sdk:10.0`, também
  Debian) resolve o RID `linux-x64` corretamente e que o binário nativo é copiado para
  `/app/publish`. Se não for automático, pode ser necessário `<RuntimeIdentifier>linux-x64
  </RuntimeIdentifier>` explícito no `.csproj` do host ou `--self-contained false -r linux-x64`
  no `dotnet publish` do `Dockerfile` — validar rodando `docker compose up --build` e fazendo um
  upload de teste antes de considerar P8/P9 concluído, não só `dotnet build` local no Windows.
- **Extração síncrona bloqueia a resposta de upload**: para um PDF grande (dezenas de MB, perto do
  limite de `FileStorage:MaxUploadBytes`), rasterizar a 1ª página pode adicionar segundos à
  resposta de `POST /api/books`. A spec já antecipa isso ("extrair para `IHostedService`/fila se o
  tempo de resposta incomodar") — este plano **não** implementa a versão assíncrona (fora do
  escopo combinado em D-04-7), só documenta que é o próximo passo se a latência for um problema
  real depois de medir.
- **`GlobalExceptionHandler` e exceções não-domínio**: qualquer `Exception` que não herde de
  `NotFoundException/ForbiddenException/ValidationException/ConflictException/
  UnauthorizedException` (ex. `NullReferenceException` por um bug real, `NpgsqlException` por
  timeout de banco) cai no branch `_ => (0, null, null)` e retorna `false` — o handler default do
  ASP.NET Core assume, devolvendo `500` genérico. Isso é o comportamento correto (não mascarar bug
  real como um 4xx), mas significa que esses casos **não** aparecem logados pelo
  `GlobalExceptionHandler` (que não loga nada explicitamente hoje) — só no log padrão de
  `UseExceptionHandler`. Se quiser um log estruturado com `traceId`/`userId` no escopo (item
  opcional "4.4 observabilidade", não coberto por este plano — ver seção 9), esse é o lugar mais
  natural para adicionar.
- **`DataAnnotations` em `record` com posição por índice de parâmetro**: atributos em
  `record Foo([property: Required] string X)` exigem o prefixo `[property: ...]` — usar
  `[Required]` sozinho no parâmetro do `record` aplica ao **parâmetro do construtor**, não à
  propriedade gerada, e o `ModelState` do ASP.NET Core valida propriedades, não parâmetros de
  construtor — atributo sem `[property: ...]` silenciosamente não teria efeito nenhum. Atenção ao
  codar P5.
- **`options.GlobalLimiter` + policy nomeada = duas contagens por request**: já documentado na
  nota de [5.6](#56-ratelimiter-diff) — confirmar que isso é aceitável (é, dado os limites
  escolhidos) antes de a policy de upload ficar mais apertada que o limite global no futuro (nesse
  caso o global nunca dispararia primeiro em rotas de upload, o que é inofensivo, só redundante).

## 8. Rollback

Cada passo é um commit isolado. Reverter com `git revert <commit>`. Para a migration:
`dotnet ef database update 0 --project EReader_API.Infra --startup-project EReader_API` antes de
`dotnet ef migrations remove --project EReader_API.Infra --startup-project EReader_API`.

## 9. Ganchos para as próximas specs / follow-ups

- **`CLAUDE.md`**: atualizar "Architecture" — `Application` ganha `Common/Exceptions/` +
  `Common/ReadingProgressCalculator.cs` (movido de `Reading`), `Catalog/IPdfInspector.cs`;
  hierarquia de exceções passa a ser mencionada como comum às três bounded contexts; `Infra` ganha
  `Pdf/DocnetPdfInspector.cs`, `Storage/FileStorageHealthCheck.cs`; `EReader_API` ganha
  `Infrastructure/GlobalExceptionHandler.cs`; todos os 6 controllers perdem seus `try/catch`.
  Atualizar também a frase sobre o gap de `DeleteAsync`/`PublicDomain` se D-04-9 for aprovado e
  implementado.
- **OpenTelemetry / logging estruturado com `traceId`/`userId` no escopo** (item "opcional" do
  4.4): não coberto por este plano — se quiser, é um plano à parte (adicionar
  `ILogger` scope middleware +, opcionalmente, `OpenTelemetry.Extensions.Hosting` +
  exporter). Mencionado aqui só para não ficar esquecido.
- **`pg_trgm`/full-text search**: se a busca por título/autor (`ILIKE '%termo%'`) ficar lenta com
  volume real (índice B-tree normal não acelera padrão `%...%`), o próximo passo é uma extensão
  `pg_trgm` + índice GIN — não implementado agora, só a checagem "não fazer `Seq Scan`" desta spec
  já cobre o caso de filtro por `Source`/ordenação por `Title`, não o `ILIKE` livre.
- **Spec 05 (Testes)**: `GlobalExceptionHandler` é testável isolado (mock de
  `IProblemDetailsService`, uma exceção de cada tipo da hierarquia → status esperado);
  `DocnetPdfInspector` precisa de um PDF de fixture real para teste de integração (não é
  mockável de forma útil, já que a lógica interessante é a chamada ao PDFium); `FileStorageHealthCheck`
  testável com um diretório temporário real (`Path.GetTempPath()`) apontado via `FileStorageOptions`.
