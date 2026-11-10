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
  - **✓ em cada linha** aplica só aquele item; **Aplicar tudo** aplica todos.
  - Cada tipo é gravado numa subtransação, então um erro (como um nome de tipo repetido na família) não impede os outros e aparece na linha.
  - As reservas ficam num DataStorage (`FJA_CodigosPontos`, JSON `{sigla: {código: UniqueId do tipo}}`).
- **Legendas:**
  - nova opção **Texto dos itens**: só o nome, "código – nome" ou só o nome com a ordem pelo código;
  - a opção fica gravada em cada legenda;
  - a dica de cada item mostra o código.

## Fases
1. Ferramenta de codificação no modelo aberto, gravando o Type Mark, com a tabela no modelo.
2. Legendas: opção de texto e ordem pelo código.
3. Vínculos: tabela no federado, leitura dos pontos dos vínculos na Legendas, exportar e importar.
4. Quando o sistema incluir as complementares: sincronizar a tabela com o sistema, como já é feito com Tipos.
