// Service worker das versões Firebase: atende /_blob/<id> (imagens e anexos) pedindo o arquivo à página,
// que busca no Google Drive com o login Google da pessoa. Guarda em cache para não baixar de novo.
const CACHE = 'fja-blobs-v1';
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', e => {
  const u = new URL(e.request.url);
  if (u.origin !== location.origin || !u.pathname.startsWith('/_blob/')) return;
  e.respondWith(servir(e, decodeURIComponent(u.pathname.slice(7))));
});
async function servir(e, id) {
  const c = await caches.open(CACHE), salvo = await c.match('/_blob/' + id);
  if (salvo) return salvo;
  const cli = (e.clientId && await self.clients.get(e.clientId)) || (await self.clients.matchAll({ type: 'window' }))[0];
  if (!cli) return new Response('sem página', { status: 503 });
  const r = await new Promise(ok => {
    const ch = new MessageChannel(); ch.port1.onmessage = m => ok(m.data);
    cli.postMessage({ tipo: 'blob', id }, [ch.port2]);
    setTimeout(() => ok({ ok: false, msg: 'tempo esgotado' }), 150000);
  });
  if (!r.ok) return new Response(r.msg || 'erro', { status: 404 });
  const resp = new Response(r.buf, { headers: { 'Content-Type': r.tipo || 'application/octet-stream', 'Cache-Control': 'private, max-age=31536000' } });
  c.put('/_blob/' + id, resp.clone()).catch(() => {});
  return resp;
}
