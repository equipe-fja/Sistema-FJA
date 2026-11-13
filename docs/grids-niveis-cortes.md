# Grids, Níveis e Cortes (painel da extensão desde 2.25.0, 13/11/2026)

Painel separado da Cotagem a pedido da FJA, com as ferramentas de eixos (grids), níveis e cortes:
- **Trocar lado** (`TrocarLadoIndicacao`): estava em Cotagem desde 2.19.0; ver `docs/cotagem.md`.
- **Estender/Alinhar** (`EstenderAlinhar`, `Comandos/EstenderAlinhar.cs`): abaixo.

## Estender/Alinhar
Como o Aparar/Estender do Revit (Trim/Extend), para eixos, níveis e cortes.

1. **Linha de referência**: clique numa linha de detalhe ou de modelo (reta), eixo, nível, plano de referência ou corte. Vale a reta inteira (prolongada), como no Revit.
2. **Já selecionados**: se havia eixos, níveis ou cortes selecionados ao abrir, todos têm a ponta mais próxima levada até a referência, de uma vez (uma transação).
3. **Um a um**: depois, cada eixo, nível ou corte clicado é ajustado na hora (uma transação por clique; Ctrl+Z desfaz o último). Esc termina.

Qual ponta muda:
- se a reta da referência **cruza** o elemento: fica o lado clicado e a outra ponta é aparada até a referência (nos já selecionados, sem clique, a ponta mais próxima da referência é a aparada);
- se **não cruza**: a ponta mais próxima vai até a referência (estende ou encurta).

Como muda:
- **Eixos e níveis**: a ponta muda no tipo de extensão em que já está na vista ativa — 3D (`Model`, afeta as outras vistas, como no Revit) ou 2D (`ViewSpecific`, só nesta vista). O tipo não é trocado (a troca para 2D esticava os níveis; ver o Trocar lado 2.23.4). A posição é calculada na tela (coordenadas da vista) e aplicada sobre a curva do tipo da ponta (`GetCurvesInView` + `SetCurveInView`).
- **Cortes** (marcador de corte na planta; chamadas e elevações ficam de fora): muda a largura do corte — `Min.X`/`Max.X` do recorte (`CropBox`) da vista do corte. A vista é achada pelo nome do marcador.
- Não muda: eixo curvo, elemento paralelo à referência, ponta que passaria além da outra, resultado com menos de 1 cm.

A testar no Revit (ainda não testado): eixos em planta (3D e 2D), níveis em corte, marcador de corte em planta, referência por linha de detalhe e por eixo; vista girada; corte segmentado (não tratado).
