# Para retomar (terça, 10/11/2026)

Situação em 09/11/2026, ~16h45.

## Sistema (web)
- **Oficial** (sistema-fja.web.app):
  - Abre no **painel da pessoa**, com o nome dela na árvore e "Olá, <nome>" no título.
  - **Drive pelo servidor** ligado: sem a barra "Conectar ao Google Drive", com a conta equipe@ autorizada em Manutenção › Conta Google do servidor.
  - **Avisos por e-mail das RIs** ligados, para pessoas reais. Disparam quando a RI é dirigida a alguém, quando chega resposta e quando é fechada ou cancelada. Cada pessoa desliga em Minha conta.
  - O **resumo diário** está desligado.
- **Homologação:** igual à oficial. Os e-mails vão todos para a equipe@, com a faixa "HOMOLOGAÇÃO". Os dados voltam a ser copiados da oficial todo dia às 04h.
- **A conferir em uso real na oficial:**
  - os primeiros avisos de RI chegando às pessoas certas;
  - imagens e planilhas abrindo sem login Google.
  - Se um aviso falhar, o motivo fica registrado (o Claude consulta).

## Revit — protótipo "Prévia LOG" (Ferramentas FJA › Legendas)
Janela com a prévia da legenda LOG desenhada pelo próprio Revit:
- **Lista de itens:**
  - incluir ou tirar cada item;
  - texto editável;
  - medida do símbolo no papel;
  - **escala por item de 25% a 300%**, em que 100% é o tamanho como aparece na prancha. Duplo clique volta a 100%.
- **Caixa vermelha:** ao mexer na escala, aparece uma caixa no tamanho novo, só com linhas, sem refazer famílias. Cada item alterado mantém a sua caixa até o **Atualizar prévia**.
- **Atualizar prévia:** refaz pelo **DWG** só os símbolos com escala diferente de 100%:
  1. a família no tamanho original é exportada numa vista de desenho 1:1;
  2. o DWG é importado na família `(LOG) (LEG) NOME` na escala que dá o tamanho pedido (mede e corrige).
- **Gerar legenda** grava. **Cancelar** desfaz tudo (grupo de transações).
- O botão **Legenda LOG** antigo continua igual.

**Estado em 09/11 (fim do dia):**
- A FJA testou e achou que "está melhorando", mas a interface estava instável e a abertura lenta.
- Últimas correções, ainda **não testadas**:
  - caixa por item, mantida em todos os itens alterados;
  - abertura mais rápida: a cor de cada símbolo é calculada uma vez por tipo e vista, não por elemento.
- Os arquivos estão no G: (extensão em uso) e no repositório.

**Ao abrir o Revit amanhã:** não usar o Reload do pyRevit (travou o Revit em 09/11). Fechar e abrir o Revit carrega a versão nova.

**A verificar no teste:**
1. Se a caixa vermelha aparece e fica visível em todos os itens alterados até o Atualizar.
2. Se a prévia do Revit redesenha sozinha depois de cada ajuste.
3. Tempo de abertura da ferramenta e tempo do Atualizar com 1 item alterado e com todos.
4. Se o símbolo vindo do DWG fica com a **cor roxa** da disciplina e com o desenho completo (preenchimentos, arcos).
5. Se a medida na lista bate com o tamanho pedido.

**Próximos passos possíveis:**
- **Desempenho do Atualizar.** Hoje cada atualização desfaz e refaz a legenda inteira. Melhorias possíveis:
  - guardar as famílias redimensionadas entre atualizações;
  - refazer só o que mudou.
- **Migrar a janela para C#.** O pedido bate nos critérios de migração da suíte (janela elaborada e desempenho), e a regra é **recomendar à FJA antes de seguir**. A versão em C# permitiria uma janela que fica aberta ao lado do modelo, prévia mais leve e menos dependência do IronPython. Decidir com a FJA.
- **Estender a janela** a AUT, SON, ELE e LUM depois que a de LOG estiver estável.

## Outros pendentes
- **Revit pelo servidor** (pedidos do Revit atendidos sem o sistema aberto): planejado, ver `docs/servidor.md`.
- **Resumo diário de e-mails:** desligado. Religa em `firebase/email/config.json` (`resumo.ativo`).
- **40 RIs fictícias na oficial:** script pronto, só sob pedido.
