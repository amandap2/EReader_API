# 04 — Robustez e operação

**Status:** Em andamento (ver [`plans/04-robustez.md`](plans/04-robustez.md) — implementado exceto
os itens marcados abaixo; falta apenas 4.6 projeção em banco + verificação ao vivo de dois
critérios de aceite antes de marcar Concluído)
**Depende de:** 01, 02, 03
**Objetivo:** Deixar a API pronta para uso real: tratamento de erros consistente
(ProblemDetails), validação centralizada, rate limiting, health checks, observabilidade,
extração de `PageCount`/capa do PDF, e revisão de índices/performance.

## Escopo

**Entra:** os itens abaixo. **Não entra:** deploy em nuvem, storage em nuvem, CI/CD,
autenticação social — ver Fase 5 em `../ESPECIFICACAO-BACKEND.md`.

## 4.1 Tratamento de erros

- [x] `AddProblemDetails()` + `IExceptionHandler` global mapeando exceções de domínio para
      status: `NotFoundException → 404`, `ForbiddenException → 403`,
      `ValidationException → 400` (com `errors` por campo), `ConflictException → 409`,
      demais → `500` sem vazar stack em produção. (Ganhou também `UnauthorizedException → 401`,
      necessário para não regredir login/refresh — ver D-04-2 no plano.)
- [x] Padronizar `type`/`title`/`detail`/`traceId` em todas as respostas de erro.
- [x] Remover `throw new NotImplementedException()` remanescentes. (Nenhum existia.)

## 4.2 Validação

- [x] Validação de entrada por DTO (`DataAnnotations`, usando a validação automática já embutida
      no `[ApiController]` em vez de um filtro customizado — ver D-04-5 no plano).
      Regras: tamanhos de string, ranges (`pageNumber >= 1`, `pageSize` 1–100), formatos
      (e-mail, cor hex, `language`).
- [x] Falha de validação → `400` ProblemDetails com dicionário `errors`.

## 4.3 Rate limiting

- [x] `AddRateLimiter`: policy `"auth"` (já introduzida na spec 01, formalizada aqui),
      policy `"upload"` para `POST /api/books` (20/hora por usuário), limite global
      brando por IP (100/min).
- [x] Resposta `429` com `Retry-After`.

## 4.4 Health checks e observabilidade

- [x] `AddHealthChecks().AddDbContextCheck<ApplicationDbContext>()` + check de escrita no
      `FileStorage:RootPath`.
- [x] Endpoints `GET /health/live` e `GET /health/ready`.
- [ ] Logging estruturado com `traceId`/`userId` no escopo da requisição; níveis por
      configuração (já existe base). **Não implementado** — fora do plano de implementação
      (ver seção 9 do plano).
- [ ] (Opcional) OpenTelemetry para traces/métricas. **Não implementado** (opcional).

## 4.5 Extração de `PageCount` e capa

- [x] `IPdfInspector` (Application) + implementação com `Docnet.Core` (não `PdfPig` — ver D-04-6
      no plano) em Infra: `InspectAsync` devolve `PageCount` + capa JPEG renderizada da 1ª
      página.
- [x] No `UploadAsync`: após salvar o PDF, extrai `PageCount` e gera a capa, gravando
      `CoverImageKey`. Extração falha → loga e segue com `PageCount` do formulário (ou nulo),
      sem bloquear o upload — verificado manualmente com um PDF corrompido (magic bytes válidos,
      corpo inválido): `Book` criado com `201`, `pageCount: null`, warning logado.
- [x] Estratégia síncrona no upload confirmada (D-04-7 no plano); migrar para
      `IHostedService`/fila fica para quando/se a latência incomodar.
- [x] Ao definir `Book.PageCount` (upload ou update), propaga para `ReadingProgress.TotalPages`
      existentes daquele livro e recalcula `PercentComplete` — verificado manualmente.

## 4.6 Índices e performance

- [x] Revisar/confirmar índices: `Book(OwnerId)`, `Book(Source)` já existiam (specs 02/03);
      `Book(Title)` adicionado nesta spec; `ReadingProgress(UserId, BookId)` único,
      `(UserId, BookId)` nas anotações e `RefreshToken(TokenHash)` já existiam.
- [ ] `QueryAsync` de livros: projeção para DTO no banco (`Select`) — **não implementado**;
      `QueryAsync` ainda materializa `Book` completo (`AsNoTracking()`, mas sem `Select` de
      DTO) antes do `BookMapper.ToDto` em memória. Paginação com `Skip/Take` + `CountAsync` numa
      única viagem: mantido como já estava (duas queries — contagem e página — como no código
      pré-existente; "numa única viagem" não foi implementado).
- [x] `AsNoTracking()` em todas as leituras — faltava em `BookRepository.GetByIdAsync`/
      `GetByIdsAsync`/`GetOwnedByUserAsync` e em todos os métodos de leitura dos repositórios de
      `Reading`; adicionado (commit "4.6 gap fix" — item que não estava nos passos originais do
      plano, pego na revisão final).
- [x] Busca textual: `ILIKE` mantido; `pg_trgm`/full-text avaliado e anotado como próximo passo
      (seção 9 do plano), não implementado agora — conforme a própria spec pede.

## 4.7 Migrations e configuração

- [x] Migration `AddPerformanceIndexes` (renomeada de `AddCoverAndIndexes` — sem mudança de
      schema para capa, `PageCount`/`CoverImageKey` já existiam desde a spec 02; ver D-04-11).
- [x] Em produção: `Database.Migrate()` já só roda dentro do bloco
      `if (app.Environment.IsDevelopment())` (pré-existente, spec 00); `dotnet ef database
      update` já documentado no `CLAUDE.md`.
- [x] Validar na inicialização: `Jwt:SigningKey` agora falha rápido via `ValidateOnStart()`
      (verificado manualmente); `ConnectionStrings:Default` já falhava rápido desde a spec 00;
      `FileStorage:RootPath` não é validado por presença (tem default, nunca "ausente") — sua
      gravabilidade é responsabilidade do health check de storage (4.4), não do startup.

## Critérios de aceite

- [x] Toda rota de erro retorna `application/problem+json` com `traceId`; nenhuma `500` vaza
  stack em `Production`. **Verificado manualmente.**
- [x] Payload inválido → `400` com `errors` por campo. **Verificado manualmente.**
- [x] Estourar a policy de `auth` → `429` com `Retry-After`. **Verificado manualmente**
  (11ª tentativa de login em <1min). A policy `upload` (20/hora) usa a mesma implementação —
  não foi exercitada ao vivo (impraticável fazer 21 uploads reais numa sessão de verificação).
- [x] `GET /health/ready` fica `Unhealthy` quando o banco está fora. **Verificado manualmente**
  (`docker stop`/`start` do container Postgres). O caso "diretório de storage não gravável" não
  foi exercitado ao vivo (mudar permissões de pasta no Windows local não reflete o ambiente do
  container Linux) — `FileStorageHealthCheck` foi revisado por código, não testado ao vivo nesse
  caminho específico.
- [x] Upload de um PDF de N páginas resulta em `Book.PageCount == N` e `CoverImageKey`
  preenchido; PDF corrompido ainda cria o `Book` (sem `PageCount`) e loga o erro. **Ambos os
  casos verificados manualmente** (upload real de PDF + upload de PDF com magic bytes válidos e
  corpo corrompido).
- [x] Após o `PageCount` ser definido, `GET /api/books/{id}/progress` passa a retornar
  `percentComplete` > 0 para quem já tinha progresso. **Verificado manualmente.**
- [ ] `EXPLAIN` da listagem de livros usa índice (sem seq scan em tabela grande). **Não
  verificado** — precisaria de massa de dados real para ser conclusivo; o índice
  `Book(Title)` foi criado (migration `AddPerformanceIndexes`), mas nenhum `EXPLAIN` foi rodado
  nesta sessão.

## Arquivos afetados (como implementado)

- `EReader_API/Program.cs` — `AddExceptionHandler`/`AddProblemDetails` customizado, ordem do
  pipeline (`UseRateLimiter` depois de `UseAuthentication`/`UseAuthorization`), `MapHealthChecks`.
- `EReader_API/Infrastructure/GlobalExceptionHandler.cs` (novo).
- `EReader_API.Application/Common/Exceptions/*` (novo — `NotFoundException`,
  `ForbiddenException`, `ValidationException`, `ConflictException`, `UnauthorizedException`).
- `EReader_API.Application/Common/ReadingProgressCalculator.cs` (movido de `Reading/`).
- DTOs de request em `Application/Identity/Dtos`, `Application/Reading/Dtos`,
  `Application/Catalog/Dtos` + `EReader_API/Controllers/BookController.cs`
  (`UploadBookForm`) — `DataAnnotations`, sem pasta `Validators/` separada (não foi necessária,
  ver D-04-5 no plano).
- `EReader_API.Application/Catalog/IPdfInspector.cs` + `EReader_API.Infra/Pdf/
  DocnetPdfInspector.cs` (não `PdfPigInspector.cs` — ver D-04-6 no plano).
- `EReader_API.Application/Catalog/BookForbiddenException.cs` (novo — bloqueia `DELETE` de
  livro `PublicDomain`, D-04-9).
- `EReader_API.Application/Catalog/BookService.cs` (extração no upload, propagação de
  `TotalPages`, bloqueio de delete público).
- `EReader_API.Application/Storage/IFileStorage.cs` + `EReader_API.Infra/Storage/
  LocalFileStorage.cs` (`SaveCoverAsync` novo) + `EReader_API.Infra/Storage/
  FileStorageHealthCheck.cs` (novo).
- `EReader_API.Domain/Interfaces/IReadingProgressRepository.cs` +
  `EReader_API.Infra/Repositories/ReadingProgressRepository.cs` (`ListByBookAsync` novo).
- `EReader_API.Infra/Context/Configurations/BookConfiguration.cs` (índice `Title`).
- `EReader_API.Infra/Repositories/*.cs` (`AsNoTracking()` em todas as leituras — **não**
  incluindo projeção para DTO em `BookRepository.QueryAsync`, que permanece pendente).
- `EReader_API.Infra/DependencyInjection.cs` (policy `upload`, limiter global, `Retry-After`,
  health checks, `IPdfInspector`, validação de `JwtOptions`).
- `EReader_API.Infra/Migrations/*AddPerformanceIndexes*` (novo).
