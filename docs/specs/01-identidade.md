# 01 — Identidade e autenticação

**Status:** Concluído
**Depende de:** 00
**Objetivo:** Implementar o contexto `Identity`: ASP.NET Core Identity com chave `Guid`,
emissão de JWT + refresh tokens rotacionados, e os endpoints `/api/auth` (register, login,
refresh, logout, forgot/reset password, `GET`/`DELETE /me`).

## Contexto

Notas originais — Feature 1: "Criar usuários: login, register, reset password, excluir
usuário, deslogar". Decisões D-1 (chave `Guid`) e D-2 (remover `Domain/Entities/Identity/User`
com `Password`).

## Escopo

**Entra:** `ApplicationUser : IdentityUser<Guid>`, `IdentityDbContext<…,Guid>`, política de
senha, geração/validação de JWT, tabela `RefreshTokens` + rotação, `IAuthService`,
`AuthController`, `IEmailSender` (impl fake em Dev), rate limiting em `/api/auth/*`,
migration `Identity`.

**Não entra:** perfis ricos, papéis/roles além do necessário, confirmação de e-mail
obrigatória (pode ficar como flag desligada), OAuth/social login.

## Modelo e contratos

```csharp
// Infra/Identity
public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// Infra/Identity/RefreshToken.cs  (tabela RefreshTokens)
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";   // hash, nunca o token em claro
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}

// Application/Identity
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

public record AuthResult(string AccessToken, string RefreshToken, int ExpiresInSeconds);
```

Configuração `Jwt`:

```jsonc
"Jwt": {
  "Issuer": "ereader-api",
  "Audience": "ereader-web",
  "SigningKey": "",              // via user-secrets/env, >= 32 bytes
  "AccessTokenMinutes": 15,
  "RefreshTokenDays": 14
}
```

Claims do access token: `sub` = `ApplicationUser.Id`, `email`, `name` = `DisplayName`,
`jti`. Assinatura HMAC-SHA256.

## Endpoints — `/api/auth`

| Método | Rota | Auth | Corpo → Resposta |
|---|---|---|---|
| POST | `/register` | anônimo | `{ email, password, displayName }` → `201` + `AuthResult` |
| POST | `/login` | anônimo | `{ email, password }` → `200` + `AuthResult` |
| POST | `/refresh` | anônimo | `{ refreshToken }` → `200` + `AuthResult` (rotaciona) |
| POST | `/logout` | Bearer | `{ refreshToken }` → `204` (revoga) |
| POST | `/forgot-password` | anônimo | `{ email }` → `202` (sempre 202, não revela existência) |
| POST | `/reset-password` | anônimo | `{ email, token, newPassword }` → `204` |
| GET | `/me` | Bearer | → `{ id, email, displayName, createdAt }` |
| DELETE | `/me` | Bearer | → `204` (hard delete; spec 02/03 encadeiam limpeza) |

## Tarefas

- [x] Trocar `ApplicationDbContext` para `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.
- [x] `ApplicationUser` herda de `IdentityUser<Guid>`; adicionar `DisplayName`, `CreatedAt`.
- [x] Remover `EReader_API.Domain/Entities/Identity/User.cs` (e ajustar usos — nenhuma outra
      entidade deve depender dele; entidades futuras usam `Guid UserId`).
- [x] `DbSet<RefreshToken>` + `IEntityTypeConfiguration<RefreshToken>` (índice em `TokenHash`,
      índice em `UserId`).
- [x] `AddInfrastructure`: `AddIdentityCore<ApplicationUser>()` (+ `AddRoles`, stores EF,
      token providers) e opções de senha/lockout.
- [x] `AddInfrastructure`/`Program.cs`: `AddAuthentication(JwtBearerDefaults...)`
      `.AddJwtBearer(...)` com `TokenValidationParameters` a partir de `Jwt`.
- [x] `Program.cs`: `AddAuthorization()`, `app.UseAuthentication()` antes de `UseAuthorization()`.
- [x] `JwtTokenGenerator` (Infra) implementando uma interface em `Application`.
- [x] `IRefreshTokenStore` / repositório: criar, achar por hash, revogar, revogar todos do usuário.
- [x] `IEmailSender` (Application) + `LogEmailSender` (Infra, loga o link de reset em Dev).
- [x] `AuthService` implementando `IAuthService` (validação, hashing via Identity, rotação de refresh).
- [x] `AuthController` com os 8 endpoints; `[Authorize]` onde indicado.
- [x] Rate limiting: policy `"auth"` (ex. 10 req/min por IP) aplicada a `/api/auth/*`.
- [x] Helper `ClaimsPrincipal.GetUserId()` → `Guid`.
- [x] Migration `AddIdentityAndRefreshTokens`.
- [ ] Testes (ver spec 05): unit de `AuthService` (rotação, credenciais inválidas, reset) +
      integração do fluxo register→login→refresh→logout e delete. **Pendente** — validado
      manualmente via `curl` (ver plano de implementação), mas sem projeto de testes ainda
      (spec 05).

## Regras

- `forgot-password` responde `202` mesmo para e-mail inexistente.
- `refresh` inválido/expirado/revogado → `401`; ao usar um refresh já rotacionado, revogar
  toda a cadeia daquele usuário (detecção de reuso).
- `DELETE /me` faz hard delete do `ApplicationUser`; specs 02 e 03 adicionam `ON DELETE
  CASCADE`/limpeza de arquivos para os dados dependentes.
- Senha: mínimo 8 caracteres, ao menos 1 dígito e 1 maiúscula (ajustável em config).

## Critérios de aceite

- Fluxo completo via HTTP: registrar → logar → chamar `GET /me` com o Bearer → `refresh`
  → `logout` → o refresh antigo passa a retornar `401`.
- Endpoint protegido sem token → `401`; com token de outro contexto/expirado → `401`.
- `reset-password` com token válido troca a senha; token usado duas vezes → `400`.
- `DELETE /me` remove o usuário; novo `login` com as credenciais → `401`.
- `> 10` logins/min do mesmo IP → `429`.
- Migration aplica e reverte sem erro.

## Arquivos afetados

- `EReader_API.Infra/Context/ApplicationDbContext.cs`
- `EReader_API.Infra/Identity/ApplicationUser.cs`, `RefreshToken.cs`
- `EReader_API.Infra/Identity/JwtTokenGenerator.cs`, `RefreshTokenStore.cs`, `LogEmailSender.cs`
- `EReader_API.Infra/Context/Configurations/RefreshTokenConfiguration.cs`
- `EReader_API.Infra/DependencyInjection.cs`
- `EReader_API.Application/Identity/*` (interfaces, DTOs, `AuthService`)
- `EReader_API.Application/DependencyInjection.cs`
- `EReader_API/Controllers/AuthController.cs` (novo)
- `EReader_API/Program.cs`
- `EReader_API/appsettings*.json` (`Jwt`)
- `EReader_API.Domain/Entities/Identity/User.cs` (removido)
- `EReader_API.Infra/Migrations/*`
