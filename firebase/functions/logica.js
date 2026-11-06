// Regras de quem recebe o quê (sem acesso ao banco nem envio: testável localmente).
const DIA = 864e5;
const nomeQ = (q, contas, empresas) => q?.tipo === 'e' ? (empresas[q.id] || q.nome || '') : ((contas.find(c => c.id === q?.id) || {}).nome || q?.nome || '');
const autorId = ri => ri.autorId || (ri.requerente?.tipo === 'u' ? ri.requerente.id : '');
const reqs = ri => Array.isArray(ri.requeridos) && ri.requeridos.length ? ri.requeridos : (ri.requerido ? [ri.requerido] : []);
const dataBr = s => { const m = String(s || '').match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/); return m ? Date.UTC(+m[3], +m[2] - 1, +m[1]) : null; };
function hojeSP(agora = Date.now()) { const d = new Date(agora - 3 * 36e5); return Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()); }
function prazoTxt(prazo, agora) { const p = dataBr(prazo); if (p == null) return ''; const n = Math.round((p - hojeSP(agora)) / DIA);
  return n > 1 ? `faltam ${n} dias` : n === 1 ? 'vence amanhã' : n === 0 ? 'vence hoje' : `vencido há ${-n} dia${n === -1 ? '' : 's'}`; }

// quem a RI vê: convidados só as RIs de projetos de que participam; a equipe vê todas
const podeVer = (c, ri) => c.nivel !== 'usuario' || (ri.participantes || []).includes(c.id);
// pessoas por trás de um requerido: a própria pessoa, ou as contas da empresa (de preferência as que participam do projeto)
function pessoasDe(q, ri, contas) {
  if (q.tipo === 'u') return contas.filter(c => c.id === q.id);
  const daEmp = contas.filter(c => c.empresa === q.id), noProj = daEmp.filter(c => (ri.participantes || []).includes(c.id));
  return noProj.length ? noProj : daEmp;
}

/* avisos imediatos para uma mudança na RI: [{conta, tipo, chamada, frase, destaque?}] */
function planejarAvisos(antes, depois, contas, empresas) {
  if (!depois) return [];
  const H = depois.historico || [], ator = (H[H.length - 1] || {}).porId || '';
  const fila = new Map(), add = (c, aviso) => { if (!c || !c.email || c.placeholder || c.id === ator || fila.has(c.id) || !podeVer(c, depois)) return; if ((c.avisos || 'todos') !== 'todos') return; fila.set(c.id, { conta: c, ...aviso }); };
  const quem = (H[H.length - 1] || {}).por || 'Alguém';
  // 1) requeridos novos (inclui a RI recém-criada)
  const ja = new Set(antes ? reqs(antes).map(q => q.tipo + ':' + q.id) : []);
  for (const q of reqs(depois).filter(q => !ja.has(q.tipo + ':' + q.id)))
    for (const c of pessoasDe(q, depois, contas))
      add(c, { tipo: 'dirigida', chamada: 'Aguardando a sua resposta', frase: `<b>${quem}</b> ${antes ? 'incluiu' : 'abriu uma RI e indicou'} ${q.tipo === 'e' ? `a empresa <b>${nomeQ(q, contas, empresas)}</b>` : 'você'} ${antes ? 'como requerido nesta RI' : 'para responder'}.` });
  // 2) resposta nova: autor e requerente
  const rA = (antes?.respostas || []).length, rD = (depois.respostas || []).length;
  if (antes && rD > rA) { const r = depois.respostas[rD - 1] || {};
    for (const id of new Set([autorId(depois), depois.requerente?.tipo === 'u' ? depois.requerente.id : ''].filter(Boolean)))
      add(contas.find(c => c.id === id), { tipo: 'resposta', chamada: 'Nova resposta', frase: `<b>${r.por || quem}</b> respondeu a sua RI.`, destaque: { rot: 'Resposta', texto: r.texto || '', por: [r.por, r.data].filter(Boolean).join(' · ') } }); }
  // 3) fechada ou cancelada: autor e requeridos
  if (antes && antes.status !== depois.status && ['FECHADA', 'CANCELADA'].includes(depois.status)) {
    const alvo = [contas.find(c => c.id === autorId(depois)), ...reqs(depois).flatMap(q => pessoasDe(q, depois, contas))];
    for (const c of alvo) add(c, { tipo: 'status', chamada: depois.status === 'FECHADA' ? 'RI fechada' : 'RI cancelada', frase: `<b>${quem}</b> ${depois.status === 'FECHADA' ? 'fechou' : 'cancelou'} esta RI.` });
  }
  return [...fila.values()];
}

/* resumo diário de uma pessoa: grupos de RIs (vazio = não envia) */
function planejarResumo(c, ris, agora = Date.now()) {
  if (!c.email || c.placeholder || (c.avisos || 'todos') === 'nenhum') return [];
  const minhas = r => reqs(r).some(q => q.tipo === 'u' ? q.id === c.id : (!!c.empresa && q.id === c.empresa));
  const vis = ris.filter(r => podeVer(c, r)), hoje = hojeSP(agora);
  const pend = vis.filter(r => r.status === 'ABERTA' && minhas(r));
  const venc = pend.filter(r => { const p = dataBr(r.prazo); return p != null && p - hoje <= 2 * DIA; });
  const novidades = vis.filter(r => autorId(r) === c.id && [...(r.respostas || []), ...(r.comentarios || [])].some(x => x.em && x.em > agora - DIA && x.porId !== c.id));
  const g = [];
  if (venc.length) g.push({ titulo: 'Prazo vencendo ou vencido', itens: venc, alerta: true });
  const resto = pend.filter(r => !venc.includes(r)); if (resto.length) g.push({ titulo: 'Aguardando a sua resposta', itens: resto });
  if (novidades.length) g.push({ titulo: 'Novidades nas RIs que você abriu (últimas 24 h)', itens: novidades });
  return g;
}

module.exports = { planejarAvisos, planejarResumo, prazoTxt, autorId, reqs, nomeQ, DIA };
