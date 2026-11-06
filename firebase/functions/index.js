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
const SMTP_SENHA = defineSecret('SMTP_SENHA');

const PROJ = process.env.GCLOUD_PROJECT || (JSON.parse(process.env.FIREBASE_CONFIG || '{}').projectId) || '';
const HOMOLOG = /homolog/.test(PROJ);
const SITE = `https://${PROJ}.web.app`;
const AMBI = HOMOLOG ? 'homolog' : 'oficial';
const ligado = () => !!(CFG.remetente && CFG.ativo && CFG.ativo[AMBI]);
const rotina = nome => !!(SRV[nome] && SRV[nome][AMBI]);

let transp = null;
const smtp = () => transp || (transp = nodemailer.createTransport({ host: 'smtp.gmail.com', port: 465, secure: true, auth: { user: CFG.remetente, pass: SMTP_SENHA.value() } }));

async function enviar(conta, assunto, html, meta) {
  const para = HOMOLOG ? (CFG.homologPara || CFG.remetente) : conta.email;
  const corpo = HOMOLOG ? html.replace(/<body([^>]*)>/, `<body$1>${M.faixaHomolog(conta.email)}`) : html;
  const ass = (HOMOLOG ? '[HOMOLOG] ' : '') + assunto;
  await smtp().sendMail({ from: `"${CFG.nomeRemetente || 'Sistema FJA'}" <${CFG.remetente}>`, to: para, subject: ass, html: corpo, text: M.texto(corpo) });
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

if (ligado()) exports.avisosRi = onDocumentWritten({ document: 'rfi/{id}', secrets: [SMTP_SENHA] }, async ev => {
  if (!ligado()) return;
  const antes = ev.data.before.exists ? ev.data.before.data() : null, ri = ev.data.after.exists ? ev.data.after.data() : null;
  if (!ri) return;
  const { contas, projetos, empresas } = await contexto();
  const avisos = L.planejarAvisos(antes, ri, contas, empresas); if (!avisos.length) return;
  const proj = nomeProj(projetos[ri.projeto]) || 'Projeto', id = ev.params.id, url = `${SITE}/#ri=${encodeURIComponent(ri.projeto)}/${encodeURIComponent(id)}`;
  const dados = { ...ri, requerenteNome: L.nomeQ(ri.requerente, contas, empresas), requeridosNomes: L.reqs(ri).map(q => L.nomeQ(q, contas, empresas)).filter(Boolean).join(', ') };
  const mov = (ri.historico || []).slice(-3).reverse().map(h => ({ quando: quando(h.em), quem: h.por || '', oque: String(h.acao || 'alterou').replace(/^./, c => c.toLowerCase()).replace(/\bRFI\b/, 'RI') }));
  for (const a of avisos) {
    try {
      const html = M.avisoRi({ chamada: a.chamada, frase: `Olá, <b>${M.esc((a.conta.nome || '').split(' ')[0] || a.conta.email)}</b>. ${a.frase}`, ri: dados, proj, prazoTxt: L.prazoTxt(ri.prazo), destaque: a.destaque, mov, url, urlConta: SITE,
        rodape: a.tipo === 'dirigida' ? 'Você recebeu este aviso porque é requerido nesta RI no Sistema FJA.' : 'Você recebeu este aviso porque participa desta RI no Sistema FJA.', preview: `${a.chamada} · RI ${ri.num ?? ''} · ${proj}` });
      await enviar(a.conta, `[RI ${ri.num ?? ''}] ${proj} · ${ri.assunto || 'Requisição de informações'}`, html, { tipo: a.tipo, rfi: id, projeto: ri.projeto });
    } catch (e) { logger.error('aviso', a.conta.email, e.message || e); }
  }
});

if (ligado()) exports.resumoDiario = onSchedule({ schedule: `0 ${(CFG.resumo && CFG.resumo.hora) || 8} * * ${CFG.resumo && CFG.resumo.diasUteis === false ? '*' : '1-5'}`, timeZone: 'America/Sao_Paulo', secrets: [SMTP_SENHA] }, async () => {
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
    } catch (e) { logger.error('resumo', c.email, e.message || e); }
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
  const DRIVE_SA = defineSecret('DRIVE_SA');
  let jwt = null;
  exports.driveToken = onCall({ region: 'southamerica-east1', secrets: [DRIVE_SA], cors: true }, async req => {
    if (!req.auth) throw new HttpsError('unauthenticated', 'entre no sistema');
    const ac = await db.collection('acessos').doc(req.auth.uid).get();
    if (!ac.exists || !['admin', 'master'].includes(ac.data().nivel)) throw new HttpsError('permission-denied', 'só administradores usam o Drive');
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
