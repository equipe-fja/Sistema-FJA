# Cotagem (Ferramentas FJA › Cotagem › Cotar vistas)

Comando C# `CotarVistas` (`revit/dotnet/FJA.Revit/Cotagem/*.cs`), desde a extensão 2.18.0 (13/11/2026).

## Como funciona
- **Vistas:** as selecionadas no navegador de projeto (folhas entram com todas as vistas colocadas nelas); sem seleção, a vista ativa (ou as vistas da folha ativa). Aceita planta, teto, planta de área, corte, elevação e detalhe.
- **Tipo da vista** (`Regras.Sugerir`), nesta ordem:
  1. o tipo escolhido antes para a vista (gravado nela, Extensible Storage `FJA_CotagemVista`);
  2. o View Template: correspondência exata em `lib/fja_cotagem.json` (`templates`), senão palavras-chave no nome do template;
  3. palavras-chave no nome da vista;
  4. o tipo da vista no Revit (planta → planta baixa, teto → teto refletido, corte → corte, elevação → fachada).
  A janela mostra a sugestão (com a origem na dica) e deixa trocar; "Aplicar a todas…" muda as marcadas. Sem sugestão, a vista fica em "— escolher —" e é preciso escolher antes de cotar.
- **Regra** = tipo × faixa de escala (`Regras.Para`): 1:500 (≥ 1:400), 1:200 (1:150 a 1:399), 1:75 (1:41 a 1:149, vale para 1:100 e 1:50) e 1:25 (≤ 1:40).
- **O que esta versão cota:**
  | Tipo | 1:500 | 1:200 | 1:75 / 1:50 | 1:25 |
  |---|---|---|---|---|
  | Planta baixa | — | ambientes (2 dimensões) + contorno; eixos + total | panos e vãos de fachada (vão livre) + total; eixos + total | — (faces de parede, próxima versão) |
  | Corte, fachada | eixos + total; níveis + total | idem | idem | níveis + total |
  | Vista/corte interno | níveis + total | idem | idem | idem |
  | Teto, pontos, paginação, implantação | — | — | — | — |
- **Cadeias** (`Motor`): eixos e níveis retos, pela linha deles na vista (`GetCurvesInView`). Linhas verticais na tela → cadeia horizontal (em cima); horizontais → cadeia vertical (à esquerda). Posição das linhas: ver "Eixos" abaixo. Espaçamento padrão 8 mm no papel (opção da janela).
- **Cotas existentes:** antes de criar, lê todas as cotas lineares da vista; cada segmento conta sozinho. Um trecho está "já cotado" quando existe cota com os mesmos pontos de início e fim, na mesma direção (tolerância 1 mm), em qualquer posição. Só os trechos que faltam são criados, em sequências contínuas.
- **Marca:** toda cota criada recebe `FJA_Cotagem` (Extensible Storage). Modo **Complementar** (padrão) não mexe em nada existente; **Refazer as minhas** apaga só as cotas marcadas da vista e gera de novo.
- **Tipo de cota:** escolhido na janela (tipos lineares do projeto; padrão = o último usado, senão o padrão do projeto).
- **Preferências por pessoa:** `%APPDATA%\FJA\cotagem.json` (tipo de cota, espaçamento).
- Avisos do Revit durante a criação são suprimidos; o relatório final lista, por vista, cotas criadas, trechos, já cotados, refeitas e erros.

## Decisões da FJA (13/11/2026)
1. **Teto refletido:** cotas a partir do acabado.
2. **Cotas internas:** face acabada das paredes.
3. **Eixos:** cadeia em cima e à esquerda, de preferência; todos os eixos são cotados.
4. **Cor das cotas** (vermelho): vem do tipo de cota (não da impressão).
5. **View Templates da FJA** em `lib/fja_cotagem.json`: fica para depois.
6. **Escala fora da tabela:** usa a faixa mais próxima (como já é).
7. **Fachada:** nos quatro lados.
8. **Vãos:** no acabado (vão livre das portas e janelas).
9. **Ordem das linhas:** as cotas de fachada junto do edifício; os eixos por fora; as bolhas vão para fora (para cima) quando necessário.
10. **1:200:** só as dimensões gerais dos ambientes (duas perpendiculares); fora isso, as dimensões gerais do contorno do edifício, amarradas aos limites do terreno.

## Planta baixa 1:75 (1:41 a 1:149) — fachadas (2.20.0; vão livre e ordem das linhas em 2.22.0)
`Cotagem/Fachada.cs`, chamado pelo `Motor` quando a regra tem `Fachada`.
- **Paredes lidas:** as da vista, retas, alinhadas a ela (horizontais ou verticais na tela), cortadas pelo plano de corte da vista; cortina fica de fora. A geometria vem com referências (`Options.ComputeReferences`, vista ativa).
- **Parede de fachada de um lado** (em cima, embaixo, à esquerda, à direita): paralela a esse lado e sem outra parede mais para fora em pelo menos um de cinco pontos ao longo dela (10 %, 30 %, 50 %, 70 % e 90 % do comprimento). Recuos e blocos com fachadas em profundidades diferentes entram na mesma cadeia.
- **Pontos da cadeia:** faces planas verticais de qualquer parede, perpendiculares ao lado, que chegam à face externa de uma parede de fachada e são cortadas pelo plano de corte (pontas de parede e o canto externo da parede perpendicular). Paredes internas que encostam na fachada não chegam à face externa e ficam de fora.
- **Vão livre (2.22.0):** portas e janelas da parede de fachada cortadas pelo plano de corte entram pelas referências **Esquerda/Direita** da família (`GetReferences(Left/Right)`), nas posições centro ± largura/2 (largura: `DOOR_WIDTH`, `WINDOW_WIDTH` ou `FAMILY_WIDTH_PARAM`, da instância ou do tipo). As ombreiras da parede até 12 cm de cada lado do vão livre saem da cadeia. Família sem essas referências ou sem largura: fica a ombreira da parede (aviso no relatório).
- **Linhas:** a cadeia a 1 espaçamento da face externa mais saliente do lado; o total da fachada (das pontas) a 2 espaçamentos, se a cadeia tiver 3 pontos ou mais. Os quatro lados.
- **Marca:** `fachada-cima`, `fachada-baixo`, `fachada-esquerda`, `fachada-direita` e `…-total`.
- Não vale para parede inclinada ou curva.

## Planta baixa 1:200 (1:150 a 1:399) — ambientes e contorno (2.22.0)
- **Ambientes** (`Cotagem/Ambientes.cs`): para cada ambiente colocado na vista, duas cotas perpendiculares — largura e profundidade gerais (pelo retângulo que envolve o contorno do ambiente medido no acabamento, `SpatialElementBoundaryLocation.Finish`), entre as faces de parede mais próximas dos limites (até 2 cm). A cota horizontal fica a 35 % da altura do ambiente, a vertical a 35 % da largura (o rótulo no centro fica livre). Limite feito por linha de separação (sem parede): a dimensão não é criada e o relatório avisa. Já cotado = cota com os mesmos pontos **dentro do ambiente** (ambientes iguais lado a lado têm cada um a sua). Marca `ambiente`.
- **Contorno:** nos quatro lados, só o total da fachada (das pontas), a 1 espaçamento do edifício; totais iguais em lados opostos não se repetem. Marca `contorno-…`.
- **Amarração com o terreno:** próxima versão (linhas de propriedade do Revit).

## Eixos (todas as vistas; 2.22.0)
- Cadeia de eixos sempre **em cima** (eixos verticais na tela) e **à esquerda** (horizontais), com todos os eixos; total por fora da cadeia.
- **Planta com fachada cotada nesse lado:** a cadeia de eixos fica a 1 espaçamento da última linha da fachada, o total a 2; os eixos cuja ponta não chega a 1 espaçamento além do total são **estendidos nesta vista** (pontas passam para 2D, `SetCurveInView`), para as bolhas ficarem por fora. O relatório conta os eixos estendidos. A bolha não troca de lado (para isso, Trocar lado).
- **Sem fachada nesse lado** (cortes, fachadas, plantas sem paredes): logo para dentro das pontas — total a 1 espaçamento delas, cadeia a 2.

## Trocar lado (2.19.0; janela desde 2.21.0; eixos × níveis desde 2.23.0, 13/11/2026)
Comando `TrocarLadoIndicacao` (`Comandos/TrocarLadoIndicacao.cs`, janela `Cotagem/JanelaTrocarLado.cs`), na vista ativa:
- **Sempre os selecionados:** eixos e níveis selecionados; sem seleção, pede para escolher no modelo (Concluir).
- **Dois cartões:** **Eixos** (em cima / embaixo) e **Níveis** (à esquerda / à direita), cada um com a quantidade selecionada; o cartão só aparece se houver itens do tipo. Num eixo horizontal na tela, "em cima" vale como a ponta da esquerda.
- **Opções em blocos desenhados** (estética do Sistema FJA, `lib/fja_estilo.xaml`; escolhido em vermelho FJ): um lado, o outro, nos dois, trocar (só nas linhas com indicação de um lado só) e não mexer. Sem textos explicativos.
- Antes de mudar, passa as duas pontas para 2D (`SetDatumExtentType(..., ViewSpecific)`), para não afetar outras vistas; liga antes de desligar. Uma transação (Ctrl+Z desfaz).
- Relatório só quando nada mudou, ou quando alguma linha ficou como estava ou deu erro.
- Preferências por pessoa: `%APPDATA%\FJA\trocar-lado.json` (escolha dos eixos e dos níveis).

## Próximas versões (regras completas na doc do projeto `claude/revit-cotagem-regras.md`)
- Planta baixa 1:75: cadeias internas atravessando ambientes pela face acabada (com espessura de parede), bonecas ≠ 15 cm, totais por bloco; 1:200/1:500: contorno amarrado aos limites do terreno; 1:25 por faces (acabado).
- Teto refletido (luminárias, a partir do acabado), pontos (osso), paginação (início e módulo).
- Ambientes ampliados: reconhecer os callouts e reduzir a cotagem interna na 1:75.
- Conflitos (cota existente medindo outra referência) no relatório, com seleção no modelo.
