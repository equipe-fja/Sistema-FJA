# Para retomar (segunda, 09/11/2026)

Situação em 06/11/2026, 18h20.

## Onde está cada versão
- **Oficial** (sistema-fja.web.app): atualizada às ~17h50.
  - Inclui as mudanças de 06/11: RI no lugar de RFI, permissões de autor/Master, históricos, imagens dos convidados, rascunho até Salvar, barra horizontal nova, sem botão Colunas.
  - Inclui as rotinas do servidor **equipe** e **autoria** ligadas.
- **Homologação** (sistema-fja-homolog.web.app): igual à oficial, mais:
  - o seletor "Ver como" (só lá);
  - a tela Manutenção › **Conta Google do servidor** (só Master).
  - Recebe a cópia da oficial todo dia às 04h, então as 5 RIs de teste do projeto 128 somem; recriar quando for testar.
- **Dev** (artifact do Claude): igual ao código atual.

## Plano Blaze (feito em 06/11)
- O faturamento é da **conta master**, em "Minha conta de faturamento 1", com orçamento configurado. A equipe@ **não** tem acesso ao faturamento.
- A conta master tem o papel "Gerente de faturamento do projeto" nos dois projetos (na oficial, também Proprietário).
- Os dois projetos (oficial e homologação) estão no Blaze. O limite de 50 mil leituras/dia não existe mais.
- Os serviços e permissões para as funções do servidor já foram liberados nos dois projetos (Cloud Shell).

## Funções do servidor no ar (oficial e homologação)
| Função | O que faz |
|---|---|
| equipeProjeto / equipeContas | atualizam na hora o que o convidado vê (participantes, envolvidos, "todos os projetos") |
| equipeDiaria | conferência geral às 3h30 |
| autoriaRi | desfaz edição/exclusão de resposta ou comentário alheio numa RI e registra "Alteração desfeita" no histórico |

## Em andamento: conta Google do servidor (Drive e e-mails sem conectar o Google)
Escolha da FJA: **autorização única feita pela equipe@**, sem envolver a conta master no Workspace.

Já conferido:
- O cliente de login Google fica no projeto **Sistema FJA** (nº 957716305391).
- O tipo de usuário é **Interno** (a autorização não vence em 7 dias).
- Na sexta foi criada uma chave secreta nova, terminando em **…vLk7**.

Próximos passos:
1. **Segredo no GitHub.** Se o valor da chave …vLk7 não foi copiado na hora:
   - Em Google Cloud › Google Auth Platform › Clientes › cliente 957716305391-ik6b3np…, apagar a …vLk7.
   - Clicar em "Add secret" e copiar o valor na janela.
   - Não mexer na chave antiga …YHQK.

   Cadastrar em github.com/equipe-fja/Sistema-FJA/settings/secrets/actions › New repository secret › nome **`GOOGLE_CLIENT_SECRET`**. O valor não passa pelo chat.
2. **Avisar no chat.** O Claude publica de novo a homologação.
3. **Autorizar na homologação.**
   - Entrar como Master e ir em Manutenção › **Conta Google do servidor** › **Autorizar**.
   - Escolher **equipe@fj.arq.br** e deixar todas as permissões marcadas (Drive, Planilhas, enviar e-mail).
4. **Ligar e testar na homologação.** O Claude liga **Drive pelo servidor** e **e-mails**. Testar:
   - administradores entram sem a barra "Conectar ao Google Drive";
   - planilhas, backups e imagens funcionam;
   - avisos de RI chegam na equipe@ com a faixa "HOMOLOGAÇÃO" (usar o Convidado Teste e RIs do projeto 128);
   - Minha conta › Avisos por e-mail.
5. **Oficial.** Quando a FJA autorizar:
   - ligar na oficial;
   - autorizar de novo em Manutenção › Conta Google do servidor, **na oficial**, porque cada projeto guarda a sua autorização.

Se um dia a senha da equipe@ for trocada (ou o acesso do "Sistema FJA" for removido em myaccount.google.com/permissions), basta autorizar de novo nessa tela.

## E-mails de aviso (prontos, desligados)
- Avisos na hora:
  - RI dirigida à pessoa;
  - resposta numa RI dela;
  - RI fechada ou cancelada.
- Resumo diário nos dias úteis às 8h.
- Preferência em Minha conta (a seção só aparece com os e-mails ligados).
- Remetente: equipe@fj.arq.br. Na homologação, tudo vai para a equipe@ com a faixa "HOMOLOGAÇÃO".
- Modelo visual: `firebase/email/modelo-ri.html`.

## Contas de teste (só na homologação)
- **Convidado Teste** (equipe+convidado@fj.arq.br): convidado com acesso às RIs de todos os projetos.
  - Senha provisória: em Manutenção › Usuários e acesso › Reenviar.
  - Entrar numa janela anônima para testar o que o convidado vê e as regras do banco.
- **Seletor "Ver como"** no cabeçalho (só Master): simula Master, administrador ou convidado. Só a tela muda; o banco continua vendo a conta Master.

## Outros pendentes
- **Revit pelo servidor** (planejado, não feito): os pedidos do Revit ainda dependem de alguém com o sistema aberto. Exige separar a lógica da biblioteca num módulo comum; ver `docs/servidor.md`.
- **40 RIs fictícias na oficial** (SESC Angra e CBPF): script pronto (`firebase/importador/exemplos-rfi.js`). Rodar só quando a FJA pedir.
- **Delegação do Workspace** (alternativa não escolhida): os IDs das contas de serviço são 110582892908017318320 (oficial) e 102450429640470430463 (homologação), caso a FJA mude de ideia.

## Onde ver as coisas
- Liga/desliga do servidor: `firebase/servidor.json` (equipe, autoria, google, drive) e `firebase/email/config.json` (e-mails).
- Explicação completa: `docs/servidor.md`.
- Resultado de cada publicação: branch `firebase-log` (`logs/publicar-homolog.txt`, `logs/publicar-oficial.txt`).
- Histórico do que foi para a oficial: `CHANGELOG.md`.
