# Especificação do Backend — Leitor de Livros Online (EReader API)

> Base: notas manuscritas em `E-reader online.pdf` (28/06/2025). Este documento traduz
> aquelas notas em uma especificação acionável para iniciar o backend. Termos de negócio
> em português; identificadores de código em inglês, para acompanhar o código já existente.

## Índice

1. [Visão geral e objetivo](#1-visão-geral-e-objetivo)
2. [Escopo](#2-escopo)
3. [Arquitetura](#3-arquitetura)
4. [Bounded contexts e modelo de domínio](#4-bounded-contexts-e-modelo-de-domínio)
5. [Requisitos funcionais](#5-requisitos-funcionais)
6. [Requisitos não-funcionais](#6-requisitos-não-funcionais)
7. [API REST](#7-api-rest)
8. [Persistência](#8-persistência)
9. [Autenticação e autorização](#9-autenticação-e-autorização)
10. [Armazenamento de arquivos](#10-armazenamento-de-arquivos)
11. [Configuração](#11-configuração)
12. [Testes](#12-testes)
13. [Decisões em aberto](#13-decisões-em-aberto)
14. [Roadmap de implementação](#14-roadmap-de-implementação)
15. [Divergências em relação ao código atual](#15-divergências-em-relação-ao-código-atual)

---

## 1. Visão geral e objetivo

API REST que serve um leitor de livros online (front-end Angular, fora do escopo deste
repositório). O usuário se cadastra, faz upload dos próprios livros em PDF, lê no navegador
(desktop/mobile) com acompanhamento de progresso, e cria anotações, destaques e marcadores.
Além da biblioteca pessoal, há uma biblioteca pública de livros de domínio público
disponível a todos.

O **core domain** é a **leitura** (contexto `Reading`): sessão de leitura, posição atual,
progresso, destaques, notas e marcadores. Catálogo e identidade são contextos de apoio.

## 2. Escopo

### Dentro do escopo (MVP)

- Cadastro/autenticação de usuários e ciclo de vida da conta.
- Upload, listagem, download e remoção de livros **PDF** do usuário.
- Biblioteca pública de domínio público (somente leitura para o usuário comum).
- Progresso de leitura por usuário/livro.
- Marcadores (`Bookmark`), destaques (`Highlight`) e notas (`Note`) por usuário/livro.
- Streaming do PDF com suporte a `Range` (para o leitor paginar sem baixar tudo).

### Fora do escopo (agora)

- Front-end / renderização do PDF (responsabilidade do Angular).
- Dark mode e temas — decisão de UI, não da API.
- Formatos além de PDF (EPUB etc.).
- Deploy em nuvem e storage em nuvem (a "dockerização no futuro" das notas).
- Compartilhamento social, comentários entre usuários, recomendações.
- Extração automática de texto/OCR do PDF.

## 3. Arquitetura

Clean Architecture em quatro projetos (já existentes), com dependências apontando para o
domínio:

```
EReader_API            → host ASP.NET Core Web API (controllers, DI, middleware)
  ├─ EReader_API.Application   → casos de uso, contratos de serviço, DTOs
  ├─ EReader_API.Infra         → EF Core + PostgreSQL, Identity, storage de arquivos
  └─ EReader_API.Domain        → entidades, regras de negócio, contratos de repositório
```

Regras:

- `Domain` não referencia nada. `Application` referencia só `Domain`. `Infra` referencia só
  `Domain`. `EReader_API` referencia `Application` e `Infra` (esta última **apenas** para
  registrar implementações na composição da raiz — controllers dependem de `Application`).
- Fluxo de request: `Controller` → serviço de aplicação (`IXxxService`) → repositório
  (`IXxxRepository`) / storage (`IFileStorage`) → EF Core / disco.
- Cada bounded context é uma pasta de primeiro nível dentro de `Entities/`, `Interfaces/`,
  `Services/` (`Catalog/`, `Identity/`, `Reading/`), como o código já começou a fazer.
- DTOs de entrada/saída vivem em `Application`; entidades de domínio **não** são
  serializadas diretamente nos controllers.

## 4. Bounded contexts e modelo de domínio

Todos os IDs são `Guid` (ver [Decisões em aberto](#13-decisões-em-aberto)). Datas em UTC.

### 4.1 Identity Context

Sistema de registro da autenticação é o **ASP.NET Core Identity**.

**`ApplicationUser : IdentityUser<Guid>`** (Infra)
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | PK, herdado |
| Email / UserName | string | e-mail é o login |
| DisplayName | string | nome exibido |
| CreatedAt | DateTime | UTC |

Não há agregado `User` separado no domínio para o MVP. As entidades de `Catalog`/`Reading`
referenciam `OwnerId` / `UserId` (`Guid`) apontando para `ApplicationUser.Id`. A senha é
gerida pelo Identity (hash) — **nunca** armazenar senha em claro.

### 4.2 Catalog Context

**`Book`** (raiz de agregado)
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | PK |
| Title | string | obrigatório (era `Name`) |
| Author | string? | opcional |
| Description | string? | opcional |
| Language | string? | ex. `pt-BR` |
| Format | string | fixo `"pdf"` no MVP |
| Source | `BookSource` | `PublicDomain` \| `UserUpload` |
| OwnerId | Guid? | nulo ⇔ `PublicDomain`; preenchido ⇔ `UserUpload` |
| FileKey | string | chave no storage (era `FilePath`) |
| FileSizeBytes | long | |
| PageCount | int? | nulo até ser processado / informado |
| CoverImageKey | string? | opcional |
| PublishedDate | DateTime? | data de publicação da obra (era `LaunchDate`) |
| CreatedAt / UpdatedAt | DateTime | UTC |

Invariantes:
- `Source == UserUpload` ⇒ `OwnerId != null`.
- `Source == PublicDomain` ⇒ `OwnerId == null`.
- Visibilidade é derivada: livro `PublicDomain` é visível a todos; livro `UserUpload` só ao
  seu `OwnerId`.

### 4.3 Reading Context (core domain)

Todas as entidades abaixo são **escopadas ao usuário autenticado** (`UserId`) e a um `Book`.
Um usuário pode ter progresso/anotações tanto em livros próprios quanto em livros públicos.

**`ReadingProgress`** (raiz de agregado) — no máximo 1 por (`UserId`, `BookId`)
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | PK |
| UserId | Guid | |
| BookId | Guid | |
| CurrentPage | int | ≥ 1 |
| TotalPages | int? | espelha `Book.PageCount` quando conhecido |
| PercentComplete | decimal | derivado: `CurrentPage / TotalPages` (0 quando `TotalPages` nulo) |
| LastReadAt | DateTime | UTC |

**`Bookmark`**
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | PK |
| UserId / BookId | Guid | |
| PageNumber | int | ≥ 1 |
| Label | string? | rótulo opcional |
| CreatedAt | DateTime | UTC |

**`Highlight`**
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | PK |
| UserId / BookId | Guid | |
| PageNumber | int | ≥ 1 |
| TextContent | string | trecho destacado |
| Color | string? | ex. `#FFEB3B` |
| Anchor | string? | JSON opcional com coordenadas/seleção para o leitor reposicionar |
| CreatedAt | DateTime | UTC |

**`Note`**
| Campo | Tipo | Notas |
|---|---|---|
| Id | Guid | PK |
| UserId / BookId | Guid | |
| PageNumber | int? | opcional (nota geral do livro) |
| HighlightId | Guid? | nota pode estar ancorada a um destaque |
| Content | string | texto da nota (era `Description`) |
| CreatedAt / UpdatedAt | DateTime | UTC |

Regras de exclusão:
- Remover um `Book` `UserUpload` remove em cascata `ReadingProgress`/`Bookmark`/`Highlight`/
  `Note` daquele livro (de qualquer usuário — só o dono pode remover).
- Livros `PublicDomain` não são removíveis por usuário comum; anotações de cada usuário sobre
  eles seguem o ciclo de vida da conta do usuário.
- Excluir a conta remove todos os dados de `Reading` do usuário e todos os seus livros
  `UserUpload` (e respectivos arquivos no storage).

## 5. Requisitos funcionais

### RF-Identity

- **RF-01** Registro com e-mail + senha; e-mail único; política de senha configurável.
- **RF-02** Login retornando token JWT de acesso + refresh token.
- **RF-03** Refresh: trocar refresh token válido por novo par de tokens.
- **RF-04** Logout: invalidar o refresh token corrente.
- **RF-05** Esqueci a senha: gerar token de reset (entregue por e-mail; no MVP pode ser
  logado/retornado em ambiente de desenvolvimento).
- **RF-06** Reset de senha usando o token.
- **RF-07** Excluir a própria conta (hard delete + limpeza de arquivos).
- **RF-08** Consultar o próprio perfil (`/me`).

### RF-Catalog

- **RF-09** Upload de livro pessoal: `multipart/form-data` com metadados + arquivo PDF.
  Validação: content-type `application/pdf`, *magic bytes* `%PDF-`, tamanho ≤ limite
  configurável. Cria `Book` com `Source = UserUpload`, `OwnerId = usuário atual`.
- **RF-10** Listar livros: filtro `scope` = `public` | `mine` | `all` (padrão `all`),
  busca por texto (`title`/`author`), paginação e ordenação. Só retorna livros do próprio
  usuário + públicos.
- **RF-11** Detalhar um livro (autorizado: dono ou público).
- **RF-12** Atualizar metadados do próprio livro.
- **RF-13** Remover o próprio livro (remove arquivo do storage).
- **RF-14** Baixar/stream do arquivo PDF com suporte a header `Range` (206 Partial Content).
- **RF-15** Obter a imagem de capa, quando existir.
- **RF-16** Biblioteca pública: livros `PublicDomain` são semeados via seed/tarefa
  administrativa (ver Roadmap). Usuário comum tem acesso somente leitura.

### RF-Reading

- **RF-17** Obter o progresso do usuário em um livro (404/vazio se nunca leu).
- **RF-18** Atualizar progresso (`currentPage`); *upsert*; atualiza `LastReadAt` e recalcula
  `PercentComplete`.
- **RF-19** CRUD de `Bookmark` por livro.
- **RF-20** CRUD de `Highlight` por livro (update parcial de `color`/`content`).
- **RF-21** CRUD de `Note` por livro (opcionalmente ligada a um `Highlight`).
- **RF-22** "Minha biblioteca": listar livros do usuário (próprios + públicos com progresso)
  com o progresso agregado, para a tela inicial do leitor.

## 6. Requisitos não-funcionais

| Área | Definição |
|---|---|
| Plataforma | .NET 10, ASP.NET Core Web API, C# `nullable` habilitado |
| Banco | PostgreSQL 17 via `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Estilo de API | REST/JSON, prefixo `/api`, versionamento por caminho (`/api/v1`) quando necessário |
| Erros | `application/problem+json` (RFC 7807) em todas as respostas de erro |
| Auth | ASP.NET Core Identity + JWT Bearer; refresh tokens |
| CORS | origem do front Angular configurável (`Cors:AllowedOrigins`) |
| Documentação | OpenAPI (já há `AddOpenApi`); expor Swagger UI em Dev |
| Upload | limite de tamanho configurável (padrão sugerido 50 MB); apenas PDF |
| Logs | logging estruturado; `LogLevel` por configuração (já presente) |
| Migrations | EF Core Migrations versionadas; aplicação automática só em Dev |
| Cultura | datas persistidas e trafegadas em UTC (ISO 8601) |
| Paginação | resposta com `items`, `page`, `pageSize`, `totalCount` |
| Segurança | rate limiting nos endpoints de auth; validação de todo input; autorização por dono do recurso |

## 7. API REST

Convenções: JSON; autenticação `Authorization: Bearer <jwt>` exceto onde indicado
"(anônimo)"; erros em `problem+json`; `201 Created` com `Location` nas criações.

### 7.1 Autenticação — `/api/auth`

| Método | Rota | Descrição |
|---|---|---|
| POST | `/register` (anônimo) | cria conta → `201` |
| POST | `/login` (anônimo) | → `{ accessToken, refreshToken, expiresIn }` |
| POST | `/refresh` (anônimo) | corpo `{ refreshToken }` → novo par |
| POST | `/logout` | invalida o refresh token corrente |
| POST | `/forgot-password` (anônimo) | corpo `{ email }` → `202` |
| POST | `/reset-password` (anônimo) | corpo `{ email, token, newPassword }` |
| GET | `/me` | perfil do usuário autenticado |
| DELETE | `/me` | exclui a conta e todos os dados |

### 7.2 Catálogo — `/api/books`

| Método | Rota | Descrição |
|---|---|---|
| GET | `/api/books?scope=&search=&author=&page=&pageSize=&sort=` | lista paginada (próprios + públicos) |
| GET | `/api/books/{id}` | detalhe |
| POST | `/api/books` | `multipart/form-data`: campos de metadados + `file` (PDF) → `201` |
| PUT | `/api/books/{id}` | atualiza metadados (dono) |
| DELETE | `/api/books/{id}` | remove livro + arquivo (dono) |
| GET | `/api/books/{id}/file` | stream do PDF, suporta `Range` |
| GET | `/api/books/{id}/cover` | imagem de capa (ou `404`) |

### 7.3 Leitura — aninhado em `/api/books/{bookId}` e recursos próprios

| Método | Rota | Descrição |
|---|---|---|
| GET | `/api/books/{bookId}/progress` | progresso do usuário |
| PUT | `/api/books/{bookId}/progress` | `{ currentPage }` — upsert |
| GET | `/api/books/{bookId}/bookmarks` | lista |
| POST | `/api/books/{bookId}/bookmarks` | `{ pageNumber, label? }` → `201` |
| DELETE | `/api/bookmarks/{id}` | remove |
| GET | `/api/books/{bookId}/highlights` | lista |
| POST | `/api/books/{bookId}/highlights` | `{ pageNumber, textContent, color?, anchor? }` |
| PATCH | `/api/highlights/{id}` | `{ color?, textContent? }` |
| DELETE | `/api/highlights/{id}` | remove |
| GET | `/api/books/{bookId}/notes` | lista |
| POST | `/api/books/{bookId}/notes` | `{ content, pageNumber?, highlightId? }` |
| PATCH | `/api/notes/{id}` | `{ content }` |
| DELETE | `/api/notes/{id}` | remove |
| GET | `/api/library` | livros do usuário + progresso agregado (tela inicial) |

Autorização: todo recurso de `Reading` e todo `Book` `UserUpload` é acessível somente pelo
respectivo `UserId`/`OwnerId`. `403` quando o recurso existe mas não pertence ao chamador;
`404` quando não existe.

## 8. Persistência

- `ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`
  (já é `IdentityDbContext<ApplicationUser>` — ajustar para chave `Guid`).
- `DbSet<Book>`, `DbSet<ReadingProgress>`, `DbSet<Bookmark>`, `DbSet<Highlight>`,
  `DbSet<Note>`.
- Configurações de mapeamento como classes `IEntityTypeConfiguration<T>` em
  `Infra/Context/Configurations/` — `OnModelCreating` já chama
  `ApplyConfigurationsFromAssembly`.
- Índices: `Book(OwnerId)`, `Book(Source)`, único `ReadingProgress(UserId, BookId)`,
  `Bookmark(UserId, BookId)`, `Highlight(UserId, BookId)`, `Note(UserId, BookId)`.
- `PercentComplete` persistido (recalculado na aplicação) ou coluna computada — decisão de
  implementação.
- Migrations: pacote `Microsoft.EntityFrameworkCore.Design` no projeto `Infra`; comandos:

  ```bash
  dotnet tool install --global dotnet-ef
  dotnet ef migrations add <Nome> --project EReader_API.Infra --startup-project EReader_API
  dotnet ef database update      --project EReader_API.Infra --startup-project EReader_API
  ```

- Em `Development`, aplicar `Database.Migrate()` no startup. Em produção, migração explícita.

## 9. Autenticação e autorização

- **Identity** para hash de senha, políticas e tokens de reset/confirmação.
- **JWT Bearer** para as chamadas da API:
  - access token curto (ex. 15 min) com claims `sub` (= `ApplicationUser.Id`), `email`.
  - refresh token opaco, persistido (tabela `RefreshTokens`: `Id`, `UserId`, `TokenHash`,
    `ExpiresAt`, `RevokedAt`, `CreatedAt`), rotacionado a cada `/refresh`.
- Configuração JWT em `Jwt:{Issuer,Audience,SigningKey,AccessTokenMinutes,RefreshTokenDays}`
  (chave via user-secrets/variável de ambiente, nunca no `appsettings.json`).
- Autorização baseada em propriedade do recurso (handler/policy `ResourceOwner`), não só em
  `[Authorize]`.
- `Program.cs` precisa registrar `AddAuthentication().AddJwtBearer(...)` e
  `AddAuthorization(...)` — hoje só há `app.UseAuthorization()` sem autenticação.
- Rate limiting (`AddRateLimiter`) nos endpoints `/api/auth/*`.

## 10. Armazenamento de arquivos

- Abstração no domínio/aplicação:

  ```csharp
  public interface IFileStorage
  {
      Task<string> SaveAsync(Stream content, string contentType, string suggestedName, CancellationToken ct);
      Task<Stream> OpenReadAsync(string fileKey, CancellationToken ct);
      Task DeleteAsync(string fileKey, CancellationToken ct);
      Task<bool> ExistsAsync(string fileKey, CancellationToken ct);
  }
  ```

- Implementação MVP `LocalFileStorage` (Infra): grava sob `FileStorage:RootPath`
  (fora do wwwroot), nome = `{Guid}.pdf`, retorna a `fileKey` relativa gravada em
  `Book.FileKey`. Nunca servir o arquivo por caminho físico direto — sempre via
  `GET /api/books/{id}/file` com verificação de autorização e `Range`.
- Validação de upload: extensão, `Content-Type`, *magic bytes* `%PDF-`, tamanho máximo.
- `PageCount`/capa: fora do MVP a extração automática. Opções: (a) o front envia
  `totalPages`; (b) tarefa posterior com `PdfPig`/`Docnet` para extrair contagem e primeira
  página como capa. Até lá `PageCount` fica nulo e `PercentComplete` = 0.
- Futuro: implementação `S3FileStorage` / `AzureBlobFileStorage` trocável por configuração.

## 11. Configuração

Chaves esperadas (`appsettings.json` + `appsettings.Development.json` + user-secrets + env):

```jsonc
{
  "ConnectionStrings": { "Default": "Host=...;Database=ereader;Username=...;Password=..." },
  "Jwt": {
    "Issuer": "ereader-api",
    "Audience": "ereader-web",
    "SigningKey": "<somente via secret/env>",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 14
  },
  "FileStorage": { "RootPath": "App_Data/books", "MaxUploadBytes": 52428800 },
  "Cors": { "AllowedOrigins": [ "http://localhost:4200" ] }
}
```

- Connection string já vem de `ConnectionStrings__Default` no `docker-compose.yml`.
- `UserSecretsId` já configurado em `EReader.API.csproj`.

## 12. Testes

- **Unitários** (`EReader_API.Domain.Tests`, `EReader_API.Application.Tests`, xUnit):
  invariantes de domínio, cálculo de `PercentComplete`, regras de autorização por dono,
  validação de upload.
- **Integração** (`EReader_API.Api.Tests`): `WebApplicationFactory` + PostgreSQL efêmero
  via **Testcontainers**; fluxos register→login→upload→progresso→anotações.
- Adicionar os projetos de teste ao `EReader.slnx`; rodar com `dotnet test`.

## 13. Decisões em aberto

| # | Assunto | Recomendação |
|---|---|---|
| D-1 | Tipo da chave de usuário | `Guid` (`IdentityUser<Guid>`) em vez do `string` padrão |
| D-2 | Entidade `Domain/Entities/Identity/User` (tem `Id:int` e `Password`) | remover; usar `ApplicationUser` + `UserId:Guid` nas outras entidades. Nunca persistir `Password` |
| D-3 | Renomear campos de `Book` | `Name`→`Title`, `FilePath`→`FileKey`, `LaunchDate`→`PublishedDate`; adicionar `Source`/`OwnerId` |
| D-4 | `int? id` nos repositórios/serviços | trocar para `Guid` |
| D-5 | `IBookService.GetCategories()` retornando `Book` | renomear para `GetBooksAsync`/`GetBookByIdAsync`; separar conceito de categoria se necessário |
| D-6 | Extração de `PageCount`/capa | adiar; front informa `totalPages` no MVP |
| D-7 | Armazenamento do refresh token | tabela no banco (auditável) vs cache distribuído — começar com tabela |
| D-8 | Paginação | offset/`page`+`pageSize` no MVP; migrar para keyset se necessário |
| D-9 | Envio de e-mail (reset de senha) | abstrair `IEmailSender`; em Dev logar o token |
| D-10 | Biblioteca pública: origem dos PDFs | seed com poucos títulos de domínio público + endpoint admin para adicionar |

## 14. Roadmap de implementação

> As fases abaixo estão detalhadas como specs de implementação independentes, com checklist
> e critérios de aceite, em [`specs/`](specs/README.md):
> `00-fundacao`, `01-identidade`, `02-catalogo-e-upload`, `03-leitura`, `04-robustez`,
> `05-testes`.


### Fase 0 — Fundação
- Referências de projeto: `EReader_API` → `Application` + `Infra`.
- `Program.cs`: registrar `DbContext` (Npgsql), Identity (`Guid`), JWT Bearer + Authorization,
  DI de repositórios/serviços/`IFileStorage`, CORS, Swagger, `AddControllers`, ProblemDetails.
- `appsettings`: `ConnectionStrings:Default`, `Jwt`, `FileStorage`, `Cors`.
- Pacote `Microsoft.EntityFrameworkCore.Design` em `Infra`; primeira migration (Identity + `Book`).
- Corrigir o `Dockerfile` (copia `EReader_API.Application.csproj` para a pasta errada e usa
  `EReader_API/EReader_API.csproj`, mas o arquivo real é `EReader_API/EReader.API.csproj`).
- Ajustar `docker-compose` se preciso; healthcheck do Postgres.

### Fase 1 — Identidade
- `register`, `login`, `refresh`, `logout`, `forgot-password`, `reset-password`, `GET/DELETE /me`.
- Tabela `RefreshTokens`; rate limiting em `/api/auth/*`.
- Testes de unidade + integração do fluxo de auth.

### Fase 2 — Catálogo + upload
- `Book` CRUD + `POST /api/books` multipart com validação de PDF.
- `LocalFileStorage`; `GET /api/books/{id}/file` com `Range`.
- Listagem com `scope`/busca/paginação; autorização por dono.
- Seed da biblioteca pública (poucos títulos de domínio público).

### Fase 3 — Leitura (core domain)
- `ReadingProgress` (upsert), `Bookmark`, `Highlight`, `Note`.
- `GET /api/library` com progresso agregado.
- Cascata de exclusão (livro, conta).

### Fase 4 — Robustez
- ProblemDetails consistente, validação centralizada, observabilidade/health checks.
- Extração de `PageCount` + capa (`PdfPig`), possivelmente em background.
- Revisão de índices e performance da listagem.

### Fase 5 — Futuro (fora do MVP)
- Dockerização completa e pipeline de deploy.
- Storage em nuvem (`IFileStorage` alternativo).
- Outros formatos (EPUB), busca full-text, sincronização multi-dispositivo.

## 15. Divergências em relação ao código atual

O scaffold atual já reflete boa parte da estrutura, mas precisa de ajustes para atender a esta
especificação:

- `EReader.API.csproj` **não referencia** `Application` nem `Infra`; `Program.cs` só registra
  controllers + OpenAPI (sem `DbContext`, Identity, autenticação, DI).
- Existem **dois modelos de usuário** não reconciliados: `Domain/Entities/Identity/User`
  (`int Id`, `Password` em claro) e `Infra/Identity/ApplicationUser : IdentityUser`
  (chave `string`). Ver D-1/D-2.
- `Book` usa `Name`/`FilePath`/`LaunchDate` e não tem `Source`/`OwnerId` (visibilidade
  pública vs. pessoal). Ver D-3.
- Não há entidade de **progresso de leitura**; `Bookmark` tem `PageNumber` mas não cobre
  "posição atual / progresso" do core domain. Adicionar `ReadingProgress`.
- Entidades de `Reading` referenciam `User User`/`int UserId` do domínio; devem referenciar
  `Guid UserId` (= `ApplicationUser.Id`).
- Repositórios/serviços usam `int? id`; `BookService` tem métodos `throw NotImplementedException`
  e `IBookService.GetCategories()` retorna `Book`. Revisar contratos (D-4/D-5).
- Só `IBookRepository` tem implementação (`BookRepository`); faltam
  `Bookmark`/`Highlight`/`Note`/`ReadingProgress` e `IFileStorage`.
- `Dockerfile` tem caminhos de `COPY`/`csproj` incorretos (ver Fase 0).
- Não há projeto de testes nem migrations.
