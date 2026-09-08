# Specs de implementação — EReader API

Cada arquivo descreve uma unidade de trabalho independente, na ordem de execução. O
documento de referência com o modelo de domínio e a API completa é
[`../ESPECIFICACAO-BACKEND.md`](../ESPECIFICACAO-BACKEND.md).

| # | Spec | Objetivo | Depende de |
|---|---|---|---|
| 00 | [Fundação](00-fundacao.md) | Ligar os 4 projetos, `Program.cs`, DI, `DbContext`, configuração, 1ª migration, corrigir `Dockerfile` | — |
| 01 | [Identidade e autenticação](01-identidade.md) | ASP.NET Identity (`Guid`), JWT + refresh tokens, endpoints `/api/auth` | 00 |
| 02 | [Catálogo e upload de livros](02-catalogo-e-upload.md) | Redesenho de `Book`, CRUD, upload PDF, `IFileStorage`/`LocalFileStorage`, streaming com `Range`, seed público | 00, 01 |
| 03 | [Leitura (core domain)](03-leitura.md) | `ReadingProgress`, `Bookmark`, `Highlight`, `Note`, `GET /api/library` | 00, 01, 02 |
| 04 | [Robustez e operação](04-robustez.md) | ProblemDetails, validação, rate limiting, health checks, extração de `PageCount`/capa, índices | 01, 02, 03 |
| 05 | [Infraestrutura de testes](05-testes.md) | Projetos xUnit, Testcontainers PostgreSQL, `WebApplicationFactory` | 00 (usada continuamente) |

## Planos de implementação

Planos passo a passo para executar cada spec ficam em [`plans/`](plans/):

- [`plans/00-fundacao.md`](plans/00-fundacao.md)

## Convenções

- **Status** de cada spec: `Não iniciado` → `Em andamento` → `Concluído`. Atualizar no topo do arquivo.
- Identificadores de código em inglês; texto de negócio em pt-BR.
- Toda spec traz **Critérios de aceite** verificáveis — só marcar `Concluído` quando todos passarem.
- Migrations: uma por spec que altera o schema, nomeada conforme indicado.
- IDs de entidades: `Guid`. Datas: `DateTime` em UTC (ISO 8601 no JSON).

## Ordem sugerida

`00 → 01 → 02 → 03 → 04`, com `05` iniciada logo após `00` e mantida em paralelo (cada spec
seguinte adiciona seus próprios testes).
