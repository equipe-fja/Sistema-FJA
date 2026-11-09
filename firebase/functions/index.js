// Envio de e-mails do Sistema FJA (Cloud Functions, plano Blaze).
// - avisosRi: a cada gravação de uma RI, avisa na hora (RI dirigida a você, resposta nova, RI fechada/cancelada).
// - resumoDiario: dias úteis às 8h (horário de Brasília), o que está pendente com cada pessoa.
// Remetente e liga/desliga: firebase/email/config.json (copiado para cá na publicação). Senha de app do Gmail: segredo SMTP_SENHA.
// Na homologação, todos os e-mails vão para config.homologPara, com uma faixa dizendo para quem iriam.
const { onDocumentWritten, onDocumentWrittenWithAuthContext } = require('firebase-functions/v2/firestore');
const { onSchedule } = require('firebase-functions/v2/scheduler');
const { defineSecret } = require('firebase-functions/params');
const { setGlobalOptions, logger } = require('firebase-functions/v2');
const admin = require('firebase-admin');
const nodemailer = require('nodemailer');
const M = require('./modelo');
const L = require('./logica');
const SV = require('./servidor');

admin.initializeApp();
const db = admin.firestore();
let CFG = {}; try { CFG = require('./config.json'); } catch { }
let SRV = {}; try { SRV = require('./servidor.json'); } catch { }
let REGIAO = 'southamerica-east1'; try { REGIAO = require('./regiao.json').regiao || REGIAO; } catch { }
setGlobalOptions({ region: REGIAO, maxInstances: 3, memory: '256MiB' });


const PROJ = process.env.GCLOUD_PROJECT || (JSON.parse(process.env.FIREBASE_CONFIG || '{}').projectId) || '';
const HOMOLOG = /homolog/.test(PROJ);
const SITE = `https://${PROJ}.web.app`;
const AMBI = HOMOLOG ? 'homolog' : 'oficial';
const ligado = () => !!(CFG.remetente && CFG.ativo && CFG.ativo[AMBI]);
const rotina = nome => !!(SRV[nome] && SRV[nome][AMBI]);
// Envio: "delegacao" (padrão) = API do Gmail em nome do remetente, com a conta de serviço autorizada no Workspace (escopo gmail.send),
// sem senha de app; "smtp" = Gmail SMTP com senha de app (segredo SMTP_SENHA). Os segredos só são declarados quando usados.
const VIA_SMTP = CFG.via === 'smtp', VIA_OAUTH = CFG.via === 'oauth';
const DRIVE_OAUTH = (SRV.drive || {}).modo === 'oauth';
const GOOGLE = rotina('google'); // autorização única da conta da FJA (Drive, Planilhas e envio de e-mail), sem delegação do Workspace
const usaChaveSA = (ligado() && !VIA_SMTP && !VIA_OAUTH) || (rotina('drive') && !DRIVE_OAUTH);
const GOOGLE_SEGREDO = GOOGLE ? defineSecret('GOOGLE_OAUTH_SECRET') : null;
let CLIENTE = ''; try { CLIENTE = require('fs').readFileSync(__dirname + '/google-client.txt', 'utf8').trim(); } catch { }
const ESCOPOS_GOOGLE = ['openid', 'email', 'https://www.googleapis.com/auth/drive', 'https://www.googleapis.com/auth/spreadsheets', 'https://www.googleapis.com/auth/gmail.send'];
let oauthCli = null, oauthRef = null;
async function tokenGoogle() { // acesso de 1 h em nome da conta autorizada
  const { OAuth2Client } = require('google-auth-library');
  const g = await db.collection('servidor').doc('google').get(), d = g.exists ? g.data() : null;
  if (!d || !d.refresh) throw new Error('a conta Google do servidor ainda não foi autorizada (Manutenção › Conta Google do servidor)');
  if (!oauthCli || oauthRef !== d.refresh) { oauthCli = new OAuth2Client(CLIENTE, GOOGLE_SEGREDO.value()); oauthCli.setCredentials({ refresh_token: d.refresh }); oauthRef = d.refresh; }
  try { const t = await oauthCli.getAccessToken(); return { token: t.token, exp: oauthCli.credentials.expiry_date || Date.now() + 3300e3 }; }
  catch (e) { oauthCli = null; await g.ref.update({ erro: String(e.message || e).slice(0, 300), erroEm: Date.now() }); throw new Error('o Google recusou a autorização guardada (autorize de novo em Manutenção › Conta Google do servidor)'); }
}
const SMTP_SENHA = ligado() && VIA_SMTP ? defineSecret('SMTP_SENHA') : null;
const DRIVE_SA = usaChaveSA ? defineSecret('DRIVE_SA') : null;
const SEGREDOS_EMAIL = [VIA_SMTP ? SMTP_SENHA : VIA_OAUTH ? GOOGLE_SEGREDO : DRIVE_SA].filter(Boolean);

let transp = null;
const smtp = () => transp || (transp = nodemailer.createTransport({ host: 'smtp.gmail.com', port: 465, secure: true, auth: { user: CFG.remetente, pass: SMTP_SENHA.value() } }));
let jwtGmail = null;
async function enviarGmail(msg) {
  const MailComposer = require('nodemailer/lib/mail-composer');
  const raw = (await new MailComposer(msg).compile().build()).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  let t;
  if (VIA_OAUTH) t = (await tokenGoogle()).token;
  else { const { JWT } = require('google-auth-library');
    if (!jwtGmail) { const k = JSON.parse(DRIVE_SA.value()); jwtGmail = new JWT({ email: k.client_email, key: k.private_key, scopes: ['https://www.googleapis.com/auth/gmail.send'], subject: CFG.remetente }); }
    t = (await jwtGmail.getAccessToken()).token; }
  const r = await fetch('https://gmail.googleapis.com/gmail/v1/users/me/messages/send', { method: 'POST', headers: { Authorization: 'Bearer ' + t, 'Content-Type': 'application/json' }, body: JSON.stringify({ raw }) });
  if (!r.ok) { jwtGmail = null; throw new Error('Gmail: HTTP ' + r.status + ' ' + (await r.text()).slice(0, 300)); }
}

async function enviar(conta, assunto, html, meta) {
  const para = HOMOLOG ? (CFG.homologPara || CFG.remetente) : conta.email;
  const corpo = HOMOLOG ? html.replace(/<body([^>]*)>/, `<body$1>${M.faixaHomolog(conta.email)}`) : html;
  const ass = (HOMOLOG ? '[HOMOLOG] ' : '') + assunto;
  const msg = { from: `"${CFG.nomeRemetente || 'Sistema FJA'}" <${CFG.remetente}>`, to: para, subject: ass, html: corpo, text: M.texto(corpo) };
  if (VIA_SMTP) await smtp().sendMail(msg); else await enviarGmail(msg);
  await db.collection('emailsEnviados').add({ em: Date.now(), para: conta.email, enviadoA: para, assunto: ass, ...meta });
}

async function contexto() {
  const [ac, pr, pa] = await Promise.all([db.collection('acessos').get(), db.collection('projetosRfi').get(), db.collection('itens').where('secao', '==', 'parceiros').get()]);
  const contas = ac.docs.map(d => ({ id: d.id, ...d.data() })).filter(c => c.email && !c.placeholder);
  const projetos = Object.fromEntries(pr.docs.map(d => [d.id, d.data()]));
  const empresas = Object.fromEntries(pa.docs.map(d => [d.id, d.data().nome || '']));
  return { contas, projetos, empresas };
}
const nomeProj = p => p ? [p.codigo, p.nome].filter(Boolean).join(' ') : '';
const quando = t => new Date(t).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).replace(',', ' ·');

if (ligado()) exports.avisosRi = onDocumentWritten({ document: 'rfi/{id}', secrets: SEGREDOS_EMAIL }, async ev => {
  if (!ligado()) return;
  const antes = ev.data.before.exists ? ev.data.before.data() : null, ri = ev.data.after.exists ? ev.data.after.data() : null;
  if (!ri) return;
  const { contas, projetos, empresas } = await contexto();
  const avisos = L.planejarAvisos(antes, ri, contas, empresas);
  await db.collection('servidor').doc('avisos').set({ ultimo: Date.now(), ri: ev.params.id, avisos: avisos.length }).catch(() => {});
  if (!avisos.length) return;
  const proj = nomeProj(projetos[ri.projeto]) || 'Projeto', id = ev.params.id, url = `${SITE}/#ri=${encodeURIComponent(ri.projeto)}/${encodeURIComponent(id)}`;
  const dados = { ...ri, requerenteNome: L.nomeQ(ri.requerente, contas, empresas), requeridosNomes: L.reqs(ri).map(q => L.nomeQ(q, contas, empresas)).filter(Boolean).join(', ') };
  const mov = (ri.historico || []).slice(-3).reverse().map(h => ({ quando: quando(h.em), quem: h.por || '', oque: String(h.acao || 'alterou').replace(/^./, c => c.toLowerCase()).replace(/\bRFI\b/, 'RI') }));
  for (const a of avisos) {
    try {
      const html = M.avisoRi({ chamada: a.chamada, frase: `Olá, <b>${M.esc((a.conta.nome || '').split(' ')[0] || a.conta.email)}</b>. ${a.frase}`, ri: dados, proj, prazoTxt: L.prazoTxt(ri.prazo), destaque: a.destaque, mov, url, urlConta: SITE,
        rodape: a.tipo === 'dirigida' ? 'Você recebeu este aviso porque é requerido nesta RI no Sistema FJA.' : 'Você recebeu este aviso porque participa desta RI no Sistema FJA.', preview: `${a.chamada} · RI ${ri.num ?? ''} · ${proj}` });
      await enviar(a.conta, `[RI ${ri.num ?? ''}] ${proj} · ${ri.assunto || 'Requisição de informações'}`, html, { tipo: a.tipo, rfi: id, projeto: ri.projeto });
    } catch (e) { logger.error('aviso', a.conta.email, e.message || e);
      await db.collection('emailsEnviados').add({ em: Date.now(), para: a.conta.email, erro: String(e.message || e).slice(0, 500), tipo: a.tipo, rfi: id }).catch(() => {}); }
  }
});

if (ligado()) exports.resumoDiario = onSchedule({ schedule: `0 ${(CFG.resumo && CFG.resumo.hora) || 8} * * ${CFG.resumo && CFG.resumo.diasUteis === false ? '*' : '1-5'}`, timeZone: 'America/Sao_Paulo', secrets: SEGREDOS_EMAIL }, async () => {
  if (!ligado()) return;
  const { contas, projetos } = await contexto();
  const ris = (await db.collection('rfi').get()).docs.map(d => ({ id: d.id, ...d.data() }));
  const data = new Date().toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo', weekday: 'long', day: '2-digit', month: '2-digit' });
  for (const c of contas) {
    try {
      const grupos = L.planejarResumo(c, ris); if (!grupos.length) continue;
      const g2 = grupos.map(g => ({ titulo: g.titulo, itens: g.itens.map(r => ({ num: 'Nº ' + (r.num ?? ''), assunto: r.assunto, proj: nomeProj(projetos[r.projeto]), url: `${SITE}/#ri=${encodeURIComponent(r.projeto)}/${encodeURIComponent(r.id)}`, nota: r.prazo ? L.prazoTxt(r.prazo) : '', alerta: !!g.alerta })) }));
      const n = grupos.reduce((s, g) => s + g.itens.length, 0);
      const html = M.resumo({ nome: (c.nome || '').split(' ')[0] || c.email, grupos: g2, url: SITE, urlConta: SITE, data, preview: `${n} RI${n === 1 ? '' : 's'} pedem a sua atenção hoje` });
      await enviar(c, `Resumo do dia · ${n} RI${n === 1 ? '' : 's'} para você`, html, { tipo: 'resumo' });
    } catch (e) { logger.error('resumo', c.email, e.message || e);
      await db.collection('emailsEnviados').add({ em: Date.now(), para: c.email, erro: String(e.message || e).slice(0, 500), tipo: 'resumo' }).catch(() => {}); }
  }
});

/* ------------------------------------------------------------------ equipe dos projetos (servidor.json: equipe) */
if (rotina('equipe')) {
  exports.equipeProjeto = onDocumentWritten('itens/{id}', async ev => {
    const a = ev.data.before.exists ? ev.data.before.data() : null, d = ev.data.after.exists ? ev.data.after.data() : null, sec = (d || a || {}).secao;
    if (sec === 'projetos') return SV.sincronizarEquipe(db, d ? ev.params.id : null);
    if (sec === 'parceiros' && (a || {}).nome !== (d || {}).nome) return SV.sincronizarEquipe(db, null); // nome de empresa aparece nos envolvidos
  });
  exports.equipeContas = onDocumentWritten('acessos/{uid}', async ev => {
    const a = ev.data.before.exists ? ev.data.before.data() : {}, d = ev.data.after.exists ? ev.data.after.data() : {};
    const k = x => JSON.stringify([x.nome, x.email, x.empresa, x.nivel, !!x.todosProjetos]);
    if (k(a) !== k(d)) return SV.sincronizarEquipe(db, null);
  });
  exports.equipeDiaria = onSchedule({ schedule: '30 3 * * *', timeZone: 'America/Sao_Paulo' }, () => SV.sincronizarEquipe(db, null)); // conferência geral
}

/* ------------------------------------------------------------------ autoria das respostas e comentários (servidor.json: autoria) */
if (rotina('autoria')) {
  exports.autoriaRi = onDocumentWrittenWithAuthContext('rfi/{id}', async ev => {
    if (ev.authType !== 'app_user' || !ev.authId) return; // gravações do próprio servidor não são conferidas
    const antes = ev.data.before.exists ? ev.data.before.data() : null, depois = ev.data.after.exists ? ev.data.after.data() : null;
    if (!antes || !depois) return;
    const ac = await db.collection('acessos').doc(ev.authId).get(), conta = ac.exists ? ac.data() : {};
    if (conta.nivel === 'master') return;
    const upd = SV.verificarAutoria(antes, depois, ev.authId, conta.nome || conta.email || '');
    if (upd) { await ev.data.after.ref.update(upd); logger.warn('autoria: alteração desfeita', { ri: ev.params.id, por: conta.email, campos: Object.keys(upd) }); }
  });
}

/* ------------------------------------------------------------------ Drive pelo servidor (servidor.json: drive) */
// Entrega à tela de um administrador um acesso temporário (1 h) ao Drive/Planilhas em nome da conta da FJA,
// para ninguém precisar conectar o Google. Modo "delegacao": a conta de serviço do projeto age como servidor.json drive.conta
// (autorizada uma vez no Admin Console do Google Workspace). Modo "compartilhado": a própria conta de serviço, membro do Drive compartilhado.
if (rotina('drive')) {
  const { onCall, HttpsError } = require('firebase-functions/v2/https');
  const { JWT } = require('google-auth-library');
  let jwt = null;
  exports.driveToken = onCall({ region: 'southamerica-east1', secrets: [DRIVE_OAUTH ? GOOGLE_SEGREDO : DRIVE_SA].filter(Boolean), cors: true }, async req => {
    if (!req.auth) throw new HttpsError('unauthenticated', 'entre no sistema');
    const ac = await db.collection('acessos').doc(req.auth.uid).get();
    if (!ac.exists || !['admin', 'master'].includes(ac.data().nivel)) throw new HttpsError('permission-denied', 'só administradores usam o Drive');
    if (DRIVE_OAUTH) { try { return await tokenGoogle(); } catch (e) { logger.error('driveToken', e.message || e); throw new HttpsError('failed-precondition', e.message || String(e)); } }
    if (!jwt) {
      const k = JSON.parse(DRIVE_SA.value()), d = SRV.drive || {};
      jwt = new JWT({ email: k.client_email, key: k.private_key, scopes: ['https://www.googleapis.com/auth/drive', 'https://www.googleapis.com/auth/spreadsheets'],
        ...(d.modo === 'compartilhado' ? {} : { subject: d.conta || 'equipe@fj.arq.br' }) });
    }
    try {
      const t = await jwt.getAccessToken(), exp = jwt.credentials.expiry_date || (Date.now() + 3300e3);
      return { token: t.token, exp };
    } catch (e) { jwt = null; logger.error('driveToken', e.message || e); throw new HttpsError('failed-precondition', 'o Google recusou o acesso (a conta de serviço foi autorizada no Workspace?)'); }
  });
}

/* ------------------------------------------------------------------ conta Google do servidor (servidor.json: google) */
// O Master abre Manutenção › Conta Google do servidor e autoriza uma vez, entrando com a conta da FJA (drive.conta / remetente).
// O código da autorização vem para cá, é trocado por uma chave de renovação e guardado em servidor/google (o sistema não lê).
if (GOOGLE) {
  const { onCall, HttpsError } = require('firebase-functions/v2/https');
  const soMaster = async req => { if (!req.auth) throw new HttpsError('unauthenticated', 'entre no sistema');
    const a = await db.collection('acessos').doc(req.auth.uid).get(); if (!a.exists || a.data().nivel !== 'master') throw new HttpsError('permission-denied', 'só o Master'); return a.data(); };
  const contaEsperada = () => ((SRV.drive || {}).conta || CFG.remetente || 'equipe@fj.arq.br').toLowerCase();
  exports.googleAutorizar = onCall({ region: 'southamerica-east1', secrets: [GOOGLE_SEGREDO], cors: true }, async req => {
    const quem = await soMaster(req);
    const { OAuth2Client } = require('google-auth-library');
    const cli = new OAuth2Client(CLIENTE, GOOGLE_SEGREDO.value(), 'postmessage');
    let tk; try { tk = (await cli.getToken(String(req.data && req.data.code || ''))).tokens; } catch (e) { throw new HttpsError('invalid-argument', 'o Google recusou o código: ' + (e.message || e)); }
    let email = ''; try { email = (await cli.verifyIdToken({ idToken: tk.id_token, audience: CLIENTE })).getPayload().email || ''; } catch { }
    if (email.toLowerCase() !== contaEsperada()) throw new HttpsError('failed-precondition', `entre com a conta ${contaEsperada()} (foi usada ${email || 'outra conta'})`);
    const tem = String(tk.scope || '').split(' '), faltam = ESCOPOS_GOOGLE.slice(2).filter(x => !tem.includes(x));
    if (faltam.length) throw new HttpsError('failed-precondition', 'faltou marcar as permissões: ' + faltam.map(x => x.split('/').pop()).join(', '));
    if (!tk.refresh_token) throw new HttpsError('failed-precondition', 'o Google não entregou a chave de renovação. Em myaccount.google.com/permissions, remova o acesso do "Sistema FJA" e autorize de novo.');
    await db.collection('servidor').doc('google').set({ refresh: tk.refresh_token, conta: email, escopos: tem, por: quem.email || '', em: Date.now() });
    oauthCli = null;
    return { ok: true, conta: email };
  });
  exports.googleEstado = onCall({ region: 'southamerica-east1', secrets: [GOOGLE_SEGREDO], cors: true }, async req => {
    await soMaster(req);
    const g = await db.collection('servidor').doc('google').get(), d = g.exists ? g.data() : null;
    if (!d || !d.refresh) return { autorizado: false, conta: contaEsperada(), escopos: ESCOPOS_GOOGLE };
    let ok = true, erro = ''; try { await tokenGoogle(); } catch (e) { ok = false; erro = e.message || String(e); }
    return { autorizado: true, funcionando: ok, erro, conta: d.conta, por: d.por, em: d.em, escopos: ESCOPOS_GOOGLE };
  });
}
