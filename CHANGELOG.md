# Histórico de versões da oficial

Cada entrada é uma publicação no artifact oficial (vNN = versão do artifact oficial; devNN = versão correspondente da desenvolvimento). Datas em 2026.

## Em desenvolvimento (ainda não publicado na oficial)
- (nada)

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
