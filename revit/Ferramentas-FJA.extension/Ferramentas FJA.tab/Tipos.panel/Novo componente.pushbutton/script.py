# -*- coding: utf-8 -*-
"""Componentes: liga cada tipo de uma família carregável (de um .rfa ou já inserida no modelo) a um código
da biblioteca central do Sistema FJA (elemento/família): Keynote, Type Mark, Descrição e vínculo por tipo;
opcionalmente renomeia a família como (SIGLA) DESCRIÇÃO e os tipos como KEYNOTE - DESCRIÇÃO."""
__persistentengine__ = True
import os

from pyrevit import revit, DB, forms, script

import fja_sistema as F

from System.Windows import Visibility, Thickness, GridLength, GridUnitType, FontWeights, VerticalAlignment, \
    HorizontalAlignment, TextTrimming, CornerRadius as System_CornerRadius
from System.Windows.Controls import ListBoxItem, Grid, ColumnDefinition, TextBlock, Border
from System.Windows.Media import SolidColorBrush, Color
from Autodesk.Revit.UI.Selection import ObjectType

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()
TITULO = u"Componentes"


def _pincel(r, g, b):
    return SolidColorBrush(Color.FromRgb(r, g, b))


COR = {"cinza": _pincel(94, 94, 92), "verde": _pincel(46, 115, 64), "ambar": _pincel(138, 93, 5),
       "vermelho": _pincel(179, 38, 30), "azul": _pincel(47, 95, 138)}
FUNDO = {"verde": _pincel(228, 241, 230), "ambar": _pincel(248, 239, 217), "vermelho": _pincel(251, 231, 229),
         "azul": _pincel(232, 239, 246), "cinza": _pincel(237, 237, 235)}
COLS = [(u"Tipo", -2), (u"Inst.", 46), (u"Código", 108), (u"Keynote", 74), (u"Descrição", -3), (u"Situação", 116)]


def aplicar_estilo(win):
    try:
        from System.Windows.Markup import XamlReader
        from System.IO import File
        caminho = os.path.join(os.path.dirname(F.__file__), "fja_estilo.xaml")
        win.Resources.MergedDictionaries.Add(XamlReader.Parse(File.ReadAllText(caminho)))
    except Exception as e:
        print(u"estilo: {}".format(e))


def _txt(t, cor=None, peso=None):
    tb = TextBlock()
    tb.Text = t or u""
    tb.VerticalAlignment = VerticalAlignment.Center
    tb.Margin = Thickness(0, 0, 8, 0)
    tb.TextTrimming = TextTrimming.CharacterEllipsis
    if cor is not None:
        tb.Foreground = cor
    if peso is not None:
        tb.FontWeight = peso
    return tb


def _grade(valores, peso=None, cor=None):
    g = Grid()
    for _, w in COLS:
        cd = ColumnDefinition()
        cd.Width = GridLength(-w, GridUnitType.Star) if w < 0 else GridLength(w)
        g.ColumnDefinitions.Add(cd)
    for i, v in enumerate(valores):
        el = v if not isinstance(v, (str, type(u""), type(None))) else _txt(v, cor=cor, peso=peso)
        Grid.SetColumn(el, i)
        g.Children.Add(el)
    return g


def _selo(rot, tom):
    b = Border()
    b.CornerRadius = System_CornerRadius(9)
    b.Background = FUNDO[tom]
    b.Padding = Thickness(7, 1, 7, 1)
    b.HorizontalAlignment = HorizontalAlignment.Left
    b.VerticalAlignment = VerticalAlignment.Center
    t = TextBlock()
    t.Text = rot
    t.FontSize = 11
    t.Foreground = COR[tom]
    b.Child = t
    return b



class Janela(forms.WPFWindow):
    def __init__(self, elemento=None):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        aplicar_estilo(self)
        self.pedir_selecao = False
        self.dados = None
        self.fam = None
        self.tipos = []
        self.cods = {}          # id do item → código compartilhado (mesmo item = mesmo keynote)
        self.lista = []
        self.grupos = []
        self.siglas = []
        self._nome_editado = False
        self._pronto = False
        self._sig_ant = -1
        self.gCab.Children.Add(_grade([c for c, _ in COLS], peso=FontWeights.SemiBold, cor=COR["cinza"]))
        tem_oficial = F._mais_recente(F.PASTA_REVIT, "biblioteca") is not None
        self.cbFonte.SelectedIndex = 1 if cfg.get_option("dev", not tem_oficial) else 0
        self._pronto = True
        self.carregar()
        try:
            if elemento is None:
                ids = list(uidoc.Selection.GetElementIds())
                elemento = doc.GetElement(ids[0]) if ids else None
            if elemento is not None:
                self._usar_elemento(elemento)
        except Exception:
            pass

    # ------------------------------------------------------------ dados do sistema
    def carregar(self):
        dev = self.cbFonte.SelectedIndex == 1
        try:
            self.dados = F.Dados(dev=dev)
        except Exception as e:
            self.dados = None
            self.txtFonte.Text = u"{}".format(e)
            self.atualizar()
            return
        d = self.dados
        self.txtFonte.Text = u"Modelo: {} · {} · publicado em {} · {} elementos/famílias na biblioteca".format(
            doc.Title, d.rotulo,
            (d.geradoEm or u"")[:16].replace("T", " "), len(F.elementos(d)))
        auto = d.projeto_do_documento(doc.Title)
        self._pronto = False
        self.cbProjeto.Items.Clear()
        self.cbProjeto.Items.Add(u"(nenhum)")
        sel = 0
        for i, p in enumerate(d.projetos):
            self.cbProjeto.Items.Add(u"{} · {}".format(p.get("codigo"), p.get("nome")))
            if auto and p["id"] == auto["id"]:
                sel = i + 1
        self.cbProjeto.SelectedIndex = sel
        self.cbSigla.Items.Clear()
        self.siglas = F.siglas_elemento(d)
        for s in self.siglas:
            self.cbSigla.Items.Add(u"{} · {}".format(s["sigla"], s.get("nome") or u""))
        self.cbSigla.Items.Add(u"+ Nova sigla…")
        self._pronto = True
        self._usar_projeto(d.projetos[sel - 1] if sel else None)

    def _usar_projeto(self, proj):
        try:
            self.dados.carregar_projeto(proj)
        except Exception as e:
            forms.alert(u"{}".format(e), title=TITULO)
            self.dados.carregar_projeto(None)
        self.cods = {}
        if self.fam:
            self._codigos_atuais()
        self.preencher()
        self.atualizar()

    def fonte_mudou(self, sender, args):
        if not self._pronto:
            return
        cfg.set_option("dev", self.cbFonte.SelectedIndex == 1)
        script.save_config()
        self.carregar()

    def projeto_mudou(self, sender, args):
        if not self._pronto or self.dados is None:
            return
        i = self.cbProjeto.SelectedIndex
        self._usar_projeto(self.dados.projetos[i - 1] if i > 0 else None)

    def _item(self, iid):
        if not iid or self.dados is None:
            return None
        it = self.dados.itens.get(iid)
        if it is None:
            for x in self.dados.itens_pendentes():
                if x["id"] == iid:
                    return x
        return it

    def _cod_de(self, it):
        return self.cods.setdefault(it["id"], {"item": it})

    # ------------------------------------------------------------ família
    def escolher(self, sender, args):
        caminho = forms.pick_file(file_ext="rfa", title=u"Escolha a família (.rfa)")
        if not caminho:
            return
        try:
            f = F.ler_familia(doc.Application, caminho)
        except Exception as e:
            forms.alert(u"Não foi possível ler a família:\n{}".format(e), title=TITULO)
            return
        self.tbArquivo.Text = caminho
        self.txtFamilia.Text = u"Família nova {} · categoria {} · {} tipo(s)".format(
            f["nome"], f["categoria"] or u"?", len(f["tipos"]))
        self._usar_familia(f)

    def selecionar(self, sender, args):
        # a seleção precisa da janela fechada (esconder a janela modal tira o contexto da API do Revit)
        self.pedir_selecao = True
        self.Close()

    def _usar_elemento(self, el):
        try:
            f = F.familia_do_elemento(doc, el)
        except Exception as e:
            forms.alert(u"{}".format(e), title=TITULO)
            return
        self.tbArquivo.Text = u"No modelo: {}".format(f["nome"])
        self.txtFamilia.Text = u"Readequar família {} · categoria {} · {} tipo(s), {} instância(s) no modelo".format(
            f["nome"], f["categoria"] or u"?", len(f["tipos"]), sum(f["instancias"].values()))
        self._usar_familia(f, sel=f["tipoAtual"])

    def _usar_familia(self, f, sel=None):
        self.fam = f
        self.tipos = [dict(t, cod=None) for t in f["tiposInfo"]]
        self._nome_editado = False
        self.cods = {}
        self._codigos_atuais()
        self.preencher()
        self.desenhar_tipos(sel=[sel] if sel else None)
        self.atualizar()

    def _codigos_atuais(self):
        """Tipos já vinculados começam com o código atual."""
        for t in self.tipos:
            v = t.get("vinculo") or {}
            it = self._item(v.get("itemCentral")) or (self._item(u"pedido:" + v["pedido"]) if v.get("pedido") else None)
            t["cod"] = self._cod_de(it) if it else None

    # ------------------------------------------------------------ tabela de tipos
    def _situacao(self, t):
        if t["cod"] is None:
            return u"sem código", "vermelho"
        v = t.get("vinculo") or {}
        it = t["cod"].get("item") or {}
        if t["cod"].get("novo"):
            return u"código novo", "azul"
        if v.get("itemCentral") and v["itemCentral"] == it.get("id"):
            return u"vinculado", "verde"
        if v.get("pedido") and it.get("pedido") == v["pedido"]:
            return u"aguardando", "ambar"
        if v.get("itemCentral") or v.get("pedido"):
            return u"troca de código", "ambar"
        return u"a vincular", "azul"

    def desenhar_tipos(self, sel=None):
        if sel is None:
            sel = [self.tipos[i]["nome"] for i in range(len(self.tipos)) if self._selecionado(i)]
        self._pronto = False
        self.lbTipos.Items.Clear()
        kn = dict((n, g["plano"]["kn"]) for g in self.grupos for n in g["tipos"])
        for t in self.tipos:
            c = t["cod"]
            it = (c or {}).get("item") or {}
            fja = it.get("fja") or ((c or {}).get("novo") and u"(novo {})".format(c["novo"].get("sigla"))) or u"—"
            desc = it.get("desc") or ((c or {}).get("novo") or {}).get("desc") or u""
            rot, tom = self._situacao(t)
            li = ListBoxItem()
            li.Padding = Thickness(4, 3, 4, 3)
            li.Content = _grade([t["nome"], u"" if t["inst"] is None else u"{}".format(t["inst"]), fja,
                                 kn.get(t["nome"]) or u"—", desc, _selo(rot, tom)])
            self.lbTipos.Items.Add(li)
            if t["nome"] in sel:
                li.IsSelected = True
        self._pronto = True
        self.tipos_selecionados(None, None)

    def _selecionado(self, i):
        try:
            return self.lbTipos.Items[i].IsSelected
        except Exception:
            return False

    def _sel(self):
        return [self.tipos[i] for i in range(len(self.tipos)) if self._selecionado(i)]

    def selecionar_todos(self, sender, args):
        self.lbTipos.SelectAll()

    def tipos_selecionados(self, sender, args):
        if not self._pronto:
            return
        s = self._sel()
        if not s:
            self.txtAlvo.Text = u"Selecione um ou mais tipos na lista."
        elif len(s) == 1:
            self.txtAlvo.Text = u"Tipo \"{}\"".format(s[0]["nome"])
        else:
            self.txtAlvo.Text = u"{} tipos: {}".format(len(s), u", ".join(t["nome"] for t in s[:6]) + (u"…" if len(s) > 6 else u""))
        self.txtSelTipos.Text = u"{} de {} tipo(s) selecionado(s)".format(len(s), len(self.tipos)) if self.tipos else u""
        # um único tipo com código existente: marca-o na lista de códigos
        if len(s) == 1 and s[0]["cod"] and not s[0]["cod"].get("novo"):
            iid = s[0]["cod"]["item"]["id"]
            for k, it in enumerate(self.lista):
                if it["id"] == iid:
                    self.lbItens.SelectedIndex = k
                    self.lbItens.ScrollIntoView(self.lbItens.SelectedItem)
                    break
        self.btAplicarCod.IsEnabled = bool(s)

    # ------------------------------------------------------------ códigos
    def modo_mudou(self, sender, args):
        if not self._pronto:
            return
        novo = bool(self.rbNovo.IsChecked)
        self.spExistente.Visibility = Visibility.Collapsed if novo else Visibility.Visible
        self.gNovo.Visibility = Visibility.Visible if novo else Visibility.Collapsed

    def busca_mudou(self, sender, args):
        if self._pronto:
            self.preencher()

    def preencher(self):
        self.lbItens.Items.Clear()
        self.lista = []
        d = self.dados
        if d is None:
            return
        q = F._norm(self.tbBusca.Text)
        cat = F._norm((self.fam or {}).get("categoria"))
        itens = F.elementos(d) + [x for x in d.itens_pendentes() if x.get("tipo") == "elemento"]
        res = []
        for it in itens:
            ln = d.linha_projeto(it["id"]) or {}
            alvo = u" ".join(u"{}".format(a or u"") for a in (it.get("fja"), it.get("sigla"), it.get("desc"), ln.get("kn"), ln.get("desc")))
            if q and q not in F._norm(alvo):
                continue
            res.append((0 if cat and F._norm(it.get("rvCat")) == cat else 1, 0 if ln else 1,
                        it.get("sigla") or u"", it.get("fja") or u"", it, ln))
        res.sort(key=lambda x: x[:4])
        for _, _, _, _, it, ln in res[:400]:
            li = ListBoxItem()
            li.Content = u"{}   {}   {}{}".format(it.get("fja") or u"", ln.get("kn") or u"—", ln.get("desc") or it.get("desc") or u"",
                                                u"   (aguardando sistema)" if it.get("pendente") else u"")
            li.ToolTip = it.get("rvCat") or u""
            self.lbItens.Items.Add(li)
            self.lista.append(it)
        self.txtRodape.Text = u"{} de {} elementos/famílias{}".format(
            len(self.lista), len(itens), u" · mesma categoria primeiro" if cat else u"")

    def sigla_mudou(self, sender, args):
        if not self._pronto:
            return
        if self.cbSigla.SelectedIndex == len(self.siglas):  # "+ Nova sigla…"
            nova = F.pedir_sigla_nova(self.dados, "elemento", u"Nova sigla de elemento/família")
            self._pronto = False
            if nova:
                self.siglas.append(nova)
                self.cbSigla.Items.Insert(len(self.siglas) - 1, u"{} · {} (nova)".format(nova["sigla"], nova["nome"]))
                self.cbSigla.SelectedIndex = len(self.siglas) - 1
            else:
                self.cbSigla.SelectedIndex = self._sig_ant
            self._pronto = True
        self._sig_ant = self.cbSigla.SelectedIndex

    def aplicar_codigo(self, sender, args):
        alvo = self._sel()
        if not alvo:
            return
        if self.rbNovo.IsChecked:
            i = self.cbSigla.SelectedIndex
            sg = self.siglas[i] if 0 <= i < len(self.siglas) else None
            desc = (self.tbDesc.Text or u"").strip()
            if sg is None or not desc:
                forms.alert(u"Escolha a sigla e informe a descrição do código novo.", title=TITULO)
                return
            cod = {"novo": {"sigla": sg["sigla"], "desc": desc, "texto": (self.tbTexto.Text or u"").strip(),
                            "siglaNova": dict((k, v) for k, v in sg.items() if k != "nova") if sg.get("nova") else None},
                   "cat": (self.fam or {}).get("categoria") or u""}
        else:
            i = self.lbItens.SelectedIndex
            if i < 0 or i >= len(self.lista):
                forms.alert(u"Escolha o código na lista.", title=TITULO)
                return
            cod = self._cod_de(self.lista[i])
        for t in alvo:
            t["cod"] = cod
        self._nome_editado = False
        self.atualizar()

    # ------------------------------------------------------------ plano
    def renomear_mudou(self, sender, args):
        self.tbNomeFam.IsEnabled = bool(self.ckRenomear.IsChecked)
        self.atualizar()

    def nome_mudou(self, sender, args):
        if self._pronto and self.tbNomeFam.IsFocused:
            self._nome_editado = True
            self.atualizar(manter_nome=True)

    def _calcular(self):
        """Agrupa os tipos por código (mesmo código = mesmo item e keynote) e planeja cada grupo."""
        self.grupos, problemas, avisos = [], [], []
        if self.fam is None or self.dados is None:
            return problemas, avisos
        tambem, ordem = set(), []
        por = {}
        for t in self.tipos:
            if t["cod"] is None:
                problemas.append(u"Tipo \"{}\" sem código: todos os tipos precisam de código.".format(t["nome"]))
                continue
            k = id(t["cod"])
            if k not in por:
                por[k] = {"cod": t["cod"], "tipos": []}
                ordem.append(k)
            por[k]["tipos"].append(t["nome"])
        for c in self.cods.values():
            tambem.add(c["item"].get("fja"))
        for k in ordem:
            g = por[k]
            p = F.plano_codigo(doc, self.dados, g["cod"], tambem)
            self.grupos.append({"plano": p, "tipos": g["tipos"]})
            problemas += [x for x in p["problemas"] if x not in problemas]
            if p.get("novo"):
                avisos.append(u"Código novo {} ({}) para {} tipo(s); o sistema confirma o código.".format(
                    p["item"]["fja"], (p["desc"] or u"").upper(), len(g["tipos"])))
            elif p.get("reservas"):
                avisos.append(u"{} entra na especificação do projeto como {}.".format(p["item"].get("fja"), p["kn"]))
        return problemas, avisos

    def atualizar(self, manter_nome=False):
        problemas, avisos = self._calcular()
        if self.fam is None:
            self.txtPrevia.Text = u"Escolha o arquivo da família ou selecione um elemento no modelo."
            self.txtAvisos.Text = self.txtProblemas.Text = u""
            self.btInserir.IsEnabled = False
            return
        if not manter_nome:
            p0 = self.grupos[0]["plano"] if self.grupos else None
            self._pronto = False
            self.tbNomeFam.Text = F.nome_familia(p0["item"].get("sigla"), p0["desc"]) if p0 else self.fam["nome"]
            self._pronto = True
        self.desenhar_tipos()
        nc = len(self.grupos)
        self.txtPrevia.Text = u"{} \"{}\" · {} tipo(s) · {} código(s){}{}".format(
            u"Readequar família" if self.fam.get("modelo") else u"Carregar família",
            F.limpar_nome(self.tbNomeFam.Text) if self.ckRenomear.IsChecked else self.fam["nome"],
            len(self.tipos), nc, u" · tipos renomeados" if self.ckRenomearTipos.IsChecked else u"",
            u"" if self.fam.get("modelo") else u" · a família é carregada no modelo")
        self.txtAvisos.Text = u"\n".join(u"• " + a for a in avisos)
        self.txtProblemas.Text = u"\n".join(u"• " + a for a in problemas)
        self.btInserir.IsEnabled = bool(self.grupos) and not problemas

    # ------------------------------------------------------------ gravar
    def inserir(self, sender, args):
        problemas, _ = self._calcular()
        if problemas or not self.grupos:
            self.atualizar()
            return
        if not F.doc_ativo_ok(doc):
            forms.alert(u"O modelo ativo mudou. Esta janela só age sobre \"{}\", o modelo ativo quando ela foi aberta: "
                        u"volte para ele ou feche e abra a ferramenta de novo.".format(doc.Title), title=TITULO)
            return
        trocas = [t["nome"] for t in self.tipos if self._situacao(t)[0] == u"troca de código"]
        if trocas and not forms.alert(u"{} tipo(s) já vinculado(s) a outro código vão trocar de código:\n{}\n\nContinuar?".format(
                len(trocas), u", ".join(trocas[:10])), title=TITULO, yes=True, no=True):
            return
        ren_fam = bool(self.ckRenomear.IsChecked)
        nome_fam = F.limpar_nome(self.tbNomeFam.Text)
        try:
            with revit.Transaction(u"Componentes: {}".format(nome_fam if ren_fam else self.fam["nome"])):
                familia, avisos = F.aplicar_componente(doc, self.dados, self.fam, self.grupos, ren_fam, nome_fam,
                                                       bool(self.ckRenomearTipos.IsChecked))
                nome = familia.Name
        except Exception as e:
            forms.alert(u"Não foi possível gravar no modelo:\n{}".format(e), title=TITULO)
            return
        msg = u"Família no modelo: {}\n{} tipo(s) com {} código(s).".format(nome, len(self.tipos), len(self.grupos))
        if any(g["plano"].get("novo") or g["plano"].get("reservas") for g in self.grupos):
            msg += u"\n\nPedido enviado ao sistema: códigos e keynotes ficam definitivos quando alguém abrir o sistema."
        res = F.resultado_pedidos(self.dados)
        if res:
            msg = msg.split(u"\n\n")[0] + u"\n\nSistema:\n" + res
        if avisos:
            msg += u"\n\n" + u"\n".join(u"• " + a for a in avisos)
        forms.alert(msg, title=TITULO)
        self.Close()

    def fechar(self, sender, args):
        self.Close()


if doc is None or doc.IsFamilyDocument:
    forms.alert(u"Abra um modelo de projeto para usar esta ferramenta.", title=TITULO)
else:
    elemento = None
    while True:
        j = Janela(elemento)
        j.ShowDialog()
        if not j.pedir_selecao:
            break
        elemento = None
        if not F.doc_ativo_ok(doc):
            forms.alert(u"O modelo ativo mudou: a seleção só vale em \"{}\".".format(doc.Title), title=TITULO)
            continue
        try:
            ref = uidoc.Selection.PickObject(ObjectType.Element, u"Clique num elemento da família a readequar (Esc cancela)")
            elemento = doc.GetElement(ref.ElementId)
        except Exception:
            pass  # Esc: reabre sem elemento
