// Manutenção pontual do banco (Firestore), pedida por firebase/manutencao.json e registrada em logs/manutencao.txt
// (branch firebase-log). Tarefa "apostrofo": documentos da Relação de documentos (coleção rdoc) de um projeto com
// apóstrofo no início de algum campo ('005), vindos de uma importação da planilha.
//   aplicar:false -> só lista o que seria feito (nada é alterado)
//   aplicar:true  -> apaga as cópias com apóstrofo que têm o documento equivalente sem apóstrofo (os comentários
//                    ligados passam para ele); as que não têm equivalente só são apagadas com "semPar":"apagar".
const fs = require('fs');
const admin = require('firebase-admin');
const pedido = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const chaveSA = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
admin.initializeApp({ credential: admin.credential.cert(chaveSA), projectId: chaveSA.project_id });
const db = admin.firestore();
const norm = t => String(t || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase();
const SIS = ['atualizadoEm', 'atualizadoPor', 'sincronizadoEm', 'editadoLocal', 'criadoEm', 'criadoPor'];
const temApos = d => Object.entries(d).some(([k, v]) => !SIS.includes(k) && typeof v === 'string' && /^['’‘`]/.test(v));
const tira = v => typeof v === 'string' ? v.replace(/^['’‘`]+/, '').trim() : v;
const chave = d => ['codDisc', 'fase', 'bloco', 'item', 'revisao'].map(k => norm(tira(d[k] || '')).trim()).join('_');
const nomeArq = d => ['codDisc', 'fase', 'bloco', 'item', 'revisao'].map(k => d[k] || '-').join('_');
(async () => {
  console.log('projeto Firebase:', pedido.projeto, '· tarefa:', pedido.tarefa, '· aplicar:', !!pedido.aplicar);
  if (pedido.tarefa !== 'apostrofo') throw new Error('tarefa desconhecida: ' + pedido.tarefa);
  const itens = await db.collection('itens').get();
  const projs = itens.docs.filter(d => {
    const x = d.data(); return norm([x.nome, x.campos?.codigo, x.campos?.codCliente].join(' ')).includes(norm(pedido.busca));
  });
  console.log(`itens com "${pedido.busca}":`, projs.map(d => `${d.id} = ${d.data().nome} (${d.data().secao || ''})`).join(' | ') || 'nenhum');
  const comRd = projs.filter(d => !pedido.pid || d.id === pedido.pid);
  for (const p of comRd) {
    const rd = await db.collection('rdoc').where('projeto', '==', p.id).get();
    if (!rd.size) continue;
    const docs = rd.docs.map(d => ({ id: d.id, ...d.data() }));
    const limpos = new Map(docs.filter(d => !temApos(d)).map(d => [chave(d), d]));
    const ruins = docs.filter(temApos);
    console.log(`\n== ${p.id} · ${p.data().nome}: ${docs.length} documentos, ${ruins.length} com apóstrofo`);
    const coms = ruins.length ? (await db.collection('coment').where('projeto', '==', p.id).get()).docs : [];
    let apagados = 0, semPar = 0;
    for (const d of ruins) {
      const par = limpos.get(chave(d));
      const ligados = coms.filter(c => c.data().rdoc === d.id);
      console.log(`${par ? 'APAGAR ' : 'SEM PAR'} ${nomeArq(d)}  [${d.id}]${par ? `  ->  equivale a ${nomeArq(par)} [${par.id}]` : ''}${d.editadoLocal ? '  (editado no sistema)' : ''}${ligados.length ? `  · ${ligados.length} comentário(s) ligado(s)` : ''}  · ${d.descricao || ''}`);
      if (!par) semPar++;
      if (!pedido.aplicar) continue;
      if (!par && pedido.semPar !== 'apagar') continue;
      for (const c of ligados) await c.ref.update(par ? { rdoc: par.id } : { rdoc: admin.firestore.FieldValue.delete() });
      await db.collection('rdoc').doc(d.id).delete();
      apagados++;
    }
    console.log(`resumo: ${ruins.length} com apóstrofo · ${ruins.length - semPar} com equivalente sem apóstrofo · ${semPar} sem equivalente${pedido.aplicar ? ` · APAGADOS: ${apagados}` : ' · nada foi alterado'}`);
  }
})().catch(e => { console.error('ERRO:', e.message); process.exit(1); });
