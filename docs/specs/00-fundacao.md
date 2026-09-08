# 00 — Fundação

**Status:** Concluído
**Depende de:** —
**Objetivo:** Sair do scaffold: ligar os quatro projetos, configurar `Program.cs`
(DbContext, DI, CORS, Swagger, ProblemDetails), definir a configuração, gerar a primeira
migration e corrigir o `Dockerfile`. Nenhuma regra de negócio nova.

## Contexto

Hoje `EReader.API.csproj` não referencia `Application`/`Infra`; `Program.cs` só tem
`AddControllers` + `AddOpenApi`; `ApplicationDbContext` nunca é registrado; não há
configuração de banco fora do `docker-compose.yml`. Ver "Divergências" em
`../ESPECIFICACAO-BACKEND.md`.

## Escopo

**Entra:** referências de projeto, registro de `DbContext` + Npgsql, DI base, opções de
configuração fortemente tipadas, CORS, Swagger em Dev, ProblemDetails, pacote de Design do
EF, primeira migration (somente Identity — as entidades vêm nas specs seguintes), correção
do `Dockerfile`, `.editorconfig` opcional.

**Não entra:** Identity/JWT (spec 01), entidades de `Catalog`/`Reading` (02/03), endpoints
de negócio.

## Tarefas

- [ ] `EReader.API.csproj`: adicionar `ProjectReference` para `EReader_API.Application` e
      `EReader_API.Infra`.
- [ ] `EReader_API.Infra`: adicionar pacote `Microsoft.EntityFrameworkCore.Design`.
- [ ] Corrigir nome do assembly/arquivo: o projeto é `EReader_API/EReader.API.csproj` mas o
      `Dockerfile` referencia `EReader_API/EReader_API.csproj`. Padronizar para
      `EReader_API.csproj` **ou** ajustar o `Dockerfile`; documentar a escolha.
- [ ] `Dockerfile`: corrigir os `COPY` dos `.csproj` (o de `Application` é copiado para
      `EReader_API/` em vez de `EReader_API.Application/`); garantir `dotnet restore` sobre a
      solution ou todos os projetos.
- [ ] `appsettings.json` / `appsettings.Development.json`: adicionar seções `ConnectionStrings:Default`,
      `Cors:AllowedOrigins`. Manter segredos fora do arquivo (user-secrets/env).
- [ ] Classe `ApplicationDbContext` já existe em `Infra/Context`; manter
      `IdentityDbContext<ApplicationUser>` por ora (a troca para `Guid` é da spec 01).
- [ ] Método de extensão `AddInfrastructure(this IServiceCollection, IConfiguration)` em
      `Infra` que registra `ApplicationDbContext` com `UseNpgsql(connectionString)`.
- [ ] Método de extensão `AddApplication(this IServiceCollection)` em `Application` (vazio
      por ora, ponto de entrada para DI dos serviços de caso de uso).
- [ ] `Program.cs`:
  - [ ] `builder.Services.AddApplication();`
  - [ ] `builder.Services.AddInfrastructure(builder.Configuration);`
  - [ ] `AddControllers()` + `AddProblemDetails()`
  - [ ] CORS: policy `"web"` lendo `Cors:AllowedOrigins`
  - [ ] `AddOpenApi()` (mantido); Swagger UI só em `IsDevelopment()`
  - [ ] `app.UseExceptionHandler()` / `app.UseStatusCodePages()` com ProblemDetails
  - [ ] `app.UseCors("web")` antes de `UseAuthorization`
  - [ ] em `Development`, aplicar `db.Database.Migrate()` no startup (scope temporário)
- [ ] `dotnet ef migrations add InitialIdentity --project EReader_API.Infra --startup-project EReader_API`
- [ ] `.editorconfig` na raiz (regras C# básicas) — opcional, mas recomendado antes de crescer.

## Configuração resultante

```jsonc
// appsettings.json
{
  "ConnectionStrings": { "Default": "" },      // valor real via env/secret
  "Cors": { "AllowedOrigins": [ "http://localhost:4200" ] },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
```

`ConnectionStrings__Default` já é injetada pelo `docker-compose.yml`. Para rodar local sem
Docker, definir via `dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;..."`.

## Critérios de aceite

- `dotnet build EReader.slnx` compila sem warnings novos.
- `dotnet run --project EReader_API` sobe; `GET /openapi/v1.json` responde 200; Swagger UI
  acessível em Dev.
- Com um PostgreSQL disponível, o startup em Dev cria o schema do Identity (tabelas
  `AspNetUsers` etc.).
- `docker compose -f EReader_API/docker-compose.yml up --build` sobe API + Postgres e a API
  fica saudável.
- Erros não tratados retornam `application/problem+json`.
- Requisição de origem `http://localhost:4200` passa no CORS.

## Arquivos afetados

- `EReader_API/EReader.API.csproj` (referências) — possivelmente renomeado para `EReader_API.csproj`
- `EReader_API.Infra/EReader_API.Infra.csproj` (pacote Design)
- `EReader_API.Infra/DependencyInjection.cs` (novo — `AddInfrastructure`)
- `EReader_API.Application/DependencyInjection.cs` (novo — `AddApplication`)
- `EReader_API/Program.cs`
- `EReader_API/appsettings*.json`
- `EReader_API/Dockerfile`
- `EReader_API.Infra/Migrations/*` (novo)
- `.editorconfig` (novo, opcional)
