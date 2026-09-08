# 04 — Robustez e operação

**Status:** Não iniciado
**Depende de:** 01, 02, 03
**Objetivo:** Deixar a API pronta para uso real: tratamento de erros consistente
(ProblemDetails), validação centralizada, rate limiting, health checks, observabilidade,
extração de `PageCount`/capa do PDF, e revisão de índices/performance.

## Escopo

**Entra:** os itens abaixo. **Não entra:** deploy em nuvem, storage em nuvem, CI/CD,
autenticação social — ver Fase 5 em `../ESPECIFICACAO-BACKEND.md`.

## 4.1 Tratamento de erros

- [ ] `AddProblemDetails()` + `IExceptionHandler` global mapeando exceções de domínio para
      status: `NotFoundException → 404`, `ForbiddenException → 403`,
      `ValidationException → 400` (com `errors` por campo), `ConflictException → 409`,
      demais → `500` sem vazar stack em produção.
- [ ] Padronizar `type`/`title`/`detail`/`traceId` em todas as respostas de erro.
- [ ] Remover `throw new NotImplementedException()` remanescentes.

## 4.2 Validação

- [ ] Validação de entrada por DTO (FluentValidation ou `DataAnnotations` + filtro).
      Regras: tamanhos de string, ranges (`pageNumber >= 1`, `pageSize` 1–100), formatos
      (e-mail, cor hex, `language`).
- [ ] Falha de validação → `400` ProblemDetails com dicionário `errors`.

## 4.3 Rate limiting

- [ ] `AddRateLimiter`: policy `"auth"` (já introduzida na spec 01, formalizar aqui),
      policy `"upload"` para `POST /api/books` (ex. 20/hora por usuário), limite global
      brando por IP.
- [ ] Resposta `429` com `Retry-After`.

## 4.4 Health checks e observabilidade

- [ ] `AddHealthChecks().AddDbContextCheck<ApplicationDbContext>()` + check de escrita no
      `FileStorage:RootPath`.
- [ ] Endpoints `GET /health/live` e `GET /health/ready`.
- [ ] Logging estruturado com `traceId`/`userId` no escopo da requisição; níveis por
      configuração (já existe base).
- [ ] (Opcional) OpenTelemetry para traces/métricas.

## 4.5 Extração de `PageCount` e capa

- [ ] `IPdfInspector` (Application) + implementação com `PdfPig` (ou `Docnet.Core`) em Infra:
      `GetPageCount(Stream)` e `RenderFirstPagePng(Stream)`.
- [ ] No `UploadAsync` (spec 02): após salvar o PDF, extrair `PageCount` e gerar a capa,
      gravando `CoverImageKey`. Se a extração falhar, logar e seguir com `PageCount` nulo
      (não bloquear o upload).
- [ ] Estratégia: síncrono no upload para o MVP; extrair para `IHostedService`/fila se o
      tempo de resposta incomodar (registrar decisão).
- [ ] Ao definir `Book.PageCount`, propagar para `ReadingProgress.TotalPages` existentes
      daquele livro e recalcular `PercentComplete`.

## 4.6 Índices e performance

- [ ] Revisar/confirmar índices: `Book(OwnerId)`, `Book(Source)`, `Book(Title)` para busca;
      `ReadingProgress(UserId, BookId)` único; `(UserId, BookId)` nas anotações;
      `RefreshToken(TokenHash)`.
- [ ] `QueryAsync` de livros: projeção para DTO no banco (`Select`), sem `Include`
      desnecessário; paginação com `Skip/Take` + `CountAsync` numa única viagem quando
      possível.
- [ ] `AsNoTracking()` em todas as leituras.
- [ ] Busca textual: `ILIKE` no MVP; avaliar `pg_trgm`/full-text se necessário (anotar, não
      implementar agora).

## 4.7 Migrations e configuração

- [ ] Migration `AddCoverAndIndexes` (se houver mudança de schema para capa/índices).
- [ ] Em produção: não aplicar migration no startup; documentar `dotnet ef database update`
      no pipeline / entrypoint.
- [ ] Validar na inicialização que `Jwt:SigningKey`, `ConnectionStrings:Default` e
      `FileStorage:RootPath` estão presentes; falhar rápido com mensagem clara.

## Critérios de aceite

- Toda rota de erro retorna `application/problem+json` com `traceId`; nenhuma `500` vaza
  stack em `Production`.
- Payload inválido → `400` com `errors` por campo.
- Estourar a policy de `auth`/`upload` → `429` com `Retry-After`.
- `GET /health/ready` fica `Unhealthy` quando o banco está fora ou o diretório de storage não
  é gravável.
- Upload de um PDF de N páginas resulta em `Book.PageCount == N` e `CoverImageKey` preenchido;
  PDF corrompido ainda cria o `Book` (sem `PageCount`) e loga o erro.
- Após o `PageCount` ser definido, `GET /api/books/{id}/progress` passa a retornar
  `percentComplete` > 0 para quem já tinha progresso.
- `EXPLAIN` da listagem de livros usa índice (sem seq scan em tabela grande).

## Arquivos afetados

- `EReader_API/Program.cs` (ProblemDetails, rate limiter, health checks, validação de config)
- `EReader_API/Infrastructure/GlobalExceptionHandler.cs` (novo)
- `EReader_API.Application/Common/Exceptions/*` (novo)
- `EReader_API.Application/**/Validators/*` (novo)
- `EReader_API.Application/Catalog/IPdfInspector.cs` + `EReader_API.Infra/Pdf/PdfPigInspector.cs`
- `EReader_API.Application/Catalog/BookService.cs` (extração no upload, propagação de `TotalPages`)
- `EReader_API.Infra/Context/Configurations/*` (índices)
- `EReader_API.Infra/Repositories/BookRepository.cs` (projeção/paginação)
- `EReader_API.Infra/Migrations/*`
