# Ferramentas FJA (pyRevit)

Extensão pyRevit da FJA: aba **Ferramentas FJA** no Revit (2024 e 2027).

Esta pasta é a **fonte** da extensão. O projeto das ferramentas ligadas ao sistema está em `docs/revit-tipos-sincronizados.md`.

## Conteúdo atual (v1.2.0)
- Legendas › **Legenda LUM** — legenda de luminárias.
- Legendas › **Legenda ELE** — legenda de pontos elétricos.
- Nomes e Parâmetros › **Renomear Tipos** — nome do tipo = "Type Mark - Descrição".
- Tipos › **Nova composição** — cria/atualiza tipos de parede, piso, forro e cobertura a partir das composições do Sistema FJA (lógica em `lib/fja_sistema.py`).

Origem: cópia de `%APPDATA%\pyRevit\Extensions\Ferramentas-FJA.extension` de um computador do escritório (03/11/2026).

## Distribuição (proposta)
- Cópia de uso no G:: `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit\Ferramentas-FJA.extension` (já copiada em 03/11/2026).
- Cada computador aponta o pyRevit para `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit` (pyRevit › Settings › Custom Extension Directories, ou `pyrevit extensions paths add "<pasta>"`) e remove a cópia local de `%APPDATA%\pyRevit\Extensions` para não carregar a aba duas vezes.
- Para atualizar: alterar aqui, commit, copiar a pasta para o G: e recarregar o pyRevit (Reload).

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
