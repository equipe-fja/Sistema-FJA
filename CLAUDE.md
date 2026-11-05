# Instruções para trabalhar no Sistema FJA

Responder ao usuário em português. O usuário é a equipe da FJA (a FJA decide quando a oficial é atualizada). Nas respostas, não citar nomes de pessoas: dizer "a FJA".

## Regras de ouro
- A fonte é `src/sistema-fja.html`, sempre com `const AMB='oficial'` e `<title>Sistema FJA</title>`. Nunca editar `dist/`.
- Toda mudança vai primeiro para a **dev**. A **oficial** só é publicada quando a FJA pede ("pode atualizar a oficial", ou a sigla "PAO").
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
- A oficial e a dev rodam no contrato 0.2.92.

## Notas técnicas
- Ver `docs/notas-tecnicas.md` (banco, módulos, Drive/Sheets, PDF, Windows).
- `confirm()`/`alert()` são bloqueados no claude.ai: usar `confirmar()` / `pedirNome()`.
- Planos pendentes em `docs/` (ex.: `docs/backup-diario.md`).

## Firebase — a OFICIAL desde 05/11/2026 (virada)
- A oficial do Claude (artifact acima) ficou somente leitura (v102, `MIGRADO_PARA`). Não publicar mais nela. A dev continua no Claude.
- Promover para a oficial = `git push origin main:oficial` (publica sistema-fja.web.app) + CHANGELOG. `firebase/virada.json` = true liga as rotinas automáticas na oficial Firebase.
- Oficial: https://sistema-fja.web.app (projeto sistema-fja; publica com push na branch `oficial`). Homologação: https://sistema-fja-homolog.web.app (publica a cada push na `main`; importa o último backup da oficial todo dia às 04h). Gerados de `src/` com `AMB='firebase'` + `firebase/shim.html` (login com e-mail e senha; contas em acessos/<uid> com nível admin/usuario, criadas em Manutenção › Usuários e acesso com senha provisória; assets e mcp ainda não). Administradores iniciais: `firebase/admin-inicial.json` + push.
- Importar manualmente: `firebase/importar.json` {"projeto":"homolog"|"oficial","pasta":"<id>"|"ultimo"} + push.
- Resultado das execuções: branch `firebase-log` (`logs/publicar-homolog.txt`, `logs/publicar-oficial.txt`, `logs/importar.txt`). A chave fica só no segredos `FIREBASE_SERVICE_ACCOUNT` (oficial) e `FIREBASE_SERVICE_ACCOUNT_HOMOLOG` do GitHub.
