# Ferramentas FJA (pyRevit)

Extensão pyRevit da FJA: aba **Ferramentas FJA** no Revit (2024 e 2027).

Esta pasta é a **fonte** da extensão. O projeto das ferramentas ligadas ao sistema está em `docs/revit-tipos-sincronizados.md`.

## Conteúdo atual (extensão 2.3.0)
- Legendas › **Legenda LUM** — legenda de luminárias.
- Legendas › **Legenda ELE** — legenda de pontos elétricos.
- Nomes e Parâmetros › **Renomear Tipos** — nome do tipo = "Type Mark - Descrição".
- Tipos › **Nova composição** — cria/atualiza tipos de parede, piso, forro e cobertura a partir das composições do Sistema FJA (lógica em `lib/fja_sistema.py`).
- Suíte › **Atualizar** — baixa a versão mais recente publicada pelo sistema (banco: `revitPub/extensao-versao` e `revitPub/extensao`, gravados por `extDbPublicar` na página Firebase), grava na pasta da extensão em uso (espelhando, menos `.pyc`) e recarrega o pyRevit (lógica em `lib/fja_suite.py`).
- Links › **Verificar alterações** — compara cada cópia registrada com a origem no link (versão carregada): alterado no link (posição, rotação, tipo, espelhamento, nível, parâmetros do módulo, link movido), alterado no projeto (cópia mexida desde a última sincronização), apagado no link, novo no link, link ausente. Ações em lote: atualizar pelo link (move/gira/troca o tipo no lugar; espelhamento, nível, orientação de face ou tipo ausente → copia de novo), aceitar como está, apagar cópias órfãs, copiar novos.
- Links › **Copiar do link** — copia em massa elementos de um link para o modelo, na mesma posição, por módulo (lógica em `lib/fja_links.py`, módulos em `lib/fja_links_modulos.json`). Ver abaixo.

Origem: cópia de `%APPDATA%\pyRevit\Extensions\Ferramentas-FJA.extension` de um computador do escritório (03/11/2026).

## Distribuição
- **Pelo sistema (desde 06/11/2026):** Biblioteca › Ferramentas › Extensão do Revit › "Baixar o instalador". O .zip traz a extensão, `instalar.cmd` e um LEIA-ME. O instalador (`revit/instalar.ps1`, embutido por `scripts/embutir-revit.py`) copia a extensão para a pasta compartilhada do G: com `robocopy /MIR` (não substitui versão mais nova), cadastra a pasta em `pyRevit_config.ini` ([core] userextensions; cópia anterior em `.antes-fja`) e move a cópia local antiga de `%APPDATA%\pyRevit\Extensions` para `%APPDATA%\pyRevit\_backup-extensoes`. Sem o G:, instala em `%APPDATA%\pyRevit\Extensions`. Testado no Windows com G: e APPDATA simulados (variáveis `FJA_TESTE_REVIT` e `FJA_TESTE_APPDATA`).
- Cópia de uso no G:: `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit\Ferramentas-FJA.extension` (em uso desde 03/11/2026 no primeiro computador).
- Cada computador aponta o pyRevit para `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit` (pyRevit › Settings › Custom Extension Directories, ou `pyrevit extensions paths add "<pasta>"`) e remove a cópia local de `%APPDATA%\pyRevit\Extensions` para não carregar a aba duas vezes.
- Para atualizar: alterar aqui, commit, copiar a pasta para o G: e recarregar o pyRevit (Reload).

## Copiar do link (desde 06/11/2026)
- Módulos: **Equipamentos** (liberado: mobiliário, marcenaria, equipamentos, aparelhos sanitários, luminárias, tomadas e demais dispositivos, sprinklers, terminais de ar), **Estrutura**, **Arquitetura** e **Encaminhamentos** (ainda "em breve"). As categorias de cada módulo ficam em `lib/fja_links_modulos.json`; cada categoria pertence a um módulo só.
- Janela: escolha do link e do módulo; lista agrupada por categoria · família · tipo · nível, com filtros e situação (novos / já copiados / precisam de hospedeiro); workset de destino; "Escolher no modelo…" para selecionar elementos do link na vista (ou selecionar com Tab antes de abrir). Sem linhas selecionadas, copia os novos de todas as linhas visíveis.
- Cópia com `ElementTransformUtils.CopyElements` usando a posição da instância do link: mesma posição e rotação, inclusive espelhados; tipos com o mesmo nome usam o tipo do modelo. Famílias **face based** vêm **sem hospedeiro**, no mesmo lugar. Famílias hospedadas em parede/forro/piso (não face based) não copiam e aparecem como "precisam de hospedeiro". Componentes aninhados compartilhados vêm com o pai; opções de projeto secundárias ficam de fora.
- O registro guarda também a posição do link (`lt`), a posição da cópia (`c`) e os parâmetros do módulo (`p`, lista `parametros` em `fja_links_modulos.json`; padrão Marca e Comentários) — base do Verificar alterações. Cópias feitas antes da 2.3.0 são comparadas sem esses campos (só posição, tipo e nível).
- Cada cópia recebe o registro `FJA_CopiaLink` (Extensible Storage): módulo, link, UniqueId da instância do link, UniqueId do elemento de origem, foto do estado da origem (família, tipo, posição e orientação no link, espelhado, nível), data e usuário. Serve para não copiar duas vezes e é a base do monitoramento (próxima etapa: "Verificar alterações").
- Testado no Revit 2027 com modelos temporários (link deslocado e girado, nível com cota diferente, espelhado, face based em laje e em parede, tomada hospedada em parede).

## Dados do sistema
O sistema publica em `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT` (desenvolvimento: `REVIT\DEV`):
- `biblioteca.json` — siglas, itens centrais (com `camadas` e `rvCat` nos itens de composição) e lista de projetos.
- `projeto-<código>.json` — especificação de cada projeto (keynote, item central, descrição).

Cada publicação cria o arquivo novo e manda o anterior para a lixeira do Drive. Se houver mais de um arquivo com o mesmo nome (ex.: `biblioteca (1).json`), usar o mais recente.

## Convenções dos scripts
- IronPython (pyRevit), `# -*- coding: utf-8 -*-`, textos em português.
- `bundle.yaml` com `title`/`tooltip` em `pt_br`.
- Elementos criados por uma ferramenta são marcados com Extensible Storage (ex.: `FJA_LegendaELE`); os tipos ligados ao sistema usarão o esquema `FJA_SistemaTipo`.

## Observações de implementação
- `lib/fja_sistema.py` não depende do pyRevit (recebe o documento): dá para testar pelo conector do Revit com `sys.path` apontando para a pasta `lib`.
- Para trocar camadas, parte-se da estrutura do tipo-base (`GetCompoundStructure` + `SetLayers`): criar a estrutura do zero falha em pisos ("wrong EndCap condition").
- Ao enviar arquivos para o G: pela ponte do Claude, usar um nome de origem novo a cada envio (reenviar pelo mesmo caminho não substituiu o conteúdo).

## Como apontar um computador para o G:
1. Fechar o Revit.
2. Em `%APPDATA%\pyRevit\pyRevit_config.ini`, seção `[core]`: `userextensions = ["G:\\Drives compartilhados\\BI-BIBLIOTECA\\SISTEMA FJA\\REVIT\\pyRevit"]` (ou pyRevit › Settings › Custom Extension Directories).
3. Tirar `Ferramentas-FJA.extension` de `%APPDATA%\pyRevit\Extensions` (mover para outra pasta), para a aba não aparecer duas vezes.
4. Abrir o Revit (ou pyRevit › Reload).
