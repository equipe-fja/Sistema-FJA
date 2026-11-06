// Homologação: 5 RFIs de teste (exemplo:true) no projeto 128, em situações diferentes, visíveis ao "Convidado Teste".
const fs = require('fs');
const admin = require('firebase-admin');
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
admin.initializeApp({ credential: admin.credential.cert(chave), projectId: chave.project_id });
const db = admin.firestore();
console.log('projeto:', chave.project_id);
const DIA = 864e5, br = t => new Date(t).toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo' });
(async () => {
  const proj = (await db.collection('itens').where('secao', '==', 'projetos').get()).docs.find(d => String(d.data().campos?.codigo || '').trim() === '128');
  if (!proj) throw new Error('projeto 128 não encontrado');
  const P = proj.data(); console.log('projeto 128:', P.nome);
  const acs = (await db.collection('acessos').get()).docs.map(d => ({ id: d.id, ...d.data() }));
  const conv = acs.find(x => x.email === 'equipe+convidado@fj.arq.br'); if (!conv) throw new Error('Convidado Teste não encontrado');
  const mst = acs.find(x => x.nivel === 'master') || acs.find(x => x.nivel === 'admin');
  const adm = acs.find(x => x.nivel === 'admin' && !x.placeholder && x.id !== mst.id) || mst;
  const parc = (await db.collection('itens').where('secao', '==', 'parceiros').get()).docs.map(d => ({ id: d.id, ...d.data() }));
  const nomeE = id => parc.find(p => p.id === id)?.nome || '';
  const fja = parc.find(p => p.campos?.papel === 'Interno (FJA)') || parc.find(p => /fja|jacopucci/i.test(p.nome || ''));
  const linhas = (P.disciplinas || []).filter(l => l.parceiro && nomeE(l.parceiro));
  const outra = linhas.find(l => l.parceiro !== fja?.id) || linhas[0];
  const discs = (P.disciplinas || []).map(d => d.cod);
  const d1 = discs[0] || 'ARQ', d2 = outra?.cod || discs[1] || d1;
  const pr = (await db.collection('projetosRfi').doc(proj.id).get()).data() || {};
  const participantes = [...new Set([...(pr.participantes || P.participantes || []), conv.id])];
  const U = x => ({ tipo: 'u', id: x.id, nome: x.nome || x.email, ...(x.empresa ? { empresa: x.empresa, empresaNome: nomeE(x.empresa) } : {}) });
  const E = id => ({ tipo: 'e', id, nome: nomeE(id) });
  const ev = (quem, texto, t) => ({ data: br(t), texto, por: quem.nome || quem.email, porId: quem.id, em: t });
  const h = (quem, acao, t, campos) => ({ em: t, por: quem.nome || quem.email, porId: quem.id, acao, ...(campos ? { campos } : {}) });
  const ex = (await db.collection('rfi').where('projeto', '==', proj.id).get()).docs;
  for (const d of ex) if (d.data().exemploTeste) await d.ref.delete(); // roda de novo sem duplicar
  let num = ex.filter(d => !d.data().exemploTeste).reduce((m, d) => Math.max(m, +d.data().num || 0), 0);
  const agora = Date.now();
  const R = [];
  // 1. dirigida ao convidado, aberta, dentro do prazo: ele pode responder
  { const t = agora - 2 * DIA; R.push({ assunto: 'Cota de piso acabado do térreo', descricao: 'Confirmar a cota de piso acabado do térreo em relação ao meio-fio, para o projeto de drenagem.', status: 'ABERTA',
      requerente: U(mst), autorId: mst.id, autorNome: mst.nome || mst.email, requeridos: [U(conv)], disciplinas: [d1], abertura: br(t), prazo: br(agora + 5 * DIA), criadoEm: t,
      respostas: [], comentarios: [], historico: [h(mst, 'Criou a RFI', t)] }); }
  // 2. dirigida a outra empresa, prazo vencido: o convidado só acompanha e comenta
  { const t = agora - 12 * DIA, quem = outra ? E(outra.parceiro) : U(adm); R.push({ assunto: 'Compatibilização de shafts com a estrutura', descricao: 'Os shafts previstos interferem com as vigas do 2º pavimento. Qual a solução?', status: 'ABERTA',
      requerente: U(adm), autorId: adm.id, autorNome: adm.nome || adm.email, requeridos: [quem], disciplinas: [...new Set([d1, d2])], abertura: br(t), prazo: br(agora - 3 * DIA), criadoEm: t,
      respostas: [], comentarios: [ev(adm, 'Reforçado por telefone; aguardando retorno.', agora - 4 * DIA)],
      historico: [h(adm, 'Criou a RFI', t), h(adm, 'Comentou', agora - 4 * DIA, [{ c: 'Comentário adicionado', para: 'Reforçado por telefone; aguardando retorno.' }])] }); }
  // 3. respondida pelo convidado, com comentário da equipe
  { const t = agora - 8 * DIA, tr = agora - 6 * DIA, tc = agora - 5 * DIA; R.push({ assunto: 'Especificação do revestimento externo', descricao: 'Qual o revestimento definido para a fachada leste: porcelanato ou ACM?', status: 'RESPONDIDA',
      requerente: U(mst), autorId: mst.id, autorNome: mst.nome || mst.email, requeridos: [U(conv)], disciplinas: [d1], abertura: br(t), prazo: br(t + 7 * DIA), criadoEm: t,
      respostas: [ev(conv, 'Porcelanato 60x120 na fachada leste; ACM só nas marquises.', tr)], comentarios: [ev(mst, 'Ok, vamos atualizar o caderno de especificações.', tc)],
      historico: [h(mst, 'Criou a RFI', t), h(conv, 'Respondeu', tr, [{ c: 'Status', de: 'ABERTA', para: 'RESPONDIDA' }, { c: 'Resposta adicionada', para: 'Porcelanato 60x120 na fachada leste; ACM só nas marquises.' }]),
        h(mst, 'Comentou', tc, [{ c: 'Comentário adicionado', para: 'Ok, vamos atualizar o caderno de especificações.' }])] }); }
  // 4. aberta pelo próprio convidado (ele é o autor: pode editar), dirigida à FJA
  { const t = agora - 1 * DIA; R.push({ assunto: 'Altura das bancadas dos sanitários acessíveis', descricao: 'Podemos usar bancada a 78 cm com cuba de sobrepor nos sanitários acessíveis?', status: 'ABERTA',
      requerente: U(conv), autorId: conv.id, autorNome: conv.nome || conv.email, requeridos: [fja ? E(fja.id) : U(mst)], disciplinas: [d1], abertura: br(t), prazo: br(agora + 7 * DIA), criadoEm: t,
      respostas: [], comentarios: [], historico: [h(conv, 'Criou a RFI', t)] }); }
  // 5. fechada: respondida pela FJA, com fechamento
  { const t = agora - 20 * DIA, tr = agora - 15 * DIA, tf = agora - 14 * DIA; R.push({ assunto: 'Posição da caixa d\'água', descricao: 'A caixa d\'água fica sobre o núcleo de escadas ou sobre a casa de máquinas?', status: 'FECHADA',
      requerente: U(conv), autorId: conv.id, autorNome: conv.nome || conv.email, requeridos: [fja ? E(fja.id) : U(mst)], disciplinas: [...new Set([d1, d2])], abertura: br(t), prazo: br(t + 7 * DIA), fechamento: br(tf), criadoEm: t,
      respostas: [ev(mst, 'Sobre o núcleo de escadas, conforme planta de cobertura R02.', tr)], comentarios: [],
      historico: [h(conv, 'Criou a RFI', t), h(mst, 'Respondeu', tr, [{ c: 'Status', de: 'ABERTA', para: 'RESPONDIDA' }, { c: 'Resposta adicionada', para: 'Sobre o núcleo de escadas, conforme planta de cobertura R02.' }]),
        h(mst, 'Alterou', tf, [{ c: 'Status', de: 'RESPONDIDA', para: 'FECHADA' }, { c: 'Fechamento', para: br(tf) }])] }); }
  for (const r of R) {
    num++; const body = { ...r, projeto: proj.id, num, participantes, requeridosIds: r.requeridos.map(q => q.tipo + ':' + q.id), atualizadoEm: agora, exemplo: true, exemploTeste: true };
    await db.collection('rfi').add(body); console.log('RFI', num, '·', r.status, '·', r.assunto, '· para', r.requeridos.map(q => q.nome).join(', '));
  }
  console.log('CONCLUÍDO: 5 RFIs no projeto 128 · convidado', conv.nome);
})().catch(e => { console.error('ERRO:', e.message || e); process.exit(1); });
