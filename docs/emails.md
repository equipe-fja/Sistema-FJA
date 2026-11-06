# E-mails de aviso das RIs

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
