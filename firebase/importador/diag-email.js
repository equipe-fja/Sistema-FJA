// Diagnóstico (homologação): o que o servidor registrou de e-mails e o estado da conta Google (sem mostrar chaves)
const fs = require('fs'), admin = require('firebase-admin');
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
admin.initializeApp({ credential: admin.credential.cert(chave), projectId: chave.project_id }); const db = admin.firestore();
(async () => {
  const e = await db.collection('emailsEnviados').orderBy('em', 'desc').limit(10).get();
  console.log('emailsEnviados:', e.size); e.docs.forEach(d => { const x = d.data(); console.log(' ', new Date(x.em).toISOString(), x.tipo, x.para, '→', x.enviadoA || '', '|', x.assunto || '', x.erro ? 'ERRO: ' + x.erro : ''); });
  const av = (await db.collection('servidor').doc('avisos').get()).data(); console.log('último gatilho de RI:', av ? new Date(av.ultimo).toISOString() + ' ri ' + av.ri + ' avisos ' + av.avisos : 'nenhum');
  const g = (await db.collection('servidor').doc('google').get()).data() || {};
  console.log('conta google:', g.conta || '-', '| autorizada em', g.em ? new Date(g.em).toISOString() : '-', '| escopos', (g.escopos || []).length, '| erro:', g.erro || '-');
  const c = (await db.collection('acessos').get()).docs.map(d => d.data()).filter(x => x.email);
  console.log('contas com e-mail:', c.length, '| empresas FJA/MEPE:', c.filter(x => x.empresa).length);
})().catch(e => { console.error('ERRO', e.message); process.exit(1); });
