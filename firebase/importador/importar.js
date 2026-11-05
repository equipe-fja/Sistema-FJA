// Importa um backup do Sistema FJA (pasta no Drive: 00-indice.json + partes por coleção) para o Firestore.
// Substitui o conteúdo de cada coleção do backup (apaga o que não está no backup). Não toca em "usuarios".
const fs = require('fs');
const admin = require('firebase-admin');
const { GoogleAuth } = require('google-auth-library');

const cfg = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
console.log('conta de serviço:', chave.client_email);
if (!cfg.pasta) { console.log('importar.json sem "pasta": nada a fazer'); process.exit(0); }

admin.initializeApp({ credential: admin.credential.cert(chave), projectId: 'sistema-fja' });
const db = admin.firestore();
const gauth = new GoogleAuth({ credentials: chave, scopes: ['https://www.googleapis.com/auth/drive.readonly'] });

async function drive(path) {
  const c = await gauth.getClient();
  const r = await c.request({ url: 'https://www.googleapis.com/drive/v3/' + path });
  return r.data;
}
async function listar(pasta) {
  let out = [], token = '';
  do {
    const q = encodeURIComponent(`'${pasta}' in parents and trashed = false`);
    const r = await drive(`files?q=${q}&fields=nextPageToken,files(id,name)&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true${token ? '&pageToken=' + token : ''}`);
    out = out.concat(r.files || []); token = r.nextPageToken || '';
  } while (token);
  return out;
}
async function ler(id) {
  const d = await drive(`files/${id}?alt=media&supportsAllDrives=true`);
  return typeof d === 'string' ? JSON.parse(d) : d;
}
async function gravarLote(ops) {
  for (let i = 0; i < ops.length; i += 400) {
    const b = db.batch();
    for (const f of ops.slice(i, i + 400)) f(b);
    await b.commit();
  }
}

(async () => {
  const arqs = await listar(cfg.pasta);
  const porNome = Object.fromEntries(arqs.map(a => [a.name, a.id]));
  if (!porNome['00-indice.json']) throw new Error('00-indice.json não encontrado na pasta ' + cfg.pasta + ' (a pasta foi compartilhada com a conta de serviço?)');
  const idx = await ler(porNome['00-indice.json']);
  console.log(`backup: ${idx.geradoEm} · ambiente ${idx.ambiente} · ${idx.total} documentos · ${idx.partes.length} partes`);

  const cols = {};
  for (const p of idx.partes) {
    if (!porNome[p.arquivo]) throw new Error('parte ausente: ' + p.arquivo);
    const parte = await ler(porNome[p.arquivo]);
    Object.assign(cols[p.colecao] ||= {}, parte.docs);
  }
  for (const [col, docs] of Object.entries(cols)) {
    const atuais = await db.collection(col).listDocuments();
    const sobra = atuais.filter(d => !(d.id in docs));
    await gravarLote(Object.entries(docs).map(([id, v]) => b => b.set(db.collection(col).doc(id), v)));
    await gravarLote(sobra.map(d => b => b.delete(d)));
    console.log(`${col}: ${Object.keys(docs).length} gravados, ${sobra.length} removidos`);
  }
  const conf = idx.config || {};
  await gravarLote(Object.entries(conf).map(([id, v]) => b => b.set(db.collection('config').doc(id), v)));
  console.log(`config: ${Object.keys(conf).length} gravados`);
  await db.collection('config').doc('importacao').set({ pasta: cfg.pasta, backup: idx.geradoEm, ambiente: idx.ambiente, total: idx.total, em: Date.now() });
  console.log('IMPORTAÇÃO CONCLUÍDA');
})().catch(e => { console.error('ERRO:', e.message || e); process.exit(1); });
