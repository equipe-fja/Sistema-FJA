# Revit × Sistema FJA — tipos sincronizados com a especificação técnica

Documento de projeto (versão 3 — 03/11/2026).

## Situação da implementação
| Fase | Situação |
|---|---|
| 1. Fundação | **Publicada na dev em 03/11/2026 (~15h35, versão `1793730938-caf4`, commit do merge em `main`).** Inclui camadas nos itens de composição, a categoria padrão do Revit na sigla, a composição em leitura no projeto (com "Incluir no projeto" para os materiais sem keynote) e Ferramentas › **Conexão com o Revit** (publicação no G:). Capacidade `trash_file` declarada na dev. A extensão já está no repositório (`revit/`) e copiada para o G:. |
| Papel Vedação / Acabamento + **correção da publicação** | **Código pronto na branch `revit-papel`, não publicado — publicar com urgência.** Além do papel, corrige um defeito da fase 1: o objeto devolvido pelo banco é somente leitura, então `config/revit` nunca era atualizado e a dev republicava `biblioteca.json`/`projeto-*.json` a cada minuto sem mandar os anteriores para a lixeira (dezenas de cópias em `REVIT\DEV` em 03/11). A correção copia o objeto antes de alterar, não regrava um arquivo publicado há menos de 30 s e limpa as cópias antigas (no **Publicar agora** e ao abrir). Para publicar: merge de `revit-papel` em `main`, `gerar.sh`, `publish_files` na dev, depois **Publicar agora**. |
| Papel + correção | Publicados na dev em 03/11 (~16h15). Pasta `REVIT\DEV` limpa (um arquivo de cada). |
| 2. Nova composição | **Pronta para teste (03/11)** — extensão v1.2.0 em `revit/` e na cópia do G: (`REVIT\pyRevit`). Painel **Tipos › Nova composição**: lista as composições publicadas (filtros categoria, papel, busca, só do projeto), liga o modelo ao projeto pelo código ou pelo nome do arquivo, mostra camadas/materiais/problemas e cria ou atualiza o tipo (camadas, materiais com keynote do projeto, Type Mark, Keynote, Descrição, Comentários de tipo = `FJ · papel`, vínculo `FJA_SistemaTipo`). Escolha Oficial/Desenvolvimento lembrada por usuário. Testado no Revit 2027 (transação desfeita): paredes de vedação e de acabamento, piso e forro. Só cria itens que já estão na especificação do projeto; materiais fora do projeto entram com o código FJ no nome e sem keynote (aviso). Camadas abaixo do mínimo do Revit (0,79 mm) sobem para o mínimo (aviso). Ainda sem materiais-base (cor por sigla). |
| 3–5 | Não iniciadas. |

Teste em 03/11: a publicação automática rodou ~15 s depois de publicar (dev aberta por alguém) e criou em `SISTEMA FJA\REVIT\DEV`: `biblioteca.json` (66 siglas, 344 itens, 4 de composição — DRY FJ-0093/0094/0095 e ALV FJ-0344 —, todos ainda **sem camadas**), `projeto-000.json` e `projeto-123.json`. Falta testar o botão **Publicar agora** (recria tudo e manda os anteriores para a lixeira; na primeira vez o Google Drive pede permissão para `trash_file`).

**Exemplos na dev (03/11, ~15h45)** — criados para testar a fase 2, todos com "Exemplo…" nas observações:
- Siglas: **IS** (material, isolamentos), **PIS** (composição, Revit: Piso), **FOR** (composição, Revit: Forro); DRY e ALV com Revit: Parede.
- Materiais: FJ-0345 lã de vidro (IS), FJ-0346 bloco de concreto 14cm (BL), FJ-0347 contrapiso (EM).
- Composições com camadas: FJ-0093/0094/0095 (drywalls, 83/96/140 mm), FJ-0344 ALV alvenaria de bloco 14cm com reboco e pintura (181 mm), FJ-0348 FOR forro de gesso com pintura (13 mm), FJ-0349 PIS porcelanato sobre contrapiso (50 mm).
- A republicação automática gerou o `biblioteca.json` novo em segundos. Observação: quando o arquivo anterior ainda está no G: no momento da criação, o Drive para Desktop nomeia o novo como `biblioteca (1).json` — o pyRevit deve sempre pegar o `biblioteca*.json` mais recente.
- Pintura com 0,5 mm: conferir na fase 2 se o Revit aceita (senão, 1 mm).
- Dado a corrigir: FJ-0020 (gesso acartonado liso) tem no texto-base a especificação de um espelho.

**Exemplos refeitos com vedação e acabamento separados (03/11, ~15h55):** FJ-0093/0094/0095 (drywall) e FJ-0344 (ALV, agora só o bloco 140 mm) são **vedação**; FJ-0350 PIN "revestimento em argamassa com pintura" (20,5 mm) e FJ-0351 PIN "pintura sobre drywall" (1 mm) são **acabamentos de parede**; FJ-0349 PIS é acabamento de piso; FJ-0348 FOR (forro) sem papel definido. Siglas: DRY e ALV = Vedação; PIN (Revit: Parede) e PIS = Acabamento.

Ao levar para a oficial: declarar `trash_file` também na oficial.

## 1. Objetivo

Usar o Sistema FJA como fonte única dos tipos do Revit, para padronizar os modelos. O Revit deixa de ser o lugar onde os tipos são inventados e passa a ser o lugar onde eles são aplicados.

- Novos tipos de parede, piso, forro e cobertura nascem a partir da **biblioteca central** da especificação técnica, ou são criados nela no mesmo momento.
- Os projetos em andamento, hoje fora do padrão, são **ajustados aos poucos** com uma ferramenta de sincronização que vincula os tipos existentes à especificação.

## 2. Ponto de partida

### No sistema (especificação técnica, em desenvolvimento na dev)
- **Biblioteca central** (`espb`): item com código permanente `FJ-NNNN`, classe NBR 15965, sigla, descrição, texto-base e fabricante.
- **Especificação do projeto** (`espec`): item puxado da biblioteca com **keynote próprio do projeto** (`SIG.NN`), descrição e texto do projeto, dimensões e variações. Há aviso quando o texto central muda depois.
- **Siglas** (`etsig` + lista padrão no código), em três tipos:
  - **Material** — 2 letras (GE, PI, BL, EM, PO, CC…); vai no Keynote do material.
  - **Composição (sistema)** — 3 letras (DRY, PIN…); tipos com camadas: paredes, pisos, forros, coberturas.
  - **Elemento / família** — 3 letras (PMD, ESQ, LAV…); famílias carregáveis.
  O tipo é escolhido no cadastro da sigla (Biblioteca › Especificação técnica › siglas).
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
| D9 | O que pode virar tipo com camadas | **Qualquer item cuja sigla seja do tipo Composição.** Não há lista fixa de siglas de parede ou piso: criar uma sigla de composição no sistema já a torna disponível no Revit. |
| D11 | Vedação e acabamento | **As composições se dividem em vedação e acabamento.** A vedação (alvenaria, drywall) é um elemento; cada face acabada recebe uma **parede aninhada** própria com tudo o que é preciso para aquele acabamento (chapisco, emboço, pintura, cerâmica…). Campo `papel` no item (padrão pela sigla). Camadas de um acabamento: da face acabada para a vedação. |
| D10 | Categoria do Revit | Fica no **item** (Parede, Piso, Forro, Cobertura), porque a mesma sigla pode servir a mais de uma (ex.: PIN em parede e em forro). A sigla pode ter uma categoria padrão, sugerida ao criar o item. |

## 4. Ferramentas no Revit

Painel **Tipos** na aba Ferramentas FJA:

| Botão | Faz |
|---|---|
| **Nova composição** | Abre a janela de escolha na biblioteca (§4.1) com **todos os itens de siglas de composição**, com filtro por categoria do Revit (Parede, Piso, Forro, Cobertura) e por sigla. |
| **Sincronizar tipos** | Confere todos os tipos do modelo contra o sistema, em todas as categorias (§4.3). |

### 4.1 Janela "Nova composição" (escolher na biblioteca)
- Cabeçalho: projeto do modelo e data da última atualização dos dados do sistema.
- Busca por FJ, keynote, descrição ou material; filtros por **categoria do Revit** (Parede, Piso, Forro, Cobertura), por **sigla** (todas as siglas de composição, agrupadas como no sistema) e **Todas / No projeto / No modelo**.
- Se a janela for aberta com elementos selecionados no modelo, o filtro de categoria já vem com a categoria deles.
- Lista dos itens centrais com sigla de composição: código FJ, descrição, espessura total e etiqueta "no modelo · ALV.01" / "no projeto · ALV.02".
- Painel do item selecionado: desenho da seção em camadas; tabela de camadas (função, keynote do material, descrição, espessura) e total.
- Prévia do resultado: keynote no projeto (o existente, ou o próximo livre da sigla), Type Mark, nome do tipo, materiais que já existem e os que serão criados.
- Se o item já tiver no projeto variações de espessura, escolher a variação ou criar uma nova (→ `E=`).
- Itens de composição **sem camadas** cadastradas aparecem na lista com aviso e o botão **Definir camadas…**, que abre o formulário do §4.2 já preenchido. Assim, qualquer item de composição que já está no sistema pode virar tipo.
- Botões: **Nova composição…** (§4.2), Cancelar, **Criar no modelo**.

Com a separação vedação/acabamento (D11), a janela ganha o filtro **Vedação / Acabamento**, e entra no plano uma terceira ferramenta:

- **Aplicar acabamento**: selecionadas paredes de vedação no modelo, escolhe-se a face (interna, externa ou as duas) e uma composição de acabamento. A ferramenta cria a parede de acabamento encostada na face, com a mesma base, topo e extensão, e a une (Join) à vedação para que portas e janelas cortem as duas. Usa a mesma regra de keynote e nome. Detalhes a confirmar com a equipe (§11).

Ao criar:
1. Se o item ainda não está na especificação do projeto, ele entra com o próximo keynote livre da sigla (como o "Puxar da biblioteca").
2. Os materiais que faltam são criados (§7).
3. O tipo é criado a partir de um tipo-base da família de sistema; recebe camadas, Type Mark, Keynote, Descrição, nome e o vínculo invisível (§8).

### 4.2 Janela "Nova composição…" (criar na biblioteca)
É o mesmo formulário do sistema:
- Sigla (qualquer sigla de **composição**); categoria do Revit (sugerida pela sigla); Descrição; Especificação (texto); Fabricante e classe NBR (opcionais).
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
  projeto-<código>.json      ← o sistema escreve (especificação de cada projeto)
  DEV\                       ← os mesmos arquivos, gerados pela dev
  pyRevit\                   ← cópia de uso da extensão Ferramentas FJA
  pedidos\                   ← o pyRevit escreve; o sistema lê, processa e apaga/arquiva
  respostas\                 ← o sistema escreve; o pyRevit lê
  materiais-base.rvt         ← materiais-base por sigla (§7)
```

### 6.1 Sistema → Revit (leitura)
- O sistema publica `biblioteca.json` e `projeto-<código>.json` 8 s depois de qualquer alteração na biblioteca, na especificação ou nas siglas, além de uma conferência a cada minuto. Só regrava os arquivos cujo conteúdo mudou (hash guardado em `config/revit`).
- **O conector do Drive não reescreve conteúdo** (`update_file` só altera nome e metadados). Por isso, cada publicação cria o arquivo novo e manda o anterior para a lixeira (`trash_file`, capacidade nova; cada pessoa verá um pedido de permissão). O pyRevit usa o arquivo mais recente com o nome esperado.
- Pastas: oficial `SISTEMA FJA\REVIT` (id `10c0Ebbm2ubHoM_LrXGZ40dbw3HP-xgkw`); dev `SISTEMA FJA\REVIT\DEV` (id `18ga9uZxZs0yFTs0xgTdmeXleAJLBfG0f`).
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

1. **Camadas no item central:** campo de composição (categoria do Revit; camadas com função, item de material e espessura) em todo item cuja sigla seja do tipo Composição. Ficha do item com editor de camadas e desenho da seção, no sistema e no Revit. A especificação do projeto pode ter variações de espessura.
   - No cadastro de siglas: **categoria padrão do Revit** para siglas de composição (opcional).
2. **Publicação no G:** `biblioteca.json` e `projeto-<código>.json` (Ferramentas › Conexão com o Revit), com a capacidade `trash_file`.
3. **Processamento de pedidos** e linha "Pedidos do Revit".
4. **Indicação "no modelo"** nos itens da especificação do projeto, a partir dos pedidos `vincular`.
5. ~~Siglas de composição~~ — feito: o tipo Composição (sistema) já existe no cadastro de siglas.

## 10. Distribuição da extensão

- A extensão **Ferramentas FJA** vai para o repositório do sistema (`revit/Ferramentas-FJA.extension`), com histórico.
- A cópia de uso fica em `BI-BIBLIOTECA\SISTEMA FJA\REVIT\pyRevit\` (copiada em 03/11/2026, ainda não em uso). Cada computador aponta o pyRevit para ela (configuração de extensões do usuário), uma vez só, e remove a cópia local de `%APPDATA%\pyRevit\Extensions`. Detalhes em `revit/README.md`.
- Atualizar = copiar a versão nova para o G: e recarregar o pyRevit.
- Compatibilidade: Revit 2024 e 2027 (pyRevit com IronPython), seguindo o estilo dos scripts atuais.

## 11. Pontos a confirmar

1. ~~Siglas de composição~~ — resolvido com o tipo Composição (sistema) no cadastro de siglas. Falta só cadastrar as siglas que a equipe já usa (hoje: DRY e PIN).
2. Nas famílias carregáveis, o nome do tipo também deve seguir "Type Mark - Descrição"? (Para portas e esquadrias o nome às vezes carrega dimensões.)
3. Pasta de distribuição da extensão: proposta `SISTEMA FJA\REVIT\pyRevit` (cópia já feita); falta confirmar e apontar o pyRevit de cada computador.
4. Quem mantém o `materiais-base.rvt` e a lista inicial de materiais-base.
5. A Legenda LUM deve passar a usar o keynote do sistema em vez de gerar códigos próprios?
6. Vedação/acabamento: como a equipe modela hoje a parede de acabamento (linha de localização, altura — até o forro ou até a laje —, união com a vedação para os vãos de portas e janelas, rodapé)? O mesmo princípio vale para pisos (laje = vedação/estrutura, piso = acabamento) e forros?

## 12. Fases sugeridas

1. **Fundação:** extensão no repositório e distribuição pelo G:; camadas e siglas no sistema; publicação de `biblioteca.json` e dos projetos.
2. **Nova composição:** escolha na biblioteca (qualquer sigla de composição) e criação no modelo, com materiais-base e vínculo.
3. **"Nova…" e pedidos:** criação de itens a partir do Revit, numeração reservada e processamento no sistema.
4. **Sincronizar tipos:** categorias com camadas, depois famílias carregáveis e materiais.
5. Ajustes: variações E=, Legenda LUM, paredes cortina.

Cada fase entra primeiro na dev do sistema e é testada num modelo de teste (ou com a transação desfeita) antes de chegar aos modelos dos projetos.
