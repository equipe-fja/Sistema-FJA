# Controle de emissões

Registro das emissões de arquivos de cada projeto (Projeto › Emissões). Decisões da FJA em 24/11/2026.

## Decisões
- **Local:** dentro de cada projeto, ao lado de Relação de documentos, Atas e Croquis.
- **Obrigatório:** só data e descrição. Destinatários e arquivos são opcionais.
- **Subpasta da emissão:** criada dentro da pasta de emissões do projeto, com o nome `AA-MM-DD EMISSÃO - Descrição` (ano com 2 dígitos; "EMISSÃO -" incluído a pedido da FJA em 24/11/2026, sem repetir se a descrição já começar com ele; caracteres inválidos no Windows viram espaço). Ex.: `26-11-24 EMISSÃO - Projeto básico - entrega ao contratante`.
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
4. Pasta compartilhada e SUBSTITUÍDOS. ✔ (só na versão Firebase; na dev do Claude aparece o aviso)
5. Envolvidos do projeto e "Enviar ao cadastro geral". ✔ (envolvidos do projeto também aparecem nas atas e pendências; parceiro novo = empresa, ou o nome quando não há empresa; se o parceiro já existe, a pessoa entra na equipe dele)
6. RD: atualização ao emitir, histórico de revisões, Conexão com PC v7 (abrir pasta com arquivos selecionados). ✔ (contador verde ao lado da revisão na RD e seção na ficha do documento; a versão do vigia é marcada por navegador em Ferramentas › Conexão com PC)

## Emissão emitida fica travada (25/11/2026)
Decisão da FJA: emitida (status Emitida ou Conferir), a emissão não pode ser alterada; para alterar, volta-se para rascunho, e isso fica registrado.
- `emTravada(e)`: ficha toda desabilitada (data, descrição, observações, disciplinas, destinatários, arquivos — adicionar, remover, arrastar) e as ações que alteram dados recusadas também no código (`emAcao`, ouvintes de input/change, `emAdicionar`). Continua permitido: "Copiar de novo" (pendentes da compartilhada) e "Enviar ao cadastro geral" (não altera a emissão). Excluir só em rascunho.
- Botão **Voltar para rascunho** (`emVoltarRascunho`): pede o motivo (obrigatório) e grava em `historico[]` `{acao:'rascunho', de, motivo, em, por, porNome}`. O Emitir grava `{acao:'emitida'|'conferir', em, por, porNome, rd}`. A ficha mostra a seção **Histórico** (mais recente primeiro).
- **Pastas existentes** passam a entrar como **Rascunho** (não mais Emitidas), sem a opção "Atualizar a RD" na janela: ao emitir uma emissão retroativa, a RD só muda onde ela for mais recente que o que já está lá; sem pasta compartilhada, a não ser que se escolha uma.

## Emissões retroativas (24/11/2026, na homologação)
Decisão da FJA: registrar emissões antigas a partir de pastas existentes, com as duas opções (só registrar, ou registrar e atualizar a RD).
- Botão **Pastas existentes** na lista de Emissões (`emRetro`). Escolhe-se a pasta pela Conexão com PC ou pelo link do Drive: se o nome for `AA-MM-DD …`, ela é a emissão; senão, as subpastas datadas dela aparecem numa janela para marcar (`emRetroEscolher`; as já ligadas a uma emissão vêm desmarcadas, "(já registrada)").
- Cada pasta vira uma emissão (`emRetroCriar`): **Emitida** na data da pasta (`26-02-02` → 02/02/2026; `emitidaEm` ao meio-dia dessa data), descrição = resto do nome (sem o prefixo "EMISSÃO -"), `retroativa:true`, pasta já com id, arquivos da pasta e das subpastas imediatas (ex.: PDF, DWG; SUBSTITUÍDOS fica de fora) já com `driveId`, disciplinas detectadas pelos nomes. **Não copia** para a pasta compartilhada.
- Os arquivos são ligados aos documentos da RD (`rdoc`), então o histórico de revisões da RD passa a mostrar essas emissões.
- **Atualizar a RD** (desmarcada por padrão): só onde a emissão for mais recente — revisão maior, ou a mesma revisão com data posterior (ou sem data na RD); grava data de emissão, EMITIDO e revisão (`emAtualizarRd`). As pastas são registradas em ordem de nome (data), então a mais recente prevalece.
- Testado na homologação (pasta de teste no BI-BIBLIOTECA, documento de RD de teste no prj-129, tudo apagado no fim): janela com as duas pastas datadas (a sem data ficou de fora); R00 (subpastas PDF/DWG) e R01 registradas como Emitidas; RD de teste R00 de 01/01 → R01 de 06/02; histórico com as duas revisões.

## Integração com o Revit (Pranchas › Exportar)
**Etapa 1 — suíte 2.38.0 (24/11/2026), na oficial:** opção "Registrar emissão no sistema" no Exportar pranchas (`Pranchas/Emissao.cs`, `Nucleo/Nuvem.cs`: `Documento`, `Consulta`, `Criar`, `Atualizar`, `Uid`, conversor `Fs`). Ao marcar, lê o sistema (projeto do modelo, disciplinas, envolvidos, Rascunhos); depois da exportação grava a emissão (`origem: 'revit'`, `pasta: {caminho, nome}` sem id, `arquivos[]` sem `driveId`, `disciplinas` + `discAuto:false`, `destinatarios`, `compart` padrão do projeto). Escolhendo um Rascunho, acrescenta os arquivos (mesmo nome substitui), une destinatários e disciplinas e só define a pasta se ela ainda não existir (senão avisa). A pasta nova do Exportar segue `AA-MM-DD EMISSÃO - Descrição` (sem repetir "EMISSÃO" se a descrição já começa com ela). Grava sempre no banco da **oficial** (o login da suíte é dela).
**Etapa 2 — sistema web (24/11/2026, na homologação; oficial depende de autorização):**
- `emLigarRevit`/`emLigarAoAbrir`: emissão com `pasta.caminho` sem id, ou arquivos sem `driveId`, é ligada ao abrir a ficha (uma vez por sessão) e de novo antes de emitir: pasta por `idPorDescida` (raízes em `DRIVES_RAIZ`; BI-BIBLIOTECA incluído) e, se falhar, `idPorCaminho`; arquivos pelo nome dentro da pasta. Arquivo que ainda não está no Drive (sincronização do Drive para computador) não vai para a compartilhada; o Emitir avisa e pergunta se continua.
- `emPlanoCompart`/`emExecutarCompart`: compartilhada **por disciplina** (`discArqEm`: disciplina do arquivo pela RD/sigla, senão a única disciplina da emissão). Subpasta = `nomeSubDisc(cod)` (nome completo em maiúsculas), criada se não existir; SUBSTITUÍDOS e conferência de revisão dentro dela; sem disciplina = raiz, com aviso na prévia. `substituidos[]` ganha `pasta`.
- Testado na homologação (pasta de teste no BI-BIBLIOTECA, projeto prj-129, emissões de teste apagadas e pasta na lixeira): ligação automática ao abrir; R00 → `COMPARTILHADA\ARQUITETURA` (criada); R01 → copiada para ARQUITETURA e R00 movida para `ARQUITETURA\SUBSTITUÍDOS`; RD sem alteração.
- Não testado: emissão real gravada pelo Revit (2.38.0) com arquivos grandes; projeto em PR-PROJETOS com a compartilhada já organizada por disciplina.

Decisões e proposta técnica:
Decisões da FJA em 24/11/2026:
- O **Exportar pranchas** da suíte do Revit passa a registrar a emissão no sistema, com **destinatários e disciplina escolhidos na própria janela do Revit**.
- O Revit grava a emissão como **Rascunho** (nova ou uma em Rascunho do projeto). O **Emitir** (pasta compartilhada, SUBSTITUÍDOS, RD) continua no sistema.
- O registro só acontece com a opção **"Registrar emissão no sistema"** marcada (o Exportar também serve para conferência e impressão interna).
- **Pasta da emissão = a pasta usada naquela exportação** (a escolhida, com ou sem a pasta nova datada); varia caso a caso e cada emissão guarda a sua (`pasta.caminho` no G:). Não há pasta de emissões fixa para as exportações do Revit.
- **Só a pasta compartilhada é fixa** no projeto (`emisCompart`), e ela tem **subpastas por disciplina**: ao emitir, cada arquivo vai para a subpasta da sua disciplina (SUBSTITUÍDOS e conferência de revisão dentro dela) — mudança no `emPlanoCompart`/`emExecutarCompart`.
  - Nome da subpasta = **nome completo da disciplina** (ex.: `ARQUITETURA`; `discNome(cod)`). A subpasta existente é reconhecida sem diferenciar maiúsculas e acentos; se não existir, é criada com o nome da disciplina em maiúsculas.
  - Arquivo sem disciplina identificada: fica na raiz da compartilhada e a emissão avisa na prévia.

Como vai funcionar (proposta técnica):
- Acesso direto ao Firestore pelo login da equipe que a suíte já guarda (`%APPDATA%\FJA\nuvem.json`; `Nucleo/Nuvem.cs`). As regras já permitem à equipe ler `itens`/`rdoc`/`pend` e criar/atualizar `emis`.
- Projeto pelo `ProjetoDoModelo`. Pasta: a da exportação; a pasta nova datada do Exportar passa a seguir o padrão do sistema, `AA-MM-DD EMISSÃO - Descrição` (regra de `pastaEm`). O sistema localiza a pasta no Drive pelo caminho do G: (`idPorDescida`/`idPorCaminho`, os mesmos da Conexão com PC).
- Destinatários: a mesma composição de `envolvidos(pid)`/`envolvidosEm(pid)` (parceiros das disciplinas do projeto, equipe interna, responsáveis de pendências, envolvidos do projeto), reproduzida em C#. "+ Novo" fica para depois.
- Disciplina: sugerida pela Disciplina das pranchas marcadas; grava `disciplinas` (códigos) e `discAuto:false` quando escolhida.
- Arquivos: `arquivos[]` com nome, base, rev, ext e tamanho; **sem `driveId`** (o Revit grava no G:). O sistema completa os IDs pelo nome ao abrir a emissão (mudança no sistema web).
- Número EM-NNN: o próximo livre, criado com trava (falha se o id já existir e tenta o seguinte).
- Testes: chave escondida no `nuvem.json` para apontar o Revit para a homologação.

## Pendências
- GRD (guia de remessa) em PDF: fica para depois.
- Painel do projeto: incluir o resumo das emissões.
- Teste na homologação com login da FJA (envio de arquivo grande, renomear pasta, emitir).
