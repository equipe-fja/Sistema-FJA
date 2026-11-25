# Histórico de versões da oficial

Cada entrada é uma publicação no artifact oficial (vNN = versão do artifact oficial; devNN = versão correspondente da desenvolvimento). Datas em 2026.

## Em desenvolvimento (ainda não publicado na oficial)
- (nada)

## oficial Firebase — 25/11, ~18h31 — commit 7abbd0e
- Extensão do Revit 2.40.0 (antes 2.39.0): Pranchas › Exportar, editor da regra do nome refeito — um **+** abaixo da última linha abre o menu com "Texto fixo" e os parâmetros; **separador por parte**, ao lado do ✕; sem o botão Padrão; a regra é gravada no modelo sempre que muda, também ao fechar sem exportar.

## oficial Firebase — 25/11, ~16h12 — commit 37d9083
- Emissões: emissão emitida fica travada; "Voltar para rascunho" pede o motivo e grava no histórico; "Pastas existentes" entram como Rascunho (commit 4f5184d). Voltar para rascunho também desfaz na RD o que a emissão gravou (só nos documentos que ninguém mudou depois) e tira o "emitida em"; emissão retroativa fica com a data da pasta como data de emissão.
- Extensão do Revit 2.39.0 (antes 2.38.0): Pranchas › Exportar, na regra do nome do arquivo, botão **+ texto** para acrescentar textos fixos além dos parâmetros (arrastáveis como as demais linhas; gravados com a regra no modelo; quem ainda estiver na 2.38 não apaga os textos ao salvar a regra).

## oficial Firebase — 24/11, ~18h40 — commit 83df833
- Emissões: "Pastas existentes" abre sempre o Explorer (Conexão com PC), sem pedir link do Drive.
- Ficha da emissão: sem o link "Pasta no Drive"; "Copiar pasta no G:" vira ícone de pasta com a dica "Abrir local dos arquivos".

## oficial Firebase — 24/11, ~18h20 — commit 19488d9
- Emissões vindas do Revit (Pranchas › Exportar, suíte 2.38.0): ao abrir a ficha e antes de emitir, o sistema localiza a pasta no Google Drive pelo caminho do G: e liga os arquivos pelo nome; arquivo que ainda não sincronizou é avisado no Emitir.
- Pasta compartilhada **por disciplina**: ao emitir, cada arquivo vai para a subpasta com o nome completo da disciplina (ex.: ARQUITETURA, criada se não existir), com SUBSTITUÍDOS e a conferência de revisão dentro dela; arquivo sem disciplina fica na raiz, com aviso na prévia. Vale para todas as emissões.
- **Emissões retroativas** (botão "Pastas existentes" em Emissões): a partir de pastas `AA-MM-DD …` (uma ou as subpastas datadas de uma pasta), registradas como Emitidas na data da pasta, com os arquivos já ligados ao Drive e às linhas da RD (histórico de revisões); sem cópia para a compartilhada; opção "Atualizar a RD" só onde a emissão for mais recente.
- Pastas no drive compartilhado BI-BIBLIOTECA também são localizadas pelo caminho do G: (além do PR-PROJETOS).
- GitHub Actions: a homologação passa a ser publicada só sob demanda (push na main não publica mais), para poupar a franquia de minutos.

## oficial Firebase — 24/11, ~17h35 — commit 1e89aef
- Extensão do Revit 2.38.0 (antes 2.37.0): **Pranchas › Exportar** com a opção **Registrar emissão no sistema** — escolhe emissão nova ou um Rascunho do projeto, disciplinas (já marcadas as das pranchas) e destinatários (envolvidos do projeto); depois de exportar, grava a emissão em Rascunho com a pasta e os arquivos, para completar e emitir em Projeto › Emissões. A pasta nova do Exportar passa a seguir o padrão `AA-MM-DD EMISSÃO - Descrição`. Na oficial, o Emitir dessas emissões ainda depende da etapa 2 no sistema web.
- Documentação: decisões da integração das Emissões com o Revit (docs/emissoes.md).

## oficial Firebase — 24/11, ~16h15 — commit f5c6d30
- **Emissões** (nova seção em cada projeto, ao lado de Relação de documentos, Atas e Croquis): registro de cada emissão com data, descrição, destinatários (opcionais) e arquivos. Ver docs/emissoes.md.
  - Arquivos enviados do computador (também arrastados do Explorer, direto do G:) para a subpasta **AA-MM-DD EMISSÃO - Descrição**, sem limite de tamanho (envio em partes, com progresso e retomada); mudar data ou descrição renomeia a pasta.
  - **Emitir** mostra um resumo antes de confirmar, com aviso quando uma prancha da RD vai sem PDF ou sem DWG ("Emitir mesmo assim").
  - **Pasta compartilhada** (opcional): copia os arquivos e move as revisões anteriores do mesmo documento, em qualquer extensão, para **SUBSTITUÍDOS**; se lá houver revisão maior, aquele documento não é copiado e a emissão fica "Conferir", com o botão "Copiar de novo".
  - **Relação de documentos**: ao emitir, os documentos de mesmo nome recebem a data de emissão, o status EMITIDO e a revisão emitida. Um contador ao lado da revisão (e a seção "Revisões emitidas" na ficha) abre o histórico, que leva à pasta de cada emissão.
  - **Destinatários**: escolhidos dos envolvidos do projeto; "+ Novo" cadastra só no projeto, e "↗" envia ao cadastro geral de Parceiros.
  - **Disciplina**: detectada pelos arquivos (documento da RD de mesmo nome, ou o código depois do projeto no nome, inclusive pela sigla do projeto) e editável; editar desliga a detecção, e "Detectar pelos arquivos" religa. Coluna na lista, com filtro.
- **Conexão com PC versão 7**: abre a pasta de uma emissão no Explorer com os arquivos da revisão já selecionados. Cada computador precisa instalar o pacote novo (Biblioteca › Ferramentas › Conexão com PC).
- **Painel inicial**: quadro **Avisos** quando as Ferramentas FJA de algum computador da pessoa ou a Conexão com PC deste navegador estão desatualizadas.
- Extensão do Revit 2.37.0 (antes 2.36.0): ao abrir o Revit, a suíte informa ao sistema a versão instalada em cada computador (usado pelos avisos do painel inicial).
- **Relação de documentos**: item duplicado (Ctrl+D) começa sempre na revisão R00.

## oficial Firebase — 24/11, ~14h05 — commit b4c6764
- **Atas de reunião**: ao excluir um item, um tópico, a ata ou a própria imagem, as imagens saem também dos arquivos. Elas são apagadas na próxima abertura do sistema, e só se nenhuma ata usar mais a imagem (assim o Ctrl+Z continua trazendo o item de volta com a imagem). Excluir item ou tópico com imagens pede confirmação.
- **Atas de reunião (Google Doc)**: o item só com imagem também entra no documento; as imagens vão reduzidas na coluna "imagens", entre item e descrição. Se o Drive recusar o documento com imagens, ele sai sem elas, com o aviso "[N imagens no PDF]" no item. Ainda falta confirmar numa exportação real se o Google Docs mostra as imagens.

## oficial Firebase — 24/11, ~16h30 — commit 6ec6c59
- **Cronograma**: um item pode **começar após o fim de outro** (campo "Começa após" na ficha, "Novo item após este" no botão direito da barra e "Item após este" na ficha). Quando o item anterior muda de data, os atrelados acompanham em cascata, sem mudar a duração. Arrastar o item atrelado define uma folga em dias, e no Detalhado uma seta liga os dois itens.
- **Relação de documentos**: **Ctrl+D** duplica o documento selecionado (ou vários). Cada cópia recebe o próximo número de item do mesmo grupo (disciplina, fase e bloco) e a emissão na data do dia, com status Emitido. Também disponível no botão direito ("Duplicar").
- **Atas de reunião**: as imagens dos itens ganham uma coluna própria, **Imagens**, entre Item e Descrição, com as miniaturas e o botão de adicionar; no PDF elas saem na coluna "imagens".

## oficial Firebase — 23/11, ~15h59 — commit bebc110
- **Atas de reunião**: cada item aceita **imagens** (ícone na linha do item, Ctrl+V com o cursor no item ou arrastar para a linha), com **miniatura** na descrição e visualizador ao clicar; no PDF da ata as imagens saem logo abaixo da linha do item, duas por linha. O Google Doc exportado continua só com o texto.

## oficial Firebase — 23/11, ~12h35 — commit 10d23a7
- Extensão do Revit 2.36.0 (antes 2.35.0): **Pranchas › Exportar** — a ordem dos parâmetros da regra do nome muda **arrastando**; **todos os parâmetros são opcionais** (em branco sai do nome, nenhuma prancha fica bloqueada) e o aviso de parâmetros em branco aparece só ao clicar em Exportar, com a lista das pranchas e o nome que vai sair.

## oficial Firebase — 23/11, ~12h30 — commit 83e8bcb
- Extensão do Revit 2.35.0 (antes 2.34.0): **Pranchas › Exportar** com **regra do nome** editável na janela (acrescentar parâmetro da folha ou do projeto, reordenar, tirar, marcar como opcional, separador), gravada no modelo para a equipe toda; o padrão FJA passa a incluir o **Trecho** como opcional antes do Sheet Number (ex.: 131-LE-ARQ-DT-HUB-H002-R00; sem Trecho, como antes).

## oficial Firebase — 23/11, ~12h15 — commit 6cb4975
- Extensão do Revit 2.34.0 (antes 2.33.0): **Pranchas › Exportar** com escolha de **conjunto de folhas** (sheet set: mostra e marca as folhas dele), opção **Criar pasta nova** na pasta de destino com o nome **AA-MM-DD + descrição** (ex.: 26-02-02 EMISSÃO FINAL EXECUTIVO - ARQUITETURA - 1ª PARTE; descrição lembrada por projeto) e janela sem textos explicativos.

## oficial Firebase — 23/11, ~12h05 — commit d488001
- Extensão do Revit 2.33.0 (antes 2.32.0): painel novo **Pranchas › Exportar** (C#) — PDF e DWG das pranchas ao mesmo tempo, um arquivo por prancha, na pasta escolhida (lembrada por projeto), com o nome do campo "Prancha" do carimbo (ex.: 128-AP-ARQ-DT-A002-R00); DWG pela configuração **FJA TRUE COLOR** (padrão do Revit, True Color, sem referências externas), criada no projeto se não existir. Painel novo: fechar e abrir o Revit depois de atualizar. Ver docs/exportar-pranchas.md.

## oficial Firebase — 23/11, ~11h25 — commit 42870ac
- Extensão do Revit 2.32.0 (antes 2.31.2): **Sincronizar tudo** com prévia na árvore — mostra só os tipos afetados, ← azul (recebe do sistema) e → roxo (envia ao sistema a Descrição alterada no Revit, só tipos já no padrão), com barra temporária de **OK** e **Cancelar** e a opção de renomear. Ver docs/revit-componentes.md.

## oficial Firebase — 23/11, ~11h10 — commit c4760e2
- Extensão do Revit 2.31.2 (antes 2.31.1): painel **Componentes** com ícones também para **Sincronizar tudo** (seta descendo do sistema para o modelo; âmbar quando há diferenças, contagem na dica) e **Reconectar** (elo, manutenção); todo ícone com dica.

## oficial Firebase — 23/11, ~11h00 — commit ebfeb54
- Extensão do Revit 2.31.1 (antes 2.31.0): painel **Componentes** com ícones de **Carregar família** e **Localizar seleção** no cabeçalho; o **↻ gira** enquanto o sistema e o modelo são relidos (a leitura do sistema passou para segundo plano, sem congelar o painel); textos explicativos viraram dicas (só os erros ficam visíveis).

## oficial Firebase — 19/11, ~14h15 — commit 53d174a
- Extensão do Revit 2.31.0 (antes 2.30.2): painel **Componentes** com caixas de seleção nos tipos, famílias e categorias e as setas **← Do sistema** (sincroniza os escolhidos) e **→ Para o sistema** (leva ao sistema a Descrição alterada no Revit, só em tipos já no padrão — ligados a um item do projeto e sincronizados antes; keynote e Type Mark não vão; o tipo fica aguardando até o sistema aplicar). Ver docs/revit-componentes.md.

## oficial Firebase — 19/11, ~13h38 — commit 14c233d
- Extensão do Revit 2.30.2 (antes 2.30.1): o **projeto do modelo é sempre automático** (pedido da FJA). No painel Componentes o projeto aparece só como texto; sem projeto reconhecido, mostra "Sem projeto" e o botão **Conectar a um projeto…**; trocar ou desfazer uma ligação existente é manutenção, no botão discreto **Reconectar…** (com aviso). Em Composições e Diagnóstico a lista de projetos fica travada quando há projeto e só se libera para conectar.

## oficial Firebase — 19/11, ~13h10 — commit 47949b2
- Extensão do Revit 2.30.1 (antes 2.30.0): painel **Componentes** com os botões "Carregar família…" e "Localizar seleção" inteiros (estavam cortados) e o ↻ visível; ao trocar de modelo, relê o sistema se os dados tiverem mais de 5 minutos.
- **Financeiro**: novo galho na árvore (tela inicial em construção) e níveis **Gerente** (administra tudo e vê o Financeiro, mas só exclui o que criou) e **Financeiro** (só o galho Financeiro e Minha conta); regras do banco: coleções fin_* só para Master, Gerente e Financeiro; Administrador só atribui Administrador e Convidado (commit d218e80; só regras, sem alterar dados).
- **Fornecedores**: campo Categoria, lista agrupável por categoria e lista com ordenar, agrupar e largura de colunas como as outras planilhas (commits 5271792, 724e229).
- **Materiais**: datasheet vira ícone pequeno com a dica "Datasheet" (commit 208fa89).

## oficial Firebase — 19/11, ~12h45 — commit fad257c
- Extensão do Revit 2.30.0 (antes 2.29.2): **projeto do modelo** — todas as ferramentas ligadas ao sistema (painel Componentes, Composições, Diagnóstico, cor da disciplina nas Legendas) reconhecem o projeto do mesmo jeito e gravam a ligação no próprio modelo (vale para a equipe depois de sincronizar). Sem ligação, pistas em ordem: Número do projeto, pasta do caminho (do central nas cópias locais), nome do arquivo, código do cliente, nome do projeto. Escolher ou trocar o projeto na lista grava a ligação. Ver docs/projeto-do-modelo.md.
- Sistema: a publicação para o Revit passa a listar **todos os projetos** (os sem especificação técnica com semEspec), para o Revit reconhecer qualquer projeto.

## oficial Firebase — 19/11, ~12h25 — commit e527398
- Extensão do Revit 2.29.2 (antes 2.29.1): o painel **Componentes** não fica mais preto depois de reabrir o Revit. A montagem automática na abertura falhava e o botão não montava de novo; agora o botão monta sempre que a moldura não tem o painel, a moldura tem fundo claro e mostra o motivo se a montagem falhar.

## oficial Firebase — 19/11, ~12h20 — commit e9e6062
- **Fornecedores e materiais** (etapas 1 a 4):
  - **Biblioteca › Fornecedores** (fabricantes): ficha com descrição, site, siglas que atende, contatos e logomarca, e a lista de materiais do fabricante. Os materiais (nome comercial, código, descrição, datasheet) são vinculados aos itens da biblioteca central que atendem, com uma nota por vínculo.
  - **Item da biblioteca central**: lista ordenada de "Materiais recomendados"; os 3 primeiros são o padrão dos projetos.
  - **Keynote do projeto**: "Materiais neste projeto" herda os 3 primeiros do item central e pode ser ajustado (só entre os vinculados ao item central), com avisos (sem item central, item central sem recomendados, menos de 3). Coluna Materiais na lista da especificação técnica.
  - **Cotação por projeto**: preço, prazo de entrega e histórico de contatos de cada material, só no keynote do projeto.
  - **Saídas**: materiais no PDF da especificação técnica (sem preços) e nos arquivos do Revit (o uso na aba Ferramentas FJA fica para a extensão). Sugestões de fornecedores a partir do campo "Fabricante" da biblioteca central, criados só com o clique.
  - Incluir e alterar fornecedores, materiais, vínculos e cotações: só administradores. Nova coleção `materiais` (entra no backup e no desfazer); nenhum dado existente foi alterado.

## oficial Firebase — 19/11, ~12h05 — commit ade36cf
- Extensão do Revit 2.29.1 (antes 2.29.0): **Componentes** abre numa janela, com as mesmas funções, quando o painel encaixável ainda não foi criado (o Revit só cria painéis na abertura; depois do Suíte › Atualizar, até fechar e abrir o Revit). Corrige o erro "The requested dockable pane has not been created yet".
- **Meu painel**: atalhos para os projetos da pessoa no topo; indicadores menores no fim (commit 837c04c).
- **Ferramentas**: o link do pyRevit vira um cartão igual aos demais (commit 5724f90).

## oficial Firebase — 19/11, ~11h45 — commit d8fa2f9
- Extensão do Revit 2.29.0 (antes 2.28.0): o painel **Componentes** passa a fazer tudo o que a janela antiga fazia — **Carregar família…** (.rfa), **Ligar › Código novo** (sigla da lista ou sigla nova, descrição e especificação; FJ e keynote reservados e confirmados pelo sistema), **Renomear a família como (SIGLA) DESCRIÇÃO** e **Localizar seleção** (mostra na árvore o tipo do elemento selecionado). O botão **Novo componente** (janela em Python) foi retirado. Ver docs/revit-componentes.md.

## oficial Firebase — 19/11, ~11h35 — commit ca0d5e6
- Extensão do Revit 2.28.0 (antes 2.27.0): **Tipos › Componentes** vira um **painel encaixável** em C# (em aba com Propriedades): árvore Categoria › Família › Tipo de todas as famílias carregáveis do modelo, com busca, filtro e cor pela situação no Sistema FJA (verde = ligado a um keynote do projeto e igual ao sistema; âmbar = com diferença; azul = aguardando; vermelho = código inexistente; cinza = sem ligação). Detalhes do nó, **Ligar a um código** (tipo ou família), **Sincronizar** (nó ou tudo; o sistema é a referência), Selecionar no modelo e Desligar. A janela antiga continua como **Novo componente** (carregar .rfa e código novo). Painel novo: fechar e abrir o Revit depois de atualizar. Ver docs/revit-componentes.md.

## oficial Firebase — 19/11, ~11h05 — commit 31934b0
- **Ficha do item**: os campos abrem travados (só leitura). O botão **Editar** (com lápis), ao lado de "Ficha", libera a edição; **Concluir** trava de novo. Vale para os campos da Ficha; nome, pasta e status continuam editáveis.
- **PDF**: a explicação do menu vira dica de um "?" ao lado do título "Exportar em PDF". O botão "Selecionar itens na tabela…" saiu: "Selecionados" entra direto no modo de seleção (commits 0d8a610 e e863971).

## oficial Firebase — 18/11, ~18h35 — commit 65268ef
- **Listas em PDF**: escolher os itens na própria tabela ("Selecionar itens na tabela…", com clique e Shift nas linhas) e gerar só os selecionados; nova opção de **capa** em papel timbrado (título, projeto, cliente, emissão e quantidade de itens), ligada por padrão.
- **Comentários**: respostas com histórico, como na RI (bloco Respostas e bloco Histórico na ficha, coluna Respostas na tabela); o histórico registra também as mudanças feitas direto nas células.
- **Ferramentas**: link para baixar a última versão do pyRevit, na lista e na página da Extensão do Revit.
- **Projetos**: os arquivados ficam recolhidos num grupo "Arquivados" no fim da lista.
- **Meu painel**: sem o texto de apresentação abaixo do título.

## oficial Firebase — 18/11, ~17h40 — commit 9de80df
- Botão **Sair** (log off) no topo, ao lado do indicador de conexão. Com alterações não salvas, avisa em vez de sair.

## oficial Firebase — 18/11, ~17h25 — commit 6eae619
- **Meu painel** mostra só o que cita a pessoa num campo: RIs em que ela é requerida ou requerente, e pendências e comentários cujo responsável cita o nome dela. Ter criado o registro, ou estar ligado só pela empresa ou pelos parceiros da disciplina, deixou de contar.

## oficial Firebase — 18/11, ~17h15 — commit ca94a32
- Botões **voltar e avançar do navegador** passam a navegar entre as telas do sistema, voltando à rolagem em que cada tela estava. Com alterações não salvas, a tela fica onde está e aparece o aviso de salvar ou descartar.

## oficial Firebase — 18/11, ~15h15 — commit 89e459e
- Realce ligado entre o calendário anual e as linhas do tempo mais suave: tons leves e contornos finos, marcadores que crescem menos, linha-guia mais discreta e uma transição curta ao entrar e sair do realce.

## oficial Firebase — 18/11, ~15h05 — commit 5e90fbb
- **Calendário/Cronograma** (antes Cronograma), com os modos Resumido e Detalhado:
  - Calendário anual em colunas (um mês por coluna, um dia por linha) abaixo da linha do tempo do Resumido, alinhado à régua dos meses. Mostra todos os marcos, os eventos, a etapa em andamento, os feriados, as datas importantes e os aniversários.
  - Clicar num dia inclui um evento do projeto; administradores também incluem datas importantes e aniversários. O painel lateral lista o que já existe naquele dia.
  - Zoom com Ctrl + roda do mouse (ou a roda sobre os meses) no Resumido e no Detalhado, com o botão "Redefinir zoom".
  - Realce ligado: o mouse num dia do calendário destaca a data na linha do tempo, e o mouse num marco ou etapa destaca os dias no calendário.
- **Painel de Projetos**: bloco "Linhas do tempo" com as etapas, os marcos principais e os eventos de cada projeto ativo numa régua comum, a linha "Datas importantes" (feriados, datas e aniversários) e o calendário anual; zoom e realce como no Resumido.
- **Datas importantes**: feriados nacionais calculados automaticamente (inclusive Carnaval, Sexta-feira Santa e Corpus Christi); feriados próprios, datas importantes e aniversários cadastrados pelos administradores.
- Nova coleção `agenda` no banco (eventos, feriados próprios, datas importantes e aniversários), incluída no backup e no desfazer. Nenhum dado existente foi alterado.

## oficial Firebase — 13/11, ~17h53 — commit 1c5052e
- Extensão do Revit 2.27.0 (antes 2.26.10): **Cotar vistas** na planta 1:75 cota também os **elementos notáveis** para levantamentos por nuvem de pontos: o comprimento de cada trecho de espessura nas paredes que mudam de espessura, as pontas livres de parede até a primeira parede perpendicular e os pilares isolados até as paredes mais próximas, nas duas direções. Testado pelo conector na PMA.NÍVEL 1 (101 cotas, nenhuma recusada). Ver docs/cotagem.md.

## oficial Firebase — 13/11, ~17h24 — commit e4cd951
- Extensão do Revit 2.26.10 (antes 2.26.9): **Estender/Alinhar** — quando a referência é outro eixo ou nível paralelo, o clicado fica com a mesma extensão dele (as duas pontas); vale também para os já selecionados. O código entrou no commit da 2.26.9, mas a compilação daquela versão saiu sem ele; esta recompila.

## oficial Firebase — 13/11, ~17h25 — commit e6b4d30
- Extensão do Revit 2.26.9 (antes 2.26.8): correções do teste do **Cotar vistas** feito pelo conector na PMA.NÍVEL 1. O desvio dos números passa a valer também nas cadeias, que antes ficavam de fora por um erro. O retângulo do número parte da base, sobre a linha, e o número não cai mais riscado pela própria linha. As cotas apoiadas em faces de paredes fora de esquadro só são feitas se o desvio ao longo do vão for de até 3 cm, o que evita as cotas inclinadas em vãos longos.

## oficial Firebase — 13/11, ~17h13 — commit 89612d3
- Extensão do Revit 2.26.8 (antes 2.26.7): **Estender/Alinhar** passa a ser só para eixos e níveis. Nos cortes, a API do Revit só muda a largura do recorte, que não move as cabeças na planta (a linha do corte é independente). Clicar num corte mostra o aviso, e o corte continua aceito como linha de referência. Os recortes dos cortes 4, 5 e 6 do modelo 131, mudados nos testes, foram restaurados com autorização da FJA.

## oficial Firebase — 13/11, ~17h05 — commit 30488d8
- Extensão do Revit 2.26.7 (antes 2.26.6): **Cotar vistas** coloca os números deslocados o mais perto possível do centro do trecho. As posições testadas partem do centro, começando por ele mesmo.

## oficial Firebase — 13/11, ~16h58 — commit 7839bca
- Extensão do Revit 2.26.6 (antes 2.26.5): **Cotar vistas** move os números das cotas o mínimo possível. Entre as posições livres, fica a mais perto da original, e o número só se move se a sobreposição cair pelo menos 20 %. A cota com número deslocado fica sem leader (parâmetro "Leader" desligado).

## oficial Firebase — 13/11, ~16h52 — commit 42b2168
- Extensão do Revit 2.26.5 (antes 2.26.4): **Estender/Alinhar** redesenha a vista depois de cada mudança e avisa quando o elemento já está na linha de referência. No teste da FJA o corte foi aparado no modelo, mas a tela não mostrava.

## oficial Firebase — 13/11, ~16h43 — commit 534b32b
- Extensão do Revit 2.26.4 (antes 2.26.3): **Cotar vistas** desvia os números das cotas também de paredes, escadas, mobiliário, louças e das linhas de outras cotas e de eixos, cada tipo de obstáculo com um peso. Fica a posição que menos atrapalha, mesmo sem nenhuma posição totalmente livre, com passos menores ao longo do trecho.

## oficial Firebase — 13/11, ~16h22 — commit 0f5e7d8
- Extensão do Revit 2.26.3 (antes 2.26.2): **Cotar vistas** volta a cotar as paredes internas fora de esquadro, que a 2.26.1 deixava sem cota. Entre faces não paralelas, mede de uma face até a quina da outra parede, e a cota sai praticamente paralela ao eixo. Se o Revit recusar, usa um par de quinas na mesma altura (até 3 cm), perto da origem. Só sem nenhuma dessas opções o trecho fica sem cota.

## oficial Firebase — 13/11, ~16h16 — commit 95e2476
- Extensão do Revit 2.26.2 (antes 2.26.1): **Estender/Alinhar** com cortes. No teste da FJA o clique no corte era ignorado sem aviso; agora os cliques aceitam qualquer elemento, o corte é reconhecido também como vista de corte ou pela categoria Cortes, e o que não serve abre uma mensagem dizendo o que foi clicado. Vistas selecionadas no navegador não entram no alinhamento automático.

## oficial Firebase — 13/11, ~16h15 — commit 2562f2f
- Extensão do Revit 2.26.1 (antes 2.26.0): **Cotar vistas** não cria mais cotas tortas entre paredes fora de esquadro. Entre faces não paralelas, usa o par de quinas na mesma altura mais perto da origem (em cima ou à esquerda), com a linha junto delas. Sem um par assim, o trecho fica sem cota e o relatório avisa. Vale para as cadeias internas e para os ambientes da 1:200.

## oficial Firebase — 13/11, ~16h05 — commit aa6d66b
- Extensão do Revit 2.26.0 (antes 2.25.1): **Cotar vistas** desvia o número das cotas que cria quando ele encosta em outro elemento: textos de outras cotas, etiquetas, textos, anotações, portas, janelas e pilares. Tenta, nesta ordem, o outro lado da linha, um deslocamento ao longo do trecho e, em trechos curtos, uma posição além das pontas. O relatório diz quantos números foram deslocados e quantos ficaram sem lugar livre. As cotas feitas à mão não são movidas.

## oficial Firebase — 13/11, ~15h57 — commit 3cd2c4d
- Extensão do Revit 2.25.1 (antes 2.25.0): **Cotar vistas**, nas cadeias internas, deixa de repetir a mesma medida em várias linhas. Cada par de paredes vizinhas é cotado uma vez só, na linha mais perto da origem (cadeias horizontais a partir da de cima, verticais a partir da da esquerda). Com paredes tortas, a medida muda ao longo da parede.

## oficial Firebase — 13/11, ~15h53 — commit b3bc9ed
- Extensão do Revit 2.25.0 (antes 2.24.1): painel novo **Grids, Níveis e Cortes**. O **Trocar lado** sai da Cotagem e vai para ele. Ferramenta nova **Estender/Alinhar**, como o Aparar/Estender do Revit: clica-se na linha de referência (linha, eixo, nível, plano de referência ou corte) e depois em cada eixo, nível ou corte, do lado que fica; os já selecionados ao abrir são alinhados de uma vez. Muda a extensão no modo em que cada ponta já está (3D ou 2D); no corte, muda a largura. Painel novo: fechar e abrir o Revit depois de atualizar. Ver docs/grids-niveis-cortes.md.

## oficial Firebase — 13/11, ~15h50 — commit 9677524
- Extensão do Revit 2.24.1 (antes 2.24.0): no primeiro teste das **cadeias internas**, a FJA achou cotas demais. Por padrão, elas passam a medir só os vãos dos ambientes, face acabada a face acabada. A opção **"Incluir a espessura das paredes"**, na janela do Cotar vistas, volta à cadeia contínua de vão + espessura.

## oficial Firebase — 13/11, ~15h40 — commit 4e105ee
- Extensão do Revit 2.24.0 (antes 2.23.5): **Cotar vistas** na planta 1:75 (1:50 a 1:149) passa a criar as **cadeias internas**. Usa o mínimo de linhas, em cada direção, que meça todas as paredes, de fachada a fachada, face acabada a face acabada (vão + espessura). Linhas que passariam por um vão são evitadas. Em paredes fora de esquadro, a cadeia é dividida em cotas de faces paralelas, ligadas pelas quinas. Ver docs/cotagem.md.

## oficial Firebase — 13/11, ~13h40 — commit 108181e
- Extensão do Revit 2.23.5 (antes 2.23.4): **Cotar vistas** passa a cotar a fachada e os ambientes pelas **quinas** das paredes (arestas verticais), como a FJA faz à mão com Tab. As paredes do modelo estão levemente fora de esquadro entre si, e o Revit recusava as cotas entre faces não paralelas. Se o vão livre de uma porta ou janela for recusado, o trecho é cotado pelas ombreiras. O texto da janela foi atualizado.

## oficial Firebase — 13/11, ~13h37 — commit f37724f
- Extensão do Revit 2.23.4 (antes 2.23.3): **Trocar lado** não mexe mais na extensão de eixos e níveis — só liga e desliga as indicações na vista ativa. A 2.23.2 ainda esticava os níveis no teste da FJA. Para os que já ficaram esticados: selecionar e usar Redefinir para extensões 3D (ou Ctrl+Z).

## oficial Firebase — 13/11, ~13h30 — commit 732b721
- Extensão do Revit 2.23.3 (antes 2.23.2): **Cotar vistas**, correções das cotas de fachada no teste da FJA (PMA.NÍVEL 1, vista girada). Antes, as cotas saíam sem valor (−0,30) e invisíveis. Agora usam referências estáveis das paredes, a face real de cada parede (que desvia um pouco da vista), os cantos, e descartam faces escondidas atrás de outra parede e tocos do outro lado do edifício. Cota que o Revit criar sem valor é apagada e avisada. Para limpar as cotas antigas, rodar com "Refazer as minhas".

## oficial Firebase — 13/11, ~13h26 — commit dac7ea4
- Extensão do Revit 2.23.2 (antes 2.23.1): correção do **Trocar lado** — ao passar eixos e níveis para 2D, a extensão que aparece na vista é mantida. Antes, com a ponta em 3D, o Revit assumia uma extensão 2D antiga e bem mais longa (linhas esticadas, cabeças longe). A mesma correção vale para a leitura e a extensão dos eixos no **Cotar vistas**.

## oficial Firebase — 13/11, ~13h17 — commit 52b62da
- Extensão do Revit 2.23.1 (antes 2.23.0): **Cotar vistas** corrigido no primeiro teste da FJA (PMA.NÍVEL 1, 1:100). A fachada passa a considerar só o edifício principal, o maior grupo de paredes que se encostam: um muro mais ao norte fazia a cadeia de eixos sair a 10 m do edifício e, à esquerda, a fachada não era encontrada. Os eixos voltam a ser estendidos em vista girada (antes: "The curve is unbound or not coincident").

## oficial Firebase — 13/11, ~13h13 — commit 9784b49
- Extensão do Revit 2.23.0 (antes 2.22.0): **Trocar lado** usa sempre os eixos e níveis selecionados (sem seleção, pede para escolher). A janela tem dois cartões, Eixos (em cima/embaixo) e Níveis (à esquerda/à direita), com as opções em blocos desenhados no estilo do Sistema FJA (um lado, o outro, nos dois, trocar, não mexer) e sem textos explicativos.

## oficial Firebase — 13/11, ~13h05 — commit 0666362
- Extensão do Revit 2.22.0 (antes 2.21.0): **Cotar vistas** segue as decisões da FJA de 13/11.
  - Planta 1:75: vãos de fachada pelo **vão livre** das portas e janelas. A cadeia de eixos fica sempre em cima e à esquerda, **por fora** das cotas de fachada; os eixos cujas bolhas não cabem são estendidos nessa vista (em 2D).
  - Planta 1:200: **dimensões gerais de cada ambiente** (duas cotas perpendiculares, pelas faces acabadas) e o total do **contorno** do edifício.
- **Legendas:** só os pontos de parede ("Elevação do ponto") mostram altura no texto das variações. Os pontos de teto, como access points, ficam sem altura.

## oficial Firebase — 13/11, ~12h58 — commit f32e827
- Extensão do Revit 2.21.0 (antes 2.20.0): **Cotagem › Trocar lado** abre uma janela. Escolhe os eixos e níveis selecionados ou todos os da vista e, separando as linhas verticais (em cima/embaixo) das horizontais (esquerda/direita), liga a indicação toda de um lado, toda do outro, dos dois lados, troca de lado ou não mexe. Só na vista ativa (pontas em 2D).

## oficial Firebase — 13/11, ~12h35 — commit a306e1a
- Extensão do Revit 2.20.0 (antes 2.19.0): **Cotar vistas** na planta baixa 1:75 (1:50 a 1:149) passa a cotar também os **panos e vãos de fachada** nos quatro lados (ombreiras, pontas de parede e cantos externos, em osso) e o total de cada fachada, além dos eixos. Ver docs/cotagem.md.

## oficial Firebase — 13/11, ~12h10 — commit 7f7afa8
- Extensão do Revit 2.19.0 (antes 2.18.0): **Cotagem › Trocar lado** — nos eixos e níveis selecionados, passa as pontas para 2D e troca a indicação (bolha/cabeça) para o lado oposto, só na vista ativa.

## oficial Firebase — 13/11, ~11h30 — commit 2a5ec1b
- Extensão do Revit 2.18.0 (antes 2.17.1): painel novo **Cotagem › Cotar vistas** (C#). Tipo da vista pelo template (ou escolhido numa lista), regra por tipo × escala, cadeias de eixos e de níveis com total, só acrescenta o que ainda não está cotado (modo Refazer as minhas para as cotas da própria ferramenta). Painel novo: fechar e abrir o Revit depois de atualizar. Ver docs/cotagem.md.

## oficial Firebase — 12/11, ~18h40 — commit a3f97c5
- Pendências: o status pelas datas passa a valer também na carga em tempo real da oficial (previsão vencida sem finalização vira pendente) e é recalculado a cada 10 minutos com a página aberta; o status fica só leitura em qualquer formulário e a edição pela tabela é recusada.

## oficial Firebase — 12/11, ~18h30 — commit 937e28e
- Pendências: status definido pelas datas — finalização preenchida = concluído; previsão "sem data" = informativo; previsão vencida = pendente; senão em andamento (nova pendência começa em andamento, com início hoje). O status deixa de ser escolhido à mão; a previsão ganha a opção "Sem data". Recalculado ao carregar, ao editar datas e ao importar (o banco só muda quando a pendência é salva). Concluídas sem data de finalização e informativas sem previsão, de antes da regra, continuam como estavam.

## oficial Firebase — 12/11, ~18h10 — commit 1d9115a
- Listas (Equipamentos, Comentários, Pendências/Histórico, Relação de documentos): ícone de exportar planilha (.xlsx) ao lado do de PDF, no mesmo estilo.
- Extensão do Revit 2.17.1: janelas de Links com filtros que quebram linha (sem esticar), resumo e rodapé numa linha, coluna Nível mais larga; sistema das conexões pelos conectores.

## oficial Firebase — 12/11, ~17h55 — commit eec4121
- Extensão do Revit 2.17.0 (antes 2.16.1): Verificar alterações colore temporariamente as cópias na vista ativa (cor por situação; Limpar cores devolve a aparência anterior). 2.16.2: painel Suíte sempre no fim da aba, também depois de Reload (startup.py).

## oficial Firebase — 12/11, ~17h30 — commit 9b13a4d
- Importar da planilha (Relação de documentos e demais listas): a revisão passa a ser feita na própria tabela, no lugar da janela de relatório — moldura vermelha temporária, faixa acima do cabeçalho com as situações (contagem, marcar todos, ver só uma situação) e botões Cancelar e Aceitar; linhas coloridas pela situação (correção, apagar, possível duplicata, novo, a atualizar com as células alteradas em destaque, só mudou de linha, editado no sistema, sem mudança, só no sistema); clicar numa linha inclui ou exclui. Nada é gravado antes de Aceitar.
- Manutenção feita no banco: removidas as 206 cópias com apóstrofo ('005) da Relação de documentos do SESC Hotel Angra, todas com o documento equivalente sem apóstrofo (registro em logs/manutencao.txt, branch firebase-log).

## oficial Firebase — 12/11, ~17h10 — commit 7c4ba15
- Extensão do Revit 2.16.1 (antes 2.15.3): Links › Copiar do link e Verificar alterações em C#; módulo **Encaminhamentos** (tubos, dutos, eletrocalhas, eletrodutos, flexíveis, conexões e acessórios — copia mesmo sem conexão) e botão **Reconectar** (como no link e pontas coincidentes). Mesmo registro FJA_CopiaLink: cópias antigas continuam reconhecidas. Painel Suíte sempre no fim da aba.
- Importar da planilha (Relação de documentos) vira ação de manutenção com relatório de conciliação antes de gravar (nada é gravado sem o aceite).

## oficial Firebase — 12/11, ~16h30 — commit 7337872
- Extensão do Revit 2.15.3 (antes 2.3.0 na oficial): ferramentas em C# — Pontos › Legendas e Codificação, Símbolos e Detalhes › Família do desenho, Suíte › Sobre. A página publica também a biblioteca C# (pasta bin/) no banco, em partes (revitPub/extensao-bin-<i>), e o instalador .zip passa a levá-la; o Suíte › Atualizar baixa a biblioteca quando ela muda. Painéis novos exigem fechar e abrir o Revit.
- Publicação para o Revit (biblioteca.json): cores das disciplinas por projeto (coresProjetos), usadas pelas Legendas.
- Seletor de cor próprio da FJA no lugar do seletor nativo (área de cor, HEX/RGB, conta-gotas, cores do projeto, padrão FJA, recentes, aviso de contraste).

## oficial Firebase — 06/11, ~15h10 — commit fcaf065 (dev v159)
- Extensão do Revit 2.2.0: botão Ferramentas FJA › Suíte › Atualizar no Revit. A página publica o pacote da extensão no banco (revitPub/extensao-versao e revitPub/extensao) cerca de 20 s depois de aberta por alguém da equipe, quando a versão embutida é mais nova; o botão baixa, grava na pasta da extensão e recarrega o pyRevit.

## oficial Firebase — 06/11, ~14h55 — commit 5980850 (dev v158)
- Extensão do Revit: botão "Baixar o instalador" — o .zip traz a extensão, instalar.cmd e LEIA-ME. O instalador copia para a pasta compartilhada do G: (sem substituir versão mais nova), cadastra a pasta no pyRevit do usuário e tira a cópia local antiga; sem o G:, instala só no computador.
- Tela da extensão: caminhos do G: voltam a aparecer com as barras.

## oficial Firebase — 06/11, ~14h45 — commit 6213333 (dev v157)
- Extensão do Revit 2.1.0 (Biblioteca › Ferramentas › Extensão do Revit): novo painel Links › Copiar do link (módulo Equipamentos); a tela avisa a novidade e explica como atualizar.
- Drive pelo servidor engatilhado (função driveToken), desligado em firebase/servidor.json — sem efeito até ser ligado.

## oficial Firebase — 09/11, ~11h45 — commit e717d87
- O sistema abre no painel da pessoa ao entrar; na árvore o painel leva o nome do usuário e a tela abre com "Olá, <nome>".

## oficial Firebase — 09/11, ~11h30 — commit 96ce36f
- Conta Google do servidor autorizada na oficial; ligados o Drive pelo servidor (sem "Conectar ao Google Drive") e os e-mails de aviso das RIs (pessoas reais; cada uma desliga em Minha conta).

## oficial Firebase — 09/11, ~11h20 — commit fa25a7b
- Meu painel: ações pendentes (RIs aguardando você, RIs suas em aberto, pendências e comentários ligados a você/sua empresa) e atualizações recentes dos projetos em que a pessoa está envolvida; também para convidados (só RI).
- Manutenção › Conta Google do servidor (só Master): autorização única da equipe@ para o servidor usar Drive, Planilhas e enviar e-mails. Drive pelo servidor e e-mails de aviso das RIs ligam depois dessa autorização. Resumo diário desligado.

## oficial Firebase — 06/11, ~17h50
- Servidor (Cloud Functions, plano Blaze): ligadas as rotinas "equipe" (o que o convidado vê de cada projeto, atualizado na hora + conferência às 3h30) e "autoria" (desfaz edição/exclusão de resposta ou comentário alheio na RI). Funções em Node 22.

## oficial Firebase — 06/11, ~17h50 — commit 2f13a21
- Servidor (Cloud Functions, plano Blaze) ligado na oficial: rotinas "equipe" (projetosRfi e participantes atualizados na hora + conferência às 3h30) e "autoria" (desfaz edição/exclusão de resposta ou comentário alheio). Funções em Node 22. E-mails e Drive pelo servidor seguem desligados.

## oficial Firebase — 06/11, ~13h45 — commit e61cf30
- RFI passa a se chamar RI · Requisição de Informações (textos, PDF e e-mail; dados sem mudança).
- Link direto para uma RI (#ri=<projeto>/<id>), usado pelos e-mails.
- Regras do banco: preferência de avisos (acessos.avisos) e registro emailsEnviados.
- Servidor (Cloud Functions) pronto e desligado: e-mails de aviso e resumo diário, equipe dos projetos, autoria das respostas/comentários. Ligam depois do Blaze (firebase/email/config.json, firebase/servidor.json).

## oficial Firebase — 06/11, ~13h10 — commit 2e80210
- RFI: histórico geral do projeto (ampulheta na lista); ampulhetas ocultas para convidados.
- Planilhas: sem o botão Colunas (botão direito no cabeçalho); barra horizontal própria, que segue o mouse sem saltos.
- RFI: coluna Comentários sempre visível; status alterável por qualquer administrador (regras do banco).
- Fichas novas (RFI, pendências, comentários, equipamentos, relação de documentos) só são gravadas ao Salvar; Descartar desiste.
- RFI: imagem guardada no próprio registro; convidados enviam, colam (Ctrl+V) e veem imagens.
- Convidado com acesso às RFIs de todos os projetos (opção na ficha da pessoa).
- Homologação: seletor "Ver como" (só lá); conta Convidado Teste e 5 RFIs de teste no projeto 128 (só lá).

## oficial Firebase — 06/11, ~12h20 — commit 68a80f2
- RFI: só o autor altera os dados e exclui a RFI; cada resposta ou comentário só quem escreveu; o Master pode tudo (regras do banco: autorId; RFIs antigas usam o requerente).
- RFI: histórico (ampulheta, ficha lateral) com criação, alterações campo a campo, respostas, comentários, imagem e notificações, com autor e hora; o banco só deixa acrescentar.
- RFI: convidado escolhe como requerido qualquer envolvido no projeto (projetosRfi.envolvidos, sem e-mails).
- RFI: coluna Imagem primeiro e visível por padrão; Ctrl+V com print vira a imagem do registro nas fichas com quadro de imagem.

## oficial Firebase — 05/11, ~19h05 — commit 8ce1283
- RFI: respostas e comentários com autor e hora; convidado cria RFI nos projetos de que participa e edita as que abriu (regras: criaConvidado, minhaRfi).

## oficial Firebase — 05/11, ~18h40 — commit d199a45
- Cache local do banco (menos leituras ao reabrir). Contexto: a cota gratuita de leituras (50 mil/dia) estourou em 05/11 (74 mil).

## oficial Firebase — 05/11, ~18h25 — commit b3802a7
- RFI: vários requeridos (pessoas e empresas; regras do banco com requeridosIds, compatível com RFIs antigas); Notificar por e-mail (abre o e-mail com a mensagem pronta e registra).
- RFI: disciplinas envolvidas e requeridos no quadro RFI, após o requerente; requeridos agrupados por disciplina (empresas que atendem e pessoas da equipe nessas empresas).
- Fichas na tela dividida mantêm a rolagem ao editar.

## oficial Firebase — 05/11, ~17h55 — commit 2096970
- RFI por projeto (no modelo das pendências): requerente/requerido ligados a pessoas e empresas, respostas e comentários, painel, PDF; RFIs no backup.
- Convidados: veem só a RFI dos projetos de que participam; respondem só RFIs dirigidas a eles ou à empresa deles; comentam em qualquer uma (regras do banco: rfi.participantes, projetosRfi).
- Disciplinas e equipe: quadro Equipe do projeto (empresas e pessoas participantes, "Sem empresa"), seleção direta com busca, ficha lateral para cadastro novo e dados da pessoa.
- Pessoas sem acesso (placeholder) e "Enviar acesso" quando o e-mail for definido.
- Relação de documentos: cada parte do nome do arquivo editável direto na tabela.
- Ficha do parceiro mostra as pessoas com conta ligadas à empresa.
- Atenção: participantes começam vazios; convidados só passam a ver um projeto depois de incluídos na equipe.

## oficial Firebase — 05/11, ~16h45 — commit 4aa7bb3
- Usuários e acesso: ordenar e agrupar pelo cabeçalho, no mesmo sistema das outras planilhas.

## oficial Firebase — 05/11, ~16h40 — commit f3879d1
- Contas: nível Master (único, equipe@fj.arq.br) sempre no topo, protegido nas regras do banco; ações alinhadas; agrupar e ordenar a lista.
- Dados: equipe@fj.arq.br passa de Administrador para Master (firebase/admin-inicial.json).

## oficial Firebase — 05/11, ~16h30 — commit f1ab40b
- Usuários e acesso: controle do link de senha (data e quem enviou); "Definir senha" em vermelho até o link ser enviado a quem ainda não entrou; Reenviar vira "Copiar mensagem".

## oficial Firebase — 05/11, ~16h10 — commit 4086d38
- Contas: quem cria a senha pelo link do e-mail não precisa trocar de novo ao entrar; link de senha enviado pelo admin dispensa a troca; "aguardando 1º acesso" depende do primeiro login (regra: a pessoa lê a própria senha provisória).

## oficial Firebase — 05/11, ~16h00 — commit 6774122
- Usuários e acesso: Remover vira ícone de X.

## oficial Firebase — 05/11, ~15h50 — commit 4ff4cc7
- Contas: nível Usuário passa a se chamar Convidado; e-mails e páginas de senha do Firebase em português, com botão Continuar de volta ao sistema.

## oficial Firebase — 05/11, ~15h35 — commit ac89a75
- Usuários e acesso: Reenviar e Alterar e-mail para contas aguardando o 1º acesso (senha provisória guardada só para administradores até o 1º acesso; regra nova `senhasProvisorias`); Alterar e-mail também nas liberações.

## oficial Firebase — 05/11, ~15h30 — commit 23d9577
- Usuários e acesso: e-mails que já tinham conta aparecem na lista como liberados (com redefinir senha e remover).

## oficial Firebase — 05/11, ~15h25 — commit 306f695
- Revit direto no banco: o sistema publica revitPub/<arquivo> e processa revitPedidos na hora; extensão do pyRevit v2.0.0 (login com e-mail e senha do sistema, leitura do banco, resultado dos pedidos na janela; sem conexão usa a cópia do G:).

## oficial v102 (Claude) — 05/11, ~15h15 — VIRADA PARA O FIREBASE
- A oficial passa a ser https://sistema-fja.web.app (Firebase): login por e-mail e senha, contas e níveis (Administrador/Usuário) com empresa, Google Drive/Sheets pelas APIs do Google, imagens e anexos na pasta ARQUIVOS DO SISTEMA, backup diário automático.
- Dados importados do backup SISTEMA-FJA_BACKUP_2026-11-05_15h04; arquivos do SISTEMA-FJA_ARQUIVOS.zip. O histórico de exportações de keynotes (knexp) não está no backup e não foi migrado.
- A oficial do Claude (v102) fica somente leitura, com aviso do endereço novo e sem rotinas automáticas.
- Homologação: https://sistema-fja-homolog.web.app. Desenvolvimento continua no Claude (dev).

## oficial v101 = dev v119 — 05/11, ~14h20 — commit a29ed0b
- Revit: ferramenta Diagnóstico (sincronização de tipos e materiais vinculados; ressincronizar, incluir no projeto, desvincular); Componentes com tabela de tipos e código por tipo (todos obrigatórios, renomear tipos opcional); siglas novas criadas pelo Revit; ferramentas agem só no modelo ativo; correções para o Revit 2026 (extensão v1.9.0).
- Especificação: ao puxar da biblioteca, a sigla do keynote fica travada na do código central.
- Descrições: variações e diagnóstico consideram só projetos em andamento.
- Backup: botão "Baixar arquivos para a migração" (.zip com as imagens e anexos guardados no Claude).
- Código das versões Firebase incluído (inativo no Claude).

## oficial v100 = dev v106 — 04/11, ~11h50 — commit 9de9681
- Especificação técnica: banco central (`espb`) com códigos FJ.SIGLA.NNNN, siglas de keynote (`etsig`), especificação por projeto (`espec`), importação de planilha ET-, exportação de keynotes com histórico de 100 exportações por projeto (`knexp/<projeto>/exp`).
- Descrição simplificada com variações e principal; diagnóstico na planilha do projeto e no banco central.
- Conexão com o Revit (publicação no G: e pedidos da aba Ferramentas FJA) e download da extensão do pyRevit em Ferramentas.
- Nova seção Manutenção: Conexão com o Revit e Backup do sistema (backup diário automático na pasta BACKUP do Drive, em partes).
- Barra lateral lembra as seções recolhidas.
- Bancos de especificação, siglas e keynotes começam vazios na oficial (preenchimento controlado).
- Contrato do artifact 0.2.91 → 0.2.92; Google Drive ganha `trash_file` (limpeza das cópias do Revit).

## oficial v99 = dev v74 — 30/10, ~18h30 — commit 7f4d3ac
- Comentários × Relação de documentos: ligação pelo nome do arquivo (`chaveDoc`) ou explícita (`coment.rdoc`); coluna e seção de Comentários na RD.
- Croquis (`croq`): lista com miniaturas, ficha com imagem, arquivos (arrastar, colar print, vincular do G: ou link), pasta no Drive, e-mail pelo Gmail.

## oficial v98 = dev v68 — 30/10, ~17h50
- Código do cliente (emissão) no projeto (`campos.codCliente`), usado em PDFs, títulos de backups e nomes de arquivo da RD.
- Atas de reunião (`ata`): participantes, tópicos e itens, pendências da ata anterior, exportação para Google Doc e PDF.

## oficial v97 = dev v65 — 30/10, ~17h20
- Relação de documentos: seleção Shift/Ctrl, edição na célula e em lote, gravação em lote com novas tentativas.

## oficial v96 = dev v61 — 30/10, ~17h
- Relação de documentos (`rdoc`): seção nova por projeto, importação da planilha, nome de arquivo gerado, prazo, painel por status e fase, backup na planilha vinculada, PDF.

## oficial v95 = dev v57 — 30/10, ~14h25
- PDF do Cronograma, com ou sem gantt.

## oficial v94 = dev v56 — 30/10, ~14h10
- Exportação de listas em PDF (Comentários, Pendências, Equipamentos).
- Conexão com PC versão 6 (Explorer à frente do navegador).

## oficial v93 = dev v53 — 30/10, ~13h40
- Backup na planilha vinculada (Comentários, Pendências, Equipamentos), backup do Cronograma, Disciplinas e Parceiros.
- Biblioteca › Ferramentas › Conexão com PC (versão 5).
- Janelas próprias `pedirNome` e `confirmar`.

## oficial v92 = dev v25 — 29/10, ~18h20
- Comentários: campo "Encaminhado em".

## oficial v91 = dev v24 — 29/10, ~17h50
- Indicador de ambiente, timeline redesenhada, cronograma detalhado com seleção múltipla, desfazer/refazer, prioridade nos comentários, ficha em painel lateral, Sugestões.

## Antes de 29/10
- v87: expandir/retrair da árvore. v89: novos status de comentários. v90: faixas da timeline mais grossas.
