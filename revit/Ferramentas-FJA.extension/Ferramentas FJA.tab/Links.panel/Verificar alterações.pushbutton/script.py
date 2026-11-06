# -*- coding: utf-8 -*-
"""Verificar alterações: compara as cópias feitas pelo Copiar do link com a origem no link (versão carregada)
e mostra o que mudou no link, o que mudou no projeto, o que foi apagado e o que é novo. Atualiza, aceita,
apaga cópias e copia os novos, em lote."""
import os

from pyrevit import revit, DB, forms, script

import fja_links as K

from System.Windows import Thickness, GridLength, GridUnitType, FontWeights, VerticalAlignment, \
    HorizontalAlignment, TextTrimming, CornerRadius
from System.Windows.Controls import ListBoxItem, Grid, ColumnDefinition, TextBlock, Border, ComboBoxItem
from System.Windows.Media import SolidColorBrush, Color
from System.Collections.Generic import List

doc = revit.doc
uidoc = revit.uidoc
TITULO = u"Verificar alterações"


def _p(r, g, b):
    return SolidColorBrush(Color.FromRgb(r, g, b))


COR = {"cinza": _p(94, 94, 92), "verde": _p(46, 115, 64), "ambar": _p(138, 93, 5), "vermelho": _p(179, 38, 30),
       "azul": _p(47, 95, 138)}
FUNDO = {"verde": _p(228, 241, 230), "ambar": _p(248, 239, 217), "vermelho": _p(251, 231, 229),
         "azul": _p(232, 239, 246), "cinza": _p(237, 237, 235)}
ESTADOS = [("link", u"Alterado no link", "ambar", u"A origem mudou no link depois da última sincronização."),
           ("projeto", u"Alterado no projeto", "azul", u"A cópia foi mexida neste modelo depois da última sincronização."),
           ("ambos", u"Alterado nos dois", "vermelho", u"Mudou no link e também neste modelo."),
           ("apagado", u"Apagado no link", "vermelho", u"A origem não existe mais no link."),
           ("novo", u"Novo no link", "cinza", u"Elemento do módulo no link que ainda não tem cópia neste modelo."),
           ("semlink", u"Link ausente", "cinza", u"O link desta cópia foi removido ou está descarregado."),
           ("ok", u"Igual", "verde", u"Sem diferenças.")]
EST = dict((k, (r, c, t)) for k, r, c, t in ESTADOS)
COLS = [(u"Categoria", 150), (u"Família : tipo", -3), (u"Nível", 110), (u"Situação", 150), (u"O que mudou", -4)]


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


def _por():
    try:
        return __revit__.Application.Username
    except Exception:
        return u""


class Janela(forms.WPFWindow):
    def __init__(self):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        aplicar_estilo(self)
        self.itens, self.vista, self.links = [], [], []
        self._pronto = False
        self.gCab.Children.Add(_grade([c for c, _ in COLS], peso=FontWeights.SemiBold, cor=COR["cinza"]))
        self.txtInfo.Text = (u"Modelo: {} · compara as cópias do Copiar do link com a versão do link carregada agora "
                             u"(recarregue o link antes de verificar)").format(doc.Title)
        self.carregar()

    # ------------------------------------------------------------ dados
    def carregar(self):
        try:
            self.itens = K.verificar(doc)
        except Exception as e:
            self.itens = []
            forms.alert(u"Não foi possível verificar: {}".format(e), title=TITULO)
        self._pronto = False
        ant_l = self.cbLink.SelectedItem.Content if self.cbLink.SelectedItem is not None else None
        ant_c, ant_e = self.cbCat.SelectedItem, self.cbEstado.SelectedIndex
        self.links = []
        for it in self.itens:
            nome = it.link.nome if it.link else u"(link ausente)"
            if nome not in self.links:
                self.links.append(nome)
        self.links.sort()
        self.cbLink.Items.Clear()
        for n in [u"Todos os links"] + self.links:
            ci = ComboBoxItem()
            ci.Content = n
            self.cbLink.Items.Add(ci)
        self.cbLink.SelectedIndex = ([u"Todos os links"] + self.links).index(ant_l) if ant_l in self.links else 0
        self.cbCat.Items.Clear()
        self.cbCat.Items.Add(u"Todas as categorias")
        for c in sorted(set(it.cat for it in self.itens)):
            self.cbCat.Items.Add(c)
        self.cbCat.SelectedItem = ant_c if ant_c in list(self.cbCat.Items) else u"Todas as categorias"
        cont = {}
        for it in self.itens:
            cont[it.estado] = cont.get(it.estado, 0) + 1
        self.cbEstado.Items.Clear()
        self.cbEstado.Items.Add(u"Precisam de atenção ({})".format(len([i for i in self.itens if i.estado != "ok"])))
        self.cbEstado.Items.Add(u"Todas as situações ({})".format(len(self.itens)))
        for k, r, _, _ in ESTADOS:
            self.cbEstado.Items.Add(u"{} ({})".format(r, cont.get(k, 0)))
        self.cbEstado.SelectedIndex = ant_e if ant_e >= 0 else 0
        self._pronto = True
        if not self.itens:
            self.txtResumo.Text = u"Nenhuma cópia registrada neste modelo (use Links › Copiar do link)."
        else:
            self.txtResumo.Text = u" · ".join(u"{} {}".format(cont[k], EST[k][0].lower())
                                             for k, _, _, _ in ESTADOS if cont.get(k))
        self.preencher()

    # ------------------------------------------------------------ lista
    def filtro_mudou(self, sender, args):
        if self._pronto:
            self.preencher()

    def preencher(self):
        q = (self.tbBusca.Text or u"").strip().lower()
        il = self.cbLink.SelectedIndex
        lk = self.links[il - 1] if il > 0 else None
        cat = self.cbCat.SelectedItem if self.cbCat.SelectedIndex > 0 else None
        ie = self.cbEstado.SelectedIndex
        self.vista = []
        for it in self.itens:
            if lk and (it.link.nome if it.link else u"(link ausente)") != lk:
                continue
            if cat and it.cat != cat:
                continue
            if ie == 0 and it.estado == "ok":
                continue
            if ie >= 2 and it.estado != ESTADOS[ie - 2][0]:
                continue
            if q and q not in u" ".join([it.cat, it.fam, it.tipo, it.nivel, it.difs]).lower():
                continue
            self.vista.append(it)
        ordem = dict((k, i) for i, (k, _, _, _) in enumerate(ESTADOS))
        self.vista.sort(key=lambda i: (ordem.get(i.estado, 9), i.cat, i.fam, i.tipo, i.nivel))
        self._pronto = False
        self.lbItens.Items.Clear()
        for it in self.vista:
            li = ListBoxItem()
            li.Padding = Thickness(4, 3, 4, 3)
            li.Content = _grade([it.cat, u"{} : {}".format(it.fam, it.tipo), it.nivel or u"—", _selo(it.estado),
                                 it.difs or (u"origem apagada no link" if it.estado == "apagado" else u"")])
            self.lbItens.Items.Add(li)
        self._pronto = True
        self.sel_mudou(None, None)

    def _sel(self):
        return [self.vista[i] for i in range(len(self.vista)) if self.lbItens.Items[i].IsSelected]

    def sel_mudou(self, sender, args):
        if not self._pronto:
            return
        s = self._sel()
        n = lambda est: len([i for i in s if i.estado in est])
        alt = n(("link", "projeto", "ambos"))
        self.btAtualizar.IsEnabled = self.btAceitar.IsEnabled = alt > 0
        self.btAtualizar.Content = u"Atualizar pelo link ({})".format(alt) if alt else u"Atualizar pelo link"
        self.btAceitar.Content = u"Aceitar como está ({})".format(alt) if alt else u"Aceitar como está"
        self.btApagar.IsEnabled = n(("apagado",)) > 0
        self.btNovos.IsEnabled = n(("novo",)) > 0
        self.btMostrar.IsEnabled = any(i.copia is not None for i in s)
        self.txtRodape.Text = (u"{} selecionado(s).".format(len(s)) if s else
                               u"Selecione linhas (Ctrl/Shift; Ctrl+A para todas) e escolha a ação.")

    # ------------------------------------------------------------ ações
    def _transacao(self, nome, fn, itens):
        t = DB.Transaction(doc, nome)
        t.Start()
        K.opcoes_transacao(t)
        feitos, falhas = [], []
        try:
            for it in itens:
                try:
                    feitos.append((it, fn(it)))
                except Exception as ex:
                    falhas.append((it, u"{}".format(ex)))
            t.Commit()
        except Exception as ex:
            if t.HasStarted() and not t.HasEnded():
                t.RollBack()
            forms.alert(u"Nada foi alterado: {}".format(ex), title=TITULO)
            return None, None
        if falhas:
            out = script.get_output()
            out.print_md(u"### {} — não concluídos".format(nome))
            for it, txt in falhas:
                out.print_md(u"- {} · {} : {} — {}".format(it.cat, it.fam, it.tipo, txt))
        return feitos, falhas

    def atualizar(self, sender, args):
        alvo = [i for i in self._sel() if i.estado in ("link", "projeto", "ambos")]
        if not alvo:
            return
        if any(i.estado in ("projeto", "ambos") for i in alvo) and not forms.alert(
                u"Algumas cópias foram mexidas neste modelo. Atualizar pelo link desfaz essas mudanças "
                u"(a cópia volta para onde está a origem no link). Continuar?", title=TITULO, yes=True, no=True):
            return
        por = _por()
        feitos, falhas = self._transacao(u"Verificar alterações: atualizar pelo link",
                                         lambda it: K.atualizar(doc, it, por), alvo)
        if feitos is None:
            return
        rec = len([1 for _, r in feitos if r == "recriado"])
        self.carregar()
        self.txtRodape.Text = u"{} atualizado(s){}{}.".format(
            len(feitos), u" ({} copiado(s) de novo do link: espelhamento, nível ou tipo)".format(rec) if rec else u"",
            u" · {} com falha (ver relatório)".format(len(falhas)) if falhas else u"")

    def aceitar(self, sender, args):
        alvo = [i for i in self._sel() if i.estado in ("link", "projeto", "ambos")]
        if not alvo:
            return
        por = _por()
        feitos, falhas = self._transacao(u"Verificar alterações: aceitar como está",
                                         lambda it: K.aceitar(doc, it, por), alvo)
        if feitos is None:
            return
        self.carregar()
        self.txtRodape.Text = u"{} aceito(s): as cópias ficaram como estão e passam a valer como referência.".format(len(feitos))

    def apagar(self, sender, args):
        alvo = [i for i in self._sel() if i.estado == "apagado"]
        if not alvo or not forms.alert(u"Apagar do modelo {} cópia(s) cuja origem foi apagada no link?".format(len(alvo)),
                                       title=TITULO, yes=True, no=True):
            return
        feitos, falhas = self._transacao(u"Verificar alterações: apagar cópias",
                                         lambda it: doc.Delete(it.copia.Id), alvo)
        if feitos is None:
            return
        self.carregar()
        self.txtRodape.Text = u"{} cópia(s) apagada(s).".format(len(feitos))

    def copiar_novos(self, sender, args):
        alvo = [i for i in self._sel() if i.estado == "novo"]
        if not alvo:
            return
        t = DB.Transaction(doc, u"Verificar alterações: copiar novos ({})".format(len(alvo)))
        t.Start()
        K.opcoes_transacao(t)
        try:
            n, falhas = 0, []
            grupos = {}
            for it in alvo:
                grupos.setdefault((it.link.uid, it.mod["id"]), []).append(it)
            ws = K.workset_ativo(doc)
            for _, lst in grupos.items():
                res = K.copiar(doc, lst[0].link, [i.origem for i in lst], lst[0].mod, workset=ws, por=_por())
                n += len(res["copiados"])
                falhas += res["falhas"]
            t.Commit()
        except Exception as ex:
            if t.HasStarted() and not t.HasEnded():
                t.RollBack()
            forms.alert(u"Nada foi copiado: {}".format(ex), title=TITULO)
            return
        self.carregar()
        self.txtRodape.Text = u"{} copiado(s){}.".format(n, u" · {} não copiado(s)".format(len(falhas)) if falhas else u"")

    def mostrar(self, sender, args):
        ids = [i.copia.Id for i in self._sel() if i.copia is not None]
        if ids:
            col = List[DB.ElementId](ids)
            uidoc.Selection.SetElementIds(col)
            try:
                uidoc.ShowElements(col)
            except Exception:
                pass
            self.txtRodape.Text = u"{} cópia(s) selecionada(s) no modelo.".format(len(ids))

    def reverificar(self, sender, args):
        self.carregar()

    def fechar(self, sender, args):
        self.Close()


if doc is None or doc.IsFamilyDocument:
    forms.alert(u"Abra um modelo de projeto para usar esta ferramenta.", title=TITULO)
else:
    Janela().ShowDialog()
