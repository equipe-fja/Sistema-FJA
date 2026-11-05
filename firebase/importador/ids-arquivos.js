// Lista os ids de arquivos (32 hex, formato do armazenamento do Claude) citados no último backup da oficial.
const fs = require('fs'); const { GoogleAuth } = require('google-auth-library');
const chave = JSON.parse(fs.readFileSync(process.env.GOOGLE_APPLICATION_CREDENTIALS, 'utf8'));
const g = new GoogleAuth({ credentials: chave, scopes: ['https://www.googleapis.com/auth/drive.readonly'] });
const drive = async p => (await (await g.getClient()).request({ url: 'https://www.googleapis.com/drive/v3/' + p })).data;
const listar = async pasta => (await drive(`files?q=${encodeURIComponent(`'${pasta}' in parents and trashed = false`)}&fields=files(id,name)&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true`)).files;
(async () => {
  const ps = (await listar('1uRdXSx8JnBNegGSD4y59klSfSyLB9HuC')).filter(f => /^SISTEMA-FJA_BACKUP_/.test(f.name)).sort((a, b) => b.name.localeCompare(a.name));
  console.log('backup:', ps[0].name);
  const ids = new Set();
  const varrer = v => { if (typeof v === 'string') { if (/^[0-9a-f]{32}$/.test(v)) ids.add(v); } else if (v && typeof v === 'object') Object.values(v).forEach(varrer); };
  for (const f of await listar(ps[0].id)) { const d = await drive(`files/${f.id}?alt=media&supportsAllDrives=true`); varrer(typeof d === 'string' ? JSON.parse(d) : d); }
  console.log('IDS ' + ids.size); console.log([...ids].join('\n'));
})().catch(e => { console.error('ERRO:', e.message); process.exit(1); });
