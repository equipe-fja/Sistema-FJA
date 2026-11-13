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
  | Planta baixa | — | eixos + total | eixos + total | — (faces de parede, próxima versão) |
  | Corte, fachada | eixos + total; níveis + total | idem | idem | níveis + total |
  | Vista/corte interno | níveis + total | idem | idem | idem |
  | Teto, pontos, paginação, implantação | — | — | — | — |
- **Cadeias** (`Motor`): eixos e níveis retos, pela linha deles na vista (`GetCurvesInView`). Linhas verticais na tela → cadeia horizontal; horizontais → cadeia vertical. Um lado só: o das bolhas/cabeças, de preferência em cima (cadeia horizontal) e à esquerda (vertical). A 1ª linha fica a 1 espaçamento das pontas (lado das bolhas), o total a 2 espaçamentos. Espaçamento padrão 8 mm no papel (opção da janela).
- **Cotas existentes:** antes de criar, lê todas as cotas lineares da vista; cada segmento conta sozinho. Um trecho está "já cotado" quando existe cota com os mesmos pontos de início e fim, na mesma direção (tolerância 1 mm), em qualquer posição. Só os trechos que faltam são criados, em sequências contínuas.
- **Marca:** toda cota criada recebe `FJA_Cotagem` (Extensible Storage). Modo **Complementar** (padrão) não mexe em nada existente; **Refazer as minhas** apaga só as cotas marcadas da vista e gera de novo.
- **Tipo de cota:** escolhido na janela (tipos lineares do projeto; padrão = o último usado, senão o padrão do projeto).
- **Preferências por pessoa:** `%APPDATA%\FJA\cotagem.json` (tipo de cota, espaçamento).
- Avisos do Revit durante a criação são suprimidos; o relatório final lista, por vista, cotas criadas, trechos, já cotados, refeitas e erros.

## Trocar lado (2.19.0, 13/11/2026)
Comando `TrocarLadoIndicacao`. Nos eixos e níveis selecionados (sem seleção, pede para escolher), na vista ativa:
- passa as duas pontas para 2D (`SetDatumExtentType(..., ViewSpecific)`), para não afetar outras vistas;
- desliga a bolha/cabeça do lado em que está e liga do lado oposto;
- com indicação dos dois lados, ou sem nenhuma, não mexe e avisa.

## Próximas versões (regras completas na doc do projeto `claude/revit-cotagem-regras.md`)
- Planta baixa: panos e vãos de fachada, cadeias internas atravessando ambientes (com espessura de parede), bonecas ≠ 15 cm, totais por bloco; afastamentos e lote (1:200/1:500); 1:25 por faces (acabado).
- Teto refletido (luminárias), pontos (osso), paginação (início e módulo).
- Ambientes ampliados: reconhecer os callouts e reduzir a cotagem interna na 1:75.
- Conflitos (cota existente medindo outra referência) no relatório, com seleção no modelo.
