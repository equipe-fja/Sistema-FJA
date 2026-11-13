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

## 2.26.2 (13/11/2026) — cortes
Teste da FJA (131, PMA.NÍVEL 1): eixos e níveis funcionaram; nos cortes, o clique não fazia nada e não aparecia mensagem. Pelo conector, a própria DLL alinhou o `PMA-LE-CORTE 6` ao eixo P.5, inclusive gravando a transação (o teste foi desfeito), então a conta e a gravação estavam certas. A causa provável é o filtro de seleção: no clique, o Revit entrega o corte ao filtro de outra forma que na seleção normal (na seleção normal ele é o marcador, `OST_Viewers`), e um clique recusado pelo filtro é ignorado em silêncio.
- Os cliques passam a aceitar **qualquer elemento**; o que não serve abre uma mensagem com o nome, a classe e a categoria do elemento clicado (também serve de diagnóstico, se o corte ainda não for reconhecido).
- O corte é reconhecido pelo marcador (`OST_Viewers`), pela categoria Cortes (`OST_Sections`) ou pela própria vista de corte (`ViewSection`).
- Vistas selecionadas no navegador de projeto não entram no alinhamento automático dos já selecionados.

## 2.26.5 (13/11/2026) — retorno na tela
Com a 2.26.2 o corte foi aparado no modelo (o `PMA-LE-CORTE 6` passou de −78,7 a −6,0, no eixo P.5), mas a FJA viu "nada acontecer": a vista não era redesenhada e um novo clique num corte que já estava na linha não dava aviso. Agora a vista ativa é redesenhada depois de cada mudança (`RefreshActiveView`), e o elemento que já está na linha de referência abre o aviso "já está na linha de referência".

## 2.26.8 (13/11/2026) — cortes fora do Estender/Alinhar
No teste seguinte da FJA (cortes 4, 5 e 6 até uma linha de detalhe tracejada), o recorte de cada corte foi exatamente até a linha, mas as cabeças na planta não se mexeram. No Revit, **a linha do corte (com as cabeças) e a largura do recorte são independentes**: o marcador do CORTE 5 se desenhava de 421 a 572 (pés, na planta) enquanto o recorte ia de 407 a 492. A API não dá acesso às pontas da linha do corte (o marcador não tem `LocationCurve` nem parâmetro para isso), só ao recorte (`CropBox`).
- Decisão da FJA: **cortes saem da ferramenta como alvo**; clicar num corte mostra o aviso da limitação. Um corte continua aceito como **linha de referência** (só a posição da linha conta).
- O botão continua no painel Grids, Níveis e Cortes, agora para eixos e níveis.
- Os testes tinham mudado o alcance (recorte) dos cortes 4, 5 e 6 do modelo 131; com autorização da FJA, foram restaurados pelo conector aos valores lidos antes dos testes (uma transação, "FJA: restaurar largura dos cortes 4, 5 e 6").

## 2.26.10 (13/11/2026) — referência paralela (o commit é "2.26.9", mas a compilação dele ficou sem o código; publicado como 2.26.10)
Pedido da FJA: quando a referência é **outro eixo (ou nível) paralelo**, o eixo/nível clicado fica com a **mesma extensão** dele — as duas pontas vão até as pontas da referência, projetadas sobre o clicado (`AlinharParalelo`). Vale também para os já selecionados ao abrir (alinhar vários de uma vez a um eixo de referência). Cada ponta muda no tipo de extensão em que já está (3D ou 2D). Com referência paralela que não é eixo nem nível (linha, plano de referência, corte), continua o aviso de paralelo.
