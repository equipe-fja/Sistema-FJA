# Sistema FJA

Sistema interno da FJA (Filipe Jacopucci Arquitetura) para gestão de projetos: projetos e catálogos (disciplinas, parceiros), comentários, pendências, equipamentos, relação de documentos, cronograma, atas de reunião, croquis, especificações e sugestões.

O sistema é uma página única publicada como artifact no claude.ai, com banco de dados próprio e acesso ao Google Drive/Sheets pelos conectores de quem usa.

## Ambientes

| Ambiente | Link | Uso |
|---|---|---|
| Oficial | https://claude.ai/artifact/1Li4gBiABfLSuY1mXQCY9b | Usado pela equipe. Só é atualizado quando o Filipe pede. |
| Desenvolvimento | https://claude.ai/artifact/XDUaQVoBGGskRBh1mViAGr | Testes. Cabeçalho listrado em amarelo. Toda mudança entra aqui primeiro. |

Cada ambiente tem o próprio banco. As planilhas e pastas do Drive são reais e compartilhadas pelos dois.

## Estrutura do repositório

```
src/sistema-fja.html   fonte única (sempre com const AMB='oficial')
scripts/gerar.sh       gera dist/oficial.html e dist/dev.html (troca AMB e o título)
docs/                  notas técnicas, decisões e planos
CHANGELOG.md           histórico das versões publicadas na oficial
CLAUDE.md              instruções de trabalho para o Claude
```

`dist/` não é versionado.

## Fluxo de trabalho

1. Alterar `src/sistema-fja.html`.
2. `./scripts/gerar.sh`.
3. Publicar `dist/dev.html` no artifact de desenvolvimento e testar.
4. Commit descrevendo a mudança.
5. Quando o Filipe pedir ("pode atualizar a oficial"): publicar `dist/oficial.html` no artifact oficial, registrar no `CHANGELOG.md` (com o commit) e avançar a branch `oficial` para esse commit.

## Branches

- `main`: desenvolvimento (o que está na dev).
- `oficial`: sempre aponta para o código exato que está publicado na oficial.

O `CHANGELOG.md` registra, para cada versão da oficial, o commit correspondente. Para voltar o código a uma versão: `git checkout <commit> -- src/sistema-fja.html`.
(As tags do git não puderam ser enviadas pelo acesso que o Claude usa; quem quiser pode criar tags/releases pela própria página do GitHub.)
