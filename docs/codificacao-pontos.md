# Codificação de pontos (disciplinas complementares) — plano

Situação: fases 1 e 2 implementadas na extensão 2.8.0 (10/11/2026), ainda não testadas no Revit. Fase 3 resolvida pelo Copiar do link (abaixo).

## Objetivo
Dar a cada tipo de ponto das disciplinas complementares — (LOG), (AUT), (SON) e, depois, ELE e LUM — um código, gravado no **Type Mark**. A ferramenta Legendas e os tags de planta passam a usar esse código.

É uma ferramenta própria, separada de Componentes e Tipos:
- **Componentes e Tipos** tratam itens do sistema: código FJ.SIGLA, keynote e Type Mark vindos da especificação.
- **A ferramenta de codificação** só mexe em tipos de famílias com prefixo de disciplina e nunca em tipos ligados ao sistema (entidade `FJA_SistemaTipo`).

## Decisões da FJA (10/11/2026)
1. **Formato:** por disciplina, `LOG.01`, `LOG.02`… (mesmo padrão "SIGLA.NN" dos keynotes).
2. **Abrangência:** por projeto (por modelo). Uma biblioteca comum entre projetos fica para quando o sistema incluir as complementares.
3. **Texto da legenda:** a pessoa escolhe entre:
   - só o nome;
   - "código – nome";
   - só ordenar pelo código.
4. **Modelos vinculados:** ver abaixo.

## Ferramenta (C#, Ferramentas FJA › Legendas › Codificação)
- Seletor de disciplina, igual ao da Legendas.
- Tabela com uma linha por tipo, com as colunas:
  - família : tipo;
  - quantidade no modelo;
  - Type Mark atual;
  - código proposto;
  - situação (novo, igual, fora do padrão, repetido, sem instância).
- **Numerar os sem código** segue a ordem da lista, que pode ser arrastada. Quem já tem código mantém o seu.
- O código pode ser editado direto na tabela.
- **Aplicar** grava os Type Marks numa única transação.
- **Numeração estável:** a tabela de códigos fica gravada no modelo (DataStorage com JSON), e um código não é reaproveitado sem confirmação.

## Modelos vinculados (decisão 4, revista em 10/11)
O Revit não deixa alterar, a partir do modelo federado, os parâmetros de um modelo vinculado. Por sugestão da FJA, o controle é feito pelo **Copiar do link** (Links › Copiar do link e Verificar alterações):
1. Os pontos do modelo da disciplina são copiados para o federado. Cada cópia leva `FJA_CopiaLink`, com o link e o UniqueId da origem.
2. Os tipos passam a existir no federado. A Codificação grava o Type Mark neles, então os tags do federado mostram o código e a Legendas lê as cópias.
3. **Verificar alterações** mantém as cópias em dia com o link. Quando a cópia é refeita, o tipo do federado, com o seu Type Mark, é mantido, porque tem o mesmo nome.
4. Na Codificação, a coluna **Origem** mostra "modelo", o nome do link ou "modelo + link".

Fica dispensado o arquivo de exportar e importar. Se um dia a equipe da disciplina quiser os mesmos códigos nos tags do próprio modelo dela, dá para acrescentar depois.

## Implementado (2.8.0)
- **Legendas › Codificação** (C#, comando `CodificacaoPontos`; `Legendas/Codificacao.cs` e `JanelaCodificacao.cs`):
  - mostra a disciplina (LOG, AUT, SON) e uma tabela de tipos;
  - **Numerar os sem código** (vazios ou fora do padrão) e **Renumerar tudo na ordem da lista**, que pede confirmação;
  - situação de cada código: sem código, repetido (bloqueia), fora do padrão (pede confirmação), já usado por tipo apagado, gravado, muda ao aplicar;
  - **Description (= nome do tipo)** (2.8.2): a Description, ao aplicar, também renomeia o tipo. Sugestão quando vazia: o nome do tipo; se a família tiver um tipo só, o nome da família sem a sigla.
  - **Família** editável sem a sigla, que é mantida, por exemplo `(LOG) `. Os tipos da mesma família acompanham a mudança.
  - **Árvore** (2.8.4): cada família é um nó, com sigla fixa, nome editável e "como está" ao lado; os tipos ficam embaixo, com Description, código e "como está". As famílias e os tipos (dentro da família) podem ser arrastados, e a ordem vale para numerar.
  - O **✓** aplica a linha: no tipo, só ele; na família, o nome e todos os tipos dela. **Aplicar tudo** aplica todos.
  - **Como está** (2.8.6): sem colunas próprias; aparece no tooltip de cada campo, e um **↶** ao lado do campo alterado volta ao valor gravado.
  - **Renumerar tudo** (2.8.7): recomeça em SIGLA.01 sem buracos. Os códigos de tipos apagados deixam de ficar reservados quando se aplica. O **Numerar os sem código** continua respeitando as reservas.
  - **Atualizar as legendas ao aplicar** (2.9.1, ligado por padrão): depois de aplicar, refaz as legendas gravadas da disciplina com os nomes, Descriptions e códigos novos. Mantém a ordem, os itens e as opções de cada legenda. O texto editado à mão de um tipo alterado volta a seguir a Description.
  - Ao renomear uma família, as famílias de símbolo dela (`(SIGLA) (LEG) NOME` e as variações) são renomeadas junto, então a legenda mantém os símbolos e as escalas.
  - Depois de gravar, a ferramenta confere se o nome do tipo e a Description ficaram mesmo gravados. Se não ficaram, mostra o erro na linha.
  - **Description por fórmula** (2.9.2): algumas famílias calculam a Description por fórmula (ex.: "(LOG) PONTO DE TV COAXIAL - CONJUNTO 4X2" = "4x2"); no projeto ela fica só leitura. A situação avisa, e ao aplicar a ferramenta pede para tirar a fórmula: abre a família, tira a fórmula mantendo o valor de cada tipo, recarrega e então grava a Description nova.
  - **✕ no tipo** (2.8.5): apaga o tipo e os elementos dele, com confirmação que mostra a quantidade. Se for o único tipo, apaga a família. O código continua reservado, e dá para desfazer com Ctrl+Z.
  - Cada tipo é gravado numa subtransação, então um erro (como um nome de tipo repetido na família) não impede os outros e aparece na linha.
  - As reservas ficam num DataStorage (`FJA_CodigosPontos`, JSON `{sigla: {código: UniqueId do tipo}}`).
- **Legendas** (2.8.8):
  - o texto de cada item é a **Description** do tipo, que a Codificação mantém igual ao nome do tipo; sem Description, continua o nome da família;
  - opção **Type Mark**: não mostrar, **em coluna própria** (entre o símbolo e o texto, com a largura do código mais largo) ou antes do texto ("LOG.01 – …"), gravada em cada legenda;
  - botão **Ordenar pelo Type Mark**.

## Fases
1. Ferramenta de codificação no modelo aberto, gravando o Type Mark, com a tabela no modelo.
2. Legendas: opção de texto e ordem pelo código.
3. Vínculos: tabela no federado, leitura dos pontos dos vínculos na Legendas, exportar e importar.
4. Quando o sistema incluir as complementares: sincronizar a tabela com o sistema, como já é feito com Tipos.
