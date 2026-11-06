// Cria (ou atualiza) a conta de teste "Convidado Teste": nível Convidado, acesso às RFIs de todos os projetos (todosProjetos).
// E-mail equipe+convidado@fj.arq.br (chega na caixa da equipe). Senha provisória gerada e guardada em senhasProvisorias/<uid>
// (aparece em Manutenção › Usuários e acesso › Reenviar); o log não mostra a senha.
const fs = require('fs'), crypto = require('crypto');
const admin = require('firebase-admin');
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
admin.initializeApp({ credential: admin.credential.cert(chave), projectId: chave.project_id });
const db = admin.firestore(), FV = admin.firestore.FieldValue;
const EMAIL = 'equipe+convidado@fj.arq.br', NOME = 'Convidado Teste';
console.log('projeto:', chave.project_id);
(async () => {
  const al = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789', senha = Array.from(crypto.randomBytes(10), b => al[b % al.length]).join('');
  let u, nova = false;
  try { u = await admin.auth().getUserByEmail(EMAIL); } catch { u = await admin.auth().createUser({ email: EMAIL, displayName: NOME, password: senha }); nova = true; }
  const ref = db.collection('acessos').doc(u.uid), at = await ref.get();
  if (!at.exists || at.data().trocarSenha) { // ainda não entrou: (re)define a senha provisória
    if (!nova) await admin.auth().updateUser(u.uid, { password: senha });
    await db.collection('senhasProvisorias').doc(u.uid).set({ email: EMAIL, senha, em: Date.now() });
  }
  await ref.set({ email: EMAIL, nome: NOME, nivel: 'usuario', empresa: '', todosProjetos: true, ...(at.exists ? {} : { trocarSenha: true, criadoPor: 'conta-teste', em: Date.now() }) }, { merge: true });
  // acesso imediato (o sistema aberto por um administrador mantém isso depois)
  let np = 0, nr = 0;
  for (const d of (await db.collection('projetosRfi').get()).docs) { await d.ref.update({ participantes: FV.arrayUnion(u.uid) }); np++; }
  for (const d of (await db.collection('rfi').get()).docs) { await d.ref.update({ participantes: FV.arrayUnion(u.uid) }); nr++; }
  console.log(`CONCLUÍDO: ${NOME} <${EMAIL}> · ${nova ? 'conta criada' : 'conta já existia'} · ${np} projetos · ${nr} RFIs · senha provisória em Usuários e acesso`);
})().catch(e => { console.error('ERRO:', e.message || e); process.exit(1); });
