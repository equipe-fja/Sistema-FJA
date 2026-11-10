# Codificação de pontos (disciplinas complementares) — plano

Situação: proposta aprovada em 10/11/2026 (decisões abaixo); implementação a começar.

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

## Modelos vinculados (decisão 4)
O Revit não deixa alterar, a partir do modelo federado, os parâmetros de um modelo vinculado. A proposta:
- **A tabela fica no modelo federado.** Num DataStorage, cada código é guardado por chave modelo vinculado (nome do arquivo) + UniqueId do tipo, com "família : tipo" como reserva. O UniqueId não muda ao recarregar o vínculo.
- **Legendas no federado:**
  - lê também os pontos dos vínculos (instâncias nos documentos vinculados);
  - usa os códigos da tabela para ordenar e montar os textos.
- **Tags de planta:** um tag de elemento vinculado mostra o Type Mark gravado no próprio vínculo. Para os tags baterem com a legenda, o código precisa chegar ao modelo da disciplina:
  - no federado, **Exportar códigos** grava `<modelo vinculado>.codigos-fja.json` na pasta do vínculo;
  - no modelo da disciplina, **Importar códigos** lê esse arquivo e grava os Type Marks;
  - a ferramenta avisa quando um Type Mark do vínculo difere da tabela do federado.
- **Alternativa mais simples** (se a FJA preferir): o código nasce sempre no modelo da disciplina, e o federado só lê o Type Mark do vínculo. Sem tabela no federado e sem arquivo intermediário, mas a codificação precisa ser feita abrindo cada modelo.

## Fases
1. Ferramenta de codificação no modelo aberto, gravando o Type Mark, com a tabela no modelo.
2. Legendas: opção de texto e ordem pelo código.
3. Vínculos: tabela no federado, leitura dos pontos dos vínculos na Legendas, exportar e importar.
4. Quando o sistema incluir as complementares: sincronizar a tabela com o sistema, como já é feito com Tipos.
