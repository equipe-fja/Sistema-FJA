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
1. Ativar o Blaze no projeto (homologação primeiro).
2. Na conta remetente: verificação em duas etapas + senha de app. Cadastrar a senha de app no GitHub como segredo **`SMTP_SENHA_APP`** (Settings › Secrets and variables › Actions). Ela não passa pelo chat.
3. Em `firebase/email/config.json`: `remetente` e `ativo.homolog: true` (depois `ativo.oficial: true`) + push. A publicação (workflow firebase-publicar) cria o segredo `SMTP_SENHA` no projeto e publica as funções; resultado em `logs/publicar-homolog.txt` / `publicar-oficial.txt` (branch firebase-log).

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
