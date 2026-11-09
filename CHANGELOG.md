# Histórico de versões da oficial

Cada entrada é uma publicação no artifact oficial (vNN = versão do artifact oficial; devNN = versão correspondente da desenvolvimento). Datas em 2026.

## Em desenvolvimento (ainda não publicado na oficial)
- (nada)

## oficial Firebase — 06/11, ~15h10 — commit fcaf065 (dev v159)
- Extensão do Revit 2.2.0: botão Ferramentas FJA › Suíte › Atualizar no Revit. A página publica o pacote da extensão no banco (revitPub/extensao-versao e revitPub/extensao) cerca de 20 s depois de aberta por alguém da equipe, quando a versão embutida é mais nova; o botão baixa, grava na pasta da extensão e recarrega o pyRevit.

## oficial Firebase — 06/11, ~14h55 — commit 5980850 (dev v158)
- Extensão do Revit: botão "Baixar o instalador" — o .zip traz a extensão, instalar.cmd e LEIA-ME. O instalador copia para a pasta compartilhada do G: (sem substituir versão mais nova), cadastra a pasta no pyRevit do usuário e tira a cópia local antiga; sem o G:, instala só no computador.
- Tela da extensão: caminhos do G: voltam a aparecer com as barras.

## oficial Firebase — 06/11, ~14h45 — commit 6213333 (dev v157)
- Extensão do Revit 2.1.0 (Biblioteca › Ferramentas › Extensão do Revit): novo painel Links › Copiar do link (módulo Equipamentos); a tela avisa a novidade e explica como atualizar.
- Drive pelo servidor engatilhado (função driveToken), desligado em firebase/servidor.json — sem efeito até ser ligado.

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
