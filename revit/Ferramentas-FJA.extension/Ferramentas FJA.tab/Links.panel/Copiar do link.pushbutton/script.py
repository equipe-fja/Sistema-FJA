# -*- coding: utf-8 -*-
"""Copiar do link: copia em massa elementos de um modelo linkado para o modelo ativo, na mesma posição,
por módulo (por enquanto: Equipamentos). Cada cópia guarda a origem (FJA_CopiaLink) para o monitoramento."""
import os

from pyrevit import revit, DB, forms, script

import fja_links as K

from System.Windows import Thickness, GridLength, GridUnitType, FontWeights, VerticalAlignment, \
    HorizontalAlignment, TextTrimming, CornerRadius, Visibility
from System.Windows.Controls import ListBoxItem, Grid, ColumnDefinition, TextBlock, Border, ComboBoxItem
from System.Windows.Media import SolidColorBrush, Color
from System.Collections.Generic import List
from Autodesk.Revit.UI.Selection import ObjectType, ISelectionFilter

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()
TITULO = u"Copiar do link"


def _p(r, g, b):
    return SolidColorBrush(Color.FromRgb(r, g, b))


COR = {"cinza": _p(94, 94, 92), "verde": _p(46, 115, 64), "ambar": _p(138, 93, 5), "vermelho": _p(179, 38, 30),
       "azul": _p(47, 95, 138)}
FUNDO = {"verde": _p(228, 241, 230), "ambar": _p(248, 239, 217), "vermelho": _p(251, 231, 229),
         "azul": _p(232, 239, 246), "cinza": _p(237, 237, 235)}
ESTADOS = [("novo", u"Novos", "azul", u"Ainda não copiados para este modelo."),
           ("copiado", u"Já copiados", "verde", u"Já existe cópia registrada no modelo (não copia de novo)."),
           ("hospedeiro", u"Precisam de hospedeiro", "vermelho",
            u"Família hospedada em parede/forro/piso (não face based): não copia sem o hospedeiro.")]
EST = dict((k, (r, c, t)) for k, r, c, t in ESTADOS)
COLS = [(u"Categoria", 170), (u"Família", -3), (u"Tipo", -2), (u"Nível", 130), (u"Qtde", 54), (u"Situação", 250)]


def aplicar_estilo(win):
    try:
        from System.Windows.Markup import XamlReader
        from System.IO import File
        caminho = os.path.join(os.path.dirname(K.__file__), "fja_estilo.xaml")
        win.Resources.MergedDictionaries.Add(XamlReader.Parse(File.ReadAllText(caminho)))
    except Exception as e:
        print(u"estilo: {}".format(e))


def _txt(t, cor=None, peso=None):
    tb = TextBlock()
    tb.Text = t or u""
    tb.VerticalAlignment = VerticalAlignment.Center
    tb.Margin = Thickness(0, 0, 8, 0)
    tb.TextTrimming = TextTrimming.CharacterEllipsis
    tb.ToolTip = t or None
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


def _selo(k, texto):
    rot, tom, dica = EST[k]
    b = Border()
    b.CornerRadius = CornerRadius(9)
    b.Background = FUNDO[tom]
    b.Padding = Thickness(7, 1, 7, 1)
    b.Margin = Thickness(0, 0, 4, 0)
    b.HorizontalAlignment = HorizontalAlignment.Left
    b.VerticalAlignment = VerticalAlignment.Center
    b.ToolTip = dica
    t = TextBlock()
    t.Text = texto
    t.FontSize = 11
    t.Foreground = COR[tom]
    b.Child = t
    return b


def _selos(linha):
    from System.Windows.Controls import StackPanel, Orientation
    sp = StackPanel()
    sp.Orientation = Orientation.Horizontal
    sp.VerticalAlignment = VerticalAlignment.Center
    if linha["novos"]:
        sp.Children.Add(_selo("novo", u"{} novo{}".format(linha["novos"], "s" if linha["novos"] > 1 else "")))
    if linha["copiados"]:
        sp.Children.Add(_selo("copiado", u"{} já copiado{}".format(linha["copiados"], "s" if linha["copiados"] > 1 else "")))
    if linha["hospedeiro"]:
        sp.Children.Add(_selo("hospedeiro", u"{} sem hospedeiro".format(linha["hospedeiro"])))
    return sp


class FiltroLink(ISelectionFilter):
    def __init__(self, link, cats):
        self.link_id = link.inst.Id
        self.ldoc = link.doc
        self.cats = set(c.IntegerValue if hasattr(c, "IntegerValue") else c.Value for c in cats)

    def AllowElement(self, e):
        return e.Id == self.link_id

    def AllowReference(self, ref, pt):
        try:
            if ref.ElementId != self.link_id:
                return False
            e = self.ldoc.GetElement(ref.LinkedElementId)
            if e is None or e.Category is None:
                return False
            cid = e.Category.Id
            return (cid.IntegerValue if hasattr(cid, "IntegerValue") else cid.Value) in self.cats
        except Exception:
            return False


class Janela(forms.WPFWindow):
    def __init__(self, estado=None):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        aplicar_estilo(self)
        self.acao = None
        self.est = estado or {}
        self.escolhidos = self.est.get("escolhidos")      # set de UniqueIds do link, ou None
        self.links = K.links(doc)
        self.mods = K.modulos()
        self.origens, self.linhas, self.vista = [], [], []
        self._pronto = False
        self.gCab.Children.Add(_grade([c for c, _ in COLS], peso=FontWeights.SemiBold, cor=COR["cinza"]))
        self.txtInfo.Text = u"Modelo: {} · copia na mesma posição; famílias face based vêm sem hospedeiro".format(doc.Title)
        # links
        ult = self.est.get("link") or cfg.get_option("link", "")
        sel = 0
        for i, l in enumerate(self.links):
            it = ComboBoxItem()
            it.Content = l.rotulo
            it.IsEnabled = l.carregado
            self.cbLink.Items.Add(it)
            if l.uid == ult and l.carregado:
                sel = i
        # módulos
        ult_m = self.est.get("modulo") or cfg.get_option("modulo", "equipamentos")
        sel_m = 0
        for i, m in enumerate(self.mods):
            it = ComboBoxItem()
            it.Content = m["nome"] if m["ativo"] else u"{} (em breve)".format(m["nome"])
            it.IsEnabled = bool(m["ativo"])
            it.ToolTip = m.get("descricao") or None
            self.cbModulo.Items.Add(it)
            if m["id"] == ult_m and m["ativo"]:
                sel_m = i
        # worksets
        self.ws = K.worksets(doc)
        if self.ws:
            ativo = K.workset_ativo(doc)
            ult_w = self.est.get("workset") or cfg.get_option("workset_" + doc.Title, "")
            sw = 0
            for i, w in enumerate(self.ws):
                self.cbWorkset.Items.Add(w.Name)
                if (ult_w and w.Name == ult_w) or (not ult_w and w.Id == ativo):
                    sw = i
            self.cbWorkset.SelectedIndex = sw
        else:
            self.spWorkset.Visibility = Visibility.Collapsed
        if not self.links:
            self.txtResumo.Text = u"Este modelo não tem links do Revit."
        self.cbModulo.SelectedIndex = sel_m
        self.cbLink.SelectedIndex = sel if self.links else -1
        self._pronto = True
        self.carregar()

    # ------------------------------------------------------------ dados
    @property
    def link(self):
        i = self.cbLink.SelectedIndex
        return self.links[i] if 0 <= i < len(self.links) else None

    @property
    def mod(self):
        i = self.cbModulo.SelectedIndex
        return self.mods[i] if 0 <= i < len(self.mods) else None

    def link_mudou(self, sender, args):
        if self._pronto:
            self.escolhidos = None
            self.carregar()

    def modulo_mudou(self, sender, args):
        if self._pronto:
            self.carregar()

    def carregar(self):
        l, m = self.link, self.mod
        self.origens = []
        if l is not None and l.carregado and m is not None and m["ativo"]:
            self.origens = K.elementos_do_link(doc, l, m)
            if self.escolhidos is not None:
                self.origens = [o for o in self.origens if o.uid in self.escolhidos]
        self.btLimparEscolha.Visibility = Visibility.Visible if self.escolhidos is not None else Visibility.Collapsed
        self.linhas = K.agrupar(self.origens)
        self._pronto = False
        ant_c, ant_n, ant_e = self.cbCat.SelectedItem, self.cbNivel.SelectedItem, self.cbEstado.SelectedIndex
        self.cbCat.Items.Clear()
        self.cbCat.Items.Add(u"Todas as categorias")
        for c in sorted(set(o.cat for o in self.origens)):
            self.cbCat.Items.Add(c)
        self.cbCat.SelectedItem = ant_c if ant_c in list(self.cbCat.Items) else u"Todas as categorias"
        self.cbNivel.Items.Clear()
        self.cbNivel.Items.Add(u"Todos os níveis")
        for n in sorted(set(o.nivel or u"—" for o in self.origens)):
            self.cbNivel.Items.Add(n)
        self.cbNivel.SelectedItem = ant_n if ant_n in list(self.cbNivel.Items) else u"Todos os níveis"
        cont = {}
        for o in self.origens:
            cont[o.estado] = cont.get(o.estado, 0) + 1
        self.cbEstado.Items.Clear()
        self.cbEstado.Items.Add(u"Todas as situações ({})".format(len(self.origens)))
        for k, r, _, _ in ESTADOS:
            self.cbEstado.Items.Add(u"{} ({})".format(r, cont.get(k, 0)))
        self.cbEstado.SelectedIndex = ant_e if ant_e >= 0 else 0
        self._pronto = True
        if l is not None and m is not None:
            esc = u" · escolhidos no modelo" if self.escolhidos is not None else u""
            self.txtResumo.Text = u"{} elementos{} · {}".format(
                len(self.origens), esc,
                u" · ".join(u"{} {}".format(cont[k], EST[k][0].lower()) for k, _, _, _ in ESTADOS if cont.get(k)) or u"nada no módulo")
        self.preencher()

    # ------------------------------------------------------------ lista
    def filtro_mudou(self, sender, args):
        if self._pronto:
            self.preencher()

    def preencher(self):
        q = (self.tbBusca.Text or u"").strip().lower()
        cat = self.cbCat.SelectedItem if self.cbCat.SelectedIndex > 0 else None
        niv = self.cbNivel.SelectedItem if self.cbNivel.SelectedIndex > 0 else None
        ie = self.cbEstado.SelectedIndex
        est = ESTADOS[ie - 1][0] if ie > 0 else None
        self.vista = []
        for ln in self.linhas:
            if cat and ln["cat"] != cat:
                continue
            if niv and (ln["nivel"] or u"—") != niv:
                continue
            if est and not ln[{"novo": "novos", "copiado": "copiados", "hospedeiro": "hospedeiro"}[est]]:
                continue
            if q and q not in u" ".join([ln["cat"], ln["fam"], ln["tipo"], ln["nivel"]]).lower():
                continue
            self.vista.append(ln)
        self._pronto = False
        self.lbItens.Items.Clear()
        for ln in self.vista:
            li = ListBoxItem()
            li.Padding = Thickness(4, 3, 4, 3)
            li.Content = _grade([ln["cat"], ln["fam"], ln["tipo"], ln["nivel"] or u"—",
                                 u"{}".format(len(ln["itens"])), _selos(ln)])
            self.lbItens.Items.Add(li)
        self._pronto = True
        self.sel_mudou(None, None)

    def _alvo(self):
        sel = [self.vista[i] for i in range(len(self.vista)) if self.lbItens.Items[i].IsSelected]
        linhas = sel or self.vista
        return [o for ln in linhas for o in ln["itens"] if o.estado == "novo"], bool(sel)

    def sel_mudou(self, sender, args):
        if not self._pronto:
            return
        alvo, tem_sel = self._alvo()
        self.btCopiar.IsEnabled = bool(alvo)
        self.btCopiar.Content = u"Copiar {} elemento{}".format(len(alvo), "s" if len(alvo) != 1 else "") if alvo else u"Copiar"
        if tem_sel:
            self.txtRodape.Text = u"Copia os novos das linhas selecionadas."
        else:
            self.txtRodape.Text = u"Sem seleção: copia os novos de todas as linhas visíveis. Ctrl/Shift para escolher linhas."

    # ------------------------------------------------------------ ações
    def _estado(self):
        l, m = self.link, self.mod
        return {"link": l.uid if l else None, "modulo": m["id"] if m else None,
                "workset": self.cbWorkset.SelectedItem if self.ws else None, "escolhidos": self.escolhidos}

    def escolher(self, sender, args):
        if self.link is None or self.mod is None:
            return
        self.acao = "escolher"
        self.est = self._estado()
        self.Close()

    def limpar_escolha(self, sender, args):
        self.escolhidos = None
        self.carregar()

    def copiar(self, sender, args):
        alvo, _ = self._alvo()
        l, m = self.link, self.mod
        if not alvo or l is None:
            return
        ws = None
        if self.ws and self.cbWorkset.SelectedIndex >= 0:
            w = self.ws[self.cbWorkset.SelectedIndex]
            ws = w.Id
            cfg.set_option("workset_" + doc.Title, w.Name)
        cfg.set_option("link", l.uid)
        cfg.set_option("modulo", m["id"])
        script.save_config()
        por = u""
        try:
            por = __revit__.Application.Username
        except Exception:
            pass
        t = DB.Transaction(doc, u"Copiar do link ({}): {} elementos".format(m["nome"], len(alvo)))
        t.Start()
        K.opcoes_transacao(t)
        try:
            with forms.ProgressBar(title=u"Copiando do link… ({value} de {max_value})") as pb:
                res = K.copiar(doc, l, alvo, m, workset=ws, por=por,
                               progresso=lambda a, b: pb.update_progress(a, b))
            t.Commit()
        except Exception as ex:
            if t.HasStarted() and not t.HasEnded():
                t.RollBack()
            forms.alert(u"A cópia foi desfeita: {}".format(ex), title=TITULO)
            return
        novos = [n.Id for _, n in res["copiados"]]
        if novos:
            try:
                uidoc.Selection.SetElementIds(List[DB.ElementId](novos))
            except Exception:
                pass
        msg = u"{} elemento(s) copiado(s) e selecionado(s) no modelo.".format(len(novos))
        if res["avisos"]:
            msg += u"\n{} com posição diferente da original (ver relatório).".format(len(res["avisos"]))
        if res["falhas"]:
            msg += u"\n{} não copiado(s) (ver relatório).".format(len(res["falhas"]))
            out = script.get_output()
            out.print_md(u"### Copiar do link — não copiados")
            for o, txt in res["falhas"]:
                out.print_md(u"- {} · {} · {} · {} — {}".format(o.cat, o.fam, o.tipo, o.nivel or u"—", txt))
        if res["avisos"]:
            out = script.get_output()
            out.print_md(u"### Copiar do link — avisos")
            for o, n, txt in res["avisos"]:
                out.print_md(u"- {} {} · {} — {}".format(out.linkify(n.Id), o.fam, o.tipo, txt))
        self.txtRodape.Text = msg.replace(u"\n", u" ")
        self.carregar()
        self.txtRodape.Text = msg.replace(u"\n", u" ")

    def fechar(self, sender, args):
        self.Close()


def _ja_escolhidos():
    """Elementos de link já selecionados no modelo antes de abrir a ferramenta (Tab no link)."""
    try:
        refs = uidoc.Selection.GetReferences()
    except Exception:
        return None, None
    por_link = {}
    for r in refs or []:
        if r.LinkedElementId != DB.ElementId.InvalidElementId:
            li = doc.GetElement(r.ElementId)
            if isinstance(li, DB.RevitLinkInstance) and li.GetLinkDocument() is not None:
                e = li.GetLinkDocument().GetElement(r.LinkedElementId)
                if e is not None:
                    por_link.setdefault(li.UniqueId, set()).add(e.UniqueId)
    if len(por_link) != 1:
        return None, None
    uid, s = list(por_link.items())[0]
    return uid, s


def principal():
    if doc is None or doc.IsFamilyDocument:
        forms.alert(u"Abra um modelo de projeto para usar esta ferramenta.", title=TITULO)
        return
    estado = None
    luid, esc = _ja_escolhidos()
    if luid:
        estado = {"link": luid, "escolhidos": esc}
    while True:
        w = Janela(estado)
        w.ShowDialog()
        if w.acao != "escolher":
            break
        estado = w.est
        link = [l for l in K.links(doc) if l.uid == estado["link"]][0]
        mod = [m for m in K.modulos() if m["id"] == estado["modulo"]][0]
        try:
            refs = uidoc.Selection.PickObjects(
                ObjectType.LinkedElement, FiltroLink(link, K.ids_categorias(mod)),
                u"Selecione no link os elementos a copiar ({}) e clique em Concluir".format(mod["nome"]))
            estado["escolhidos"] = set(link.doc.GetElement(r.LinkedElementId).UniqueId for r in refs)
        except Exception:
            pass   # Esc: volta para a janela com a escolha anterior


principal()
