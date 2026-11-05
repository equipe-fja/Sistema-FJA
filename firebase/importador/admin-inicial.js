// Garante contas de administrador (cria a conta se não existir, sem senha; a pessoa usa "Esqueci a senha" para criar a sua).
// Uso: node admin-inicial.js pedido.json   {"admins":[{"email":"...","nome":"..."}]}
const fs = require('fs');
const admin = require('firebase-admin');
const cfg = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
admin.initializeApp({ credential: admin.credential.cert(chave), projectId: chave.project_id });
console.log('projeto:', chave.project_id);
(async () => {
  for (const a of cfg.admins || []) {
    const email = a.email.trim().toLowerCase();
    let u; try { u = await admin.auth().getUserByEmail(email); console.log(email, ': conta já existia'); }
    catch { u = await admin.auth().createUser({ email }); console.log(email, ': conta criada'); }
    const ref = admin.firestore().collection('acessos').doc(u.uid), g = await ref.get();
    await ref.set({ email, nome: a.nome || (g.exists && g.data().nome) || email.split('@')[0], nivel: 'admin', trocarSenha: false,
      criadoPor: 'configuração inicial', em: (g.exists && g.data().em) || Date.now() }, { merge: true });
    console.log(email, ': administrador');
  }
  console.log('CONCLUÍDO');
})().catch(e => { console.error('ERRO:', e.message || e); process.exit(1); });
