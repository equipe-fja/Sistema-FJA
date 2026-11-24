# Controle de emissões

Registro das emissões de arquivos de cada projeto (Projeto › Emissões). Decisões da FJA em 24/11/2026.

## Decisões
- **Local:** dentro de cada projeto, ao lado de Relação de documentos, Atas e Croquis.
- **Obrigatório:** só data e descrição. Destinatários e arquivos são opcionais.
- **Subpasta da emissão:** criada dentro da pasta de emissões do projeto, com o nome `AA-MM-DD Descrição` (ano com 2 dígitos, espaço depois da data; caracteres inválidos no Windows viram espaço). Ex.: `26-11-24 Projeto básico - entrega ao contratante`.
- **Destinatários:** opcionais; escolhidos da lista de envolvidos do projeto (`envolvidos(pid)`). "+ Novo" cadastra um envolvido só no projeto (nome, empresa, e-mail), com o botão "Enviar ao cadastro geral" (cria o parceiro no catálogo, conferindo se já existe um com o mesmo nome).
- **Arquivos:** enviados do computador (arrastar ou escolher). Na versão Firebase (homologação e oficial), envio em partes (resumable upload do Drive), sem limite prático de tamanho, com progresso e retomada. A dev do Claude fica com o limite do conector.
- **Pasta compartilhada (opcional):** ao emitir, duplica os arquivos para a pasta escolhida. Os arquivos correspondentes com revisão anterior vão para a subpasta `SUBSTITUÍDOS` (criada se não existir).
  - Correspondente = mesmo nome sem o sufixo `_R\d+` e revisão menor, **em qualquer extensão** (a nova revisão substitui o documento inteiro: PDF, DWG…).
  - Arquivo sem revisão emitido de novo com o mesmo nome: o antigo vai para SUBSTITUÍDOS.
  - Revisão **maior** na pasta compartilhada que a emitida: não copia nem move nada daquele documento; a emissão fica marcada "Conferir" com o motivo, para resolver manualmente; botão "Copiar de novo" refaz só os pendentes.
  - Nome já existente em SUBSTITUÍDOS: o movido ganha ` (2)`, ` (3)`…
- **Relação de documentos:** ao emitir, os documentos com o mesmo nome (sem extensão) recebem a data de emissão e o status EMITIDO (com prévia). Revisão nova atualiza a linha existente (troca a revisão; uma linha por documento). Arquivos sem documento aparecem como "fora da RD", sem bloquear.
  - Histórico: clicar na revisão na RD mostra as revisões anteriores (rev., data, emissão, destinatários, arquivos), vindas das emissões; clicar numa revisão abre a pasta daquela emissão.
  - Com a Conexão com PC **v7**, a pasta abre no Explorer com os arquivos daquela revisão selecionados; sem ela, abre a pasta no Drive.
  - Aviso na prévia quando uma prancha da RD vier sem PDF ou sem DWG, com a opção de seguir mesmo assim.

## Modelo de dados
- Coleção `emis`, id `<projeto>__emNNN`.
  - `projeto`, `num` (EM-001…), `data` (dd/mm/aaaa), `descricao`, `obs`, `status` (Rascunho | Emitida | Conferir)
  - `destinatarios`: `[{nome, empresa, email}]`
  - `arquivos`: `[{nome, base, rev, ext, tamanho, mime, driveId, rdoc}]`
  - `pasta`: `{id, caminho, url}` (subpasta datada)
  - `compart`: `{id, caminho, url, em, por, copiados:[…], substituidos:[{nome, de, para}], conferir:[{nome, motivo}]}`
  - `emitidaEm`, `emitidaPor`, `criadoEm`, `criadoPor`, `atualizadoEm`, `atualizadoPor`
- No projeto: `emisPasta` e `emisCompart` (`{id, caminho, url}`), `envolvidosProj` (`[{nome, empresa, email}]`).

## Etapas
1. Shim do Drive (`firebase/google.html`): envio em partes (`FJA_GOOGLE.enviar`), `copy_file`, `move_file`; coleção `emis` ligada ao sistema (tempo real, backup, regras). ✔
2. Lista (planilha com filtros) e ficha (tela dividida). ✔
3. Emitir: pasta datada, envio dos arquivos, prévia com aviso PDF/DWG. ✔ (aviso vale para documentos da RD; memoriais sem DWG: preencher a Extensão na RD só com PDF)
4. Pasta compartilhada e SUBSTITUÍDOS.
5. Envolvidos do projeto e "Enviar ao cadastro geral".
6. RD: atualização ao emitir, histórico de revisões, Conexão com PC v7 (abrir pasta com arquivos selecionados).

## Pendências
- GRD (guia de remessa) em PDF: fica para depois.
- Painel do projeto: incluir o resumo das emissões.
- Teste na homologação com login da FJA (envio de arquivo grande, renomear pasta, emitir).
