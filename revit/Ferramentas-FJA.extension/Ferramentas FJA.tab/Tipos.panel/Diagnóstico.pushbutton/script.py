# -*- coding: utf-8 -*-
"""Diagnóstico da sincronização: confere todos os tipos e materiais do modelo vinculados ao Sistema FJA
(keynote, Type Mark, descrição e camadas) e ressincroniza a partir do sistema, que é a referência."""
__persistentengine__ = True
import os

from pyrevit import revit, DB, forms, script

import fja_sistema as F

from System.Windows import Thickness, GridLength, GridUnitType, FontWeights, VerticalAlignment, HorizontalAlignment, \
    TextTrimming, TextWrapping, CornerRadius
from System.Windows.Controls import ListBoxItem, Grid, ColumnDefinition, TextBlock, Border, StackPanel
from System.Windows.Media import SolidColorBrush, Color
from System.Collections.Generic import List

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()
TITULO = u"Diagnóstico"


def _p(r, g, b):
    return SolidColorBrush(Color.FromRgb(r, g, b))


COR = {"cinza": _p(94, 94, 92), "verde": _p(46, 115, 64), "ambar": _p(138, 93, 5), "vermelho": _p(179, 38, 30),
       "azul": _p(47, 95, 138)}
FUNDO = {"verde": _p(228, 241, 230), "ambar": _p(248, 239, 217), "vermelho": _p(251, 231, 229),
         "azul": _p(232, 239, 246), "cinza": _p(237, 237, 235)}
ESTADOS = [("ok", u"Sincronizado", "verde", u"Igual ao sistema."),
           ("desatualizado", u"Desatualizado", "ambar", u"O sistema mudou depois da última sincronização."),
           ("revit", u"Alterado no Revit", "azul", u"Alguém mudou no Revit o keynote ou a descrição; ressincronizar volta ao valor do sistema."),
           ("aguardando", u"Aguardando", "ambar", u"Pedido ainda não processado pelo sistema (abra o sistema)."),
           ("fora", u"Fora do projeto", "vermelho", u"O item existe na biblioteca, mas não está na especificação do projeto."),
           ("orfao", u"Órfão", "vermelho", u"O item central não existe mais (ou o pedido foi recusado).")]
EST = dict((k, (r, c, t)) for k, r, c, t in ESTADOS)
COLS = [(u"Tipo / material", -3), (u"Categoria", 120), (u"FJ", 104), (u"Keynote", 74), (u"Situação", 140)]


def aplicar_estilo(win):
    try:
        from System.Windows.Markup import XamlReader
        from System.IO import File
        caminho = os.path.join(os.path.dirname(F.__file__), "fja_estilo.xaml")
        win.Resources.MergedDictionaries.Add(XamlReader.Parse(File.ReadAllText(caminho)))
    except Exception as e:
        print(u"estilo: {}".format(e))


def _txt(t, cor=None, peso=None, quebra=False, tam=None):
    tb = TextBlock()
    tb.Text = t or u""
    tb.VerticalAlignment = VerticalAlignment.Center
    tb.Margin = Thickness(0, 0, 8, 0)
    if quebra:
        tb.TextWrapping = TextWrapping.Wrap
    else:
        tb.TextTrimming = TextTrimming.CharacterEllipsis
    if cor is not None:
        tb.Foreground = cor
    if peso is not None:
        tb.FontWeight = peso
    if tam:
        tb.FontSize = tam
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


def _selo(k):
    rot, tom, dica = EST[k]
    b = Border()
    b.CornerRadius = CornerRadius(9)
    b.Background = FUNDO[tom]
    b.Padding = Thickness(7, 1, 7, 1)
    b.HorizontalAlignment = HorizontalAlignment.Left
    b.VerticalAlignment = VerticalAlignment.Center
    b.ToolTip = dica
    t = TextBlock()
    t.Text = rot
    t.FontSize = 11
    t.Foreground = COR[tom]
    b.Child = t
    return b


class Janela(forms.WPFWindow):
    def __init__(self):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        aplicar_estilo(self)
        self.dados = None
        self.diags = []
        self.vista = []
        self._pronto = False
        self.gCab.Children.Add(_grade([c for c, _ in COLS], peso=FontWeights.SemiBold, cor=COR["cinza"]))
        tem_oficial = F._mais_recente(F.PASTA_REVIT, "biblioteca") is not None
        self.cbFonte.SelectedIndex = 1 if cfg.get_option("dev", not tem_oficial) else 0
        self._pronto = True
        self.carregar()

    # ------------------------------------------------------------ dados
    def carregar(self):
        dev = self.cbFonte.SelectedIndex == 1
        try:
            self.dados = F.Dados(dev=dev)
        except Exception as e:
            self.dados = None
            self.txtFonte.Text = u"{}".format(e)
            return
        d = self.dados
        self.txtFonte.Text = u"Modelo: {} · {} · publicado em {}".format(
            doc.Title, d.rotulo, (d.geradoEm or u"")[:16].replace("T", " "))
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
        self._pronto = True
        self._usar_projeto(d.projetos[sel - 1] if sel else None)

    def _usar_projeto(self, proj):
        try:
            self.dados.carregar_projeto(proj)
        except Exception as e:
            forms.alert(u"{}".format(e), title=TITULO)
            self.dados.carregar_projeto(None)
        self.analisar()

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

    def analisar(self, manter=None):
        self.diags = []
        for e, v in F.elementos_vinculados(doc):
            try:
                self.diags.append(F.diagnosticar(doc, self.dados, e, v))
            except Exception as ex:
                print(u"diagnóstico {}: {}".format(F.nome_elem(e), ex))
        cont = {}
        for d in self.diags:
            cont[d["estado"]] = cont.get(d["estado"], 0) + 1
        self._pronto = False
        ant_e, ant_c = self.cbEstado.SelectedIndex, self.cbCat.SelectedIndex
        self.cbEstado.Items.Clear()
        self.cbEstado.Items.Add(u"Todas as situações ({})".format(len(self.diags)))
        self.cbEstado.Items.Add(u"Precisam de atenção ({})".format(len([d for d in self.diags if d["estado"] != "ok"])))
        for k, r, _, _ in ESTADOS:
            self.cbEstado.Items.Add(u"{} ({})".format(r, cont.get(k, 0)))
        self.cbEstado.SelectedIndex = ant_e if ant_e >= 0 else (1 if len(cont) > 1 or "ok" not in cont else 0)
        cats = sorted(set(d["cat"] for d in self.diags))
        self.cbCat.Items.Clear()
        self.cbCat.Items.Add(u"Todas as categorias")
        for c in cats:
            self.cbCat.Items.Add(c)
        self.cats = cats
        self.cbCat.SelectedIndex = ant_c if 0 <= ant_c < len(cats) + 1 else 0
        self._pronto = True
        self.txtResumo.Text = u"{} elementos vinculados · {}".format(
            len(self.diags), u" · ".join(u"{} {}".format(cont[k], EST[k][0].lower()) for k, _, _, _ in ESTADOS if cont.get(k)))
        self.preencher(manter)

    # ------------------------------------------------------------ lista
    def filtro_mudou(self, sender, args):
        if self._pronto:
            self.preencher()

    def preencher(self, manter=None):
        q = F._norm(self.tbBusca.Text)
        ie, ic = self.cbEstado.SelectedIndex, self.cbCat.SelectedIndex
        self.vista = []
        for d in self.diags:
            if ie == 1 and d["estado"] == "ok":
                continue
            if ie >= 2 and d["estado"] != ESTADOS[ie - 2][0]:
                continue
            if ic > 0 and d["cat"] != self.cats[ic - 1]:
                continue
            fja = (d["item"] or {}).get("fja") or d["vinc"].get("fja")
            if q and q not in F._norm(u" ".join([d["nome"], fja or u"", d["kn"], d["desc"]])):
                continue
            self.vista.append(d)
        ordem = dict((k, i) for i, (k, _, _, _) in enumerate(ESTADOS))
        self.vista.sort(key=lambda d: (d["estado"] == "ok", ordem.get(d["estado"], 9), d["cat"], d["nome"]))
        self._pronto = False
        self.lbItens.Items.Clear()
        for d in self.vista:
            li = ListBoxItem()
            li.Padding = Thickness(4, 3, 4, 3)
            fja = (d["item"] or {}).get("fja") or d["vinc"].get("fja") or u"—"
            kn = F._param_txt(d["elem"], DB.BuiltInParameter.KEYNOTE_PARAM)
            li.Content = _grade([d["nome"], d["cat"], fja, kn or u"—", _selo(d["estado"])])
            self.lbItens.Items.Add(li)
            if manter and d["elem"].Id in manter:
                li.IsSelected = True
        self._pronto = True
        self.sel_mudou(None, None)

    def _sel(self):
        return [self.vista[i] for i in range(len(self.vista)) if self.lbItens.Items[i].IsSelected]

    def sel_mudou(self, sender, args):
        if not self._pronto:
            return
        s = self._sel()
        self.btResinc.IsEnabled = any(d["estado"] in ("desatualizado", "revit") for d in s)
        self.btIncluir.IsEnabled = any(d["estado"] == "fora" for d in s) and bool(self.dados and self.dados.projeto)
        self.btDesvincular.IsEnabled = any(d["estado"] == "orfao" for d in s)
        self.btMostrar.IsEnabled = any(d["tipo"] != "material" for d in s)
        self.txtRodape.Text = u"{} selecionado(s)".format(len(s)) if s else u"Ctrl ou Shift para selecionar vários"
        self.mostrar(s[0] if len(s) == 1 else None, len(s))

    def mostrar(self, d, n):
        sp = self.spDetalhe
        sp.Children.Clear()
        if d is None:
            sp.Children.Add(_txt(u"{} itens selecionados.".format(n) if n else u"Selecione um item para ver os detalhes.",
                                 cor=COR["cinza"], quebra=True))
            return
        sp.Children.Add(_txt(d["nome"], peso=FontWeights.SemiBold, quebra=True, tam=15))
        sp.Children.Add(_txt(u"{} · {}".format(d["cat"], (d["item"] or {}).get("fja") or d["vinc"].get("fja") or u"sem FJ"),
                             cor=COR["cinza"], quebra=True))
        b = _selo(d["estado"])
        b.Margin = Thickness(0, 8, 0, 4)
        sp.Children.Add(b)
        sp.Children.Add(_txt(EST[d["estado"]][2], cor=COR["cinza"], quebra=True))
        if d["difs"]:
            sp.Children.Add(_txt(u"Diferenças", peso=FontWeights.SemiBold))
            for campo, rv, si in d["difs"]:
                g = StackPanel()
                g.Margin = Thickness(0, 6, 0, 0)
                g.Children.Add(_txt(campo, cor=COR["cinza"], tam=12))
                g.Children.Add(_txt(u"Revit:   {}".format(rv or u"(vazio)"), cor=COR["vermelho"], quebra=True))
                g.Children.Add(_txt(u"Sistema: {}".format(si or u"(vazio)"), cor=COR["verde"], quebra=True))
                sp.Children.Add(g)
        if d["item"]:
            sp.Children.Add(_txt(u"No sistema", peso=FontWeights.SemiBold))
            sp.Children.Add(_txt(u"Keynote {} · {}".format(d["kn"] or u"—", d["desc"] or (d["item"].get("desc") or u"")),
                                 quebra=True))
        if d["tipo"] != "material":
            n_inst = len(F.instancias_do_tipo(doc, d["elem"]))
            sp.Children.Add(_txt(u"{} instância(s) no modelo".format(n_inst), cor=COR["cinza"]))

    # ------------------------------------------------------------ ações
    def _checar_doc(self):
        if F.doc_ativo_ok(doc):
            return True
        forms.alert(u"O modelo ativo mudou. Esta janela só age sobre \"{}\": volte para ele ou abra a ferramenta de novo."
                    .format(doc.Title), title=TITULO)
        return False

    def ressincronizar(self, sender, args):
        alvo = [d for d in self._sel() if d["estado"] in ("desatualizado", "revit")]
        if not alvo or not self._checar_doc():
            return
        avisos = []
        try:
            with revit.Transaction(u"Diagnóstico: ressincronizar {} item(ns)".format(len(alvo))):
                for d in alvo:
                    avisos += F.ressincronizar(doc, self.dados, d, bool(self.ckRenomear.IsChecked))
        except Exception as e:
            forms.alert(u"Não foi possível ressincronizar:\n{}".format(e), title=TITULO)
            return
        if avisos:
            forms.alert(u"\n".join(u"• " + a for a in avisos), title=TITULO)
        self.analisar([d["elem"].Id for d in alvo])

    def incluir(self, sender, args):
        alvo = [d for d in self._sel() if d["estado"] == "fora"]
        if not alvo or not self._checar_doc():
            return
        feitos = {}
        try:
            with revit.Transaction(u"Diagnóstico: incluir no projeto"):
                for d in alvo:
                    iid = d["item"]["id"]
                    if iid not in feitos:  # mesmo item = mesmo keynote
                        feitos[iid] = (F.incluir_no_projeto(doc, self.dados, d), d.get("_pedido") or u"")
                    else:
                        F.gravar_valores(doc, self.dados, d, feitos[iid][0], (d["item"].get("desc") or u"").upper(),
                                         feitos[iid][1])
        except Exception as e:
            forms.alert(u"Não foi possível incluir:\n{}".format(e), title=TITULO)
            return
        res = F.resultado_pedidos(self.dados)
        forms.alert(u"{} item(ns) enviados ao sistema para entrar no projeto ({}).".format(len(feitos), u", ".join(x[0] for x in feitos.values()))
                    + (u"\n\nSistema:\n" + res if res else u" Os keynotes ficam definitivos quando alguém abrir o sistema."), title=TITULO)
        self.analisar([d["elem"].Id for d in alvo])

    def desvincular(self, sender, args):
        alvo = [d for d in self._sel() if d["estado"] == "orfao"]
        if not alvo or not self._checar_doc():
            return
        if not forms.alert(u"Remover o vínculo com o Sistema FJA de {} elemento(s)? Keynote e descrição continuam como estão."
                           .format(len(alvo)), title=TITULO, yes=True, no=True):
            return
        with revit.Transaction(u"Diagnóstico: desvincular"):
            for d in alvo:
                F.apagar_vinculo(d["elem"])
        self.analisar()

    def mostrar_modelo(self, sender, args):
        ids = []
        for d in self._sel():
            if d["tipo"] != "material":
                ids += F.instancias_do_tipo(doc, d["elem"])
        if not ids:
            forms.alert(u"Nenhuma instância desses tipos no modelo.", title=TITULO)
            return
        uidoc.Selection.SetElementIds(List[DB.ElementId](ids))
        self.txtRodape.Text = u"{} instância(s) selecionada(s) no modelo".format(len(ids))

    def fechar(self, sender, args):
        self.Close()


if doc is None or doc.IsFamilyDocument:
    forms.alert(u"Abra um modelo de projeto para usar esta ferramenta.", title=TITULO)
else:
    Janela().ShowDialog()
