# Plano de implementação — Spec 01: Identidade e autenticação

> Plano de execução para [`../01-identidade.md`](../01-identidade.md). Depende da spec 00
> (já concluída). Objetivo: `ApplicationUser : IdentityUser<Guid>`, JWT + refresh tokens
> rotacionados, endpoints `/api/auth` completos, rate limiting, migration aplicada.

## 0. Estado atual (verificado)

- `ApplicationDbContext : IdentityDbContext<ApplicationUser>` — chave `string` (padrão),
  sem `DbSet<RefreshToken>`, sem configs em `Context/Configurations/`.
- `ApplicationUser : IdentityUser` — vazio, sem `DisplayName`/`CreatedAt`, sem `<Guid>`.
- Migration `20260908204639_InitialIdentity` já aplicada ao esquema Identity **string-keyed**
  (feita pela spec 00).
- `EReader_API.Application/DependencyInjection.cs` e `EReader_API.Infra/DependencyInjection.cs`
  existem mas só registram `AddDbContext`; nenhum serviço de domínio/aplicação registrado.
- `Program.cs` não tem `AddAuthentication`/`AddAuthorization`/`UseAuthentication`; só
  `UseAuthorization()` (sem efeito sem autenticação configurada).
- `EReader_API.Domain/Entities/Identity/User.cs` ainda existe: `int Id`, `string Password`
  em claro — é o modelo que a spec manda remover (D-2 do documento de referência).
- `EReader_API.Domain/Interfaces/IUserRepository.cs` opera sobre esse `User` antigo; **nada o
  implementa** (não existe pasta `Repositories/` em `Infra` — spec 00 removeu os stubs de
  `Book`, e um repositório de `User` nunca chegou a ser escrito).
- `Bookmark`, `Highlight`, `Note` (`Domain/Entities/Reading`) têm `public User User { get; set; }`
  + `int UserId` apontando para o `User` do Domain — **isso vai quebrar a compilação** assim
  que `User.cs` for removido, então esta spec precisa tocar essas três entidades mesmo não
  sendo o foco dela (spec 03 faz o redesenho completo de `Reading`).
- Nenhum pacote de JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`) referenciado em
  nenhum projeto ainda.
- `EReader_API.csproj` tem `UserSecretsId` configurado (user-secrets já funciona no projeto).
- Nenhum controller tem lógica; `BookController` está vazio (fora de escopo aqui).

## 1. Pré-condições

- Spec 00 concluída: build verde, `docker compose` sobe, migration `InitialIdentity` aplicada.
- `dotnet-ef` instalado globalmente (feito na spec 00).
- Acesso ao NuGet.org para `Microsoft.AspNetCore.Authentication.JwtBearer`.

## 2. Decisões a confirmar antes de começar

A spec original (`01-identidade.md`) lista `EReader_API.Application/Identity/*` (incluindo
`AuthService`) em "Arquivos afetados". Isso conflita com a regra do `CLAUDE.md` — *"Application
— references Domain only"* — porque uma implementação real de `AuthService` precisa de
`UserManager<ApplicationUser>`, e `ApplicationUser` é um tipo de `Infra`. As decisões abaixo
resolvem esse e outros pontos em aberto.

| # | Decisão | Recomendação | Impacto se não seguir |
|---|---|---|---|
| D-01-1 | Onde mora a **implementação** de `AuthService`/`JwtTokenGenerator`/`RefreshTokenStore`/`LogEmailSender` | **`Infra`**, referenciando `Application` (novo `ProjectReference` Infra→Application). `IAuthService`, `IJwtTokenGenerator`, `IRefreshTokenStore`, `IEmailSender` e os DTOs (records puros, sem `ApplicationUser`) ficam em `Application/Identity` — são as *portas*. Isso é a direção usual de Clean Architecture (Infra implementa portas de Application) e não expõe Identity/EF a `Application`. | Alternativa (literal da spec): colocar `AuthService` em `Application` e dar a `Application` uma referência a `Infra` — inverte a direção pretendida de dependência e contradiz o `CLAUDE.md` atual. Passa a exigir atualizar o `CLAUDE.md` para "Infra references Domain + Application" (ver §9). |
| D-01-2 | `Bookmark`/`Highlight`/`Note` referenciam `Domain.Entities.Identity.User` | Trocar `User User` + `int UserId` por só `Guid UserId` (sem navigation — `Domain` não pode depender de `ApplicationUser`, que é `Infra`). `IBookmarkRepository`/`IHighlightRepository`/`INoteRepository` e o `int Id` dessas entidades **não mudam** aqui (fora de escopo; spec 03 redesenha `Reading` por completo). `IUserRepository.cs` é removido — nada o implementa, e sua função é substituída por `UserManager<ApplicationUser>`. | Não remover `User.cs`/ajustar essas 3 entidades quebra o build (CS0246 nas três, referência a tipo removido). |
| D-01-3 | Migration | **Apagar** a migration `20260908204639_InitialIdentity` (+ `ApplicationDbContextModelSnapshot.cs`) e gerar **uma única** migration nova `AddIdentityAndRefreshTokens` do zero. Trocar a PK de `string` para `Guid` não é limpo como incremental (chaves, FKs e índices do Identity mudam de tipo); o banco ainda não tem dados reais (descartável). | Alternativa: manter a migration antiga e empilhar uma segunda — gera uma migration de "mudança de tipo de coluna" complexa e frágil para nenhum ganho, já que não há dados a preservar. |
| D-01-4 | Onde registrar `AddAuthentication`/`AddJwtBearer`/Identity options | Dentro de `AddInfrastructure` (`Infra`), não em `Program.cs` — mantém o padrão já usado por `AddApplication`/`AddInfrastructure` e mantém `Program.cs` fino. `Program.cs` só chama `AddAuthorization()` e `app.UseAuthentication()`/`app.UseAuthorization()`. | Espalhar configuração de auth em `Program.cs` foge do padrão dos outros specs e dificulta testar `AddInfrastructure` isoladamente (spec 05). |
| D-01-5 | Rate limiting | Middleware nativo `Microsoft.AspNetCore.RateLimiting` (já no shared framework do ASP.NET Core, **sem pacote novo**) — policy fixed-window `"auth"` (10 req/min por IP, particionada por `HttpContext.Connection.RemoteIpAddress`), aplicada via `[EnableRateLimiting("auth")]` no `AuthController`. | Bibliotecas de terceiros (`AspNetCoreRateLimit`) adicionam dependência e configuração (JSON de regras) sem necessidade — o nativo cobre o critério de aceite (`> 10/min` → `429`). |
| D-01-6 | Detecção de reuso de refresh token | Ao apresentar um refresh token com `RevokedAt` já setado (ou seja, já rotacionado/revogado), revogar **todos os refresh tokens ativos daquele `UserId`** (simples: `UPDATE ... SET RevokedAt = now() WHERE UserId = @id AND RevokedAt IS NULL`). | Caminhar a cadeia via `ReplacedByTokenHash` byte a byte é mais "correto" no papel mas não é exigido pelo critério de aceite e adiciona complexidade/consultas extras sem benefício mensurável aqui. |
| D-01-7 | Onde mora `ClaimsPrincipal.GetUserId()` | `Application/Common/ClaimsPrincipalExtensions.cs` — é lógica pura (ler claim `sub`, `Guid.Parse`), reutilizável e testável sem EF/Identity. | Colocar em `EReader_API/` (host) funciona mas fica menos acessível caso `Application` precise dele depois (ex.: em outro caso de uso). |

Este plano assume as recomendações de D-01-1 a D-01-7. Se qualquer uma mudar, os passos e
os arquivos finais abaixo mudam de posição/assinatura.

## 3. Ordem de execução

```
P1 baseline ──▶ P2 limpar Domain (User.cs, IUserRepository, Bookmark/Highlight/Note)
   ──▶ P3 pacotes + referência Infra→Application ──▶ P4 Infra/Identity (ApplicationUser, RefreshToken)
   ──▶ P5 ApplicationDbContext + RefreshTokenConfiguration ──▶ P6 Application/Identity (portas + DTOs)
   ──▶ P7 Application/Common (ClaimsPrincipal.GetUserId) ──▶ P8 Infra: JwtTokenGenerator, RefreshTokenStore,
   LogEmailSender ──▶ P9 Infra: AuthService ──▶ P10 Infra/DependencyInjection (Identity + JWT + DI dos serviços)
   ──▶ P11 appsettings (Jwt) + user-secrets ──▶ P12 Program.cs (auth + rate limiter)
   ──▶ P13 AuthController ──▶ P14 migration AddIdentityAndRefreshTokens ──▶ P15 verificação
```

Commits pequenos sugeridos: (P2), (P3+P4+P5), (P6+P7), (P8+P9), (P10+P11+P12), (P13), (P14).

---

## 4. Passo a passo

### P1 — Baseline

`dotnet build EReader.slnx` → deve estar verde (spec 00 concluída). Se não estiver, parar e
investigar antes de prosseguir.

### P2 — Limpar `Domain` (D-01-2)

1. `git rm EReader_API.Domain/Entities/Identity/User.cs EReader_API.Domain/Interfaces/IUserRepository.cs`
2. Em `Bookmark.cs`, `Highlight.cs`, `Note.cs`: remover `using EReader_API.Domain.Entities.Identity;`,
   remover a propriedade de navegação `public User User { get; set; }`, trocar `public int UserId { get; set; }`
   por `public Guid UserId { get; set; }`. Não tocar em mais nada (o `Id:int` dessas entidades
   e os repositórios `IBookmarkRepository`/`IHighlightRepository`/`INoteRepository` ficam como
   estão — spec 03 os redesenha).
3. `dotnet build EReader.slnx` → deve compilar (nada mais referencia o `User` removido).

### P3 — Pacotes e referência de projeto (D-01-1)

```powershell
dotnet add EReader_API.Infra/EReader_API.Infra.csproj package Microsoft.AspNetCore.Authentication.JwtBearer

dotnet add EReader_API.Infra/EReader_API.Infra.csproj reference EReader_API.Application/EReader_API.Application.csproj
```

> `Microsoft.AspNetCore.Authentication.JwtBearer` funciona em projeto `Microsoft.NET.Sdk`
> puro (não exige `Sdk.Web`) — só traz `Microsoft.IdentityModel.*` como transitiva.

### P4 — `Infra/Identity`: `ApplicationUser` e `RefreshToken`

- `ApplicationUser` passa a herdar de `IdentityUser<Guid>`; adicionar `DisplayName` e `CreatedAt`
  (ver [5.1](#51-infraidentityapplicationusercs)).
- Criar `RefreshToken.cs` (ver [5.2](#52-infraidentityrefreshtokencs)) — exatamente como no
  corpo da spec.

### P5 — `ApplicationDbContext` + configuração de `RefreshToken`

- `ApplicationDbContext : IdentityDbContext<ApplicationUser>` →
  `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` + `DbSet<RefreshToken> RefreshTokens`
  (ver [5.3](#53-infracontextapplicationdbcontextcs)).
- Criar `Infra/Context/Configurations/RefreshTokenConfiguration.cs` (ver
  [5.4](#54-infracontextconfigurationsrefreshtokenconfigurationcs)) — índice único em
  `TokenHash`, índice em `UserId`, FK para `AspNetUsers` com `ON DELETE CASCADE` (para que
  `DELETE /me` limpe os refresh tokens do usuário junto).

### P6 — `Application/Identity`: portas e DTOs

Criar (sem qualquer dependência de EF/Identity/`ApplicationUser` — só tipos primitivos e
`Guid`):

- `IAuthService.cs`, `IJwtTokenGenerator.cs`, `IRefreshTokenStore.cs`, `IEmailSender.cs`
  (ver [5.5](#55-applicationidentity-portas)).
- `Dtos/RegisterRequest.cs`, `LoginRequest.cs`, `ResetPasswordRequest.cs`, `AuthResult.cs`,
  `UserProfile.cs` (ver [5.6](#56-applicationidentitydtos)).

`AddApplication()` continua sem registrar nada aqui (as implementações vivem em `Infra`,
registradas em `AddInfrastructure` — D-01-1/D-01-4).

### P7 — `Application/Common/ClaimsPrincipalExtensions.cs` (D-01-7)

Ver [5.7](#57-applicationcommonclaimsprincipalextensionscs).

### P8 — `Infra`: `JwtTokenGenerator`, `RefreshTokenStore`, `LogEmailSender`

- `JwtTokenGenerator : IJwtTokenGenerator` — lê `JwtOptions` (bind de `Jwt` em `appsettings`),
  monta claims (`sub`, `email`, `name`, `jti`), assina HMAC-SHA256, retorna `(string accessToken, int expiresInSeconds)`.
  Ver esqueleto em [5.8](#58-infraidentityjwttokengeneratorcs).
- `RefreshTokenStore : IRefreshTokenStore` — usa `ApplicationDbContext`. Métodos: `CreateAsync`,
  `FindByTokenHashAsync`, `RevokeAsync`, `RevokeAllForUserAsync`. Hash do token: SHA-256 do
  valor opaco antes de gravar (nunca gravar o token em claro — igual ao comentário do modelo).
  Ver [5.9](#59-infraidentityrefreshtokenstorecs).
- `LogEmailSender : IEmailSender` — em Dev, `ILogger.LogInformation` com o link de reset
  (`{FrontendBaseUrl}/reset-password?email=...&token=...`, urlencoded). Ver
  [5.10](#510-infraidentitylogemailsendercs).

### P9 — `Infra`: `AuthService : IAuthService`

Depende de `UserManager<ApplicationUser>`, `IJwtTokenGenerator`, `IRefreshTokenStore`,
`IEmailSender`. Algoritmo por método (não é código pronto — implementar na hora de codar):

- **RegisterAsync**: `UserManager.CreateAsync(user, password)` (a política de senha é validada
  pelo próprio Identity via `IdentityOptions.Password`, D-01 não precisa reimplementar). Se
  falhar, mapear `IdentityResult.Errors` para uma exceção/`400` (ver §6 do documento de
  referência para o padrão de erro adotado no projeto, ou `ValidationProblem` direto no
  controller). Se ok: gerar par de tokens (ver LoginAsync) e devolver `AuthResult`.
- **LoginAsync**: `UserManager.FindByEmailAsync` → `UserManager.CheckPasswordAsync` (não usar
  `SignInManager` — não há cookies/sessão nesta API, é Bearer puro). Credenciais inválidas →
  lançar um erro de auth (a spec pede `401`; decidir na hora se é exceção customizada
  capturada em middleware, ou retorno `Result`-like — este plano não fixa esse detalhe, é
  code style, não arquitetura). Se ok: `IJwtTokenGenerator.Generate(user)` para o access token
  + `IRefreshTokenStore.CreateAsync(user.Id)` para o refresh token opaco; devolver `AuthResult`.
- **RefreshAsync**: `IRefreshTokenStore.FindByTokenHashAsync(hash(refreshToken))`. Não existe
  ou expirado → `401`. Já revogado (reuso) → `RevokeAllForUserAsync(stored.UserId)` (D-01-6) e
  `401`. Válido → revogar o antigo (`ReplacedByTokenHash` = hash do novo), criar um novo refresh
  token, gerar novo access token, devolver `AuthResult`.
- **LogoutAsync**: achar pelo hash, revogar (sem gerar novo). Idempotente-ish: se não achar,
  `204` do mesmo jeito (não vaza informação).
- **ForgotPasswordAsync**: `UserManager.FindByEmailAsync`; se existir, `GeneratePasswordResetTokenAsync`
  + `IEmailSender.SendPasswordResetAsync`. Sempre retorna (o `202` é responsabilidade do
  controller, que sempre devolve 202 independentemente do resultado do service).
- **ResetPasswordAsync**: `UserManager.ResetPasswordAsync(user, token, newPassword)` — o
  próprio Identity invalida o token após uso (baseado no `SecurityStamp`), então "token usado
  duas vezes → 400" já vem de graça.
- **GetProfileAsync**: `UserManager.FindByIdAsync` → mapear para `UserProfile`.
- **DeleteAccountAsync**: `UserManager.DeleteAsync` (hard delete). `ON DELETE CASCADE` na FK
  de `RefreshTokens` (P5) cuida da limpeza dos tokens.

### P10 — `Infra/DependencyInjection.cs`

Estender `AddInfrastructure` (ver [5.11](#511-infradependencyinjectioncs)):

1. `AddIdentityCore<ApplicationUser>(options => { /* senha, lockout */ })`
   `.AddRoles<IdentityRole<Guid>>()`
   `.AddEntityFrameworkStores<ApplicationDbContext>()`
   `.AddDefaultTokenProviders();`
2. `services.Configure<JwtOptions>(configuration.GetSection("Jwt"));`
3. `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => { ... })`
   lendo `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey` para `TokenValidationParameters`.
4. `AddRateLimiter` com a policy `"auth"` (D-01-5).
5. `services.AddScoped<IAuthService, AuthService>();`
   `services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();`
   `services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();`
   `services.AddScoped<IEmailSender, LogEmailSender>();`

### P11 — `appsettings` + user-secrets

- `appsettings.json` ganha o bloco `Jwt` (chaves não-secretas — ver [5.12](#512-appsettingsjson)).
- `Jwt:SigningKey` **não** entra em `appsettings*.json` (segue o padrão do projeto —
  `ConnectionStrings__Default`/user-secrets, ver `CLAUDE.md`):
  ```powershell
  dotnet user-secrets set "Jwt:SigningKey" "<>=32 bytes aleatórios, base64 ou string longa>" --project EReader_API
  ```
  Em produção/container: variável de ambiente `Jwt__SigningKey`.

### P12 — `Program.cs`

Adicionar (ver [5.13](#513-programcs-diff)):

```csharp
builder.Services.AddAuthorization();
// ...
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
```

`UseAuthentication()` antes de `UseAuthorization()` (já é o `UseAuthorization()` que já
existia). `UseRateLimiter()` pode vir antes ou depois de `UseCors`/`UseAuthentication` — só
precisa estar antes de `MapControllers()`.

### P13 — `AuthController`

`EReader_API/Controllers/AuthController.cs`, `[Route("api/auth")]`, `[EnableRateLimiting("auth")]`
na classe. 8 endpoints conforme a tabela da spec; injeta `IAuthService`. `GET`/`DELETE /me`
usam `[Authorize]` + `User.GetUserId()` (P7). Ver esqueleto em
[5.14](#514-controllersauthcontrollercs-esqueleto).

### P14 — Migration

```powershell
git rm EReader_API.Infra/Migrations/20260908204639_InitialIdentity.cs `
       EReader_API.Infra/Migrations/20260908204639_InitialIdentity.Designer.cs `
       EReader_API.Infra/Migrations/ApplicationDbContextModelSnapshot.cs

dotnet ef migrations add AddIdentityAndRefreshTokens `
  --project EReader_API.Infra `
  --startup-project EReader_API `
  --output-dir Migrations
```

Conferir no arquivo gerado: `AspNetUsers.Id` e as demais PKs/FKs do Identity são `uuid`;
existe `AspNetRoles`; existe `RefreshTokens` com índice único em `TokenHash` e FK para
`AspNetUsers` com `ON DELETE CASCADE`.

```powershell
docker compose -f EReader_API/docker-compose.yml up -d db
dotnet ef database update --project EReader_API.Infra --startup-project EReader_API
```

> Se o banco local ainda tiver o schema antigo (string-keyed) de uma spec 00 já aplicada,
> `database update` pode falhar por incompatibilidade. Mais simples: derrubar o volume do
> Postgres (`docker compose -f EReader_API/docker-compose.yml down -v`) já que não há dados
> reais a preservar, e recriar do zero com a migration nova.

### P15 — Verificação (ver seção 6)

---

## 5. Arquivos finais (esqueletos)

### 5.1 `Infra/Identity/ApplicationUser.cs`

```csharp
using Microsoft.AspNetCore.Identity;

namespace EReader_API.Infra.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
```

### 5.2 `Infra/Identity/RefreshToken.cs`

```csharp
namespace EReader_API.Infra.Identity;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
```

### 5.3 `Infra/Context/ApplicationDbContext.cs`

```csharp
using EReader_API.Infra.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EReader_API.Infra.Context;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
```

### 5.4 `Infra/Context/Configurations/RefreshTokenConfiguration.cs`

```csharp
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(rt => rt.Id);
        builder.HasIndex(rt => rt.TokenHash).IsUnique();
        builder.HasIndex(rt => rt.UserId);
        builder.Property(rt => rt.TokenHash).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### 5.5 `Application/Identity` — portas

```csharp
// IAuthService.cs
namespace EReader_API.Application.Identity;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string refreshToken, CancellationToken ct);
    Task ForgotPasswordAsync(string email, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct);
    Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken ct);
    Task DeleteAccountAsync(Guid userId, CancellationToken ct);
}

// IJwtTokenGenerator.cs
namespace EReader_API.Application.Identity;

public interface IJwtTokenGenerator
{
    (string AccessToken, int ExpiresInSeconds) Generate(Guid userId, string email, string displayName);
}

// IRefreshTokenStore.cs
namespace EReader_API.Application.Identity;

public interface IRefreshTokenStore
{
    Task<string> CreateAsync(Guid userId, CancellationToken ct);          // devolve o token opaco (não o hash)
    Task<StoredRefreshToken?> FindByTokenAsync(string refreshToken, CancellationToken ct);
    Task RevokeAsync(string refreshToken, string? replacedByToken, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}

public record StoredRefreshToken(Guid UserId, bool IsActive, bool WasRevoked);

// IEmailSender.cs
namespace EReader_API.Application.Identity;

public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct);
}
```

> `IRefreshTokenStore` expõe o token **opaco** (não o hash) para o `AuthService` — quem faz o
> hash e compara é a implementação em `Infra` (`RefreshTokenStore`), que é o único lugar que
> sabe como o hash é calculado.

### 5.6 `Application/Identity/Dtos`

```csharp
namespace EReader_API.Application.Identity;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
public record AuthResult(string AccessToken, string RefreshToken, int ExpiresInSeconds);
public record UserProfile(Guid Id, string Email, string DisplayName, DateTime CreatedAt);
```

### 5.7 `Application/Common/ClaimsPrincipalExtensions.cs`

```csharp
using System.Security.Claims;

namespace EReader_API.Application.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("Claim 'sub' ausente.");
        return Guid.Parse(sub);
    }
}
```

### 5.8 `Infra/Identity/JwtTokenGenerator.cs`

```csharp
using EReader_API.Application.Identity;
using Microsoft.Extensions.Options;
// + System.IdentityModel.Tokens.Jwt, Microsoft.IdentityModel.Tokens

namespace EReader_API.Infra.Identity;

public class JwtTokenGenerator(IOptions<JwtOptions> options) : IJwtTokenGenerator
{
    public (string AccessToken, int ExpiresInSeconds) Generate(Guid userId, string email, string displayName)
    {
        // claims: sub=userId, email, name=displayName, jti=Guid.NewGuid()
        // SymmetricSecurityKey a partir de options.Value.SigningKey (>= 32 bytes)
        // SigningCredentials HMAC-SHA256, expira em options.Value.AccessTokenMinutes
        throw new NotImplementedException();
    }
}
```

`JwtOptions` (record/classe simples, bind de `Jwt` em `appsettings`) vive em `Infra/Identity`
ou `Application/Identity` — como não é referenciada fora de `Infra` (só `JwtTokenGenerator` e
o `AddJwtBearer` em `AddInfrastructure` a usam), pode ficar em `Infra/Identity/JwtOptions.cs`.

### 5.9 `Infra/Identity/RefreshTokenStore.cs`

```csharp
using EReader_API.Application.Identity;
using EReader_API.Infra.Context;
// + System.Security.Cryptography para o hash SHA-256

namespace EReader_API.Infra.Identity;

public class RefreshTokenStore(ApplicationDbContext db) : IRefreshTokenStore
{
    // CreateAsync: gera token opaco (RandomNumberGenerator, ex. 32 bytes → base64url),
    //   grava RefreshToken { TokenHash = Sha256(token), ExpiresAt = now + RefreshTokenDays }.
    // FindByTokenAsync: hash do token recebido, busca por TokenHash, devolve StoredRefreshToken.
    // RevokeAsync: acha por hash, seta RevokedAt = now, ReplacedByTokenHash = hash(replacedByToken).
    // RevokeAllForUserAsync: UPDATE em massa (RevokedAt = now) onde UserId = @id e RevokedAt IS NULL.
}
```

### 5.10 `Infra/Identity/LogEmailSender.cs`

```csharp
using EReader_API.Application.Identity;
using Microsoft.Extensions.Logging;

namespace EReader_API.Infra.Identity;

public class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct)
    {
        logger.LogInformation("Reset de senha para {Email}: token={Token}", email, resetToken);
        return Task.CompletedTask;
    }
}
```

### 5.11 `Infra/DependencyInjection.cs` (diff conceitual)

```csharp
using EReader_API.Application.Identity;
using EReader_API.Infra.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
// ...

services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()!;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
            ClockSkew = TimeSpan.Zero,
        };
    });

services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

services.AddScoped<IAuthService, AuthService>();
services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
services.AddScoped<IEmailSender, LogEmailSender>();
```

> A policy de rate limiting por padrão particiona globalmente; usar
> `RateLimitPartition.GetFixedWindowLimiter(partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", ...)`
> via `AddPolicy` (em vez de `AddFixedWindowLimiter`) para particionar **por IP**, que é o que
> o critério de aceite pede.

### 5.12 `appsettings.json` (diff)

```json
{
  "Jwt": {
    "Issuer": "ereader-api",
    "Audience": "ereader-web",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 14
  }
}
```

(`Jwt:SigningKey` não entra aqui — user-secrets/env, P11.)

### 5.13 `Program.cs` (diff)

```csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorization();
// ... (Cors como já está)

var app = builder.Build();
// ...
app.UseHttpsRedirection();
app.UseCors(WebCors);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
```

### 5.14 `Controllers/AuthController.cs` (esqueleto)

```csharp
using EReader_API.Application.Common;
using EReader_API.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EReader_API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
    {
        var result = await authService.RegisterAsync(req, ct);
        return Created(string.Empty, result); // ou 201 sem Location, conforme decidido na implementação
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login(LoginRequest req, CancellationToken ct) =>
        Ok(await authService.LoginAsync(req, ct));

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResult>> Refresh([FromBody] string refreshToken, CancellationToken ct) =>
        Ok(await authService.RefreshAsync(refreshToken, ct));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] string refreshToken, CancellationToken ct)
    {
        await authService.LogoutAsync(refreshToken, ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] string email, CancellationToken ct)
    {
        await authService.ForgotPasswordAsync(email, ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
    {
        await authService.ResetPasswordAsync(req, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfile>> Me(CancellationToken ct) =>
        Ok(await authService.GetProfileAsync(User.GetUserId(), ct));

    [Authorize]
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(CancellationToken ct)
    {
        await authService.DeleteAccountAsync(User.GetUserId(), ct);
        return NoContent();
    }
}
```

> Corpo exato dos requests (`{ refreshToken }`, `{ email }`) e mapeamento de erros (`401` em
> credenciais inválidas, `400` em falha de validação do Identity) são detalhe de implementação
> — este plano fixa rotas, verbos, autorização e formato de resposta, não o tratamento de
> exceção (que depende de como a spec 04, "Robustez", padroniza `ProblemDetails` — pode-se
> antecipar um `try/catch` local nesta spec e alinhar com 04 depois).

---

## 6. Verificação final (mapeada aos critérios de aceite da spec)

| Critério de aceite (spec 01) | Como verificar | Passo |
|---|---|---|
| Fluxo completo via HTTP: registrar → logar → `GET /me` com Bearer → `refresh` → `logout` → refresh antigo `401` | `curl` sequencial (ver roteiro abaixo) | P13/P14 |
| Endpoint protegido sem token → `401`; com token expirado/de outro contexto → `401` | `curl -i http://localhost:5035/api/auth/me` (sem header) → `401`; repetir com um JWT manipulado/expirado | P12/P13 |
| `reset-password` com token válido troca a senha; token usado duas vezes → `400` | `forgot-password` → pegar o token do log (`LogEmailSender`) → `reset-password` duas vezes | P9 |
| `DELETE /me` remove o usuário; novo `login` → `401` | `curl -X DELETE` com Bearer, depois `login` de novo | P13 |
| `> 10` logins/min do mesmo IP → `429` | `for ($i=0; $i -lt 12; $i++) { curl -i http://localhost:5035/api/auth/login -d '...' }` (PowerShell) → últimas respostas `429` | P10/P12 |
| Migration aplica e reverte sem erro | `dotnet ef database update --project EReader_API.Infra --startup-project EReader_API` e `dotnet ef database update 0 ...` | P14 |

Roteiro de `curl` sugerido (PowerShell, `curl` = `curl.exe`):

```powershell
$base = "http://localhost:5035/api/auth"
curl -s -X POST "$base/register" -H "Content-Type: application/json" -d '{"email":"a@a.com","password":"Abcdef12","displayName":"A"}'
curl -s -X POST "$base/login" -H "Content-Type: application/json" -d '{"email":"a@a.com","password":"Abcdef12"}'
# guardar accessToken/refreshToken da resposta
curl -s "$base/me" -H "Authorization: Bearer $accessToken"
curl -s -X POST "$base/refresh" -H "Content-Type: application/json" -d "{`"refreshToken`":`"$refreshToken`"}"
curl -s -X POST "$base/logout" -H "Authorization: Bearer $accessToken" -H "Content-Type: application/json" -d "{`"refreshToken`":`"$refreshToken`"}"
curl -s -i -X POST "$base/refresh" -H "Content-Type: application/json" -d "{`"refreshToken`":`"$refreshToken`"}"   # -> 401
```

## 7. Riscos e armadilhas

- **`IdentityOptions` padrão do Identity Core** exige confirmação de e-mail se
  `SignInManager` for usado com certas opções — como o login aqui é manual via
  `UserManager.CheckPasswordAsync` (não `SignInManager.PasswordSignInAsync`), a spec já evita
  esse comportamento sem precisar desligar nada explicitamente. Confirmar isso ao implementar.
- **`SymmetricSecurityKey` exige >= 256 bits (32 bytes)** para HMAC-SHA256 — uma
  `Jwt:SigningKey` curta lança em runtime só quando o primeiro token é emitido/validado, não
  no build. Gerar com `[Convert]::ToBase64String((1..32 | %{Get-Random -Max 256}))` ou
  equivalente ao rodar o `user-secrets set` (P11).
- **`AddIdentityCore` vs `AddIdentity`**: usar `AddIdentityCore` (sem cookies/sessão) é
  proposital para uma API Bearer-only; `AddIdentity` traz `SignInManager` com cookies por
  padrão e mais peso do que o necessário.
- **Migration antiga apagada (P14)**: se alguém já rodou a spec 00 num banco com dados de
  teste que importam, avisar antes de `down -v`. Neste projeto ainda não há dados reais.
- **Referência nova `Infra → Application` (D-01-1)**: o `CLAUDE.md` hoje documenta "Infra
  references Domain only" — depois de implementar esta spec, esse trecho fica desatualizado
  e deve ser corrigido (ver §9). Isso não é uma regressão de arquitetura: é a direção normal
  de Clean Architecture (Infra implementa portas definidas em Application).
- **Rate limiting em `[EnableRateLimiting]`**: por padrão, o particionamento do
  `AddFixedWindowLimiter` (sem `AddPolicy`+`RateLimitPartition`) é global, não por IP — conferir
  a nota em [5.11](#511-infradependencyinjectioncs) ao implementar, senão o `429` dispara pra
  todo mundo junto.

## 8. Rollback

Cada passo é um commit isolado. Reverter com `git revert <commit>`. Para a migration:
`dotnet ef migrations remove --project EReader_API.Infra --startup-project EReader_API`
(com o banco ainda sem a migration aplicada) ou `dotnet ef database update 0` antes de
remover o arquivo.

## 9. Ganchos para as próximas specs / follow-ups

- **`CLAUDE.md`**: atualizar a seção "Architecture" para refletir que `Infra` agora referencia
  `Domain` **e** `Application` (D-01-1), e que `EReader_API.Domain/Entities/Identity/User.cs`
  foi removido (D-01-2) — a seção "Two parallel user models" deixa de existir; passa a haver
  um único modelo de usuário (`ApplicationUser`, chave `Guid`).
- **Spec 02** (Catálogo): `Book` ganha `OwnerId : Guid` referenciando `ApplicationUser.Id` —
  já compatível, já que a chave é `Guid` a partir desta spec.
- **Spec 03** (Leitura): redesenha `Bookmark`/`Highlight`/`Note` por completo, incluindo
  trocar `Id:int` → `Guid` e decidir se voltam a ter navigation property (agora que poderiam
  viver em `Infra` ou usar um `IUserRepository` novo, se necessário) — o que esta spec fez em
  P2 é o mínimo para compilar, não o desenho final dessas entidades.
- **Spec 04** (Robustez): padroniza `ProblemDetails` para os erros de auth (credenciais
  inválidas, senha fraca, etc.) — o `AuthController` desta spec pode usar tratamento de erro
  provisório até lá (ver nota em [5.14](#514-controllersauthcontrollercs-esqueleto)).
- **Spec 05** (Testes): os testes de unidade de `AuthService` ficam mais simples por ele
  depender só de interfaces (`IJwtTokenGenerator`, `IRefreshTokenStore`, `IEmailSender`,
  `UserManager<ApplicationUser>` é mockável via `Mock<UserManager<ApplicationUser>>` com um
  `IUserStore<ApplicationUser>` fake) — nenhum ganho ou perda por causa de D-01-1.
