# Revit × Sistema FJA — tipos sincronizados com a especificação técnica

Documento de projeto (versão 1 — 03/11/2026). Nada implementado ainda: este texto é para revisão antes da implementação.

## 1. Objetivo

Usar o Sistema FJA como fonte única dos tipos do Revit, para padronizar os modelos. O Revit deixa de ser o lugar onde os tipos são inventados e passa a ser o lugar onde eles são aplicados.

- Novos tipos de parede, piso, forro e cobertura nascem a partir da **biblioteca central** da especificação técnica, ou são criados nela no mesmo momento.
- Os projetos em andamento, hoje fora do padrão, são **ajustados aos poucos** com uma ferramenta de sincronização que vincula os tipos existentes à especificação.

## 2. Ponto de partida

### No sistema (especificação técnica, em desenvolvimento na dev)
- **Biblioteca central** (`espb`): item com código permanente `FJ-NNNN`, classe NBR 15965, sigla, descrição, texto-base e fabricante.
- **Especificação do projeto** (`espec`): item puxado da biblioteca com **keynote próprio do projeto** (`SIG.NN`), descrição e texto do projeto, dimensões e variações. Há aviso quando o texto central muda depois.
- **Siglas** (`etsig` + lista padrão no código): 2 letras = material (GE, PI, BL, EM, PO, CC…), 3 letras = elemento ou família (DRY, PMD, ESQ…).
- **Já existe:** exportação do arquivo de keynotes (.txt) do projeto e importação das planilhas ET-.

### No Revit
- Revit 2027 e 2024 instalados, pyRevit nos dois.
- Aba **Ferramentas FJA** (`Ferramentas-FJA.extension`, v1.1.5, hoje só em `%APPDATA%\pyRevit\Extensions` de um computador):
  - Legendas › **Legenda LUM** — legenda de luminárias; atribui Type Mark `PREF.NN` a quem não tem.
  - Legendas › **Legenda ELE** — legenda de pontos elétricos; marca o que cria com Extensible Storage (`FJA_LegendaELE`).
  - Nomes e Parâmetros › **Renomear Tipos** — nome do tipo = "Type Mark - Descrição".
- Modelos atuais (ex.: 131): nomes misturados (`PIS_PO1-2cm_CP1-3cm_5cm`, `PAREDE GENÉRICA 14CM`, `LAJ_CON-15cm`), keynotes inconsistentes (forros e telhados com "BLOCO DE CONCRETO 190mm" no campo Keynote), materiais com nomes de vários padrões.

## 3. Decisões tomadas

| # | Tema | Decisão |
|---|---|---|
| D1 | Onde ficam as ferramentas | Painel novo na aba **Ferramentas FJA** (pyRevit). |
| D2 | Quem cria itens na biblioteca a partir do Revit | **Todos**, por enquanto. |
| D3 | Nome do tipo | **"Type Mark - Descrição"** (mesmo padrão do Renomear Tipos). |
| D4 | Type Mark | **Cópia do keynote do projeto** (ex.: `DRY.01`). |
| D5 | Variação de espessura | O mesmo item (mesmo keynote) pode ter vários tipos; quando houver variação, o nome ganha **` E=<espessura total em mm>`** no fim. |
| D6 | Materiais | Criados a partir de **materiais-base padrão por sigla**, ajustáveis depois. |
| D7 | Sincronização | Um botão **Sincronizar tipos geral**, para todas as categorias. |
| D8 | Numeração | O pyRevit reserva o próximo FJ e o próximo keynote a partir dos dados do sistema; o sistema confirma (ver §6.3). |

## 4. Ferramentas no Revit

Painel **Tipos** na aba Ferramentas FJA:

| Botão | Faz |
|---|---|
| **Nova parede** · **Novo piso** · **Novo forro** · **Nova cobertura** | Abre a janela de escolha na biblioteca (§4.1) filtrada pela categoria. |
| **Sincronizar tipos** | Confere todos os tipos do modelo contra o sistema, em todas as categorias (§4.3). |

### 4.1 Janela "Nova parede" (escolher na biblioteca)
- Cabeçalho: categoria, projeto do modelo e data da última atualização dos dados do sistema.
- Busca por FJ, keynote, descrição ou material; filtros **Todas / No projeto / No modelo**.
- Lista dos itens centrais da categoria: código FJ, descrição, espessura total e etiqueta "no modelo · ALV.01" / "no projeto · ALV.02".
- Painel do item selecionado: desenho da seção em camadas; tabela de camadas (função, keynote do material, descrição, espessura) e total.
- Prévia do resultado: keynote no projeto (o existente, ou o próximo livre da sigla), Type Mark, nome do tipo, materiais que já existem e os que serão criados.
- Se o item já tiver no projeto variações de espessura, escolher a variação ou criar uma nova (→ `E=`).
- Botões: **Nova parede…** (§4.2), Cancelar, **Criar no modelo**.

Ao criar:
1. Se o item ainda não está na especificação do projeto, ele entra com o próximo keynote livre da sigla (como o "Puxar da biblioteca").
2. Os materiais que faltam são criados (§7).
3. O tipo é criado a partir de um tipo-base da família de sistema; recebe camadas, Type Mark, Keynote, Descrição, nome e o vínculo invisível (§8).

### 4.2 Janela "Nova parede…" (criar na biblioteca)
É o mesmo formulário do sistema:
- Sigla (lista das siglas de elemento da categoria); Descrição; Especificação (texto); Fabricante e classe NBR (opcionais).
- **Camadas:** função (Acabamento 1, Acabamento 2, Substrato, Estrutura, Isolamento, Membrana), material (itens de **material** da biblioteca, siglas de 2 letras) e espessura em mm. Inclui adicionar, remover e reordenar.
- Prévia: FJ que será criado, keynote no projeto, nome do tipo e espessura total.
- **Criar na biblioteca e no modelo:** grava o item central, a linha na especificação do projeto e o tipo no modelo.

### 4.3 Sincronizar tipos (todas as categorias)
Lê os tipos do modelo e os separa em três grupos:

1. **Vinculados e iguais** — nada a fazer.
2. **Vinculados com diferenças** — mostra campo a campo o que mudou no sistema (descrição, keynote, camadas, espessuras, materiais). Marca-se o que atualizar e aplica-se em lote.
3. **Não vinculados** (tipos antigos) — sugere o item da especificação do projeto por Type Mark, keynote ou semelhança de nome. O usuário confirma, escolhe outro ou ignora. Ao vincular, o tipo é ajustado e renomeado.

O que é sincronizado depende da categoria:

| Categoria | Sincroniza |
|---|---|
| Paredes básicas, pisos, forros compostos, coberturas básicas | Nome, Type Mark, Keynote, Descrição, **camadas, espessuras e materiais** |
| Famílias carregáveis (portas, janelas, esquadrias, louças e metais, equipamentos, luminárias, mobiliário…) | Nome, Type Mark, Keynote, Descrição (a geometria continua sendo a da família) |
| Paredes cortina, empilhadas, guarda-corpos | Só parâmetros, numa segunda fase |

Também se aplica a materiais: os materiais ligados a itens da especificação recebem nome, keynote e descrição atualizados.

O Sincronizar substitui, para o que estiver vinculado, o **Renomear Tipos**. O Renomear continua útil para categorias fora da especificação.

## 5. Regras de nome e parâmetros

| Onde | Valor |
|---|---|
| Type Mark do tipo | keynote do projeto (`DRY.01`) |
| Keynote do tipo | o mesmo keynote |
| Descrição do tipo | descrição do item **no projeto**, em maiúsculas |
| Nome do tipo | `<Type Mark> - <DESCRIÇÃO>`; com variação de espessura: `<Type Mark> - <DESCRIÇÃO> E=<mm>` |
| Nome do material | `<keynote do material> - <DESCRIÇÃO>` (ex.: `GE.01 - CHAPA DE GESSO ACARTONADO ST 12,5MM`) |
| Keynote / Descrição do material | keynote e descrição do item de material no projeto |

- **E=** usa a **espessura total** em mm e só aparece quando há mais de um tipo para o mesmo keynote. Quando a segunda variação surge, o primeiro tipo também passa a ter `E=` (o Sincronizar faz isso).
- Caracteres proibidos no Revit (`` \ : { } [ ] | ; < > ? ` ~ ``) são trocados por `-`, como no Renomear Tipos.
- Ordem das camadas: de fora para dentro (paredes) e de cima para baixo (pisos, forros, coberturas). O núcleo fica nas camadas de Estrutura; sem estrutura, no Substrato.

## 6. Comunicação entre o Revit e o sistema

O banco do sistema está dentro do claude.ai: o pyRevit não consegue consultá-lo nem gravar nele diretamente. A comunicação passa por uma **caixa de correio no G:**.

Pasta: `G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT\`

```
REVIT\
  biblioteca.json            ← o sistema escreve; o pyRevit lê
  projetos\<id-projeto>.json ← o sistema escreve (especificação de cada projeto)
  pedidos\                   ← o pyRevit escreve; o sistema lê, processa e apaga/arquiva
  respostas\                 ← o sistema escreve; o pyRevit lê
  materiais-base.rvt         ← materiais-base por sigla (§7)
```

### 6.1 Sistema → Revit (leitura)
- O sistema reescreve `biblioteca.json` e o arquivo do projeto alguns segundos depois de qualquer alteração na biblioteca, na especificação ou nas siglas. Usa o mesmo mecanismo dos backups em planilha e os retoma quando há alguém conectado.
- Exige incluir **`update_file`** (Google Drive) nas capacidades do sistema. Hoje há só `create_file`, e cada pessoa verá um novo pedido de permissão.
- O pyRevit lê pelo Google Drive para Desktop: as janelas abrem na hora, mesmo sem ninguém com o sistema aberto. A defasagem é a da sincronização do Drive (segundos a um minuto).
- Conteúdo: itens centrais, com camadas e variações; siglas; itens de material; e, por projeto, a especificação com keynotes e o código do projeto.

### 6.2 Revit → Sistema (pedidos)
Ações no Revit que alteram o sistema viram arquivos em `pedidos\`:
- `incluir-no-projeto` — item central entra na especificação do projeto com o keynote reservado.
- `novo-item` — item central novo (com camadas), já incluído no projeto.
- `nova-variacao` — variação de espessura de um item do projeto.
- `vincular` — registro de que um tipo do modelo foi vinculado a um item (para o painel do projeto saber o que está no modelo).

O sistema verifica a pasta quando aberto (a cada ~90 s, como as pendências de backup), aplica, grava a resposta e arquiva o pedido. No sistema, uma linha "Pedidos do Revit" mostra o que entrou e quem pediu.

### 6.3 Numeração
- O pyRevit **reserva** o próximo `FJ-NNNN` e o próximo keynote da sigla com base no `biblioteca.json` e no arquivo do projeto, e já cria o tipo no modelo com esses números.
- O sistema confirma ao processar. Se houver conflito (duas pessoas criando ao mesmo tempo), ele renumera o segundo pedido e grava a correção na resposta. O próximo **Sincronizar** ajusta o tipo no modelo.
- Enquanto o pedido não é processado, o tipo fica marcado como **pendente** no vínculo e aparece assim no Sincronizar.

### 6.4 Alternativa complementar (não é a base)
Quando o sistema é aberto no app do Claude para desktop, ele pode chamar o conector local do Revit e criar tipos direto a partir do sistema. Só funciona no app desktop, e o Revit pede aprovação a cada execução.

## 7. Materiais-base

- Arquivo `materiais-base.rvt` no G:, com **um material por sigla de 2 letras** (GE, PI, BL, EM, PO, CC, MD…): cor, hachura de superfície e de corte, e aparência.
- Para criar um material que falta, o pyRevit copia o material-base da sigla para o modelo e troca nome, keynote e descrição.
- Sem base para a sigla, cria um material neutro e avisa.
- Quem cuida da biblioteca melhora o arquivo-base; materiais criados depois já nascem com a aparência nova. Os já existentes não mudam sozinhos.

## 8. Vínculo tipo ↔ item (Extensible Storage)

Esquema `FJA_SistemaTipo`, no mesmo padrão do `FJA_LegendaELE`, gravado em cada tipo e material criado ou vinculado:

| Campo | Conteúdo |
|---|---|
| `fja` | código FJ do item central |
| `itemCentral` | id do item na biblioteca (`espb`) |
| `itemProjeto` | id do item na especificação do projeto (`espec`) |
| `projeto` | id do projeto no sistema |
| `variacao` | espessura da variação (mm) ou vazio |
| `pedido` | id do pedido ainda não confirmado (pendente) ou vazio |
| `sincronizadoEm` | data da última sincronização |

Isso permite renomear sem perder o vínculo, saber o que veio do sistema e mostrar no sistema o que está em cada modelo.

## 9. O que muda no sistema

1. **Camadas no item central:** campo de composição (função, item de material, espessura) para itens de sigla de parede, piso, forro e cobertura. A especificação do projeto pode ter variações de espessura.
2. **Publicação no G:** `biblioteca.json` e `projetos\<id>.json`, com a capacidade `update_file`.
3. **Processamento de pedidos** e linha "Pedidos do Revit".
4. **Indicação "no modelo"** nos itens da especificação do projeto, a partir dos pedidos `vincular`.
5. Siglas de elemento para paredes, pisos, forros e coberturas (§11).

## 10. Distribuição da extensão

- A extensão **Ferramentas FJA** vai para o repositório do sistema (`revit/Ferramentas-FJA.extension`), com histórico.
- A cópia de uso fica numa pasta do G:, por exemplo `BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit\`. Cada computador aponta o pyRevit para ela (configuração de extensões do usuário), uma vez só.
- Atualizar = copiar a versão nova para o G: e recarregar o pyRevit.
- Compatibilidade: Revit 2024 e 2027 (pyRevit com IronPython), seguindo o estilo dos scripts atuais.

## 11. Pontos a confirmar

1. **Siglas de 3 letras** para paredes, pisos, forros e coberturas. O sistema ainda não as tem cadastradas: a lista padrão do código só tem DRY (drywall), e a coleção de siglas da dev está vazia. Precisamos da lista para cadastrar.
2. Nas famílias carregáveis, o nome do tipo também deve seguir "Type Mark - Descrição"? (Para portas e esquadrias o nome às vezes carrega dimensões.)
3. Onde fica a pasta de distribuição da extensão no G:.
4. Quem mantém o `materiais-base.rvt` e a lista inicial de materiais-base.
5. A Legenda LUM deve passar a usar o keynote do sistema em vez de gerar códigos próprios?

## 12. Fases sugeridas

1. **Fundação:** extensão no repositório e distribuição pelo G:; camadas e siglas no sistema; publicação de `biblioteca.json` e dos projetos.
2. **Nova parede / piso / forro / cobertura:** escolha na biblioteca e criação no modelo, com materiais-base e vínculo.
3. **"Nova…" e pedidos:** criação de itens a partir do Revit, numeração reservada e processamento no sistema.
4. **Sincronizar tipos:** categorias com camadas, depois famílias carregáveis e materiais.
5. Ajustes: variações E=, Legenda LUM, paredes cortina.

Cada fase entra primeiro na dev do sistema e é testada num modelo de teste (ou com a transação desfeita) antes de chegar aos modelos dos projetos.
