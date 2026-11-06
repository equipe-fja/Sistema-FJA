// Corpo dos e-mails do Sistema FJA (mesmo desenho de firebase/email/modelo-ri.html): tabelas e estilos embutidos,
// para funcionar no Gmail, Outlook e celular.
const esc = s => String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const F = 'font-family:Arial,Helvetica,sans-serif';
const ROT = 'font-size:11px;letter-spacing:.8px;color:#8A8A86;text-transform:uppercase';
const COR_ST = { ABERTA: ['#FDF0D5', '#8A5A00'], RESPONDIDA: ['#E3EEF9', '#1D4E89'], FECHADA: ['#E3F1E3', '#2E6B2E'], CANCELADA: ['#EEEEEC', '#5E5E5C'] };

function moldura({ preview, corpo, rodape, url }) {
  return `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light">
<style>@media (max-width:480px){.px{padding-left:18px!important;padding-right:18px!important}.ficha td.col{display:block!important;width:100%!important;padding-left:0!important}}</style></head>
<body style="margin:0;padding:0;background:#F2F2EF;-webkit-text-size-adjust:100%">
<div style="display:none;max-height:0;overflow:hidden;font-size:1px;line-height:1px;color:#F2F2EF">${esc(preview)}</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#F2F2EF"><tr><td align="center" style="padding:28px 12px">
<table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:600px;background:#FFFFFF;border:1px solid #E2E2DF">
<tr><td class="px" style="padding:22px 32px 18px 32px;border-bottom:3px solid #8E1B1B">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
    <td width="44" valign="middle"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td width="38" height="38" align="center" valign="middle" style="width:38px;height:38px;border:1px solid #C9C9C5;${F};font-size:16px;font-weight:bold;color:#8E1B1B">FJ</td></tr></table></td>
    <td valign="middle" style="padding-left:12px;${F}"><div style="font-size:17px;font-weight:bold;color:#1C1C1A;line-height:20px">Sistema <span style="color:#8E1B1B">FJA</span></div>
      <div style="font-size:11px;letter-spacing:1.2px;color:#6E6E6A;line-height:15px;text-transform:uppercase">Filipe Jacopucci <b style="color:#1C1C1A">Arquitetura</b></div></td>
    <td align="right" valign="middle" style="${F};font-size:11px;letter-spacing:1.2px;color:#6E6E6A;text-transform:uppercase;white-space:nowrap">RI</td>
  </tr></table></td></tr>
${corpo}
<tr><td class="px" style="padding:26px 32px 26px 32px;${F}"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="border-top:1px solid #E2E2DF"><tr><td style="padding-top:16px;font-size:12px;line-height:18px;color:#8A8A86">
  ${rodape}<br>Para escolher quais avisos receber (todos, só o resumo diário ou nenhum), acesse <a href="${esc(url)}" style="color:#8E1B1B">Minha conta</a> no sistema.
</td></tr><tr><td style="padding-top:14px;font-size:10px;letter-spacing:1.6px;color:#A5A5A1;text-transform:uppercase">Filipe <b>Jacopucci</b> Arquitetura · <a href="https://www.fj.arq.br" style="color:#A5A5A1;text-decoration:none">www.fj.arq.br</a></td></tr></table></td></tr>
</table></td></tr></table></body></html>`;
}

const selo = st => { const [bg, cor] = COR_ST[st] || COR_ST.ABERTA; return `<span style="display:inline-block;background:${bg};color:${cor};font-size:11px;font-weight:bold;letter-spacing:.6px;padding:4px 9px;border-radius:999px;text-transform:uppercase">${esc(st || 'ABERTA')}</span>`; };
const botao = (url, txt) => `<table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#8E1B1B" style="border-radius:4px"><a href="${esc(url)}" target="_blank" style="display:inline-block;padding:13px 30px;${F};font-size:14px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:4px">${esc(txt)}</a></td></tr></table>`;

/* aviso de uma RI: chamada (motivo), frase, ficha, destaque (resposta), botão, movimentação */
function avisoRi({ chamada, frase, ri, proj, prazoTxt, destaque, mov, url, urlConta, rodape, preview }) {
  const campo = (rot, val) => `<div style="${ROT}">${esc(rot)}</div><div style="color:#1C1C1A">${val}</div>`;
  const corpo = `
<tr><td class="px" style="padding:28px 32px 6px 32px;${F}">
  <div style="font-size:12px;letter-spacing:1.2px;color:#8E1B1B;font-weight:bold;text-transform:uppercase">${esc(chamada)}</div>
  <div style="font-size:22px;line-height:28px;color:#1C1C1A;font-weight:bold;padding-top:6px">${esc(ri.assunto || '(sem assunto)')}</div>
  <div style="font-size:14px;line-height:21px;color:#4A4A47;padding-top:8px">${frase}</div></td></tr>
<tr><td class="px" style="padding:18px 32px 0 32px"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="border:1px solid #E2E2DF;background:#FAFAF8">
  <tr><td style="padding:14px 18px;border-bottom:1px solid #E2E2DF;${F}"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
    <td style="font-size:13px;color:#1C1C1A"><span style="display:inline-block;background:#8E1B1B;color:#FFFFFF;font-weight:bold;font-size:12px;padding:3px 8px;border-radius:3px">Nº ${esc(ri.num ?? '')}</span>&nbsp; <b>${esc(proj)}</b></td>
    <td align="right">${selo(ri.status)}</td></tr></table></td></tr>
  <tr><td style="padding:6px 18px 12px 18px;${F}"><table role="presentation" class="ficha" width="100%" cellpadding="0" cellspacing="0" border="0" style="font-size:13px;line-height:19px">
    <tr><td class="col" width="50%" valign="top" style="padding:10px 10px 0 0">${campo('Requerente', esc(ri.requerenteNome || '—'))}</td><td class="col" width="50%" valign="top" style="padding:10px 0 0 10px">${campo('Requeridos', esc(ri.requeridosNomes || '—'))}</td></tr>
    <tr><td class="col" width="50%" valign="top" style="padding:12px 10px 0 0">${campo('Prazo para resposta', ri.prazo ? `<b>${esc(ri.prazo)}</b>${prazoTxt ? ` <span style="color:#8E1B1B">· ${esc(prazoTxt)}</span>` : ''}` : '—')}</td>
        <td class="col" width="50%" valign="top" style="padding:12px 0 0 10px">${campo('Disciplinas', (ri.disciplinas || []).map(d => `<span style="display:inline-block;border:1px solid #C9C9C5;border-radius:3px;padding:0 5px;font-size:12px;font-family:Consolas,Menlo,monospace">${esc(d)}</span>`).join(' ') || '—')}</td></tr>
  </table></td></tr>
  <tr><td style="padding:4px 18px 18px 18px;${F}"><div style="${ROT};padding-bottom:6px">Pergunta</div>
    <div style="border-left:3px solid #8E1B1B;background:#FFFFFF;padding:12px 14px;font-size:14px;line-height:21px;color:#1C1C1A;white-space:pre-line">${esc(ri.descricao || '—')}</div>
    ${destaque ? `<div style="${ROT};padding:14px 0 6px">${esc(destaque.rot)}</div><div style="border-left:3px solid #2E6B2E;background:#FFFFFF;padding:12px 14px;font-size:14px;line-height:21px;color:#1C1C1A;white-space:pre-line">${esc(destaque.texto)}${destaque.por ? `<div style="font-size:12px;color:#8A8A86;padding-top:6px">— ${esc(destaque.por)}</div>` : ''}</div>` : ''}
  </td></tr></table></td></tr>
<tr><td align="center" class="px" style="padding:26px 32px 6px 32px">${botao(url, 'Abrir a RI no sistema')}
  <div style="${F};font-size:12px;color:#8A8A86;padding-top:10px">Responda pelo sistema: assim tudo fica registrado no histórico da RI.</div></td></tr>
${(mov || []).length ? `<tr><td class="px" style="padding:24px 32px 4px 32px;${F}"><div style="${ROT};letter-spacing:1.2px;padding-bottom:8px;border-bottom:1px solid #E2E2DF">Movimentação</div>
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="font-size:13px;line-height:19px">${mov.map((m, i) => `<tr><td width="118" valign="top" style="padding:9px 0;color:#8A8A86${i < mov.length - 1 ? ';border-bottom:1px solid #F0F0ED' : ''}">${esc(m.quando)}</td><td valign="top" style="padding:9px 0;color:#1C1C1A${i < mov.length - 1 ? ';border-bottom:1px solid #F0F0ED' : ''}"><b>${esc(m.quem)}</b> ${esc(m.oque)}</td></tr>`).join('')}</table></td></tr>` : ''}`;
  return moldura({ preview, corpo, rodape, url: urlConta });
}

/* resumo diário: grupos de linhas (cada linha = uma RI) */
function resumo({ nome, grupos, urlConta, url, preview, data }) {
  const linha = r => `<tr><td valign="top" style="padding:10px 0;border-bottom:1px solid #F0F0ED;${F};font-size:13px;line-height:19px">
    <a href="${esc(r.url)}" style="color:#1C1C1A;text-decoration:none"><span style="display:inline-block;background:#8E1B1B;color:#FFFFFF;font-weight:bold;font-size:11px;padding:2px 6px;border-radius:3px">${esc(r.num)}</span>&nbsp; <b>${esc(r.assunto || '(sem assunto)')}</b></a>
    <div style="color:#6E6E6A;font-size:12px;padding-top:3px">${esc(r.proj)}${r.nota ? ` · <span style="color:${r.alerta ? '#8E1B1B' : '#6E6E6A'}">${esc(r.nota)}</span>` : ''}</div></td></tr>`;
  const corpo = `
<tr><td class="px" style="padding:28px 32px 6px 32px;${F}">
  <div style="font-size:12px;letter-spacing:1.2px;color:#8E1B1B;font-weight:bold;text-transform:uppercase">Resumo do dia · ${esc(data)}</div>
  <div style="font-size:22px;line-height:28px;color:#1C1C1A;font-weight:bold;padding-top:6px">Bom dia, ${esc(nome)}</div>
  <div style="font-size:14px;line-height:21px;color:#4A4A47;padding-top:8px">Estas são as RIs que pedem a sua atenção hoje.</div></td></tr>
${grupos.map(g => `<tr><td class="px" style="padding:22px 32px 0 32px;${F}"><div style="${ROT};letter-spacing:1.2px;padding-bottom:8px;border-bottom:1px solid #E2E2DF">${esc(g.titulo)} <span style="color:#8E1B1B">(${g.itens.length})</span></div>
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">${g.itens.map(linha).join('')}</table></td></tr>`).join('')}
<tr><td align="center" class="px" style="padding:26px 32px 6px 32px">${botao(url, 'Abrir o sistema')}</td></tr>`;
  return moldura({ preview, corpo, rodape: 'Você recebe este resumo nos dias úteis porque há RIs ligadas a você no Sistema FJA.', url: urlConta });
}

/* homologação: faixa no topo dizendo para quem o e-mail iria */
const faixaHomolog = para => `<div style="background:#FFE9B8;border-bottom:2px solid #C98A00;padding:10px 16px;${F};font-size:13px;color:#6B4A00;text-align:center">HOMOLOGAÇÃO · na oficial este e-mail iria para <b>${esc(para)}</b></div>`;

const texto = html => String(html).replace(/<style[\s\S]*?<\/style>/g, '').replace(/<br\s*\/?>/g, '\n').replace(/<\/(tr|div|p)>/g, '\n').replace(/<[^>]+>/g, '').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/[ \t]+/g, ' ').replace(/\n\s*\n+/g, '\n\n').trim();

module.exports = { avisoRi, resumo, faixaHomolog, texto, esc };
