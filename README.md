# EReader_API

## Testes

`dotnet test EReader.slnx` roda os três projetos de teste (`tests/EReader_API.Domain.Tests`,
`tests/EReader_API.Application.Tests`, `tests/EReader_API.Api.Tests`). `Api.Tests` exige **Docker
em execução** — sobe um `postgres:17` efêmero via Testcontainers (um container reutilizado para
toda a coleção de testes de integração) e reseta o banco com Respawn antes de cada teste.

Ver `CLAUDE.md` para a arquitetura e o estado de cada spec.