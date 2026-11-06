// Rotinas do servidor (além dos e-mails). Cada uma é ligada por ambiente em firebase/servidor.json.
// - equipe: mantém projetosRfi (o que o convidado vê de cada projeto) e rfi.participantes, na hora em que o projeto,
//   uma empresa ou uma conta muda. Hoje isso é feito pelo navegador de um administrador a cada 20 s (continua lá; é inofensivo).
// - autoria: garante no servidor que cada resposta/comentário de uma RI só é alterado ou excluído por quem escreveu
//   (ou pelo Master). Uma alteração indevida é desfeita e registrada no histórico da RI.
const { logger } = require('firebase-functions/v2');

/* ------------------------------------------------------------------ equipe */
const partsDe = it => Array.isArray(it && it.participantes) ? it.participantes : [];
function empresasProj(p, contas, empresas) {
  const ids = new Set();
  for (const l of (p.disciplinas || [])) { if (l.parceiro) ids.add(l.parceiro); for (const a of (l.apoio || [])) ids.add(a); }
  for (const e of (p.empresasExtra || [])) ids.add(e);
  for (const uid of partsDe(p)) { const u = contas.find(x => x.id === uid); if (u && u.empresa) ids.add(u.empresa); }
  return [...ids].filter(id => empresas[id] != null).sort((a, b) => String(empresas[a]).localeCompare(String(empresas[b]), 'pt'));
}
function envolvidos(p, contas, empresas) {
  const es = empresasProj(p, contas, empresas).map(id => { const discs = (p.disciplinas || []).filter(l => l.parceiro === id || (l.apoio || []).includes(id)).map(l => l.sigla || l.cod);
    return { tipo: 'e', id, nome: empresas[id] || '', ...(discs.length ? { discs } : {}) }; });
  const us = partsDe(p).map(uid => contas.find(x => x.id === uid)).filter(Boolean)
    .map(u => ({ tipo: 'u', id: u.id, nome: u.nome || u.email || '', ...(u.empresa && empresas[u.empresa] != null ? { empresa: u.empresa, empresaNome: empresas[u.empresa] } : {}) }))
    .sort((a, b) => String(a.nome).localeCompare(String(b.nome), 'pt'));
  return [...es, ...us];
}
// mesmo conteúdo que o navegador grava (projetosRfiSincronizar), para os dois não ficarem se sobrescrevendo
function docProjeto(p, contas, empresas, todos) {
  return { nome: p.nome || '', codigo: (p.campos && p.campos.codigo) || '', pasta: p.pasta || '', status: p.status || '',
    disciplinas: (p.disciplinas || []).map(d => ({ cod: d.cod, ...(d.sigla ? { sigla: d.sigla } : {}) })),
    participantes: [...new Set([...partsDe(p), ...todos])], envolvidos: envolvidos(p, contas, empresas) };
}
async function sincronizarEquipe(db, soProjeto) {
  const [it, ac, pr] = await Promise.all([db.collection('itens').where('secao', 'in', ['projetos', 'parceiros']).get(), db.collection('acessos').get(), db.collection('projetosRfi').get()]);
  const contas = ac.docs.map(d => ({ id: d.id, ...d.data() })), todos = contas.filter(u => u.todosProjetos && u.nivel === 'usuario').map(u => u.id);
  const empresas = {}, projetos = [];
  for (const d of it.docs) { const v = d.data(); if (v.secao === 'parceiros') empresas[d.id] = v.nome || ''; else projetos.push({ id: d.id, ...v }); }
  const atual = Object.fromEntries(pr.docs.map(d => [d.id, d.data()]));
  let np = 0, nr = 0;
  for (const p of projetos) {
    if (soProjeto && p.id !== soProjeto) continue;
    const v = docProjeto(p, contas, empresas, todos);
    if (JSON.stringify(atual[p.id] || null) !== JSON.stringify(v)) { await db.collection('projetosRfi').doc(p.id).set(v); np++; }
    const pj = JSON.stringify(v.participantes);
    for (const r of (await db.collection('rfi').where('projeto', '==', p.id).get()).docs)
      if (JSON.stringify(r.data().participantes || []) !== pj) { await r.ref.update({ participantes: v.participantes }); nr++; }
  }
  if (!soProjeto) { const ids = new Set(projetos.map(p => p.id)); for (const id of Object.keys(atual)) if (!ids.has(id)) { await db.collection('projetosRfi').doc(id).delete(); np++; } }
  if (np || nr) logger.info('equipe', { projetos: np, ris: nr, so: soProjeto || 'todos' });
}

/* ------------------------------------------------------------------ autoria */
const chave = x => x.em ? 't' + x.em + '|' + (x.porId || '') : 'x' + (x.data || '') + '|' + x.texto;
/* compara antes/depois de uma lista de respostas ou comentários e devolve a lista corrigida (ou null se está tudo certo) */
function corrigirLista(antes, depois, uid, nome) {
  const A = new Map((antes || []).map(x => [chave(x), x])), B = (depois || []).map(x => ({ ...x }));
  const notas = []; let mexeu = false;
  const bKeys = new Set(B.map(chave));
  // excluído por quem não escreveu: volta
  for (const [k, x] of A) if (!bKeys.has(k) && x.porId !== uid) {
    // pode ter sido editado (mesmo em/porId, outro texto): trata abaixo
    const ed = x.em ? B.findIndex(y => y.em === x.em && (y.porId || '') === (x.porId || '')) : -1;
    if (ed < 0) { B.push(x); mexeu = true; notas.push(`exclusão desfeita (registro de ${x.por || 'outra pessoa'})`); }
  }
  for (let i = 0; i < B.length; i++) {
    const y = B[i], x = A.get(chave(y)) || (y.em ? [...A.values()].find(a => a.em === y.em && (a.porId || '') === (y.porId || '')) : null);
    if (x) { if ((x.texto !== y.texto || (x.data || '') !== (y.data || '')) && x.porId !== uid) { B[i] = x; mexeu = true; notas.push(`edição desfeita (registro de ${x.por || 'outra pessoa'})`); } }
    else if (y.porId !== uid) { B[i] = { ...y, porId: uid, por: nome }; mexeu = true; notas.push('autor corrigido num registro novo'); }
  }
  B.sort((a, b) => (a.em || 0) - (b.em || 0) || 0);
  return mexeu ? { lista: B, notas } : null;
}
function verificarAutoria(antes, depois, uid, nome) {
  if (!antes || !depois) return null;
  const upd = {}, notas = [];
  for (const c of ['respostas', 'comentarios']) { const r = corrigirLista(antes[c], depois[c], uid, nome); if (r) { upd[c] = r.lista; notas.push(...r.notas.map(n => (c === 'respostas' ? 'Resposta: ' : 'Comentário: ') + n)); } }
  // histórico: as entradas novas têm de ser de quem gravou
  const hA = (antes.historico || []).length, H = (depois.historico || []).map(x => ({ ...x }));
  let hMexeu = false; for (let i = hA; i < H.length; i++) if (H[i].porId !== uid) { H[i].porId = uid; H[i].por = nome; hMexeu = true; }
  if (!Object.keys(upd).length && !hMexeu) return null;
  if (notas.length) H.push({ em: Date.now(), por: 'Sistema', porId: 'sistema', acao: 'Alteração desfeita', campos: notas.map(n => ({ c: 'Servidor', para: n })) });
  if (hMexeu && !notas.length) H.push({ em: Date.now(), por: 'Sistema', porId: 'sistema', acao: 'Autor corrigido no histórico' });
  upd.historico = H;
  return upd;
}

module.exports = { sincronizarEquipe, docProjeto, envolvidos, empresasProj, verificarAutoria, corrigirLista };
