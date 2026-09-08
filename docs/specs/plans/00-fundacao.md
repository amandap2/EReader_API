# Plano de implementação — Spec 00: Fundação

> Plano de execução para [`../00-fundacao.md`](../00-fundacao.md). Nenhuma regra de negócio.
> Objetivo: o repositório compila, roda, expõe OpenAPI + UI, conecta no PostgreSQL, aplica a
> primeira migration (Identity) e o `docker compose` sobe a stack.

## 0. Estado atual (verificado)

- `EReader.API.csproj` **não referencia** `Application` nem `Infra`.
- `Program.cs` só tem `AddControllers` + `AddOpenApi` + `UseHttpsRedirection` + `UseAuthorization` + `MapControllers`.
- `ApplicationDbContext` (`IdentityDbContext<ApplicationUser>`, `DbSet<Book> Books`) **nunca é registrado**.
- **O build está quebrado hoje:** `EReader_API.Application/Services/BookService.cs` — `public Task Add(Book bookDTO) { }` não retorna valor (CS0161).
- `Dockerfile`: `COPY` do `.csproj` de `Application` vai para a pasta errada; referencia `EReader_API/EReader_API.csproj`, mas o arquivo é `EReader_API/EReader.API.csproj`; `ENTRYPOINT` espera `EReader_API.dll` (assembly hoje seria `EReader.API.dll`).
- `EReader.slnx` aponta para `EReader_API/EReader.API.csproj`.
- `appsettings*.json` sem `ConnectionStrings`/`Cors`. `docker-compose.yml` injeta `ConnectionStrings__Default`.
- Sem `dotnet-ef` instalado; sem pasta `Migrations`.

## 1. Pré-condições

- .NET SDK 10 (`dotnet --version` → 10.x). ✔ já presente (10.0.400).
- Docker Desktop em execução (para `docker compose` e para `dotnet ef database update` local).
- Acesso ao NuGet.org para restaurar `Microsoft.EntityFrameworkCore.Design`, `Scalar.AspNetCore`, `Microsoft.Extensions.DependencyInjection.Abstractions`.

## 2. Decisões a confirmar antes de começar

| # | Decisão | Recomendação | Impacto se não seguir |
|---|---|---|---|
| D-00-1 | Nome do `.csproj` da API | **Renomear** `EReader_API/EReader.API.csproj` → `EReader_API/EReader_API.csproj` e definir `<RootNamespace>EReader_API</RootNamespace>` / `<AssemblyName>EReader_API</AssemblyName>`. Alinha pasta, demais projetos, `slnx`, `Dockerfile` (`COPY` e `ENTRYPOINT EReader_API.dll`). | Manter o nome atual exige `<AssemblyName>EReader_API</AssemblyName>` + corrigir 2 linhas de `COPY` do Dockerfile mesmo assim. |
| D-00-2 | Stubs de `Book` que não compilam | **Remover** os stubs `Application/Interfaces/IBookService.cs`, `Application/Services/BookService.cs`, `Domain/Interfaces/IBookRepository.cs`, `Infra/Repositories/BookRepository.cs` e o `DbSet<Book>` de `ApplicationDbContext`. Manter `Domain/Entities/Catalog/Book.cs`. A migration fica **só Identity** (como a spec pede) e a spec 02 recria tudo com o desenho correto. | Alternativa: corrigir `BookService.Add` para `throw new NotImplementedException();` e manter `DbSet<Book>` — a migration cria uma tabela `Books` provisória que a spec 02 terá de reescrever. |
| D-00-3 | UI de documentação | **Scalar** (`Scalar.AspNetCore`, UI em `/scalar/v1`), só em `Development`. | Alternativas: Swashbuckle (`Swashbuckle.AspNetCore` + `SwaggerUI`) ou nenhuma UI (só `/openapi/v1.json`) — esta última não atende o critério de aceite "Swagger UI acessível em Dev". |
| D-00-4 | Connection string local | Colocar a string de dev (credenciais descartáveis, iguais ao compose) em `appsettings.Development.json`; produção/container usam `ConnectionStrings__Default` (env). Facilita `dotnet run` e `dotnet ef` locais. | Alternativa: `dotnet user-secrets set "ConnectionStrings:Default" "..."` ou exportar `ConnectionStrings__Default` no shell antes de cada comando `ef`. |

Este plano assume **D-00-1 = renomear**, **D-00-2 = remover stubs**, **D-00-3 = Scalar**, **D-00-4 = Development.json**.

## 3. Ordem de execução

```
P1 baseline verde ──▶ P2 renomear csproj + slnx + Dockerfile ──▶ P3 referências de projeto
   ──▶ P4 pacotes NuGet ──▶ P5 DI Application ──▶ P6 DI Infra (DbContext/Npgsql)
   ──▶ P7 appsettings + CorsOptions ──▶ P8 Program.cs ──▶ P9 limpar stubs de Book
   ──▶ P10 dotnet-ef + migration InitialIdentity ──▶ P11 .editorconfig
   ──▶ P12 verificação (local + docker compose)
```

Faça commits pequenos por passo (P2, P3+P4, P5+P6, P7+P8+P9, P10, P11).

---

## 4. Passo a passo

### P1 — Baseline

1. `dotnet restore EReader.slnx`
2. `dotnet build EReader.slnx` → confirmar que **falha** em `BookService.Add` (CS0161). Isso valida o diagnóstico; será resolvido em P9 (ou já agora, se preferir destravar o build antes).

> Se quiser o build verde já: aplique P9 antes de P2. A ordem aqui só agrupa a limpeza de `Book` num passo próprio.

### P2 — Renomear o `.csproj` da API + `slnx` + `Dockerfile`

1. Renomear o arquivo:
   ```powershell
   git mv "EReader_API/EReader.API.csproj" "EReader_API/EReader_API.csproj"
   # sem git: Rename-Item "EReader_API/EReader.API.csproj" "EReader_API.csproj"
   ```
2. Editar `EReader_API/EReader_API.csproj` — adicionar no `<PropertyGroup>`:
   ```xml
   <RootNamespace>EReader_API</RootNamespace>
   <AssemblyName>EReader_API</AssemblyName>
   ```
3. `EReader.slnx` — trocar a última linha:
   ```xml
   <Project Path="EReader_API/EReader_API.csproj" />
   ```
4. `EReader_API/Controllers/BookController.cs` — alinhar o namespace (1 arquivo):
   `namespace EReader.API.Controllers;` → `namespace EReader_API.Controllers;`
   (e trocar `: Controller` por `: ControllerBase` enquanto está aqui — controller de API).
5. `EReader_API/Dockerfile` — usar a versão corrigida da [seção 5.6](#56-dockerfile).
6. `dotnet build EReader.slnx` (ainda vai falhar em `BookService.Add` se P9 não foi feito; o resto deve resolver).

**Verificação:** `dotnet msbuild EReader_API/EReader_API.csproj -getProperty:AssemblyName` → `EReader_API`.

### P3 — Referências de projeto na API

`EReader_API/EReader_API.csproj` — adicionar um `<ItemGroup>`:

```xml
<ItemGroup>
  <ProjectReference Include="..\EReader_API.Application\EReader_API.Application.csproj" />
  <ProjectReference Include="..\EReader_API.Infra\EReader_API.Infra.csproj" />
</ItemGroup>
```

> A API só **precisa** de `Application`; `Infra` entra apenas para a composição da raiz
> (registrar implementações no DI). Controllers devem depender de tipos de `Application`.

### P4 — Pacotes NuGet

```powershell
# UI de documentação (só usada em Development)
dotnet add EReader_API/EReader_API.csproj package Scalar.AspNetCore

# EF Core Design — no projeto que contém o DbContext
dotnet add EReader_API.Infra/EReader_API.Infra.csproj package Microsoft.EntityFrameworkCore.Design

# IServiceCollection no projeto Application (Microsoft.NET.Sdk puro)
dotnet add EReader_API.Application/EReader_API.Application.csproj package Microsoft.Extensions.DependencyInjection.Abstractions
```

Depois, em `EReader_API.Infra/EReader_API.Infra.csproj`, marcar o Design como privado:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

Se o build de `Infra` reclamar de `IConfiguration` ausente:
`dotnet add EReader_API.Infra/EReader_API.Infra.csproj package Microsoft.Extensions.Configuration.Abstractions`
(normalmente já vem transitivo do EF Core / Identity).

> Versões: deixe o `10.0.*` resolver para a mesma minor do EF Core já presente
> (`Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2`). Para `Scalar.AspNetCore`, use a última 2.x.
> Fixe as versões exatas após o primeiro restore.

### P5 — DI em `Application`

Criar `EReader_API.Application/DependencyInjection.cs` — ver [5.3](#53-eReader_apiapplicationdependencyinjectioncs).

### P6 — DI em `Infra` (DbContext + Npgsql)

Criar `EReader_API.Infra/DependencyInjection.cs` — ver [5.4](#54-eReader_apiinfradependencyinjectioncs).
`ApplicationDbContext` permanece `IdentityDbContext<ApplicationUser>` (a troca para `Guid` é da spec 01).

### P7 — `appsettings` + opções de CORS

- `EReader_API/appsettings.json` → ver [5.1](#51-appsettingsjson).
- `EReader_API/appsettings.Development.json` → ver [5.2](#52-appsettingsdevelopmentjson) (inclui a connection string local).
- (Opcional) `CorsOptions` tipado em `Application` ou lido inline no `Program.cs` — este plano lê inline.

### P8 — `Program.cs`

Substituir o conteúdo pelo da [seção 5.5](#55-eReader_apiprogramcs).

### P9 — Limpar os stubs de `Book` (D-00-2)

```powershell
git rm EReader_API.Application/Interfaces/IBookService.cs `
       EReader_API.Application/Services/BookService.cs `
       EReader_API.Domain/Interfaces/IBookRepository.cs `
       EReader_API.Infra/Repositories/BookRepository.cs
```

Em `EReader_API.Infra/Context/ApplicationDbContext.cs` — remover a linha
`public DbSet<Book> Books { get; set; }` e o `using EReader_API.Domain.Entities.Catalog;`
(manter `ApplyConfigurationsFromAssembly` — inofensivo sem configs).

Manter: `EReader_API.Domain/Entities/Catalog/Book.cs`, os demais repositórios/entidades
(`Bookmark`, `Highlight`, `Note`, `User`, `IBookmarkRepository`, ...). A spec 02 reintroduz
`Book` no contexto e recria serviço/repo com o desenho correto.

`dotnet build EReader.slnx` → **verde**.

### P10 — `dotnet-ef` + migration `InitialIdentity`

```powershell
dotnet tool install --global dotnet-ef   # ou: dotnet tool update --global dotnet-ef
dotnet ef --version                       # confirmar 10.x

dotnet ef migrations add InitialIdentity `
  --project EReader_API.Infra `
  --startup-project EReader_API `
  --output-dir Migrations
```

Gera `EReader_API.Infra/Migrations/*_InitialIdentity.cs` com as tabelas do Identity
(`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, ...). **Não deve haver tabela `Books`**
(D-00-2). Se aparecer, o `DbSet<Book>` não foi removido.

Aplicar (precisa de PostgreSQL em `localhost:5432` — suba só o banco):

```powershell
docker compose -f EReader_API/docker-compose.yml up -d db
dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

> `migrations add` não conecta ao banco; `database update` sim. Em `Development`, o
> `Program.cs` também roda `Database.Migrate()` no startup — rodar o `update` aqui é para
> validar o fluxo de linha de comando (usado no pipeline em produção).

### P11 — `.editorconfig`

Criar `.editorconfig` na raiz — ver [5.7](#57-editorconfig). Rode `dotnet format --verify-no-changes`
para ver o gap (não precisa corrigir tudo agora).

### P12 — Verificação (ver seção 6)

---

## 5. Arquivos finais

### 5.1 `EReader_API/appsettings.json`

```json
{
  "ConnectionStrings": {
    "Default": ""
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:4200" ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### 5.2 `EReader_API/appsettings.Development.json`

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=ereader;Username=postgres;Password=postgres"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

> Credenciais idênticas às do `docker-compose.yml` (descartáveis, só dev). Produção/container
> continuam usando `ConnectionStrings__Default` via variável de ambiente.

### 5.3 `EReader_API.Application/DependencyInjection.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace EReader_API.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Serviços de caso de uso serão registrados aqui a partir da spec 01.
        return services;
    }
}
```

### 5.4 `EReader_API.Infra/DependencyInjection.cs`

```csharp
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EReader_API.Infra;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'Default' não configurada (ConnectionStrings:Default / ConnectionStrings__Default).");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Repositórios e serviços de infraestrutura entram aqui a partir da spec 01.
        return services;
    }
}
```

### 5.5 `EReader_API/Program.cs`

```csharp
using EReader_API.Application;
using EReader_API.Infra;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Serviços ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

const string WebCors = "web";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(WebCors, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// --- Pipeline ---
app.UseExceptionHandler();   // usa IProblemDetailsService => application/problem+json
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                 // /openapi/v1.json
    app.MapScalarApiReference();      // UI em /scalar/v1

    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();

    // Auxílio de verificação da spec 00 — REMOVER após validar o ProblemDetails.
    app.MapGet("/_throw", () => { throw new InvalidOperationException("boom"); });
}

app.UseHttpsRedirection();
app.UseCors(WebCors);
app.UseAuthorization();

app.MapControllers();

app.Run();
```

> `using EReader_API.Infra.Context;` + `Microsoft.EntityFrameworkCore` são só para o
> `Database.Migrate()` do bloco de Development. A partir da spec 05, adicionar
> `public partial class Program { }` ao final para o `WebApplicationFactory`.

### 5.6 `EReader_API/Dockerfile`

```dockerfile
# See https://aka.ms/customizecontainer

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["EReader_API/EReader_API.csproj", "EReader_API/"]
COPY ["EReader_API.Application/EReader_API.Application.csproj", "EReader_API.Application/"]
COPY ["EReader_API.Domain/EReader_API.Domain.csproj", "EReader_API.Domain/"]
COPY ["EReader_API.Infra/EReader_API.Infra.csproj", "EReader_API.Infra/"]
RUN dotnet restore "EReader_API/EReader_API.csproj"
COPY . .
WORKDIR "/src/EReader_API"
RUN dotnet build "EReader_API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EReader_API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EReader_API.dll"]
```

Correções: `COPY` do `.csproj` de `Application` agora vai para `EReader_API.Application/`;
o nome `EReader_API.csproj` casa com o arquivo renomeado; `ENTRYPOINT EReader_API.dll` casa
com `<AssemblyName>EReader_API</AssemblyName>`.

### 5.6.1 `EReader_API/docker-compose.yml` (ajustes)

```yaml
name: ereader

services:
  api:
    build:
      context: ..
      dockerfile: EReader_API/Dockerfile
    ports:
      - "8080:8080"
    depends_on:
      db:
        condition: service_healthy
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_HTTP_PORTS=8080
      - ConnectionStrings__Default=Host=db;Port=5432;Database=ereader;Username=postgres;Password=postgres

  db:
    image: postgres:17
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: ereader
    volumes:
      - ereader-pgdata:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d ereader"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  ereader-pgdata:
```

Motivos: `ASPNETCORE_ENVIRONMENT=Development` habilita OpenAPI/Scalar e o auto-migrate na
stack de dev; `healthcheck` + `depends_on: condition: service_healthy` evita o `Migrate()`
falhar por o Postgres ainda não estar pronto. Deploy de produção usará outra configuração
(sem auto-migrate, ambiente `Production`) — fora do escopo da spec 00.

### 5.7 `.editorconfig` (raiz)

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space

[*.{cs,csx}]
indent_size = 4
csharp_style_namespace_declarations = file_scoped:suggestion
csharp_using_directive_placement = outside_namespace:suggestion
dotnet_sort_system_directives_first = true
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion
csharp_prefer_braces = true:suggestion
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
dotnet_diagnostic.CA2007.severity = none

[*.{json,jsonc,yml,yaml,csproj,slnx,props,targets}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

---

## 6. Verificação final (mapeada aos critérios de aceite da spec)

| Critério de aceite (spec 00) | Como verificar | Passo |
|---|---|---|
| `dotnet build EReader.slnx` sem warnings novos | `dotnet build EReader.slnx -warnaserror` (ou revisar a saída) | P9 |
| `dotnet run` sobe; `GET /openapi/v1.json` → 200 | `dotnet run --project EReader_API` e `curl http://localhost:5035/openapi/v1.json -i` | P8 |
| Swagger UI acessível em Dev | abrir `http://localhost:5035/scalar/v1` | P4/P8 |
| Startup em Dev cria schema do Identity | `docker compose ... up -d db`; `dotnet run`; conectar no banco e ver `AspNetUsers` (`\dt` no `psql`) | P6/P8/P10 |
| `docker compose up --build` sobe API + Postgres, API saudável | `docker compose -f EReader_API/docker-compose.yml up --build`; `curl http://localhost:8080/openapi/v1.json -i` → 200; `docker compose ps` mostra `db` healthy | P2/P8 |
| Erros não tratados → `application/problem+json` | `curl http://localhost:5035/_throw -i` → `500` com `Content-Type: application/problem+json`; depois **remover** o endpoint `/_throw` | P8 |
| Origem `http://localhost:4200` passa no CORS | `curl -i -X OPTIONS http://localhost:5035/openapi/v1.json -H "Origin: http://localhost:4200" -H "Access-Control-Request-Method: GET"` → header `Access-Control-Allow-Origin: http://localhost:4200` | P7/P8 |

Comandos de verificação são PowerShell/curl no Windows; `curl` é o `curl.exe` real do Windows 10+.

Ao final: **remover o endpoint `/_throw`** e marcar a spec como `Status: Concluído`.

## 7. Riscos e armadilhas

- **`dotnet ef` cria a `ApplicationDbContext` em tempo de design.** Como o `Database.Migrate()`
  está *depois* de `builder.Build()` no `Program.cs`, as EF Tools não o executam (elas
  interrompem após construir o host). Mas `UseNpgsql("")` com string vazia lança na
  construção do modelo — por isso a connection string de dev vai em
  `appsettings.Development.json` (D-00-4). Sem ela: `dotnet ef` falha com "connection string".
- **Container em `Production` não expõe `/openapi`.** Por isso o compose de dev seta
  `ASPNETCORE_ENVIRONMENT=Development`. Não replicar isso em produção.
- **`Migrate()` no startup x Postgres ainda subindo** (compose): resolvido pelo `healthcheck`
  + `depends_on: service_healthy`. Rodando local sem Docker, suba o `db` antes do `dotnet run`.
- **Renomear o `.csproj`** pode deixar o `.vs/` e caches do VS inconsistentes — feche o VS,
  `git clean -xdf` em `bin/ obj/ .vs/` se necessário, reabra.
- **`AllowAnyHeader/AllowAnyMethod` sem `AllowCredentials`** é o correto agora; a spec 01
  ajusta se o front precisar enviar cookies (não é o caso — o token vai no header).
- **`-warnaserror`**: o código legado (`Bookmark`, `Highlight`, `Note`, `User`) tem
  propriedades `string` não-anuláveis sem inicialização → warnings `CS8618`. Não são "novos"
  (já existem), mas se usar `-warnaserror` eles aparecem. Tratá-los é da spec 03; aqui,
  apenas não introduzir warnings novos.

## 8. Rollback

Cada passo é um commit isolado. Para reverter: `git revert <commit>` do passo. A migration
`InitialIdentity` só existe em arquivo (P10) — se precisar refazer:
`dotnet ef migrations remove --project EReader_API.Infra --startup-project EReader_API`
(com o banco ainda sem a migration aplicada) ou `dotnet ef database update 0` antes.

## 9. Ganchos para as próximas specs

- **Spec 01** troca `IdentityDbContext<ApplicationUser>` → `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, adiciona Identity/JWT no `AddInfrastructure`/`Program.cs`, e a migration passa a ter conteúdo real de Identity com chave `Guid` (a `InitialIdentity` daqui pode ser regenerada nesse momento, já que o banco ainda é descartável).
- **Spec 02** reintroduz `DbSet<Book>` e recria `IBookRepository`/`BookService`/`BookController`.
- **Spec 04** substitui `UseExceptionHandler()` padrão por um `IExceptionHandler` global e adiciona `/health/*`.
- **Spec 05** adiciona `public partial class Program { }` e os projetos de teste ao `slnx`.
