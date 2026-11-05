// Cria pessoas fictícias por empresa (Biblioteca › Parceiros) para teste: 2 a 3 por empresa,
// uma com conta (e-mail @<empresa>.example.com, sem senha: aparece "aguardando 1º acesso") e as outras sem acesso (placeholder).
// Marca tudo com exemplo:true. Roda de novo sem duplicar (procura pelo e-mail / nome).
const fs = require('fs');
const admin = require('firebase-admin');
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
admin.initializeApp({ credential: admin.credential.cert(chave), projectId: chave.project_id });
const db = admin.firestore();
console.log('projeto:', chave.project_id);
const NOMES = ['Ana Ribeiro','Bruno Tavares','Carla Mendes','Diego Fontes','Elisa Prado','Fábio Nogueira','Gabriela Lins','Henrique Sá','Isabela Rocha','João Valente',
  'Karina Dias','Leonardo Paz','Marina Couto','Nicolas Faria','Olívia Brandão','Paulo Teixeira','Quésia Moura','Rafael Lobo','Sabrina Guedes','Tiago Amaral',
  'Úrsula Pires','Vitor Campos','Wesley Moreira','Yasmin Cardoso','Zeca Albuquerque','Bianca Leal','César Fontana','Débora Matos','Eduardo Reis','Fernanda Sales'];
const slug = s => String(s).normalize('NFKD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '');
(async () => {
  const parc = (await db.collection('itens').where('secao', '==', 'parceiros').get()).docs;
  const ja = (await db.collection('acessos').get()).docs.map(d => ({ id: d.id, ...d.data() }));
  let k = 0, contas = 0, ph = 0;
  for (const p of parc.sort((a, b) => String(a.data().nome).localeCompare(String(b.data().nome)))) {
    const emp = p.data(), interno = emp.campos?.papel === 'Interno (FJA)', n = interno ? 2 : 2 + (k % 2);
    for (let i = 0; i < n; i++) {
      const nome = NOMES[(k * 3 + i) % NOMES.length], base = { nome, empresa: p.id, nivel: interno ? 'admin' : 'usuario', exemplo: true, criadoPor: 'exemplos', em: Date.now() };
      if (i === 0) { // conta de verdade (sem senha)
        const email = `${slug(nome.split(' ')[0])}.${slug(nome.split(' ').slice(-1)[0])}@${slug(emp.nome).slice(0, 20) || 'empresa'}.example.com`;
        if (ja.some(x => x.email === email)) continue;
        let u; try { u = await admin.auth().getUserByEmail(email); } catch { u = await admin.auth().createUser({ email, displayName: nome }); }
        await db.collection('acessos').doc(u.uid).set({ ...base, email, trocarSenha: false });
        contas++; console.log('conta  ', emp.nome, '·', nome, '·', email);
      } else {
        if (ja.some(x => x.nome === nome && x.empresa === p.id)) continue;
        const id = 'ph-ex' + slug(p.id).slice(0, 8) + i + Math.random().toString(36).slice(2, 8);
        await db.collection('acessos').doc(id).set({ ...base, email: '', placeholder: true });
        ph++; console.log('sem acesso', emp.nome, '·', nome);
      }
    }
    k++;
  }
  // duas pessoas sem empresa
  for (const nome of ['Roberto Lima (fiscal da obra)', 'Sônia Barros (cliente)']) if (!ja.some(x => x.nome === nome)) {
    await db.collection('acessos').doc('ph-exsem' + slug(nome).slice(0, 10)).set({ nome, empresa: '', nivel: 'usuario', exemplo: true, email: '', placeholder: true, criadoPor: 'exemplos', em: Date.now() }); ph++;
    console.log('sem empresa', nome);
  }
  console.log(`CONCLUÍDO: ${parc.length} empresas · ${contas} contas · ${ph} sem acesso`);
})().catch(e => { console.error('ERRO:', e.message || e); process.exit(1); });
