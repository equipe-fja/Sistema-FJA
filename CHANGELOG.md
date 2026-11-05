# Histórico de versões da oficial

Cada entrada é uma publicação no artifact oficial (vNN = versão do artifact oficial; devNN = versão correspondente da desenvolvimento). Datas em 2026.

## Em desenvolvimento (ainda não publicado na oficial)
- (nada)

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
