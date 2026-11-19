# Projeto do modelo (Revit × Sistema FJA)

Desde a extensão 2.30.0 (19/11/2026), a pedido da FJA: todas as ferramentas reconhecem do mesmo jeito o projeto do sistema a que o modelo pertence, e a ligação fica gravada no próprio modelo.

## Regra
1. **Ligação gravada no modelo** (fonte principal): DataStorage com o esquema `FJA_ProjetoModelo` (GUID `5e1b7c3a-2d84-4f69-9a1e-7c40b2d8f613`; campos `id`, `codigo`, `nome`, `gravadoEm`, `gravadoPor`). Vale para a equipe depois de sincronizar com o central, com o arquivo renomeado e nas cópias locais.
2. **Sem ligação gravada**, a primeira pista que aponta para **um único** projeto vale, nesta ordem:
   1. **Número do projeto** (Informações do projeto do Revit) = código do projeto (ou começa por ele), ou = código do cliente;
   2. código no início de uma **pasta do caminho** do arquivo (a mais próxima do arquivo), ex.: `PR-PROJETOS\131 - SESC…\`; nas cópias locais, o caminho do **arquivo central**;
   3. código no início do **nome do arquivo** (`131-MO-0001…`, como antes);
   4. **código do cliente** no caminho, no título ou nas Informações do projeto;
   5. **nome do projeto** (5 letras ou mais) no caminho, no título ou nas Informações do projeto.
   Achou um projeto: a ferramenta **grava a ligação no modelo** na hora (transação "FJA: projeto do modelo") e avisa de onde veio. Várias possibilidades ou nenhuma: fica "Sem projeto", com Conectar.
3. **Sempre automático** (2.30.2, pedido da FJA): o projeto não se escolhe numa lista. Sem projeto reconhecido, o painel mostra "Sem projeto" e o botão **Conectar a um projeto…**; nas ferramentas em Python a lista só fica liberada nesse caso, para conectar. **Reconectar…** (painel Componentes, botão discreto de manutenção, com aviso) troca ou desfaz uma ligação existente.

## Onde está
- C#: `Nucleo/ProjetoDoModelo.cs` (`Ler`, `Gravar`, `GravarAgora`, `Pistas`, `Resolver`). Usado pelo painel Componentes (`EscolherProjeto`, `EscolherManual`) e pela cor da disciplina nas Legendas (`Legendas/CorSistema.cs`, só leitura: não grava).
- Python: `lib/fja_sistema.py` (`ler_projeto_modelo`, `gravar_projeto_modelo`, `pistas_projeto`, `projeto_do_modelo`), com o mesmo esquema e as mesmas pistas. Usado por Tipos › Composições e Diagnóstico.
- Sistema: a publicação para o Revit (`rvMontar`) passa a listar **todos os projetos**; os que ainda não têm especificação técnica vêm com `semEspec: true` (não há `projeto-*.json` para eles) e as ferramentas abrem com a especificação vazia.

## Próximos passos possíveis
- Ao ligar, preencher o Número do projeto do Revit com o código, se estiver vazio.
- O sistema mostrar em "Conexão com o Revit" quais modelos estão ligados a cada projeto (aviso enviado pelo Revit ao gravar a ligação).
