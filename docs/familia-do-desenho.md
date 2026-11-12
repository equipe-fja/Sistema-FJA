# Símbolos e Detalhes › Família do desenho (2.15.0, 12/11/2026)

Cria uma família de **item de detalhe** ou de **anotação genérica** com o desenho 2D selecionado na vista, sem abrir o editor de famílias. Em C#: comando `FamiliaDoDesenho` (`Comandos/FamiliaDoDesenho.cs`), lógica em `Detalhes/` (`Desenho.cs` leitura, `Criacao.cs` família e substituição, `JanelaDesenho.cs` janela).

## Decisões da FJA (12/11/2026)
- Prioridade: anotações e detalhes típicos a partir do que já está desenhado. Só **elementos 2D**.
- Nome livre; a sigla da disciplina é opcional.
- O .rfa vai para o projeto (cópia em `%TEMP%\FJA_FAMILIAS`); opção de salvar também numa pasta escolhida na hora.
- Família com o mesmo nome: **perguntar na hora** (substituir, que atualiza as instâncias, ou criar com "(2)").
- Opção de **substituir os originais** pela família (mesmo lugar, uma transação, Ctrl+Z desfaz).
- Painel novo **Símbolos e Detalhes**.
- O caminho via **DWG** (símbolos de famílias 3D, com ajuste de escala, como nas Legendas) fica para a etapa 2, opcional.

## O que entra
| Elemento | Como vai para a família |
|---|---|
| Linhas de detalhe (retas, arcos, splines, elipses) | redesenhadas; cada estilo vira, na família, um **estilo da categoria Linhas com o mesmo nome** do estilo do projeto (2.15.1). Ao carregar, o Revit liga os dois pelo nome: cor, espessura e padrão vêm exatamente do projeto, e a espessura segue a tabela de modelo, como as linhas de detalhe. Em "tudo preto": estilo preto do projeto com a mesma espessura e padrão (ex.: `PENA 2`) ou um novo `PRETO PENA n`. `<Linhas invisíveis>` usa o estilo invisível da família |
| Regiões preenchidas e de mascaramento | redesenhadas; o **tipo é recriado** na família (padrões de frente/fundo copiados como `FillPatternElement`, cores, espessura, mascaramento) — a cópia de tipos entre documentos falha; contorno com o estilo lido das arestas da região (o contorno só aceita estilos da categoria Linhas) |
| Textos | item de detalhe: **copiados da vista** (tipo e formatação) e postos no lugar; anotação genérica: a vista da família é uma folha e a cópia falha, então o texto é criado com o **tipo recriado** (parâmetros do tipo copiados; negrito/itálico em parte do texto se perdem) |
| Componentes de detalhe | só no item de detalhe; copiados da vista (tipo e parâmetros) e postos no lugar |
| Anotações genéricas aninhadas | item de detalhe: copiadas da vista; anotação genérica: a família é carregada antes e a instância é posta com parâmetros, rotação e espelhamento |
| Grupos de detalhe | abertos nos membros; na substituição, o grupo inteiro é apagado |

Fica de fora (listado nos avisos da janela): elementos de modelo, cotas, etiquetas, nuvens de revisão, detalhes repetidos, isolamento.

## Escala e ponto de inserção
- Coordenadas da vista (direita/cima), então funciona em plantas, cortes, elevações, desenho, detalhe, legenda e folha.
- Item de detalhe: tamanho real. Anotação genérica: medidas divididas pela escala da vista (tamanho do papel).
- Ponto de inserção: centro, canto inferior esquerdo ou ponto clicado no modelo (a janela fecha, pede o ponto e abre de novo).
- Cores: manter ou tudo preto (mantém espessuras; regiões e textos ganham tipos "… - PRETO"; componentes aninhados mantêm as cores próprias).
- Preferências por pessoa: `%APPDATA%\FJA\familia-desenho.json` (cores, substituir, salvar fora, pasta, origem).

## Testes pelo conector (Revit 2027, modelo 129, 12/11/2026)
- Leitura (só leitura): 2677 linhas de detalhe, 51 arcos, 4 splines, 81 regiões, 233 textos e 50 grupos fora das folhas; estilos com nome, cor, peso e padrão (ex.: `PENA 1 TRAÇO` com `.FJA-TRAÇO 1mm`).
- Em famílias criadas na memória e fechadas sem salvar: subcategoria com padrão copiado, linha/arco/spline com ela, tipo de região recriado, região, texto copiado (item de detalhe) e texto com tipo preto — tudo ok nos dois modelos (`Item de detalhe métrico.rft`, `Anotação genérica métrica.rft`).
- Falham (contornados): `CopyElements` de `FilledRegionType` entre documentos; cópia de texto pela vista para a anotação genérica (vista "Folha").
- IronPython no conector: `ElementType.Name` falha (usar `Element.Name.GetValue`); `FilledRegion` não tem leitura do estilo do contorno (vem das arestas do sólido da geometria); Revit 2027 não tem `ElementId.IntegerValue`.

## Etapa 2 (a fazer)
Opção de leitura por DWG para símbolos de famílias de ponto/equipamento (o desenho em planta delas é a projeção do 3D; a API não remove linhas ocultas), reaproveitando `Simbolos.ViaDwg`/`Reescalar`. Antes, testar com autorização (cria e apaga uma vista temporária): hachuras, espessuras e tracejados na ida e volta.

## 2.15.1 (12/11/2026) — cores e espessuras
Primeiro teste da FJA (família "teste", anotação genérica): o contorno das hachuras voltou para `<Thin Lines>` e as espessuras mudaram.
- Causa 1: os estilos eram criados como subcategorias da categoria da família (Anotações genéricas / Itens de detalhe); o contorno de região só aceita estilos da categoria Linhas, então o Revit usava `<Thin Lines>`.
- Causa 2: subcategorias da anotação genérica usam a tabela de espessuras de anotação (fixa); as linhas de detalhe do projeto usam a de modelo (varia com a escala) — o mesmo número de pena sai com outra espessura.
- Correção: estilos da categoria Linhas com o nome do estilo do projeto (testado nos dois modelos .rft: linha, estilo embutido e contorno de região aceitos).

## 2.15.2 (12/11/2026) — cor como aparece na vista e espessura na anotação
Segundo teste da FJA: símbolo da legenda SNZ (9 linhas `PENA 2 VERMELHA (HID.Q)`) aparece verde e grosso; a família saiu vermelha e fina.
- Causa da cor: o verde (140,195,25) é sobreposição gráfica em cada elemento (pintura da ferramenta Legendas). A ferramenta lia só o estilo.
- Causa da espessura: dentro da anotação genérica o Revit desenha pela tabela de espessuras de anotação; as linhas de detalhe usam a de modelo da escala da vista (a pena 2 na 1:25 sai mais grossa). A API não dá acesso às tabelas (só `Category.Get/SetLineWeight`), então não dá para converter sozinho.
- Correção (decisões da FJA):
  - Cor, espessura e padrão **como aparecem na vista**: sobreposição do elemento › filtros (na ordem da vista ou do modelo de vista, só ativos e visíveis) › sobreposição da categoria › estilo. Regiões: também a cor do preenchimento sobreposta (tipo "NOME r-g-b").
  - Quando o resultado difere do estilo original, a família usa um estilo da categoria Linhas com o nome **"PENA 2 VERDE 140-195-25 SOLID"** (pena, nome da cor pelo matiz, RGB, tipo de linha: SOLID ou o nome do padrão). Sem diferença, continua o estilo original (ligado ao do projeto pelo nome).
  - **Espessura na anotação**: campo na janela, +0 a +6 penas, só para anotação genérica; guardado por pessoa (`ajustePena`).
  - "Tudo preto": estilo preto do projeto com a pena final e o mesmo padrão; senão "PENA n PRETO 0-0-0 SOLID".

## 2.15.3 (12/11/2026) — opção de desconsiderar as sobreposições
- Pedido da FJA: considerar por padrão as sobreposições (by element, filtros, categoria), nomeando a pena pelo que é visível (já era assim na 2.15.2), e ter a opção de desconsiderá-las.
- Campo "Cores": **Como aparecem na vista (com sobreposições)** (padrão) · **Estilos originais (sem sobreposições)** · **Tudo preto**. Guardado por pessoa (`semSobreposicao`). A prévia acompanha a escolha.
- Cada peça guarda o estilo efetivo (`Estilo`) e o original (`EstiloOriginal`); regiões, a cor efetiva (`CorRegiao`) e a do tipo (`CorOriginal`).
