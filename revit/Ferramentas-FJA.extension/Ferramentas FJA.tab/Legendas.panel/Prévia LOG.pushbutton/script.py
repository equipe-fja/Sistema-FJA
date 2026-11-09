# -*- coding: utf-8 -*-
"""Prévia LOG (protótipo): a legenda de pontos de lógica numa janela com prévia do próprio Revit.
Cada "Atualizar prévia" refaz a legenda dentro de um grupo de transações que é desfeito na
atualização seguinte; "Gerar legenda" grava o último resultado; "Cancelar" (ou fechar) desfaz tudo.
A lógica é a mesma do botão Legenda LOG (lib/fja_legenda_disciplina.py)."""
import os

from pyrevit import revit, DB, forms

import fja_legenda_disciplina as L

from Autodesk.Revit.UI import PreviewControl
from System.Windows import Thickness, VerticalAlignment, GridLength, GridUnitType, TextTrimming
from System.Windows.Controls import Grid, ColumnDefinition, CheckBox, TextBox, TextBlock
from System.Windows.Media import SolidColorBrush, Color

doc = revit.doc
uidoc = revit.uidoc

CFG = {
    "botao": u"Legenda LOG",
    "alvo": u"LOG - LEGENDA DE PONTOS DE LÓGICA",
    "titulo": u"LEGENDA DE PONTOS DE LÓGICA",
    "familias": ("(LOG)",),
    "vistas": ("LOG",),
    "bases": ("LOG - LEGENDA GRAFICA", "LOG - LEGENDA"),
    "guid": "9a4e7c21-5b3d-4e8f-a6c2-3d1f8b7e5a92",
    "schema": "FJA_LegendaLOG",
    "mark": "LegendaLOG",
    "cor": (121, 86, 169),
}
TITULO = u"Legenda LOG · prévia"


def aplicar_estilo(win):
    try:
        from System.Windows.Markup import XamlReader
        from System.IO import File
        caminho = os.path.join(os.path.dirname(L.__file__), "fja_estilo.xaml")
        win.Resources.MergedDictionaries.Add(XamlReader.Parse(File.ReadAllText(caminho)))
    except Exception as e:
        print(u"estilo: {}".format(e))


def _mm(v):
    return (u"{:.1f}".format(v)).replace(".", ",")


class Janela(forms.WPFWindow):
    def __init__(self, itens):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        aplicar_estilo(self)
        self.itens = itens
        self.tg = None
        self.prev = None
        self.res = None
        self.gravado = False
        self.linhas = []
        self._montar_lista()
        self.slTam.ValueChanged += self._mudou
        self.slPasso.ValueChanged += self._mudou
        self.chkTodosRedim.Click += self._todos_redim
        self.btAtualizar.Click += lambda s, a: self.atualizar()
        self.btGerar.Click += self._gerar
        self.btCancelar.Click += lambda s, a: self.Close()
        self.Closing += self._fechando
        self.ContentRendered += self._primeira
        self._rotulos()

    # ------------------------------------------------------------ lista de itens
    def _montar_lista(self):
        cinza = SolidColorBrush(Color.FromRgb(94, 94, 92))
        for it in self.itens:
            g = Grid()
            g.Margin = Thickness(4, 3, 4, 3)
            for w in (26, -1, 92, 96):
                cd = ColumnDefinition()
                cd.Width = GridLength(1, GridUnitType.Star) if w < 0 else GridLength(w)
                g.ColumnDefinitions.Add(cd)
            chk = CheckBox()
            chk.IsChecked = True
            chk.VerticalAlignment = VerticalAlignment.Center
            chk.ToolTip = u"Incluir na legenda"
            txt = TextBox()
            txt.Text = it["label"]
            txt.VerticalAlignment = VerticalAlignment.Center
            txt.Padding = Thickness(4, 2, 4, 2)
            txt.ToolTip = u"{}  ·  {} elemento(s) em {} vista(s)".format(it["sym"].FamilyName, it["count"], it["views"])
            med = TextBlock()
            med.VerticalAlignment = VerticalAlignment.Center
            med.Margin = Thickness(8, 0, 4, 0)
            med.Foreground = cinza
            med.FontSize = 12
            med.TextTrimming = TextTrimming.CharacterEllipsis
            red = CheckBox()
            red.Content = u"Redimensionar"
            red.FontSize = 12
            red.VerticalAlignment = VerticalAlignment.Center
            red.ToolTip = u"Refaz o símbolo (LEG) no tamanho escolhido acima"
            for el, c in ((chk, 0), (txt, 1), (med, 2), (red, 3)):
                Grid.SetColumn(el, c)
                g.Children.Add(el)
            chk.Click += self._mudou
            red.Click += self._mudou
            txt.TextChanged += self._mudou
            self.lista.Children.Add(g)
            self.linhas.append({"it": it, "chk": chk, "txt": txt, "med": med, "red": red})
        self.txtItens.Text = u"Itens ({})".format(len(self.itens))

    def _todos_redim(self, s, a):
        v = bool(self.chkTodosRedim.IsChecked)
        for ln in self.linhas:
            ln["red"].IsChecked = v
        self._mudou()

    def _rotulos(self):
        self.txtTam.Text = _mm(self.slTam.Value) + u" mm"
        self.txtPasso.Text = _mm(self.slPasso.Value) + u" mm"

    def _mudou(self, *a):
        self._rotulos()
        if self.res is not None:
            self.txtStatus.Text = u"Há ajustes que ainda não estão na prévia: clique em Atualizar prévia."

    # ------------------------------------------------------------ prévia
    def _primeira(self, s, a):
        self.ContentRendered -= self._primeira
        self.atualizar()

    def _fechar_prev(self):
        if self.prev is not None:
            try:
                self.hostPrev.Children.Clear()
                self.prev.Dispose()
            except Exception:
                pass
            self.prev = None
        self.txtPrevVazia.Visibility = 0  # Visible

    def _desfazer(self):
        self._fechar_prev()
        if self.tg is not None:
            try:
                if self.tg.HasStarted() and not self.tg.HasEnded():
                    self.tg.RollBack()
            except Exception:
                pass
            self.tg = None

    def atualizar(self):
        self.txtStatus.Text = u"Gerando a prévia…"
        self._desfazer()
        escolhidos, redim = [], set()
        for ln in self.linhas:
            ln["med"].Text = u""
            if not ln["chk"].IsChecked:
                continue
            it = dict(ln["it"])
            rot = (ln["txt"].Text or u"").strip()
            if rot:
                it["label"] = rot.upper()
            escolhidos.append(it)
            if ln["red"].IsChecked:
                redim.add(it["sym"].Family.Id.ToString())
        if not escolhidos:
            self.res = None
            self.txtStatus.Text = u"Nenhum item marcado."
            return
        self.tg = DB.TransactionGroup(doc, u"Prévia " + CFG["botao"])
        self.tg.Start()
        try:
            res = L.atualizar_legenda(doc, escolhidos, {"tamanho_mm": self.slTam.Value, "redim": redim,
                                                         "passo_mm": self.slPasso.Value})
        except Exception as e:
            self._desfazer()
            self.res = None
            self.txtStatus.Text = u"Erro ao gerar a prévia: {}".format(e)
            return
        if not res.get("ok"):
            self._desfazer()
            self.res = None
            self.txtStatus.Text = res.get("msg") or u"Não foi possível gerar a legenda."
            return
        self.res = res
        medidas = {}
        for it in res["items"]:
            medidas[it["sym"].Id.ToString()] = it
        for ln in self.linhas:
            it = medidas.get(ln["it"]["sym"].Id.ToString())
            if it is None:
                continue
            mm = it.get("mm")
            ln["med"].Text = (u"{} × {} mm".format(_mm(mm[0]), _mm(mm[1])) if mm else u"sem símbolo")
            modo = {"leg": u"família (LEG)", "simbolos": u"anotações", "planta": u"desenho da planta",
                    "componente": u"componente de legenda"}.get(it.get("modo"), it.get("modo") or u"")
            ln["med"].ToolTip = u"Símbolo: {}{}".format(modo, u" · redimensionado" if it.get("redimensionado") else u"")
        try:
            self.prev = PreviewControl(doc, res["view"].Id)
            self.hostPrev.Children.Add(self.prev)
            self.txtPrevVazia.Visibility = 2  # Collapsed
        except Exception as e:
            self.txtStatus.Text = u"Legenda gerada, mas a prévia não abriu: {}".format(e)
            return
        partes = [u"{} itens".format(len(res["items"]))]
        if res.get("cols") and res["cols"][1]:
            partes.append(u"2 colunas ({} + {})".format(res["cols"][0], res["cols"][1]))
        if res.get("leg_criadas"):
            partes.append(u"{} símbolo(s) (LEG) criados".format(len(res["leg_criadas"])))
        if res.get("leg_erros"):
            partes.append(u"{} símbolo(s) com erro".format(len(res["leg_erros"])))
        self.txtStatus.Text = u"Prévia: " + u" · ".join(partes) + u". Use a roda do mouse para aproximar."

    # ------------------------------------------------------------ gravar / desfazer
    def _gerar(self, s, a):
        if self.res is None or self.tg is None:
            self.atualizar()
            if self.res is None:
                return
        self._fechar_prev()
        try:
            self.tg.Assimilate()
            self.gravado = True
        except Exception as e:
            forms.alert(u"Não foi possível gravar: {}".format(e), title=TITULO)
            self._desfazer()
            return
        self.tg = None
        self.Close()

    def _fechando(self, s, a):
        if not self.gravado:
            self._desfazer()
        else:
            self._fechar_prev()


def main():
    L.configurar(CFG)
    try:
        itens = L.collect_items(doc)
    except Exception as e:
        forms.alert(u"Erro ao ler os itens: {}".format(e), title=TITULO)
        return
    if not itens:
        forms.alert(u"Nenhuma família '(LOG)' aparece nas vistas das pranchas.", title=TITULO)
        return
    j = Janela(itens)
    j.ShowDialog()
    if j.gravado and j.res is not None:
        try:
            uidoc.ActiveView = j.res["view"]
        except Exception:
            pass


if __name__ == "__main__":
    main()
