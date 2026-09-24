# Plano de implementação — Spec 05: Infraestrutura de testes

> Plano de execução para [`../05-testes.md`](../05-testes.md). Depende só de 00 (concluída);
> na prática também precisa observar o estado atual de 01–04 (concluídas), porque os helpers de
> teste (auth, upload, storage, rate limiting, health checks) têm que refletir o design *as-built*
> dessas specs, não o texto original de 05 — que foi escrito antes delas existirem. Objetivo: três
> projetos xUnit (`Domain.Tests`, `Application.Tests`, `Api.Tests`), Postgres efêmero via
> Testcontainers, `WebApplicationFactory<Program>`, Respawn entre testes, helpers compartilhados
> (auth, builders, PDF mínimo), `dotnet test` verde e repetível.

## 0. Estado atual (verificado)

- **Nenhum projeto de teste existe** — sem pasta `tests/`, sem referência a `xunit`/
  `Microsoft.NET.Test.Sdk` em nenhum `.csproj`, `EReader.slnx` só lista os 4 projetos de produção.
- **`Program.cs` não expõe `Program`** para `WebApplicationFactory<Program>` — usa top-level
  statements, o que gera uma classe `Program` `internal` por padrão. Precisa do
  `public partial class Program;` no fim do arquivo (item já previsto no checklist da spec).
- **`ReadingProgressCalculator` não mora mais em `Domain`** — a spec 04 já o moveu para
  `EReader_API.Application.Common.ReadingProgressCalculator` (função estática pura,
  `PercentComplete(int currentPage, int? totalPages)`), porque `Catalog/BookService` também passou
  a precisar dele (propagação de `PageCount`). O item literal do checklist de 05
  ("`Domain.Tests`: primeiro teste real — cálculo de `PercentComplete`") está desatualizado: hoje
  esse cálculo não é código de `Domain`. Ver [D-05-1](#d-05-1).
- **`Domain` hoje é só POCO anêmico** — conferido todo `Entities/Catalog` e `Entities/Reading`:
  `Book`, `Bookmark`, `Highlight`, `Note`, `ReadingProgress` são classes com só propriedades
  auto-implementadas, sem construtor com regra, sem método, sem validação. `BookQuery` é um
  `record` de parâmetros. `BookScope`/`BookSource` são enums simples. **Não há nenhuma invariante
  de domínio hoje para testar** — toda regra de negócio (visibilidade `PublicDomain`/`UserUpload`,
  validação de upload, `PercentComplete`, etc.) vive em `Application`/`Infra`. Ver
  [D-05-1](#d-05-1).
- **`AuthService` mora em `Infra`, não em `Application`** — `Infra/Identity/AuthService.cs`
  implementa `IAuthService` (porta declarada em `Application/Identity`) e depende de
  `UserManager<ApplicationUser>` (classe concreta do ASP.NET Core Identity, `Infra`-only por
  design do projeto — ver `CLAUDE.md`/Arquitetura). O item do checklist de 05 ("`Application.Tests`:
  primeiro teste — `AuthService` rejeita credenciais inválidas e rotaciona refresh token") não bate
  com a divisão de camadas atual: testar `AuthService` isoladamente exige mockar
  `UserManager<ApplicationUser>` (possível — seus métodos são `virtual` — mas é código de `Infra`).
  Ver [D-05-2](#d-05-2).
- **`FileStorageOptions` é um singleton comum, não `IOptions<T>`** —
  `Infra/DependencyInjection.AddInfrastructure` faz
  `configuration.GetSection("FileStorage").Get<FileStorageOptions>()` uma vez e registra a
  instância via `services.AddSingleton(fileStorageOptions)`. Isso significa que sobrescrever
  `FileStorage:RootPath` via configuração **antes** do host montar os serviços é suficiente — não
  precisa de `ConfigureTestServices` trocando o registro.
- **`LocalFileStorage.ResolvePath`** faz `Path.GetFullPath(Path.Combine(env.ContentRootPath,
  options.RootPath))`. Como `Path.Combine` descarta o primeiro argumento quando o segundo já é um
  caminho absoluto, apontar `FileStorage:RootPath` para um caminho absoluto (ex.
  `Path.Combine(Path.GetTempPath(), "ereader-tests", runId)`) funciona independente de qual
  `ContentRootPath` o `WebApplicationFactory` resolver para o host de teste.
- **`Jwt:SigningKey` vem vazio em `appsettings.json`** (é secret — user-secrets/env var em
  produção) e, desde a spec 04, `services.AddOptions<JwtOptions>().Validate(...).ValidateOnStart()`
  faz o host **falhar no `Build()`** se estiver vazio/ausente. O `EReaderApiFactory` precisa
  injetar um `Jwt:SigningKey` (Base64 válido) via configuração antes do build, ou nenhum teste sobe.
- **`Program.cs` só migra/semeia dentro de `if (app.Environment.IsDevelopment())`** — esse mesmo
  bloco também mapeia `/openapi`/`/scalar`. Se o `WebApplicationFactory` herdar o environment
  padrão "Development", o host de teste tentaria `Database.Migrate()` (idempotente, inofensivo) e
  `PublicLibrarySeeder.SeedAsync` (que faz `INSERT`s reais) toda vez que o factory é criado — e o
  Respawn depois apaga essas linhas entre testes, deixando só o *primeiro* teste da coleção com
  catálogo público semeado. Ver [D-05-5](#d-05-5).
- **Rate limiting já está ativo globalmente** (spec 04): policy `"auth"` (10/min) partition por
  `httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`, mais um `GlobalLimiter`
  (100/min) com a mesma partição por IP. **Achado importante**: sob `WebApplicationFactory`
  (`TestServer`, transporte in-memory), `Connection.RemoteIpAddress` normalmente vem `null` — ou
  seja, toda chamada não autenticada de todo teste da suíte (register/login/forgot-password/etc.)
  cairia na **mesma partição `"unknown"`**, e a policy `"auth"` sozinha já limitaria a suíte
  inteira a 10 chamadas de auth *no total*, não por teste. Isso quebraria a suíte de integração já
  no smoke test do checklist. Ver [D-05-9](#d-05-9).
- **`docker-compose.yml`** já roda `postgres:17` — usar a mesma versão de imagem no Testcontainers
  evita divergência de comportamento entre CI local e o container de produção.
- **SDK/target framework**: todos os 4 projetos são `net10.0`; os projetos de teste devem ser
  `net10.0` também, sem motivo para divergir.

## 1. Pré-condições

- Specs 00–04 concluídas (build verde, migrations `AddIdentityAndRefreshTokens`,
  `AddBookCatalog`, `AddReadingContext`, `AddPerformanceIndexes` aplicadas).
- Docker Desktop (ou daemon Docker equivalente) rodando localmente — Testcontainers precisa dele
  para subir o `postgres:17` efêmero. `dotnet test` sem Docker ativo deve falhar de forma legível
  (Testcontainers lança exceção clara se não conseguir falar com o daemon) — documentar isso no
  `README`/`CLAUDE.md` (item já previsto no checklist).

## 2. Decisões a confirmar antes de começar

| # | Decisão | Recomendação | Impacto se não seguir |
|---|---|---|---|
| D-05-1 | Onde fica o "primeiro teste real" de `Domain.Tests` | `Domain` hoje não tem nenhuma invariante testável (só POCOs/enums/record de parâmetros — ver [seção 0](#0-estado-atual-verificado)). Ainda criar o projeto `EReader_API.Domain.Tests` (está no escopo/tabela da spec, e as próximas specs de negócio podem introduzir invariantes reais em `Domain`), mas com um teste mínimo honesto (ex. `BookQuery` é um record com igualdade estrutural, ou um teste trivial dos valores dos enums `BookSource`/`BookScope`) em vez de forçar o `PercentComplete` para dentro de `Domain`. O teste de `PercentComplete` (o que o checklist da spec realmente quer) vai para `Application.Tests`, contra `EReader_API.Application.Common.ReadingProgressCalculator` — é onde o código mora de verdade hoje. | Recriar `ReadingProgressCalculator` dentro de `Domain` só para bater com o texto literal da spec desfaria a decisão D-04-8 (movida deliberadamente para ser compartilhada por `Catalog` e `Reading` sem inverter dependência) — regressão arquitetural para satisfazer um checklist desatualizado. |
| D-05-2 | Onde testar `AuthService` (rejeição de credenciais, rotação de refresh token) | `AuthService` é uma classe de `Infra` (depende de `UserManager<ApplicationUser>`). `EReader_API.Application.Tests` ganha uma `ProjectReference` extra para `EReader_API.Infra` (além de `Application`/`Domain`, que já viriam transitivamente) — só para viabilizar esse teste unitário. Mockar `UserManager<ApplicationUser>` com NSubstitute (seus membros são `virtual`, técnica padrão: `Substitute.For<UserManager<ApplicationUser>>(Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null)`). `IJwtTokenGenerator`/`IRefreshTokenStore`/`IEmailSender`/`IBookService` (as outras 4 dependências do construtor) são interfaces de `Application` — mockáveis normalmente. | Criar um 4º projeto `EReader_API.Infra.Tests` só para isso expande o escopo além da tabela literal da spec (3 projetos) por causa de uma única classe; testar `AuthService` só via `Api.Tests` (HTTP) cobre o comportamento observável mas perde a granularidade unitária que o checklist pede explicitamente ("rotaciona refresh token" é mais fácil de verificar isolado, mockando o `IRefreshTokenStore`, do que via round-trip HTTP). |
| D-05-3 | Biblioteca de mock | `NSubstitute` (a spec já lista como alternativa ao Moq). Mais simples para o caso do D-05-2 (`Substitute.For<UserManager<T>>(...)` é um padrão conhecido) e evita a sintaxe mais verbosa de `Setup`/`Verify` do Moq. | Moq funcionaria igualmente bem para os outros serviços (`IBookRepository`, `IFileStorage`, etc.), mas para `UserManager<T>` especificamente NSubstitute costuma exigir menos boilerplate. |
| D-05-4 | Versão do `FluentAssertions` | A partir da v8 (lançada em 2025), `FluentAssertions` passou a exigir licença paga (Xceed) para uso comercial acima de um limiar de receita/empresa — ambíguo para decidir sem input seu. Fixar a versão em `7.x` (última release sob a licença Apache 2.0/MIT antiga) no `PackageReference`, evitando a ambiguidade de licença por completo. | Deixar sem pin explícito puxa a versão mais recente (`8.x`+) por padrão do NuGet — risco de ficar sob uma licença comercial sem decisão consciente. Alternativa se preferir não lidar com isso: `AwesomeAssertions` (fork comunitário, API praticamente idêntica, MIT) — posso trocar se preferir. |
| D-05-5 | Environment do `WebApplicationFactory` | Fixar `"Testing"` (via `builder.UseEnvironment("Testing")` em `ConfigureWebHost`) em vez de herdar `"Development"`. Isso pula o bloco `if (app.Environment.IsDevelopment())` do `Program.cs` (migração automática, seed do catálogo público, `/openapi`/`/scalar`) — a migração fica 100% responsabilidade do `EReaderApiFactory` (uma vez, no `InitializeAsync`), e nenhum teste depende de dados semeados que o Respawn apagaria de qualquer forma depois do primeiro teste. Também é o gancho usado em [D-05-9](#d-05-9) para pular o rate limiter. | Herdar `"Development"` faz `PublicLibrarySeeder.SeedAsync` rodar toda vez que uma nova instância do factory é criada (uma por `ICollectionFixture`, então só 1x por execução da suíte — inofensivo em si), mas mistura duas responsabilidades de setup (`Program.cs` E `EReaderApiFactory` tentando migrar) e mapeia rotas de dev (`/scalar`) sem necessidade nos testes. |
| D-05-6 | Como apontar `IFileStorage` para um diretório temporário | Só sobrescrever a chave de configuração `FileStorage:RootPath` com um caminho absoluto por execução (`Path.Combine(Path.GetTempPath(), "ereader-tests", Guid.NewGuid():N)`), via `ConfigureAppConfiguration` do `WebApplicationFactory`. Não precisa `ConfigureTestServices` substituindo `IFileStorage`/`FileStorageOptions` — a instância real de `LocalFileStorage` grava nesse diretório normalmente (ver [seção 0](#0-estado-atual-verificado) sobre `Path.Combine` com caminho absoluto). Limpar o diretório no `DisposeAsync` do factory. | Substituir `IFileStorage` por um fake in-memory testaria menos coisa de verdade (o critério de aceite da spec pede um teste de integração que faz upload+download real via HTTP, então usar o `LocalFileStorage` real contra um diretório temporário é mais fiel). |
| D-05-7 | Config de `Jwt`/segredo em teste | `EReaderApiFactory` injeta via `ConfigureAppConfiguration`: `Jwt:SigningKey` = uma chave Base64 fixa de teste (ex. `Convert.ToBase64String(Encoding.UTF8.GetBytes("test-signing-key-32-bytes-min!!"))`, gerada uma vez, não secreta de verdade — só para satisfazer o `ValidateOnStart()`). `Issuer`/`Audience`/`AccessTokenMinutes`/`RefreshTokenDays` continuam vindo do `appsettings.json` normal (já têm valores não-secretos). | Sem isso, `builder.Build()` lança `OptionsValidationException` na primeira resolução de `JwtOptions` (que acontece cedo, no pipeline de auth) e **todo** teste de integração falha, não só os de auth. |
| D-05-8 | `PostgresFixture` separado vs. fundido no `EReaderApiFactory` | Fundir: `EReaderApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` sobe o `PostgreSqlContainer` (Testcontainers) ele mesmo em `InitializeAsync`, expõe `ConnectionString`, roda `Database.Migrate()` uma vez, e o próprio `ConfigureWebHost` já usa essa connection string. Um `[CollectionDefinition]` com `ICollectionFixture<EReaderApiFactory>` compartilha essa única instância (e o container) entre todas as classes de teste de integração da coleção — 1 container para toda a suíte, como pede o critério de aceite de tempo. | Manter `PostgresFixture` como classe separada (como o texto da spec sugere) exigiria compor duas fixtures de vida coordenada (o factory precisa da connection string do Postgres *antes* de montar o host) — xUnit não tem um jeito limpo de injetar uma `ICollectionFixture` dentro de outra sem gambiarra (`static` compartilhado ou um `IClassFixture` aninhado); fundir numa classe só é estritamente mais simples para o mesmo resultado observável. Nomeio a classe única de `EReaderApiFactory` (mantém o nome que os testes vão referenciar) e documento no XML doc que ela também é a fixture do Postgres. |
| D-05-9 | Rate limiter durante os testes de integração | `Program.cs`: `if (!app.Environment.IsEnvironment("Testing")) app.UseRateLimiter();` — pula o middleware inteiro (e com ele, toda `[EnableRateLimiting]`) quando `ASPNETCORE_ENVIRONMENT=Testing` (D-05-5). Resolve de uma vez o achado da [seção 0](#0-estado-atual-verificado) (partição `"unknown"` compartilhada por toda a suíte sob `TestServer`) sem precisar mexer na lógica de limiter em si. Se algum teste específico quiser validar o comportamento de rate limiting (`429`+`Retry-After`), ele usa `factory.WithWebHostBuilder(b => b.UseSetting("ASPNETCORE_ENVIRONMENT", "Development"))` só naquela classe, isolado dos demais. | Não mexer nisso faz a suíte de integração falhar de forma não-determinística (depende de quantos testes de auth já rodaram antes na mesma execução) assim que passar de ~10 chamadas de `/api/auth/*` no total — inclusive o próprio smoke test do checklist ("register→login→GET /me") já é 2 chamadas de auth, então um punhado de classes de teste estoura o limite. |

Este plano assume as recomendações de D-05-1 a D-05-9. Nenhuma delas altera comportamento de
produção de forma observável, exceto D-05-9 (uma linha condicional em `Program.cs` gated por
`ASPNETCORE_ENVIRONMENT=Testing`, que nunca é o environment usado em produção/Docker Compose).
Sinalize se quiser revisar alguma antes de eu começar a implementar.

## 3. Ordem de execução

```
P1  Criar tests/EReader_API.Domain.Tests, tests/EReader_API.Application.Tests,
    tests/EReader_API.Api.Tests + adicionar ao EReader.slnx
P2  Pacotes NuGet nos 3 projetos (xunit, FluentAssertions 7.x pin (D-05-4), NSubstitute (D-05-3);
    Api.Tests ganha Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql, Respawn,
    coverlet.collector)
P3  Program.cs: `public partial class Program;` + guard do rate limiter (D-05-9)
P4  Domain.Tests: teste mínimo (D-05-1) — smoke de build do projeto
P5  Application.Tests: ReadingProgressCalculator.PercentComplete (0/arredondamento/limites)
P6  Application.Tests: referência a Infra (D-05-2) + teste de AuthService (credenciais
    inválidas + rotação de refresh token, UserManager<ApplicationUser> mockado)
P7  Api.Tests: EReaderApiFactory (Testcontainers Postgres + Migrate + config overrides
    D-05-6/D-05-7 + environment "Testing" D-05-5) fundido com a responsabilidade de
    PostgresFixture (D-05-8)
P8  Api.Tests: Respawn (reset entre testes) + IntegrationTestBase/collection fixture
P9  Api.Tests: AuthHelper (RegisterAndLoginAsync) + fake IEmailSender (captura token de reset)
P10 Api.Tests: BookBuilder + PDF mínimo válido embutido como recurso
P11 Api.Tests: smoke tests — GET /health/ready → 200; register→login→GET /me
P12 Api.Tests: teste ponta a ponta do critério de aceite — registrar, autenticar, upload de
    PDF, download com Range, apagar
P13 Documentar Docker/dotnet test no README/CLAUDE.md
P14 Verificação final (suíte 2x seguidas, dotnet test completo)
```

Commits pequenos sugeridos: (P1+P2), (P3), (P4+P5), (P6), (P7+P8), (P9+P10), (P11+P12), (P13).

---

## 4. Passo a passo

### P1 — Projetos e solução

```powershell
dotnet new xunit -o tests/EReader_API.Domain.Tests -n EReader_API.Domain.Tests
dotnet new xunit -o tests/EReader_API.Application.Tests -n EReader_API.Application.Tests
dotnet new xunit -o tests/EReader_API.Api.Tests -n EReader_API.Api.Tests
```

`ProjectReference`s:
- `Domain.Tests` → `EReader_API.Domain`
- `Application.Tests` → `EReader_API.Application` + `EReader_API.Infra` (D-05-2)
- `Api.Tests` → `EReader_API` (host, expõe `Program`)

```powershell
dotnet sln EReader.slnx add tests/EReader_API.Domain.Tests/EReader_API.Domain.Tests.csproj
dotnet sln EReader.slnx add tests/EReader_API.Application.Tests/EReader_API.Application.Tests.csproj
dotnet sln EReader.slnx add tests/EReader_API.Api.Tests/EReader_API.Api.Tests.csproj
```

(`EReader.slnx` é o formato XML novo — `dotnet sln add` já sabe escrever nele; conferir o diff
depois, deve só acrescentar 3 `<Project Path="..."/>`.)

### P2 — Pacotes

Comuns aos 3 (o template `xunit` já traz `Microsoft.NET.Test.Sdk`/`xunit`/
`xunit.runner.visualstudio`):
- `FluentAssertions` **pinado em `7.x`** (D-05-4) — checar a última release `7.` no restore, ex.
  `dotnet add package FluentAssertions --version 7.*` (ajustar para o número exato resolvido).
- `coverlet.collector` (já vem no template `xunit`, manter).

`Application.Tests` adicional:
- `NSubstitute` (D-05-3).

`Api.Tests` adicional:
- `NSubstitute` (fake do `IEmailSender`, se optar por mock em vez de classe fake escrita à mão —
  ver [5.6](#56-fake-iemailsender)).
- `Microsoft.AspNetCore.Mvc.Testing` (traz `WebApplicationFactory`).
- `Testcontainers.PostgreSql`.
- `Respawn`.

Todos os pacotes: usar a versão mais recente estável compatível com `net10.0` resolvida no
`dotnet restore` no momento da implementação — não travar números aqui além do pin explícito de
`FluentAssertions` (D-05-4).

### P3 — `Program.cs`

1. Acrescentar no fim do arquivo:
   ```csharp
   public partial class Program;
   ```
2. Guard do rate limiter (D-05-9), substituindo a linha atual `app.UseRateLimiter();`:
   ```csharp
   if (!app.Environment.IsEnvironment("Testing"))
       app.UseRateLimiter();
   ```
3. `dotnet build EReader.slnx` → verde.

### P4 — `Domain.Tests`

Teste mínimo honesto (D-05-1) — por exemplo, igualdade estrutural de `BookQuery` (é um `record`)
ou os valores numéricos de `BookSource`/`BookScope` (usados como `int` implícito no banco via
`CHECK`/índice — vale garantir que não mudam sem querer). Ver [5.1](#51-domaintests-exemplo).

### P5 — `Application.Tests`: `PercentComplete`

`ReadingProgressCalculatorTests.cs` contra `EReader_API.Application.Common
.ReadingProgressCalculator.PercentComplete` — casos: `totalPages: null` → `0`; `totalPages: 0` →
`0`; arredondamento (`currentPage: 1, totalPages: 3` → `33.33`); limites (`currentPage ==
totalPages` → `100`). Ver [5.2](#52-applicationtests-percentcomplete).

### P6 — `Application.Tests`: `AuthService`

1. `EReader_API.Application.Tests.csproj`: `ProjectReference` para `EReader_API.Infra` (D-05-2).
2. `AuthServiceTests.cs` — `Substitute.For<UserManager<ApplicationUser>>(...)` +
   `IJwtTokenGenerator`/`IRefreshTokenStore`/`IEmailSender`/`IBookService` mockados via
   `Substitute.For<T>()`. Casos: `LoginAsync` com usuário inexistente → `AuthException`;
   `LoginAsync` com senha errada (`CheckPasswordAsync` retorna `false`) → `AuthException`;
   `RefreshAsync` com token já revogado (`stored.WasRevoked == true`) → chama
   `RevokeAllForUserAsync` e lança `AuthException`; `RefreshAsync` válido → chama
   `RevokeAsync(oldToken, newToken, ct)` (rotação) e retorna `AuthResult` com o novo token. Ver
   [5.3](#53-applicationtests-authservice).

### P7 — `Api.Tests`: `EReaderApiFactory`

`Fixtures/EReaderApiFactory.cs` — ver [5.4](#54-apitests-ereaderapifactorycs). Responsabilidades
(fundidas, D-05-8):
1. `IAsyncLifetime.InitializeAsync`: sobe `PostgreSqlContainer` (imagem `postgres:17`, igual ao
   `docker-compose.yml`), guarda `ConnectionString`, chama
   `ApplicationDbContext.Database.MigrateAsync()` uma vez via um `IServiceScope` deste mesmo
   factory (depois do primeiro `Services` ser resolvido).
2. `ConfigureWebHost`: `builder.UseEnvironment("Testing")` (D-05-5) +
   `ConfigureAppConfiguration` sobrescrevendo `ConnectionStrings:Default` (connection string do
   container), `Jwt:SigningKey` (D-05-7), `FileStorage:RootPath` (diretório temp por execução,
   D-05-6) + `services.AddScoped<IEmailSender, FakeEmailSender>()` substituindo o
   `LogEmailSender` real (via `ConfigureTestServices`, único ponto onde troca DI — ver
   [5.6](#56-fake-iemailsender)).
3. `IAsyncLifetime.DisposeAsync`: para o container, apaga o diretório temp de `FileStorage`.

### P8 — Respawn + fixture de coleção

1. `Fixtures/ApiCollection.cs`:
   ```csharp
   [CollectionDefinition("Api")]
   public class ApiCollection : ICollectionFixture<EReaderApiFactory>;
   ```
2. `EReaderApiFactory.ResetDatabaseAsync()` — cria o `Respawner` (lazy, uma vez, guardado em
   campo) contra a `ConnectionString` do container, `DbAdapter.Postgres`,
   `TablesToIgnore = ["__EFMigrationsHistory"]`; expõe `Task ResetAsync()` chamando
   `respawner.ResetAsync(connectionString)`.
3. `IntegrationTestBase.cs` — classe base abstrata, `[Collection("Api")]`, `IAsyncLifetime`:
   `InitializeAsync` chama `factory.ResetAsync()` **antes** de cada teste (xUnit cria uma
   instância nova da classe de teste por `[Fact]`, então isso roda por método). Expõe
   `HttpClient Client` (via `factory.CreateClient()`). Ver [5.5](#55-apitests-fixtures-de-reset).

### P9 — `AuthHelper` + fake de e-mail

1. `Helpers/AuthHelper.cs`: `RegisterAndLoginAsync(HttpClient client, string? email = null)` →
   `POST /api/auth/register` (gera e-mail único por chamada, ex. `$"user-{Guid.NewGuid():N}
   @test.local"`), extrai `AccessToken` do `AuthResult`, seta
   `client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken)`, faz `GET
   /api/auth/me` para obter o `userId` (`UserProfile.Id` — `AuthResult` não carrega `userId`),
   retorna `(HttpClient, Guid UserId)`. Ver [5.7](#57-apitests-authhelpercs).
2. `Fakes/FakeEmailSender.cs` implementa `IEmailSender`, guarda o último `(email, token)` recebido
   num campo estático `ConcurrentDictionary<string,string>` **ou** scoped + exposto via um
   singleton auxiliar registrado no DI — ver [5.6](#56-fake-iemailsender) para a escolha exata
   (precisa sobreviver entre a chamada HTTP de `forgot-password` e a leitura pelo teste).

### P10 — `BookBuilder` + PDF mínimo

1. `Fixtures/minimal.pdf` — PDF válido mínimo (`%PDF-1.4` … `%%EOF`, sem conteúdo real) como
   `<EmbeddedResource>` no `.csproj` do `Api.Tests`.
2. `Builders/BookBuilder.cs` — helper que monta o `MultipartFormDataContent` para
   `POST /api/books` a partir do PDF embutido + campos default sobrescrevíveis (`Title`,
   `Author`, etc.), usado pelos testes de catálogo. Ver [5.8](#58-apitests-bookbuildercs).

### P11 — Smoke tests

1. `HealthEndpointsTests.cs`: `GET /health/ready` → `200`.
2. `AuthEndpointsTests.cs`: `Register_ValidPayload_ReturnsTokensAndMe` — registra, usa o
   `AccessToken` para `GET /api/auth/me`, confere `200` + e-mail bate.

### P12 — Teste ponta a ponta (critério de aceite)

`BooksEndpointsTests.cs`: `UploadDownloadDelete_FullFlow_Succeeds` — `AuthHelper
.RegisterAndLoginAsync`, `BookBuilder` + `POST /api/books` → `201`, `GET /api/books/{id}/file`
com header `Range: bytes=0-99` → `206` + `Content-Range`, `DELETE /api/books/{id}` → `204`,
`GET /api/books/{id}` subsequente → `404`.

### P13 — Documentação

`CLAUDE.md`/`README`: seção "Testes" — `dotnet test` roda os 3 projetos; `Api.Tests` exige Docker
ativo (Testcontainers); tempo esperado da suíte de integração (minutos, container reutilizado por
coleção — D-05-8).

### P14 — Verificação final (ver seção 6)

---

## 5. Arquivos finais (esqueletos)

### 5.1 `Domain.Tests` (exemplo)

```csharp
using EReader_API.Domain.Entities.Catalog;
using FluentAssertions;
using Xunit;

namespace EReader_API.Domain.Tests;

public class BookQueryTests
{
    [Fact]
    public void BookQuery_SameValues_AreEqual()
    {
        var a = new BookQuery(Guid.Empty, BookScope.Public, null, null, 1, 20, null);
        var b = new BookQuery(Guid.Empty, BookScope.Public, null, null, 1, 20, null);

        a.Should().Be(b); // record: igualdade estrutural
    }
}
```

> Placeholder deliberado (D-05-1) — trocar/expandir assim que `Domain` ganhar uma invariante de
> verdade (ex. se uma spec futura mover validação de `Book` para dentro da entidade).

### 5.2 `Application.Tests`: `PercentComplete`

```csharp
using EReader_API.Application.Common;
using FluentAssertions;
using Xunit;

namespace EReader_API.Application.Tests.Common;

public class ReadingProgressCalculatorTests
{
    [Theory]
    [InlineData(5, null, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(1, 3, 33.33)]
    [InlineData(10, 10, 100)]
    public void PercentComplete_VariousInputs_ReturnsExpected(
        int currentPage, int? totalPages, decimal expected) =>
        ReadingProgressCalculator.PercentComplete(currentPage, totalPages).Should().Be(expected);
}
```

### 5.3 `Application.Tests`: `AuthService`

```csharp
using EReader_API.Application.Catalog;
using EReader_API.Application.Identity;
using EReader_API.Infra.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Xunit;

namespace EReader_API.Application.Tests.Identity;

public class AuthServiceTests
{
    private static UserManager<ApplicationUser> MockUserManager() =>
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);

    [Fact]
    public async Task LoginAsync_UnknownEmail_ThrowsAuthException()
    {
        var userManager = MockUserManager();
        userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        var sut = new AuthService(userManager, Substitute.For<IJwtTokenGenerator>(),
            Substitute.For<IRefreshTokenStore>(), Substitute.For<IEmailSender>(),
            Substitute.For<IBookService>());

        var act = () => sut.LoginAsync(new LoginRequest("nobody@test.local", "whatever1"), default);

        await act.Should().ThrowAsync<AuthException>();
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesAndRevokesOldToken()
    {
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = "a@test.local", DisplayName = "A" };
        var refreshStore = Substitute.For<IRefreshTokenStore>();
        refreshStore.FindByTokenAsync("old-token", Arg.Any<CancellationToken>())
            .Returns(new StoredRefreshToken(userId, IsActive: true, WasRevoked: false));
        refreshStore.CreateAsync(userId, Arg.Any<CancellationToken>()).Returns("new-token");

        var userManager = MockUserManager();
        userManager.FindByIdAsync(userId.ToString()).Returns(user);

        var jwt = Substitute.For<IJwtTokenGenerator>();
        jwt.Generate(userId, user.Email!, user.DisplayName).Returns(("access-token", 900));

        var sut = new AuthService(userManager, jwt, refreshStore,
            Substitute.For<IEmailSender>(), Substitute.For<IBookService>());

        var result = await sut.RefreshAsync("old-token", default);

        result.RefreshToken.Should().Be("new-token");
        await refreshStore.Received(1).RevokeAsync("old-token", "new-token", Arg.Any<CancellationToken>());
    }
}
```

> Esqueleto — conferir o shape exato de `StoredRefreshToken`/`IRefreshTokenStore` (nomes de
> propriedade `IsActive`/`WasRevoked`, assinatura de `RevokeAsync`) contra o código real de
> `Infra/Identity/RefreshTokenStore.cs` antes de codar; não lido neste levantamento.

### 5.4 `Api.Tests/EReaderApiFactory.cs`

```csharp
using System.Text;
using EReader_API.Application.Identity;
using EReader_API.Infra.Context;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace EReader_API.Api.Tests.Fixtures;

public class EReaderApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();
    private readonly string _fileStorageRoot =
        Path.Combine(Path.GetTempPath(), "ereader-tests", Guid.NewGuid().ToString("N"));
    private Respawner? _respawner;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Database.MigrateAsync();

        _respawner = await Respawner.CreateAsync(_db.GetConnectionString(), new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }

    public Task ResetAsync() => _respawner!.ResetAsync(_db.GetConnectionString());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); // D-05-5

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
        [
            new("ConnectionStrings:Default", _db.GetConnectionString()),
            new("Jwt:SigningKey", Convert.ToBase64String(Encoding.UTF8.GetBytes("test-signing-key-32-bytes-min!!"))),
            new("FileStorage:RootPath", _fileStorageRoot), // D-05-6
        ]));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddScoped<IEmailSender, FakeEmailSender>();
        });
    }

    public new async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_fileStorageRoot))
            Directory.Delete(_fileStorageRoot, recursive: true);
    }
}
```

> `builder.ConfigureAppConfiguration` num `WebApplicationFactory<Program>` que usa hosting
> mínimo (top-level statements) funciona porque o `WebApplicationFactory` intercepta o
> `IHostBuilder` por trás de `WebApplication.CreateBuilder` — padrão documentado da Microsoft
> desde .NET 6 para testar `Program.cs` sem `Startup.cs`. Confirmar comportamento assim que o
> pacote `Microsoft.AspNetCore.Mvc.Testing` estiver resolvido (versão exata do .NET 10 SDK).

### 5.5 `Api.Tests`: fixtures de reset

```csharp
using EReader_API.Api.Tests.Fixtures;
using Xunit;

namespace EReader_API.Api.Tests;

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<EReaderApiFactory>;

[Collection("Api")]
public abstract class IntegrationTestBase(EReaderApiFactory factory) : IAsyncLifetime
{
    protected HttpClient Client { get; } = factory.CreateClient();
    protected EReaderApiFactory Factory { get; } = factory;

    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}
```

### 5.6 Fake `IEmailSender`

```csharp
using System.Collections.Concurrent;
using EReader_API.Application.Identity;

namespace EReader_API.Api.Tests.Fakes;

public class FakeEmailSender : IEmailSender
{
    public static readonly ConcurrentDictionary<string, string> CapturedTokens = new();

    public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct)
    {
        CapturedTokens[email] = resetToken;
        return Task.CompletedTask;
    }
}
```

> Estático porque a instância `Scoped` do `IEmailSender` que recebe o token (dentro do request
> HTTP `forgot-password`) não é a mesma instância que o método de teste (fora de qualquer request)
> consegue resolver via `Factory.Services` — um dicionário estático simples é o jeito mais direto
> de atravessar essa fronteira num fake de teste. Limpar `CapturedTokens` no `ResetAsync`/
> `InitializeAsync` da `IntegrationTestBase` se algum teste depender de estado limpo entre execuções.

### 5.7 `Api.Tests/AuthHelper.cs`

```csharp
using System.Net.Http.Json;

namespace EReader_API.Api.Tests.Helpers;

public static class AuthHelper
{
    public record AuthedUser(Guid UserId, string Email);

    public static async Task<AuthedUser> RegisterAndLoginAsync(HttpClient client, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email,
            Password = "Test1234!",
            DisplayName = "Test User",
        });
        register.EnsureSuccessStatusCode();
        var tokens = await register.Content.ReadFromJsonAsync<AuthResultDto>();

        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens!.AccessToken);

        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        return new AuthedUser(me!.Id, email);
    }

    private record AuthResultDto(string AccessToken, string RefreshToken, int ExpiresInSeconds);
    private record MeDto(Guid Id, string Email, string DisplayName, DateTime CreatedAt);
}
```

> `AuthResult`/`UserProfile` reais (em `EReader_API.Application.Identity`) poderiam ser
> referenciados diretamente em vez de DTOs locais duplicados — decisão a bater na implementação:
> reusar os tipos reais deixa o helper mais acoplado ao contrato de `Application`, mas evita
> duplicação; DTOs locais isolam o teste de um refactor de shape na API real. Recomendo reusar os
> tipos reais (`AuthResult`, `UserProfile`) já que `Api.Tests` já referencia `EReader_API` (que
> por sua vez referencia `Application`) — os `record` locais acima são só para deixar o esqueleto
> autocontido nesta seção.

### 5.8 `Api.Tests/BookBuilder.cs`

```csharp
using System.Net.Http.Headers;
using System.Reflection;

namespace EReader_API.Api.Tests.Builders;

public class BookBuilder
{
    private string _title = "Livro de Teste";
    private string? _author = "Autor de Teste";

    public BookBuilder WithTitle(string title) { _title = title; return this; }

    public MultipartFormDataContent Build()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var pdfStream = assembly.GetManifestResourceStream(
            "EReader_API.Api.Tests.Fixtures.minimal.pdf")!;
        var pdfBytes = new byte[pdfStream.Length];
        pdfStream.ReadExactly(pdfBytes);

        var content = new MultipartFormDataContent
        {
            { new StringContent(_title), "Title" },
        };
        if (_author is not null)
            content.Add(new StringContent(_author), "Author");

        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "File", "test.pdf");

        return content;
    }
}
```

`Fixtures/minimal.pdf` no `.csproj`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Fixtures/minimal.pdf" />
</ItemGroup>
```

Conteúdo mínimo válido (bytes literais, sem página real — só o suficiente para passar pela
checagem de magic bytes `%PDF-` e não travar o `try/catch` de extração do `Docnet.Core`, que já
tolera falha de parsing sem bloquear o upload):
```
%PDF-1.4
1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj
2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj
3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >> endobj
xref
0 4
trailer << /Root 1 0 R /Size 4 >>
%%EOF
```

## 6. Verificação final (mapeada aos critérios de aceite da spec)

| Critério de aceite (spec 05) | Como verificar | Passo |
|---|---|---|
| `dotnet test` (com Docker ativo) roda os três projetos e passa | `dotnet test EReader.slnx` do zero, Docker Desktop rodando | P14 |
| Rodar a suíte duas vezes seguidas dá o mesmo resultado | `dotnet test EReader.slnx` duas vezes em sequência sem `dotnet clean` entre elas → mesmo resultado (Respawn garante isolamento; container novo a cada execução do `dotnet test`, não entre `[Fact]`s dentro da mesma execução) | P8/P14 |
| Um teste de integração registra, autentica, faz upload de PDF, baixa com `Range`, e apaga — tudo pela API HTTP real | `BooksEndpointsTests.UploadDownloadDelete_FullFlow_Succeeds` | P12 |
| Tempo da suíte de integração em minutos, não dezenas de minutos (container reutilizado por coleção) | Medir `dotnet test tests/EReader_API.Api.Tests` — 1 container Postgres para toda a coleção (D-05-8), não 1 por classe/teste | P7/P14 |

## 7. Riscos e armadilhas

- **`ConfigureAppConfiguration` num host de hosting mínimo**: o padrão do
  `WebApplicationFactory<Program>` interceptando configuração antes de `Program.cs` montar o
  `WebApplicationBuilder` é bem estabelecido, mas não foi testado neste levantamento contra a
  combinação exata .NET 10 + `Microsoft.AspNetCore.Mvc.Testing` resolvida no restore — validar
  logo no P7 antes de emendar os passos seguintes nele.
- **API exata do `Testcontainers.PostgreSql`/`Respawn`** (nomes de classe `PostgreSqlBuilder`,
  `RespawnerOptions`, `DbAdapter.Postgres`) — esqueleto baseado no uso comum dessas libs, não
  conferido contra a versão resolvida no restore (mesmo aviso que o `Docnet.Core` da spec 04) —
  checar a API real assim que os pacotes forem restaurados.
- **`Substitute.For<UserManager<ApplicationUser>>(...)`**: `UserManager<TUser>` tem um construtor
  com vários parâmetros opcionais que aceitam `null` (`IEnumerable<IUserValidator<TUser>>`, etc.)
  — o esqueleto em [5.3](#53-applicationtests-authservice) assume que NSubstitute consegue
  instanciar a partir desse construtor só com `IUserStore` real e o resto `null`; é uma técnica
  conhecida mas sensível à versão exata do pacote `Microsoft.AspNetCore.Identity` — validar no P6.
- **Timing dos containers em CI local**: `PostgreSqlContainer.StartAsync()` + `Database
  .MigrateAsync()` uma vez por execução de `dotnet test tests/EReader_API.Api.Tests` já é o
  "reuso por coleção" pedido — mas se no futuro alguém rodar `Api.Tests` com paralelismo de
  classes desabilitado incorretamente (xUnit por padrão já roda classes da mesma `[Collection]`
  sequencialmente, mas coleções diferentes em paralelo), confirmar que só existe **uma**
  `[CollectionDefinition]` no projeto — duas coleções diferentes usando
  `ICollectionFixture<EReaderApiFactory>` cada uma subiria seu próprio container.
- **`FakeEmailSender` estático**: adequado para o escopo de 05 (poucos testes de forgot/reset
  password), mas se a suíte crescer e rodar testes em paralelo entre *processos* (não é o caso do
  `dotnet test` padrão, que roda um processo por projeto de teste), o dicionário estático
  compartilhado poderia vazar entre execuções não relacionadas — não é um problema real hoje,
  documentado por transparência.

## 8. Rollback

Cada passo é um commit isolado; reverter com `git revert <commit>`. Nenhuma migration nova (a
suíte de teste roda `Database.Migrate()` só dentro do container efêmero do Testcontainers, que é
descartado ao fim de cada execução — nada toca o Postgres real de desenvolvimento/produção).

## 9. Ganchos para as próximas specs / follow-ups

- **`CLAUDE.md`**: acrescentar seção "Testes" — 3 projetos em `tests/`, `dotnet test` exige
  Docker (Testcontainers), `Program.cs` ganha `public partial class Program;` e o guard de
  `ASPNETCORE_ENVIRONMENT=Testing` no `UseRateLimiter()`.
- **Cada spec futura (ou retroativa 01–04) adiciona seus próprios casos** nesses mesmos 3
  projetos — este plano só cobre a infraestrutura + os testes mínimos que o checklist de 05 pede
  explicitamente (`PercentComplete`, `AuthService`, smoke health/auth, fluxo completo de livro).
  Cobertura de `BookService`/`ReadingProgressService`/`BookmarkService`/`HighlightService`/
  `NoteService`/`LibraryService`/`GlobalExceptionHandler` (testável isolado, já observado no plano
  de 04) fica para quando cada spec (ou uma spec de "cobertura retroativa") decidir priorizar.
- **`coverlet.collector`** já entra por ser opcional-mas-barato (item "(Opcional)" do checklist,
  incluído em P2 sem custo extra); relatório de cobertura (`reportgenerator` ou similar) fica de
  fora deste plano — mencionar só se você quiser medir cobertura formalmente depois.
