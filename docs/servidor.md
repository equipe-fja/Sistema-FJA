# Servidor (Cloud Functions): e-mails de aviso das RIs e outras rotinas

Envio pelo servidor (Cloud Functions, exige plano **Blaze**), pela conta Google do remetente (SMTP do Gmail com senha de app).

## Quando sai e-mail
- **Na hora** (preferência "todos"): RI dirigida à pessoa (criada ou requerido incluído; empresa = contas da empresa que participam do projeto, ou todas da empresa), resposta nova (autor e requerente), RI fechada ou cancelada (autor e requeridos). Quem fez a ação não recebe. Comentários não geram aviso.
- **Resumo diário** (dias úteis, 8h de Brasília; preferências "todos" e "resumo"): prazo vencendo (≤ 2 dias) ou vencido, RIs aguardando resposta da pessoa, novidades das últimas 24 h nas RIs que ela abriu. Sem nada → não envia.
- Convidados só recebem sobre RIs de projetos de que participam.
- Preferência em **Minha conta › Avisos por e-mail** (`acessos/<uid>.avisos`: todos | resumo | nenhum).
- Registro de cada envio em `emailsEnviados` (só a equipe lê).
- Na homologação tudo vai para `homologPara`, com uma faixa dizendo para quem iria.

## Como ligar
Padrão (`"via": "delegacao"`): os e-mails saem pela API do Gmail em nome de `remetente` (equipe@fj.arq.br), com a mesma autorização
do Workspace usada pelo Drive (escopo `gmail.send`, ver "drive" abaixo). Não há senha de app.
Depois de autorizado: `ativo.homolog: true` (depois `ativo.oficial: true`) + push.
Alternativa (`"via": "smtp"`): senha de app do Gmail no segredo do GitHub `SMTP_SENHA_APP`.

## Arquivos
- `firebase/functions/index.js` (gatilhos), `logica.js` (quem recebe o quê), `modelo.js` (HTML dos e-mails).
- `firebase/email/modelo-ri.html`: modelo visual de referência.
- Link dos e-mails: `https://<projeto>.web.app/#ri=<projeto>/<id>` abre direto a RI.

# Outras rotinas do servidor (firebase/servidor.json)
Ligar por ambiente (`"homolog": true`, depois `"oficial": true`) e fazer push. Exigem o Blaze; não precisam de senha.

## equipe
Mantém `projetosRfi/<projeto>` (o que o convidado vê: nome, disciplinas, participantes, envolvidos) e `rfi.participantes`
no instante em que um projeto, uma empresa ou uma conta muda, e confere tudo às 3h30. Mesmo conteúdo que o navegador
de um administrador grava hoje a cada 20 s (os dois convivem; o navegador pode deixar de fazer isso depois).

## autoria
A cada gravação de uma RI por uma pessoa (não pelo servidor), confere respostas e comentários: edição ou exclusão de registro
de outra pessoa é desfeita, registro novo com autor trocado é corrigido, e o histórico ganha a entrada "Alteração desfeita".
O Master pode tudo. Antes, isso era garantido só pela tela.

## Revit (planejado, ainda não feito)
Os pedidos do Revit (`revitPedidos`) e a publicação de `revitPub` dependem de um sistema aberto. Levar para o servidor exige
tirar do arquivo do sistema a lógica da biblioteca (criar item central, reservar código FJ e keynote, incluir no projeto,
montar biblioteca.json/projeto-*.json) para um módulo usado pelo navegador e pelo servidor, sem duplicar regras.

## drive (Drive e Planilhas pelo servidor)
Com `drive.<ambiente>: true`, administradores não precisam mais conectar o Google: a tela pede ao servidor (função `driveToken`,
região southamerica-east1) um acesso de 1 h em nome de `drive.conta`, renovado sozinho. Convidados não recebem.
A chave da conta de serviço do projeto (a mesma do segredo do GitHub usado para publicar) vira o segredo `DRIVE_SA` do projeto.

Antes de ligar, autorizar a conta de serviço (uma vez por projeto), no Google Workspace da FJA:
1. admin.google.com › Segurança › Acesso e controle de dados › Controles de API › **Gerenciar delegação em todo o domínio** › Adicionar novo.
2. ID do cliente: o número que aparece no log da publicação ("drive: conta de serviço …"), ou em Google Cloud Console › IAM › Contas de serviço › firebase-adminsdk › ID exclusivo.
3. Escopos (Drive, Planilhas e envio de e-mail): `https://www.googleapis.com/auth/drive,https://www.googleapis.com/auth/spreadsheets,https://www.googleapis.com/auth/gmail.send`.
4. Fazer para as duas contas de serviço: `firebase-adminsdk-fbsvc@sistema-fja.iam.gserviceaccount.com` e `firebase-adminsdk-fbsvc@sistema-fja-homolog.iam.gserviceaccount.com` (cada uma tem o seu ID).
Alternativa sem delegação (`"modo": "compartilhado"`): as pastas do sistema num Drive compartilhado, com o e-mail da conta de serviço como membro (Administrador de conteúdo).
