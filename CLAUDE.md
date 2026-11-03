# Instruções para trabalhar no Sistema FJA

Responder ao usuário em português. O usuário é a equipe da FJA (o Filipe decide quando a oficial é atualizada).

## Regras de ouro
- A fonte é `src/sistema-fja.html`, sempre com `const AMB='oficial'` e `<title>Sistema FJA</title>`. Nunca editar `dist/`.
- Toda mudança vai primeiro para a **dev**. A **oficial** só é publicada quando o Filipe pede ("pode atualizar a oficial").
- Antes de promover: listar o que entra (`git log origin/oficial..main`) e avisar antes de qualquer migração que altere dados existentes.
- Depois de publicar na oficial: entrada no `CHANGELOG.md` (versão vNN + commit), commit, e `git push origin main:oficial` (a branch `oficial` sempre aponta para o que está publicado). Tags não passam pelo proxy (push de tag é recusado).
- Commit a cada mudança publicada na dev, com mensagem em português.

## Artifacts
| | slug (UUID, usar este nas ferramentas) | link |
|---|---|---|
| Oficial | `02c07015-c828-4d0d-b8fe-2b78db996436` | https://claude.ai/artifact/1Li4gBiABfLSuY1mXQCY9b |
| Dev | `f4b0144e-7fd7-47ac-8d2a-32f38daf3983` | https://claude.ai/artifact/XDUaQVoBGGskRBh1mViAGr |

- Baixar o código publicado para disco: `read_file` com `paths:["index.html"]` (o arquivo é salvo em disco). Antes de começar, conferir se o publicado bate com `dist/` gerado do repositório; se não bater, alguém publicou fora do repositório — trazer a diferença para o git primeiro.
- Publicar: `./scripts/gerar.sh` e depois `publish_files` com `{as:"index.html", path:".../dist/dev.html"}`, `slug` e `base_version` atual. Recarregar com Ctrl+F5.
- Alterar artifacts exige sessão com acesso de rede; sem ele, só artifacts criados na própria sessão podem ser alterados.
- A oficial roda no contrato 0.2.91 e a dev no 0.2.92.

## Notas técnicas
- Ver `docs/notas-tecnicas.md` (banco, módulos, Drive/Sheets, PDF, Windows).
- `confirm()`/`alert()` são bloqueados no claude.ai: usar `confirmar()` / `pedirNome()`.
- Planos pendentes em `docs/` (ex.: `docs/backup-diario.md`).
