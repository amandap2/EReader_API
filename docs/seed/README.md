# Seed da biblioteca pública

`public-domain.json` é o manifesto lido por `PublicLibrarySeeder` (Infra) no startup em
ambiente `Development`. Para cada item cujo `sourceFile` já exista em `files/`, o seeder cria
um `Book` com `Source = PublicDomain`. Itens cujo arquivo não esteja presente são pulados (log
de warning), não travam o startup.

## Como popular `files/`

Os PDFs em si **não** entram no controle de versão (ver `.gitignore`). Baixe manualmente de uma
fonte de domínio público e salve com o nome exato indicado em `sourceFile`:

- **Dom Casmurro** (Machado de Assis) → `files/dom-casmurro.pdf`
- **Memórias Póstumas de Brás Cubas** (Machado de Assis) → `files/memorias-postumas-bras-cubas.pdf`

Fontes sugeridas (obras em domínio público no Brasil, autor falecido há mais de 70 anos):

- [Domínio Público (governo federal)](http://www.dominiopublico.gov.br/)
- [Project Gutenberg](https://www.gutenberg.org/) (buscar pelo título; exportar como PDF)

## Adicionando mais títulos

Adicione uma entrada em `public-domain.json` (`title`, `author`, `language`, `publishedDate`,
`sourceFile`) e coloque o PDF correspondente em `files/`. O seeder identifica duplicatas por
`title`+`author` exatos — não roda duas vezes para o mesmo livro.
