// Cria RFIs fictícias (exemplo:true) em dois projetos, dirigidas ao "usuário teste" e à empresa FJA.
const fs = require('fs');
const admin = require('firebase-admin');
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
admin.initializeApp({ credential: admin.credential.cert(chave), projectId: chave.project_id });
const db = admin.firestore();
console.log('projeto:', chave.project_id);
const sem = s => String(s || '').normalize('NFKD').replace(/[̀-ͯ]/g, '').toLowerCase();
const T = {
  angra: [
    ['Pé-direito livre nos quartos', 'Qual o pé-direito livre mínimo nos quartos tipo, considerando forro e duto de ar-condicionado?', ['ARQ', 'CLI']],
    ['Furação de vigas para dutos', 'É possível furar as vigas do 2º pavimento (Ø150 mm) para os dutos de exaustão dos banheiros?', ['EST']],
    ['Cota de soleira do restaurante', 'Qual a cota de soleira definitiva do restaurante em relação ao deck da piscina?', ['ARQ']],
    ['Carga na casa de máquinas da piscina', 'Qual a carga distribuída a considerar na laje da casa de máquinas da piscina?', ['EST']],
    ['Tomadas nas copas dos quartos', 'As copas dos quartos terão cooktop ou apenas frigobar e chaleira?', ['ELE', 'ARQ']],
    ['Sondagem do anexo de serviço', 'As sondagens existentes cobrem a área do anexo? Precisa de furo complementar?', ['GEO', 'FUN']],
    ['Rota de fuga do bloco B', 'A escada do bloco B atende como rota de fuga única para o 3º pavimento?', ['ARQ']],
    ['Reserva técnica de incêndio', 'A reserva técnica de incêndio fica no reservatório superior ou no enterrado?', ['HID']],
    ['Pontos de TV e dados', 'Quantos pontos de dados e TV por quarto? Haverá IPTV?', ['ARQ']],
    ['Iluminação da fachada', 'Fachada com projetores no piso ou fitas de LED nas marquises?', ['LUM', 'ARQ']],
    ['Doca de serviço', 'Qual o raio de giro a considerar para caminhões na doca de serviço?', ['VIA']],
    ['Gás da cozinha industrial', 'Central de GLP ou gás natural da concessionária?', ['GAS']],
    ['Drenagem do estacionamento', 'Piso drenante ou captação por grelhas e caixas?', ['DRE', 'ARQ']],
    ['Shafts dos banheiros', 'Os shafts podem ser compartilhados entre hidráulica e elétrica?', ['HID', 'ELE']],
    ['Borda da piscina', 'Borda infinita com calha de transbordo ou borda convencional?', ['ARQ']],
    ['Gerador de emergência', 'O gerador atende todo o hotel ou só as cargas essenciais?', ['ELE']],
    ['Núcleo de elevadores', 'Dimensões das caixas de elevador com o layout revisado do núcleo?', ['ARQ', 'EST']],
    ['Ar-condicionado dos quartos', 'VRF ou split individual? Onde ficam as condensadoras?', ['CLI']],
    ['Aprovação nos bombeiros', 'O projeto de incêndio será protocolado já com o anexo de serviço?', ['ARQ', 'LEG']],
    ['Acessibilidade dos chalés', 'Quantos chalés acessíveis são exigidos e onde ficam?', ['ARQ', 'LEG']],
  ],
  cbpf: [
    ['Isolamento de vibração do laboratório', 'Qual o critério de vibração (VC-A, VC-B?) para a laje do laboratório de microscopia?', ['EST', 'ARQ']],
    ['Sala limpa ISO 7', 'A sala limpa será ISO 7 em toda a área ou só na antecâmara de equipamentos?', ['CLI', 'ARQ']],
    ['Blindagem eletromagnética', 'O laboratório de RMN exige gaiola de Faraday? Qual a atenuação mínima?', ['ELE', 'ARQ']],
    ['Gases especiais', 'Quais gases especiais (N2, Ar, He) serão distribuídos e onde fica a central de cilindros?', ['GAS']],
    ['Nitrogênio líquido', 'Haverá tanque externo de nitrogênio líquido? Precisa de acesso de caminhão?', ['GAS', 'VIA']],
    ['Carga de piso do biotério', 'Qual a sobrecarga a considerar no biotério (racks ventilados)?', ['EST']],
    ['Capelas de exaustão', 'Quantas capelas por laboratório de química e qual a vazão de projeto?', ['CLI']],
    ['Água ultrapura', 'O sistema de água ultrapura é central ou por laboratório?', ['HID']],
    ['Aterramento dos equipamentos', 'Os equipamentos sensíveis exigem malha de aterramento dedicada?', ['ELE', 'SPDA']],
    ['Estabilização de temperatura', 'Qual a tolerância de temperatura nas salas de medição (±0,5 °C)?', ['CLI']],
    ['Pé-direito do galpão de testes', 'Pé-direito livre necessário para a ponte rolante do galpão?', ['ARQ', 'EST']],
    ['Ponte rolante', 'Capacidade da ponte rolante e posição dos trilhos?', ['EST']],
    ['Rede de dados de alta velocidade', 'Os laboratórios precisam de fibra até a bancada (10 Gb)?', ['ARQ']],
    ['Nobreak dos laboratórios', 'Quais cargas vão em nobreak e qual a autonomia?', ['ELE']],
    ['Efluentes químicos', 'Os efluentes dos laboratórios precisam de tratamento antes da rede pública?', ['HID', 'ESG']],
    ['Acesso controlado', 'Quais áreas terão controle de acesso por cartão?', ['ARQ', 'ELE']],
    ['Fachada técnica', 'As condensadoras podem ficar na fachada técnica leste?', ['ARQ', 'CLI']],
    ['Tombamento do entorno', 'O terreno está em área de entorno de bem tombado? Há restrição de gabarito?', ['LEG', 'ARQ']],
    ['Auditório', 'O auditório precisa de tratamento acústico para gravação?', ['ARQ']],
    ['Paisagismo', 'Há exigência de percentual mínimo de área permeável no terreno?', ['ARQ', 'LEG']],
  ],
};
const ST = ['ABERTA', 'ABERTA', 'RESPONDIDA', 'FECHADA', 'ABERTA', 'RESPONDIDA', 'ABERTA', 'FECHADA', 'CANCELADA', 'ABERTA'];
const d = (dia, mes) => `${String(dia).padStart(2, '0')}/${String(mes).padStart(2, '0')}/2026`;
(async () => {
  const itens = (await db.collection('itens').get()).docs.map(x => ({ id: x.id, ...x.data() }));
  const projs = itens.filter(i => i.secao === 'projetos'), parc = itens.filter(i => i.secao === 'parceiros');
  const acessos = (await db.collection('acessos').get()).docs.map(x => ({ id: x.id, ...x.data() }));
  const fja = parc.find(p => sem(p.nome) === 'fja') || parc.find(p => p.campos?.papel === 'Interno (FJA)');
  const teste = acessos.find(a => /teste/.test(sem(a.nome) + ' ' + sem(a.email)));
  const master = acessos.find(a => a.nivel === 'master');
  console.log('FJA:', fja ? fja.nome + ' ' + fja.id : 'NÃO ENCONTRADA');
  console.log('usuário teste:', teste ? (teste.nome || '') + ' <' + (teste.email || '') + '> ' + teste.id : 'NÃO ENCONTRADO');
  for (const [chaveP, lista] of Object.entries(T)) {
    const pj = projs.find(p => sem(p.nome).includes(chaveP === 'angra' ? 'angra' : 'cbpf'));
    if (!pj) { console.log('projeto não encontrado:', chaveP); continue; }
    console.log('projeto:', pj.campos?.codigo, pj.nome, pj.id);
    let parts = Array.isArray(pj.participantes) ? pj.participantes.slice() : [];
    if (teste && !parts.includes(teste.id)) { parts.push(teste.id); await db.collection('itens').doc(pj.id).update({ participantes: parts }); console.log('  usuário teste incluído na equipe do projeto'); }
    const ja = (await db.collection('rfi').where('projeto', '==', pj.id).get()).docs.map(x => x.data());
    let num = ja.reduce((m, r) => Math.max(m, +r.num || 0), 0);
    if (ja.some(r => r.exemplo)) { console.log('  já tem RFIs de exemplo: pulando'); continue; }
    const lote = db.batch();
    lista.forEach(([assunto, descricao, ds], i) => {
      const st = ST[i % ST.length], ab = d(1 + i, 10), pr = d(Math.min(28, 8 + i), i < 12 ? 10 : 11);
      const req = [];
      if (teste && i % 3 !== 2) req.push({ tipo: 'u', id: teste.id, nome: teste.nome || teste.email });
      if (fja && (i % 3 !== 0 || !teste)) req.push({ tipo: 'e', id: fja.id, nome: fja.nome });
      const r = {
        projeto: pj.id, num: ++num, assunto, descricao, status: st, abertura: ab, prazo: pr, fechamento: st === 'FECHADA' ? d(Math.min(28, 10 + i), 10) : '',
        requerente: master ? { tipo: 'u', id: master.id, nome: master.nome || master.email } : (fja ? { tipo: 'e', id: fja.id, nome: fja.nome } : null),
        requeridos: req, requeridosIds: req.map(q => q.tipo + ':' + q.id), participantes: parts, disciplinas: ds,
        respostas: st === 'RESPONDIDA' || st === 'FECHADA' ? [{ data: d(Math.min(28, 9 + i), 10), texto: 'Resposta de exemplo: definição registrada para seguir com o projeto.' }] : [],
        comentarios: i % 4 === 0 ? [{ data: d(Math.min(28, 2 + i), 10), texto: 'Comentário de exemplo: conferir na próxima reunião de coordenação.' }] : [],
        exemplo: true, criadoEm: Date.now() + i, atualizadoEm: Date.now() + i,
      };
      lote.set(db.collection('rfi').doc(), r);
    });
    await lote.commit();
    console.log(`  ${lista.length} RFIs criadas (nº ${num - lista.length + 1} a ${num})`);
  }
  console.log('CONCLUÍDO');
})().catch(e => { console.error('ERRO:', e.message || e); process.exit(1); });
