# 05 — Infraestrutura de testes

**Status:** Não iniciado
**Depende de:** 00 (iniciar logo após; evoluir junto com 01–04)
**Objetivo:** Montar a base de testes automatizados — projetos xUnit, PostgreSQL efêmero via
Testcontainers e `WebApplicationFactory` — para que cada spec seguinte adicione seus testes
sem retrabalho de setup.

## Escopo

**Entra:** projetos de teste, dependências, helpers compartilhados (fixtures de banco, factory
da API, builders de dados, autenticação de teste), integração no `EReader.slnx`, alvo
`dotnet test` verde no CI local.

**Não entra:** cobertura exaustiva de features (cada spec traz seus próprios casos); testes
de carga; pipeline de CI remoto (Fase 5).

## Projetos

| Projeto | Tipo | Testa |
|---|---|---|
| `EReader_API.Domain.Tests` | unit puro | invariantes de entidades, `PercentComplete`, enums/regras sem I/O |
| `EReader_API.Application.Tests` | unit com fakes/mocks | serviços de caso de uso, autorização por dono, validação, rotação de refresh token |
| `EReader_API.Api.Tests` | integração | `WebApplicationFactory` + Testcontainers PostgreSQL: fluxos HTTP ponta a ponta |

Stack: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`,
`NSubstitute` (ou `Moq`), `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`,
`Respawn` (reset de estado entre testes).

## Helpers compartilhados (`EReader_API.Api.Tests`)

- [ ] `PostgresFixture` (`ICollectionFixture`): sobe um container `postgres:17`, expõe a
      connection string, roda `Database.Migrate()` uma vez.
- [ ] `EReaderApiFactory : WebApplicationFactory<Program>`: substitui a connection string pela
      do container; troca `IEmailSender` por um fake que captura o token de reset; usa o
      `IFileStorage` local apontando para um diretório temporário por execução.
- [ ] `Respawn` para limpar as tabelas (exceto migrations) entre testes.
- [ ] `AuthHelper`: `RegisterAndLoginAsync(client)` → devolve `HttpClient` com o Bearer já
      setado + o `userId`.
- [ ] Builders: `BookBuilder`, `pdf de teste` mínimo válido (`%PDF-...`) embutido como
      recurso.
- [ ] Expor a classe `Program` para o `WebApplicationFactory` (`public partial class Program {}`
      no fim de `Program.cs`).

## Tarefas

- [ ] Criar os três projetos em `tests/` e adicioná-los ao `EReader.slnx`.
- [ ] Adicionar os pacotes NuGet listados.
- [ ] Implementar os helpers acima.
- [ ] `EReader_API.Domain.Tests`: primeiro teste real — cálculo de `PercentComplete`
      (0 quando `TotalPages` nulo/zero; arredondamento; limites).
- [ ] `EReader_API.Application.Tests`: primeiro teste — `AuthService` rejeita credenciais
      inválidas e rotaciona refresh token.
- [ ] `EReader_API.Api.Tests`: smoke — `GET /health/ready` → 200; register→login→`GET /me`.
- [ ] Documentar no `README`/`CLAUDE.md`: `dotnet test` exige Docker em execução (Testcontainers).
- [ ] (Opcional) `coverlet.collector` + relatório de cobertura.

## Convenções de teste

- Nome: `Metodo_Cenario_ResultadoEsperado`.
- Integração: uma classe por recurso (`BooksEndpointsTests`, `AuthEndpointsTests`, ...).
- Sem `Thread.Sleep`; usar polling/awaits determinísticos.
- Cada teste cria seus próprios dados; nada de dependência de ordem.
- Testes de unidade não tocam banco, disco nem rede.

## Critérios de aceite

- `dotnet test` (com Docker ativo) roda os três projetos e passa.
- Rodar a suíte duas vezes seguidas dá o mesmo resultado (isolamento via Respawn).
- Um teste de integração consegue: registrar usuário, autenticar, fazer upload de PDF,
  baixar com `Range`, e apagar — tudo pela API HTTP real.
- O tempo da suíte de integração local fica em minutos, não dezenas de minutos (container
  reutilizado por coleção).

## Arquivos afetados

- `EReader.slnx`
- `tests/EReader_API.Domain.Tests/*` (novo)
- `tests/EReader_API.Application.Tests/*` (novo)
- `tests/EReader_API.Api.Tests/*` (novo, inclui fixtures/helpers)
- `EReader_API/Program.cs` (`public partial class Program`)
- `CLAUDE.md` / `README` (nota sobre Docker para `dotnet test`)
