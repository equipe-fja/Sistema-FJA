# Pranchas › Exportar (2.33.0, 23/11/2026; regra do nome em 2.35.0)

Exporta as pranchas em **PDF e DWG ao mesmo tempo**, um arquivo por prancha, para a pasta escolhida. Comando C# `ExportarPranchas` (`revit/dotnet/FJA.Revit/Comandos/ExportarPranchas.cs`; lógica em `Pranchas/Exportacao.cs`, janela em `Pranchas/JanelaExportar.cs`). Painel novo **Pranchas**: depois de atualizar, fechar e abrir o Revit.

## Decisões da FJA (23/11/2026)
- Nome do arquivo = o campo **"Prancha"** do carimbo, montado pelos mesmos parâmetros, separados por "-":
  `Project Number - Etapa (sigla) - Disciplina (abreviação) - Tipo de documento - Sheet Number - Current Revision` → `128-AP-ARQ-DT-A002-R00.pdf` / `.dwg`.
- Um arquivo por prancha (PDF único com todas fica para depois, se precisar).
- DWG pela configuração de exportação **FJA TRUE COLOR**: as opções padrão do Revit, com cores **True Color (RGB)** e **sem referências externas** (as vistas da prancha vão dentro do próprio DWG). Se o projeto não tiver essa configuração, a ferramenta cria ao exportar.
- Painel novo **Pranchas**.

## Como funciona
- **Lista:** todas as folhas do projeto (menos os espaços reservados), em ordem de número (A2 antes de A10), com o nome do arquivo ao lado. Vêm marcadas as folhas selecionadas no navegador de projeto; sem seleção, a folha ativa. Filtro por texto (número, nome da folha ou do arquivo) e por disciplina; "Marcar as visíveis" e "Desmarcar todas".
- **Conjuntos de folhas (2.34.0):** lista dos sheet sets do projeto; escolher um mostra só as folhas dele, todas marcadas ("Todas as folhas" volta a mostrar tudo sem mudar as marcas).
- **Regra do nome (2.35.0; editor refeito em 2.40.0):** a API do Revit não dá acesso aos parâmetros de um rótulo do carimbo, então a composição do campo "Prancha" é uma **regra** editável na janela ("Nome do arquivo"): uma linha por parte, que pode ser um **parâmetro** ou um **texto fixo** (digitado na própria linha, igual em todas as pranchas; vazio não entra). **Arrastar** a linha muda a ordem; ao lado do ✕ (tirar) fica o **separador que vem depois daquela parte** (-, _, ., espaço ou nenhum; oculto na última). O **+** abaixo da última linha abre o menu com "Texto fixo" e os parâmetros ainda fora do nome (da folha e das informações do projeto criados no projeto, mais Project Number, Sheet Number e Current Revision); a parte nova entra no fim. Parâmetro em branco numa folha sai do nome junto com o separador dele. A lista de pranchas se atualiza na hora. O botão Padrão saiu (2.40.0; um padrão por projeto fica para depois). A regra é **gravada no modelo** (DataStorage `FJA_ExportarPranchas`) sempre que muda, também ao fechar a janela sem exportar, e vale para toda a equipe naquele projeto: o projeto segue sempre a última configuração.
  - **Padrão FJA:** Project Number - Etapa (sigla) - Disciplina (abreviação) - Tipo de documento - **Trecho** - Sheet Number - Current Revision. Com Trecho: `131-LE-ARQ-DT-HUB-H002-R00`; sem Trecho (projeto que não tem o parâmetro ou folha com ele vazio): `128-AP-ARQ-DT-A002-R00`.
  - **Todos os parâmetros são opcionais** (decisão da FJA, 2.36.0): parâmetro em branco sai do nome (sem "--") e nenhuma prancha fica bloqueada. O aviso vem **só ao clicar em Exportar**: lista as pranchas marcadas com parâmetros em branco (e o nome que vai sair) e pergunta se é para exportar assim mesmo. Na lista, o que está em branco aparece só na dica da coluna Arquivo.
- **Parâmetros:** Project Number nas informações do projeto; Sheet Number e Current Revision da folha; os demais pelo nome, na folha ou, se não estiverem nela, nas informações do projeto. Nomes repetidos entre folhas aparecem em laranja (a segunda substitui a primeira). Caracteres que o Windows não aceita viram "_".
- **PDF:** exportação nativa do Revit (`PDFExportOptions`), uma chamada por folha (`Combine` + `FileName`), papel do tamanho da folha, zoom 100 %, sem limites de recorte, caixas de escopo, planos de referência e marcas de vista sem referência. Cor: colorido (padrão), tons de cinza ou preto.
- **DWG:** `Document.Export(pasta, nome, [folha], opções)` com a configuração escolhida (padrão FJA TRUE COLOR: `ExportColorMode.TrueColorPerView`, `MergedViews = true`). Outra configuração do projeto pode ser escolhida na lista.
- **Pasta:** escolhida na hora e lembrada **por projeto** (caminho do modelo central, ou do arquivo). Opção de subpastas `PDF\` e `DWG\`.
- **Pasta nova (2.34.0):** opção "Criar pasta nova" dentro da pasta de destino, com o nome **AA-MM-DD + descrição** (data de hoje), no modelo das emissões da FJA: `26-02-02 EMISSÃO FINAL EXECUTIVO - ARQUITETURA - 1ª PARTE`. A descrição é lembrada por projeto; se a pasta já existir, é usada.
- **Arquivo que já existe:** substituir (padrão; apaga e exporta de novo — se estiver aberto em outro programa, vai para os erros) ou pular.
- **Progresso:** janela com a prancha atual e Cancelar (para depois da prancha em andamento). No fim, relatório com quantos PDFs e DWGs saíram, pulados, configuração criada e erros; abre a pasta (opção).
- **Preferências por pessoa:** `%APPDATA%\FJA\exportar-pranchas.json` (PDF/DWG, cor, configuração, subpastas, substituir, abrir a pasta, pasta nova, e as pastas e descrições por projeto).
- **Janela sem textos explicativos** (pedido da FJA, 2.34.0): só os controles; detalhes nas dicas (tooltips).
- **Registrar emissão no sistema (2.38.0):** opção desmarcada por padrão. Marcada, lê o projeto do modelo no sistema e mostra: emissão (Nova ou uma em Rascunho), disciplinas do projeto (já marcadas as das pranchas marcadas) e destinatários (envolvidos do projeto). A descrição da pasta nova vale também para a emissão. A pasta nova passa a ser `AA-MM-DD EMISSÃO - Descrição`. Depois de exportar, grava a emissão em Rascunho com a pasta e os arquivos; o Emitir é no sistema. Detalhes em `docs/emissoes.md`.

## Próximas etapas (a combinar)
- Revisões anteriores da mesma prancha na pasta (ex.: `…-R00` quando sai a `R01`): avisar e mover para `_SUPERADOS`.
- PDF único com as pranchas marcadas.
- Ligação com o Sistema FJA: conferir as pranchas contra a Relação de documentos e registrar a emissão.
