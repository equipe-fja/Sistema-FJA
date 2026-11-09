# -*- coding: utf-8 -*-
"""Prévia LOG (protótipo): a legenda de pontos de lógica numa janela com prévia do próprio Revit.
Cada "Atualizar prévia" refaz a legenda dentro de um grupo de transações que é desfeito na
atualização seguinte; "Gerar legenda" grava o último resultado; "Cancelar" (ou fechar) desfaz tudo.
Escala por item (25% a 300%; 100% = como aparece na prancha): ao mexer, só uma caixa vermelha é
desenhada na prévia no tamanho novo (rápido); os símbolos são refeitos (pelo DWG) ao atualizar.
A lógica é a mesma do botão Legenda LOG (lib/fja_legenda_disciplina.py)."""
import os

from pyrevit import revit, DB, forms

import fja_legenda_disciplina as L

from Autodesk.Revit.UI import PreviewControl
from System import TimeSpan
from System.Collections.Generic import List
from System.Windows import Thickness, VerticalAlignment, GridLength, GridUnitType, TextTrimming, Visibility, \
    HorizontalAlignment, FontWeights
from System.Windows.Controls import Grid, ColumnDefinition, CheckBox, TextBox, TextBlock, Slider
from System.Windows.Media import SolidColorBrush, Color
from System.Windows.Threading import DispatcherTimer

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
COR_CAIXA = (200, 30, 30)


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
        self.base = {}        # id do tipo -> (largura, altura) em mm a 100% (como na prancha)
        self.caixas = {}      # id do tipo -> (cx, cy, w, h) do simbolo na vista da previa (pes)
        self.pct_feito = {}   # id do tipo -> % com que a previa atual foi gerada
        self.ids_caixa = []
        self.timer = DispatcherTimer()
        self.timer.Interval = TimeSpan.FromMilliseconds(150)
        self.timer.Tick += self._tick
        self._montar_lista()
        self.slPasso.ValueChanged += self._mudou
        self.btTodos100.Click += self._todos100
        self.btAtualizar.Click += lambda s, a: self.atualizar()
        self.btGerar.Click += self._gerar
        self.btCancelar.Click += lambda s, a: self.Close()
        self.Closing += self._fechando
        self.ContentRendered += self._primeira
        self.txtPasso.Text = _mm(self.slPasso.Value) + u" mm"

    # ------------------------------------------------------------ lista de itens
    def _montar_lista(self):
        cinza = SolidColorBrush(Color.FromRgb(94, 94, 92))
        for it in self.itens:
            g = Grid()
            g.Margin = Thickness(4, 3, 4, 3)
            for w in (26, -1, 96, 120, 46):
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
            sl = Slider()
            sl.Minimum, sl.Maximum, sl.Value = 25, 300, 100
            sl.TickFrequency = 5
            sl.IsSnapToTickEnabled = True
            sl.VerticalAlignment = VerticalAlignment.Center
            sl.ToolTip = u"Escala do símbolo (100% = como aparece na prancha). Duplo clique volta a 100%."
            pct = TextBlock()
            pct.Text = u"100%"
            pct.VerticalAlignment = VerticalAlignment.Center
            pct.HorizontalAlignment = HorizontalAlignment.Right
            pct.FontWeight = FontWeights.SemiBold
            for el, c in ((chk, 0), (txt, 1), (med, 2), (sl, 3), (pct, 4)):
                Grid.SetColumn(el, c)
                g.Children.Add(el)
            ln = {"it": it, "chk": chk, "txt": txt, "med": med, "sl": sl, "pct": pct, "id": it["sym"].Id.ToString()}
            chk.Click += self._mudou
            txt.TextChanged += self._mudou
            sl.ValueChanged += lambda s, a, ln=ln: self._escala(ln)
            sl.MouseDoubleClick += lambda s, a, ln=ln: setattr(ln["sl"], "Value", 100)
            self.lista.Children.Add(g)
            self.linhas.append(ln)
        self.txtItens.Text = u"Itens ({})".format(len(self.itens))

    def _todos100(self, s, a):
        for ln in self.linhas:
            ln["sl"].Value = 100

    def _escala(self, ln):
        v = int(round(ln["sl"].Value))
        ln["pct"].Text = u"{}%".format(v)
        b = self.base.get(ln["id"])
        if b:
            ln["med"].Text = u"{} × {} mm".format(_mm(b[0] * v / 100.0), _mm(b[1] * v / 100.0))
        # caixas na previa: desenhadas logo depois que o controle para de mexer
        self.timer.Stop()
        self.timer.Start()
        self._pendente()

    def _mudou(self, *a):
        self.txtPasso.Text = _mm(self.slPasso.Value) + u" mm"
        self._pendente()

    def _pendente(self):
        if self.res is not None and self._ha_mudanca():
            self.txtStatus.Text = u"Há ajustes que ainda não estão na prévia (caixas vermelhas = tamanho novo): clique em Atualizar prévia."

    def _ha_mudanca(self):
        return any(int(round(ln["sl"].Value)) != self.pct_feito.get(ln["id"], 100) for ln in self.linhas)

    # ------------------------------------------------------------ caixas (rapido: so linhas na previa)
    def _tick(self, s, a):
        self.timer.Stop()
        self._desenhar_caixas()

    def _desenhar_caixas(self):
        if self.res is None or self.tg is None:
            return
        view = self.res["view"]
        t = DB.Transaction(doc, u"Prévia: caixas")
        t.Start()
        try:
            vivos = [i for i in self.ids_caixa if doc.GetElement(i) is not None]
            if vivos:
                doc.Delete(List[DB.ElementId](vivos))
            self.ids_caixa = []
            for ln in self.linhas:
                cx = self.caixas.get(ln["id"])
                if cx is None or not ln["chk"].IsChecked:
                    continue
                v = int(round(ln["sl"].Value))
                feito = self.pct_feito.get(ln["id"], 100)
                if v == feito:
                    continue
                f = float(v) / feito
                x, y, w, h = cx
                hw, hh = w * f / 2.0, h * f / 2.0
                pts = [DB.XYZ(x - hw, y - hh, 0), DB.XYZ(x + hw, y - hh, 0), DB.XYZ(x + hw, y + hh, 0), DB.XYZ(x - hw, y + hh, 0)]
                for a, b in zip(pts, pts[1:] + pts[:1]):
                    c = doc.Create.NewDetailCurve(view, DB.Line.CreateBound(a, b))
                    L.paint(view, c.Id, COR_CAIXA)
                    self.ids_caixa.append(c.Id)
            t.Commit()
        except Exception as e:
            if t.HasStarted() and not t.HasEnded():
                t.RollBack()
            self.txtStatus.Text = u"Não foi possível desenhar as caixas: {}".format(e)

    def _apagar_caixas(self):
        if not self.ids_caixa:
            return
        t = DB.Transaction(doc, u"Prévia: caixas")
        t.Start()
        vivos = [i for i in self.ids_caixa if doc.GetElement(i) is not None]
        if vivos:
            doc.Delete(List[DB.ElementId](vivos))
        t.Commit()
        self.ids_caixa = []

    # ------------------------------------------------------------ previa
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
        self.txtPrevVazia.Visibility = Visibility.Visible

    def _desfazer(self):
        self.timer.Stop()
        self._fechar_prev()
        self.ids_caixa = []
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
        escolhidos, tamanhos, pcts = [], {}, {}
        for ln in self.linhas:
            if not ln["chk"].IsChecked:
                continue
            it = dict(ln["it"])
            rot = (ln["txt"].Text or u"").strip()
            if rot:
                it["label"] = rot.upper()
            escolhidos.append(it)
            v = int(round(ln["sl"].Value))
            pcts[ln["id"]] = v
            b = self.base.get(ln["id"])
            if v != 100 and b:
                fk = it["sym"].Family.Id.ToString()
                tamanhos.setdefault(fk, max(b) * v / 100.0)
        if not escolhidos:
            self.res = None
            self.txtStatus.Text = u"Nenhum item marcado."
            return
        self.tg = DB.TransactionGroup(doc, u"Prévia " + CFG["botao"])
        self.tg.Start()
        try:
            res = L.atualizar_legenda(doc, escolhidos, {"tamanhos": tamanhos, "passo_mm": self.slPasso.Value})
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
        view = res["view"]
        porid = dict((it["sym"].Id.ToString(), it) for it in res["items"])
        self.caixas = {}
        for ln in self.linhas:
            it = porid.get(ln["id"])
            if it is None:
                ln["med"].Text = u""
                continue
            ids = it.get("sym_ids") or []
            bb = L.union_bbox(doc, ids, view) if ids else None
            if bb:
                self.caixas[ln["id"]] = ((bb[0] + bb[2]) / 2.0, (bb[1] + bb[3]) / 2.0, bb[2] - bb[0], bb[3] - bb[1])
            mm = it.get("mm")
            v = pcts.get(ln["id"], 100)
            if mm and (v == 100 or ln["id"] not in self.base):
                # 100% = tamanho como esta na prancha (ou o primeiro medido)
                self.base[ln["id"]] = (mm[0] * 100.0 / v, mm[1] * 100.0 / v)
            ln["med"].Text = (u"{} × {} mm".format(_mm(mm[0]), _mm(mm[1])) if mm else u"sem símbolo")
            modo = {"leg": u"família (LEG)", "simbolos": u"anotações", "planta": u"desenho da planta",
                    "componente": u"componente de legenda"}.get(it.get("modo"), it.get("modo") or u"")
            ln["med"].ToolTip = u"Símbolo: {}{}".format(modo, u" · redimensionado pelo DWG" if it.get("redimensionado") else u"")
        self.pct_feito = pcts
        try:
            self.prev = PreviewControl(doc, view.Id)
            self.hostPrev.Children.Add(self.prev)
            self.txtPrevVazia.Visibility = Visibility.Collapsed
        except Exception as e:
            self.txtStatus.Text = u"Legenda gerada, mas a prévia não abriu: {}".format(e)
            return
        partes = [u"{} itens".format(len(res["items"]))]
        if res.get("cols") and res["cols"][1]:
            partes.append(u"2 colunas ({} + {})".format(res["cols"][0], res["cols"][1]))
        n_red = len([1 for it in res["items"] if it.get("redimensionado")])
        if n_red:
            partes.append(u"{} símbolo(s) redimensionado(s)".format(n_red))
        self.txtStatus.Text = u"Prévia: " + u" · ".join(partes) + u". Use a roda do mouse para aproximar."
        self.txtStatus.ToolTip = None
        if res.get("leg_erros"):
            self.txtStatus.Text += u"\n" + u"\n".join(res["leg_erros"][:3])
            self.txtStatus.ToolTip = u"\n".join(res["leg_erros"])

    # ------------------------------------------------------------ gravar / desfazer
    def _gerar(self, s, a):
        if self.res is None or self.tg is None or self._ha_mudanca():
            self.atualizar()
            if self.res is None:
                return
        self.timer.Stop()
        self._fechar_prev()
        try:
            self._apagar_caixas()
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
