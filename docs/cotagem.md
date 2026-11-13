# Cotagem (Ferramentas FJA › Cotagem › Cotar vistas)

Comando C# `CotarVistas` (`revit/dotnet/FJA.Revit/Cotagem/*.cs`), desde a extensão 2.18.0 (13/11/2026).

## Como funciona (1ª versão)
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
  | Planta baixa | — | eixos + total | eixos + total; panos e vãos de fachada + total (2.20.0) | — (faces de parede, próxima versão) |
  | Corte, fachada | eixos + total; níveis + total | idem | idem | níveis + total |
  | Vista/corte interno | níveis + total | idem | idem | idem |
  | Teto, pontos, paginação, implantação | — | — | — | — |
- **Cadeias** (`Motor`): eixos e níveis retos, pela linha deles na vista (`GetCurvesInView`). Linhas verticais na tela → cadeia horizontal; horizontais → cadeia vertical. Um lado só: o das bolhas/cabeças, de preferência em cima (cadeia horizontal) e à esquerda (vertical). A 1ª linha fica a 1 espaçamento das pontas (lado das bolhas), o total a 2 espaçamentos. Espaçamento padrão 8 mm no papel (opção da janela).
- **Cotas existentes:** antes de criar, lê todas as cotas lineares da vista; cada segmento conta sozinho. Um trecho está "já cotado" quando existe cota com os mesmos pontos de início e fim, na mesma direção (tolerância 1 mm), em qualquer posição. Só os trechos que faltam são criados, em sequências contínuas.
- **Marca:** toda cota criada recebe `FJA_Cotagem` (Extensible Storage). Modo **Complementar** (padrão) não mexe em nada existente; **Refazer as minhas** apaga só as cotas marcadas da vista e gera de novo.
- **Tipo de cota:** escolhido na janela (tipos lineares do projeto; padrão = o último usado, senão o padrão do projeto).
- **Preferências por pessoa:** `%APPDATA%\FJA\cotagem.json` (tipo de cota, espaçamento).
- Avisos do Revit durante a criação são suprimidos; o relatório final lista, por vista, cotas criadas, trechos, já cotados, refeitas e erros.

## Panos e vãos de fachada (2.20.0, 13/11/2026) — planta baixa 1:75 (1:41 a 1:149)
`Cotagem/Fachada.cs`, chamado pelo `Motor` quando a regra tem `Fachada`.
- **Paredes lidas:** as da vista, retas, alinhadas a ela (horizontais ou verticais na tela), cortadas pelo plano de corte da vista; cortina fica de fora. A geometria vem com referências (`Options.ComputeReferences`, vista ativa).
- **Parede de fachada de um lado** (em cima, embaixo, à esquerda, à direita): paralela a esse lado e sem outra parede mais para fora em pelo menos um de cinco pontos ao longo dela (10 %, 30 %, 50 %, 70 % e 90 % do comprimento). Recuos e blocos com fachadas em profundidades diferentes entram na mesma cadeia.
- **Pontos da cadeia:** faces planas verticais de qualquer parede, perpendiculares ao lado, que chegam à face externa de uma parede de fachada e são cortadas pelo plano de corte: ombreiras dos vãos (portas e janelas abertas na parede — vão em osso), pontas de parede e o canto externo da parede perpendicular. Paredes internas que encostam na fachada não chegam à face externa e ficam de fora. Janela acima do plano de corte não entra.
- **Linhas:** a cadeia a 1 espaçamento da face externa mais saliente do lado; o total da fachada (das pontas) a 2 espaçamentos, se a cadeia tiver 3 pontos ou mais. Os quatro lados são cotados.
- **Cotas existentes e marca:** as mesmas regras dos eixos (só os trechos que faltam; `FJA_Cotagem` com regra `fachada-cima`, `fachada-baixo`, `fachada-esquerda`, `fachada-direita` e `…-total`).
- **A conferir com a FJA:** se a cadeia de fachada vai nos quatro lados ou só em alguns; se o vão é medido em osso (hoje) ou no acabado; ordem das linhas em relação à cadeia de eixos (hoje a fachada fica junto do edifício e a de eixos junto das bolhas; se as bolhas estiverem perto das paredes, as linhas podem se sobrepor).
- Não vale ainda para 1:200 (anteprojeto), nem para parede inclinada ou curva.

## Trocar lado (2.19.0, 13/11/2026)
Comando `TrocarLadoIndicacao`. Nos eixos e níveis selecionados (sem seleção, pede para escolher), na vista ativa:
- passa as duas pontas para 2D (`SetDatumExtentType(..., ViewSpecific)`), para não afetar outras vistas;
- desliga a bolha/cabeça do lado em que está e liga do lado oposto;
- com indicação dos dois lados, ou sem nenhuma, não mexe e avisa.

## Próximas versões (regras completas na doc do projeto `claude/revit-cotagem-regras.md`)
- Planta baixa: cadeias internas atravessando ambientes (com espessura de parede), bonecas ≠ 15 cm, totais por bloco; afastamentos e lote (1:200/1:500); 1:25 por faces (acabado).
- Teto refletido (luminárias), pontos (osso), paginação (início e módulo).
- Ambientes ampliados: reconhecer os callouts e reduzir a cotagem interna na 1:75.
- Conflitos (cota existente medindo outra referência) no relatório, com seleção no modelo.
