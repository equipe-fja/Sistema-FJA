# -*- coding: utf-8 -*-
"""Prévia LOG (protótipo): a legenda de pontos de lógica numa janela com prévia do próprio Revit.
Cada "Atualizar prévia" refaz a legenda dentro de um grupo de transações que é desfeito na
atualização seguinte; "Gerar legenda" grava o último resultado; "Cancelar" (ou fechar) desfaz tudo.
Escala por item (25% a 300%; 100% = como aparece na prancha): ao mexer, só uma caixa vermelha é
desenhada na prévia no tamanho novo (rápido); os símbolos são refeitos (pelo DWG) ao atualizar.
A lógica é a mesma do botão Legenda LOG (lib/fja_legenda_disciplina.py)."""
import os

from System.Diagnostics import Stopwatch
_T0 = Stopwatch.StartNew()

from pyrevit import revit, DB, forms, script

import fja_legenda_disciplina as L

from Autodesk.Revit.UI import PreviewControl
from System import TimeSpan
from System.Collections.Generic import List
from System.Windows import Thickness, VerticalAlignment, GridLength, GridUnitType, TextTrimming, Visibility, \
    HorizontalAlignment, FontWeights, FontStyles
from System.Windows.Controls import Grid, ColumnDefinition, CheckBox, TextBox, TextBlock, Slider, Border, \
    ContextMenu, MenuItem
from System.Windows import DragDrop, DataObject, DragDropEffects
from System.Windows.Input import Cursors
from System.Windows.Media import Brushes
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
    "disc": "LOG",
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


def _cfg():
    try:
        return script.get_config()
    except Exception:
        return None


def _ler_cfg(nome, padrao):
    c = _cfg()
    try:
        return c.get_option(nome, padrao) if c is not None else padrao
    except Exception:
        return padrao


def _gravar_cfg(**valores):
    c = _cfg()
    if c is None:
        return
    try:
        for k, v in valores.items():
            setattr(c, k, v)
        script.save_config()
    except Exception:
        pass


def _log_tempos(titulo):
    """Tempos da abertura gravados em REVIT\\pyRevit\\logs (para o diagnóstico de lentidão)."""
    linhas = [u"{:<62} {:>7.2f} s".format(n, d) for (n, d, _) in L.TEMPOS]
    try:
        import io, datetime
        pasta = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(L.__file__))), u"logs")
        if not os.path.isdir(pasta):
            os.makedirs(pasta)
        nome = os.path.join(pasta, u"previa-legenda-tempos.txt")
        cab = u"\n=== {}  {}  {}  ({})\n".format(datetime.datetime.now().strftime("%d/%m/%Y %H:%M:%S"),
                                              os.environ.get("COMPUTERNAME", u""), doc.Title, titulo)
        with io.open(nome, "a", encoding="utf-8") as f:
            f.write(cab + u"\n".join(linhas) + u"\n")
    except Exception:
        pass
    return u"\n".join(linhas)


def _mm(v):
    return (u"{:.1f}".format(v)).replace(".", ",")


class Janela(forms.WPFWindow):
    def __init__(self, itens, desmarcados=None):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        aplicar_estilo(self)
        self.itens = itens
        self.desmarcados = desmarcados or set()
        self._arrastando = None
        self.tg = None
        self.prev = None
        self.res = None
        self.gravado = False
        self.linhas = []
        self.base = {}        # id do tipo -> (largura, altura) em mm a 100% (como na prancha)
        self.caixas = {}      # id do tipo -> (cx, cy, w, h) do simbolo na vista da previa (pes)
        self.pct_feito = {}   # id do tipo -> % com que a previa atual foi gerada
        self.ids_caixa = {}   # id do tipo -> ids das 4 linhas da caixa vermelha
        self.sujos = set()    # itens cuja caixa precisa ser redesenhada
        self.timer = DispatcherTimer()
        self.timer.Interval = TimeSpan.FromMilliseconds(150)
        self.timer.Tick += self._tick
        self._montar_lista()
        self.slPasso.ValueChanged += self._mudou
        for c in (self.chkLinhasH, self.chkLinhaV, self.chkMargens):
            c.Click += self._mudou
        self.txtLargura.TextChanged += self._mudou
        self.cor = None                 # None = cor do modelo; "preto"; ou (r, g, b)
        self.cor_sistema = None         # (rgb, texto) lido do sistema (só quando pedido)
        self.cor_pers = _ler_cfg("cor_personalizada", None)
        modo = _ler_cfg("modo_cor_v2", 0)
        self._mudando_cor = True
        self.cbCor.SelectedIndex = modo if modo in (0, 1, 2, 3) else 0
        self._mudando_cor = False
        self.cbCor.SelectionChanged += self._modo_cor
        self.swCor.MouseLeftButtonUp += lambda s, a: self._escolher_cor()
        self.swCor.ToolTip = u"Clique para escolher outra cor"
        self._aplicar_modo(self.cbCor.SelectedIndex, inicial=True)
        self.btTodos100.Click += self._todos100
        self.btAdicionar.Click += self._adicionar
        self.btAtualizar.Click += lambda s, a: self.atualizar()
        self.btGerar.Click += self._gerar
        self.btCancelar.Click += lambda s, a: self.Close()
        self.Closing += self._fechando
        self.ContentRendered += self._primeira
        self.txtPasso.Text = _mm(self.slPasso.Value) + u" mm"

    # ------------------------------------------------------------ lista de itens
    def _montar_lista(self):
        for it in self.itens:
            self._criar_linha(it, desmarcado=L.chave_item(it) in self.desmarcados)
        self._titulo_lista()

    def _titulo_lista(self):
        n_fora = len([1 for ln in self.linhas if ln["it"].get("em_prancha") is False])
        n_extra = len([1 for ln in self.linhas if ln["it"].get("extra")])
        partes = []
        if n_extra:
            partes.append(u"{} acrescentado(s)".format(n_extra))
        if n_fora:
            partes.append(u"{} fora das pranchas, em laranja".format(n_fora))
        self.txtItens.Text = u"Itens ({}{})".format(len(self.linhas), u" · " + u" · ".join(partes) if partes else u"")

    def _criar_linha(self, it, desmarcado=False):
        cinza = SolidColorBrush(Color.FromRgb(94, 94, 92))
        extra = bool(it.get("extra"))
        borda = Border()
        borda.BorderThickness = Thickness(0, 2, 0, 2)
        borda.BorderBrush = Brushes.Transparent
        borda.AllowDrop = True
        borda.Background = Brushes.Transparent
        g = Grid()
        g.Margin = Thickness(2, 1, 4, 1)
        for w in (18, 26, -1, 96, 120, 46):
            cd = ColumnDefinition()
            cd.Width = GridLength(1, GridUnitType.Star) if w < 0 else GridLength(w)
            g.ColumnDefinitions.Add(cd)
        alca = TextBlock()
        alca.Text = u"≡"
        alca.FontSize = 16
        alca.Foreground = cinza
        alca.VerticalAlignment = VerticalAlignment.Center
        alca.HorizontalAlignment = HorizontalAlignment.Center
        alca.Cursor = Cursors.SizeAll
        alca.Background = Brushes.Transparent
        alca.ToolTip = u"Arraste para mudar a ordem na legenda"
        chk = CheckBox()
        chk.IsChecked = not desmarcado
        chk.VerticalAlignment = VerticalAlignment.Center
        chk.ToolTip = u"Incluir na legenda"
        txt = TextBox()
        txt.Text = it["label"]
        txt.VerticalAlignment = VerticalAlignment.Center
        txt.Padding = Thickness(4, 2, 4, 2)
        fora = it.get("em_prancha") is False
        if extra:
            cat = it["sym"].Category.Name if it["sym"].Category else u""
            dica = u"Acrescentado: {} : {} ({})\nClique com o botão direito para retirar.".format(
                it["sym"].FamilyName, L.elem_name(it["sym"]), cat)
        else:
            dica = u"{}  ·  {} elemento(s) no modelo".format(it["sym"].FamilyName, it["count"])
            if it.get("em_prancha"):
                dica += u"  ·  aparece em {} planta(s) {} em prancha".format(it["views"], u"/".join(CFG["vistas"]))
            elif fora:
                dica += u"\nNão aparece em nenhuma planta {} colocada em prancha (desmarque se não deve entrar)".format(u"/".join(CFG["vistas"]))
        txt.ToolTip = dica
        if fora:
            txt.Foreground = SolidColorBrush(Color.FromRgb(178, 107, 0))
            txt.FontStyle = FontStyles.Italic
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
        if extra:
            sl.IsEnabled = False
            sl.ToolTip = u"Símbolo acrescentado: entra no tamanho da própria família"
            pct.Text = u"—"
            med.Text = u"acrescentado"
        for el, c in ((alca, 0), (chk, 1), (txt, 2), (med, 3), (sl, 4), (pct, 5)):
            Grid.SetColumn(el, c)
            g.Children.Add(el)
        borda.Child = g
        ln = {"it": it, "chk": chk, "txt": txt, "med": med, "sl": sl, "pct": pct, "id": L.chave_item(it),
              "borda": borda, "padrao": it.get("padrao", it["label"])}
        chk.Click += lambda s, a, ln=ln: (self.sujos.add(ln["id"]), self.timer.Stop(), self.timer.Start(), self._mudou())
        txt.TextChanged += self._mudou
        if not extra:
            sl.ValueChanged += lambda s, a, ln=ln: self._escala(ln)
            sl.MouseDoubleClick += lambda s, a, ln=ln: setattr(ln["sl"], "Value", 100)
        else:
            menu = ContextMenu()
            mi = MenuItem()
            mi.Header = u"Retirar este item da lista"
            mi.Click += lambda s, a, ln=ln: self._retirar(ln)
            menu.Items.Add(mi)
            borda.ContextMenu = menu
        # arrastar para reordenar
        alca.PreviewMouseLeftButtonDown += lambda s, a, ln=ln: self._arrastar(ln)
        borda.PreviewDragOver += lambda s, a, ln=ln: self._sobre(ln, a)
        borda.DragLeave += lambda s, a, ln=ln: self._marca(ln, None)
        borda.PreviewDrop += lambda s, a, ln=ln: self._soltar(ln, a)
        self.lista.Children.Add(borda)
        self.linhas.append(ln)
        return ln

    # ------------------------------------------------------------ ordem (arrastar)
    def _arrastar(self, ln):
        self._arrastando = ln
        try:
            DragDrop.DoDragDrop(ln["borda"], DataObject("fjaItem", ln["id"]), DragDropEffects.Move)
        finally:
            self._arrastando = None
            for x in self.linhas:
                self._marca(x, None)

    def _depois(self, ln, a):
        p = a.GetPosition(ln["borda"])
        return p.Y > ln["borda"].ActualHeight / 2.0

    def _marca(self, ln, depois):
        cor = SolidColorBrush(Color.FromRgb(121, 86, 169))
        b = ln["borda"]
        b.BorderBrush = Brushes.Transparent if depois is None else cor
        if depois is not None:
            b.BorderThickness = Thickness(0, 0, 0, 2) if depois else Thickness(0, 2, 0, 0)
        else:
            b.BorderThickness = Thickness(0, 2, 0, 2)

    def _sobre(self, ln, a):
        if getattr(self, "_arrastando", None) is None:
            a.Effects = getattr(DragDropEffects, "None")
        else:
            a.Effects = DragDropEffects.Move
            self._marca(ln, self._depois(ln, a))
        a.Handled = True

    def _soltar(self, alvo, a):
        orig = getattr(self, "_arrastando", None)
        a.Handled = True
        self._marca(alvo, None)
        if orig is None or orig is alvo:
            return
        depois = self._depois(alvo, a)
        self.linhas.remove(orig)
        i = self.linhas.index(alvo) + (1 if depois else 0)
        self.linhas.insert(i, orig)
        self.lista.Children.Remove(orig["borda"])
        self.lista.Children.Insert(i, orig["borda"])
        self._mudou()

    def _ordem(self):
        return [ln["id"] for ln in self.linhas]

    # ------------------------------------------------------------ itens acrescentados
    def _adicionar(self, s, a):
        ja = set(ln["id"] for ln in self.linhas)
        opcoes = []
        for sy in L.simbolos_para_extra(doc):
            it = L.item_extra(doc, sy)
            if L.chave_item(it) in ja:
                continue
            cat = sy.Category.Name if sy.Category else u""
            opcoes.append((u"{} · {} : {}".format(cat, sy.FamilyName, L.elem_name(sy)), it))
        if not opcoes:
            forms.alert(u"O projeto não tem outros símbolos (anotações genéricas) nem detail items para acrescentar.", title=TITULO)
            return
        nomes = [o[0] for o in opcoes]
        esc = forms.SelectFromList.show(nomes, title=u"Acrescentar à legenda: símbolos e detail items",
                                        multiselect=True, button_name=u"Acrescentar")
        if not esc:
            return
        por_nome = dict(opcoes)
        for n in esc:
            ln = self._criar_linha(por_nome[n])
            self.sujos.add(ln["id"])
        self._titulo_lista()
        self._mudou()

    def _retirar(self, ln):
        self.linhas.remove(ln)
        self.lista.Children.Remove(ln["borda"])
        self._titulo_lista()
        self._mudou()

    def _config_json(self):
        """Ajustes gravados na legenda ao Gerar: ordem, textos editados, desmarcados, extras."""
        import json
        rot, desm, extras = {}, [], []
        for ln in self.linhas:
            t = (ln["txt"].Text or u"").strip().upper()
            if ln["it"].get("extra"):
                extras.append({"uid": ln["it"]["sym"].UniqueId, "rotulo": t})
            elif t and t != (ln["padrao"] or u"").upper():
                rot[ln["id"]] = t
            if not ln["chk"].IsChecked:
                desm.append(ln["id"])
        return json.dumps({"ordem": self._ordem(), "rotulos": rot, "desmarcados": desm, "extras": extras})

    def _todos100(self, s, a):
        for ln in self.linhas:
            if ln["it"].get("extra"):
                continue
            ln["sl"].Value = 100
            self.sujos.add(ln["id"])

    def _escala(self, ln):
        v = int(round(ln["sl"].Value))
        ln["pct"].Text = u"{}%".format(v)
        b = self.base.get(ln["id"])
        if b:
            ln["med"].Text = u"{} × {} mm".format(_mm(b[0] * v / 100.0), _mm(b[1] * v / 100.0))
        # caixas na previa: desenhadas logo depois que o controle para de mexer (so a deste item)
        self.sujos.add(ln["id"])
        self.timer.Stop()
        self.timer.Start()
        self._pendente()

    def _mudou(self, *a):
        self.txtPasso.Text = _mm(self.slPasso.Value) + u" mm"
        self._pendente()

    def _pendente(self):
        if self.res is not None and self._ha_mudanca():
            self.txtStatus.Text = u"Há ajustes que ainda não estão na prévia (caixas vermelhas = tamanho novo): clique em Atualizar prévia."

    # ------------------------------------------------------------ cor dos símbolos
    def _cor_modelo(self):
        """Cor do modelo: uma só para a legenda (a mais usada nos elementos da disciplina)."""
        return L.cor_dominante([ln["it"] for ln in self.linhas] or self.itens)

    def _amostra(self):
        c = self._cor_modelo() if self.cor is None else ((0, 0, 0) if self.cor == "preto" else self.cor)
        self.swCor.Background = SolidColorBrush(Color.FromRgb(c[0], c[1], c[2]))

    def _modo_cor(self, s, a):
        if not self._mudando_cor:
            self._aplicar_modo(self.cbCor.SelectedIndex)

    def _voltar_modo(self, i):
        self._mudando_cor = True
        self.cbCor.SelectedIndex = i
        self._mudando_cor = False

    def _aplicar_modo(self, i, inicial=False):
        anterior = self.cor
        if i == 1:
            if self.cor_sistema is None:
                t = L.agora()
                try:
                    self.cor_sistema = L.cor_do_sistema(doc, CFG["disc"])
                except Exception as e:
                    self.cor_sistema = (None, u"não foi possível ler o sistema: {}".format(e))
                L.tempo(u"cor do projeto no Sistema FJA", t)
            rgb, txt = self.cor_sistema
            self.cbCor.ToolTip = (u"Cor de " if rgb else u"Usando a cor do modelo: ") + txt
            if rgb is None:
                if not inicial:
                    forms.alert(u"Não achei a cor no Sistema FJA: {}.\n\nOs símbolos ficam com a cor do modelo.".format(txt), title=TITULO)
                self.cor = None
                self._voltar_modo(0)
                i = 0
            else:
                self.cor = tuple(rgb)
        elif i == 2:
            if self.cor_pers is None and not inicial:
                if not self._escolher_cor(aplicar=False):
                    self._voltar_modo(3 if self.cor == "preto" else (1 if self.cor_sistema and self.cor_sistema[0] and self.cor == tuple(self.cor_sistema[0]) else 0))
                    return
            self.cor = tuple(self.cor_pers) if self.cor_pers else None
            self.cbCor.ToolTip = u"Cor escolhida: RGB {}-{}-{}".format(*self.cor) if self.cor else None
        elif i == 3:
            self.cor = "preto"
            self.cbCor.ToolTip = u"Símbolos em preto (sem sobreposição de cor)"
        else:
            self.cor = None
            c = self._cor_modelo()
            self.cbCor.ToolTip = u"A cor mais usada nos elementos {} do modelo (filtros, sobreposições, estilo de objeto): RGB {}-{}-{}".format(
                u"/".join(CFG["vistas"]), c[0], c[1], c[2])
        self._amostra()
        if not inicial:
            _gravar_cfg(modo_cor_v2=i)
            if self.cor != anterior:
                self._repintar()

    def _escolher_cor(self, aplicar=True):
        import clr
        clr.AddReference("System.Windows.Forms")
        clr.AddReference("System.Drawing")
        from System.Windows.Forms import ColorDialog, DialogResult
        from System.Drawing import Color as DColor
        dlg = ColorDialog()
        dlg.FullOpen = True
        c = self.cor if isinstance(self.cor, tuple) else (self.cor_pers or self._cor_modelo())
        if c:
            dlg.Color = DColor.FromArgb(c[0], c[1], c[2])
        if dlg.ShowDialog() != DialogResult.OK:
            return False
        self.cor_pers = [dlg.Color.R, dlg.Color.G, dlg.Color.B]
        _gravar_cfg(cor_personalizada=self.cor_pers)
        if aplicar:
            if self.cbCor.SelectedIndex != 2:
                self._voltar_modo(2)
                _gravar_cfg(modo_cor_v2=2)
            anterior = self.cor
            self.cor = tuple(self.cor_pers)
            self.cbCor.ToolTip = u"Cor escolhida: RGB {}-{}-{}".format(*self.cor)
            self._amostra()
            if self.cor != anterior:
                self._repintar()
        return True

    def _repintar(self):
        """Troca a cor dos símbolos na prévia na hora, sem refazer a legenda."""
        if self.res is None or self.tg is None:
            return
        view = self.res["view"]
        cor = self._cor_modelo() if self.cor is None else self.cor
        t = DB.Transaction(doc, u"Prévia: cor")
        t.Start()
        try:
            for it in self.res["items"]:
                for i in it.get("sym_ids") or []:
                    if doc.GetElement(i) is not None:
                        L.pintar(view, i, L.cor_item(it, cor))
            t.Commit()
            self.cor_feita = self.cor
        except Exception as e:
            if t.HasStarted() and not t.HasEnded():
                t.RollBack()
            self.txtStatus.Text = u"Não foi possível trocar a cor: {}".format(e)

    def _ha_mudanca(self):
        return (any(int(round(ln["sl"].Value)) != self.pct_feito.get(ln["id"], 100) for ln in self.linhas)
                or self._opcoes_tabela() != getattr(self, "tabela_feita", None)
                or self.cor != getattr(self, "cor_feita", None)
                or self._ordem() != getattr(self, "ordem_feita", None))

    def _largura_mm(self):
        try:
            v = float((self.txtLargura.Text or u"").strip().replace(",", "."))
            return min(max(v, 5.0), 120.0) * 10.0
        except Exception:
            return 175.0

    def _opcoes_tabela(self):
        return {"largura_mm": self._largura_mm(), "linhas_h": bool(self.chkLinhasH.IsChecked),
                "linha_v": bool(self.chkLinhaV.IsChecked), "margens": bool(self.chkMargens.IsChecked),
                "passo_mm": round(self.slPasso.Value, 1)}

    # ------------------------------------------------------------ caixas (rapido: so linhas na previa)
    def _tick(self, s, a):
        self.timer.Stop()
        self._desenhar_caixas()

    def _desenhar_caixas(self, todos=False):
        """Redesenha só as caixas dos itens que mudaram; as demais ficam como estão.
        Cada item alterado (escala diferente da prévia atual) mantém a caixa até o Atualizar."""
        if self.res is None or self.tg is None:
            return
        alvos = [ln for ln in self.linhas if todos or ln["id"] in self.sujos]
        self.sujos = set()
        if not alvos:
            return
        view = self.res["view"]
        t = DB.Transaction(doc, u"Prévia: caixas")
        t.Start()
        try:
            for ln in alvos:
                velhos = [i for i in self.ids_caixa.pop(ln["id"], []) if doc.GetElement(i) is not None]
                if velhos:
                    doc.Delete(List[DB.ElementId](velhos))
                cx = self.caixas.get(ln["id"])
                if cx is None or not ln["chk"].IsChecked:
                    continue
                v = int(round(ln["sl"].Value))
                feito = self.pct_feito.get(ln["id"], 100)
                if v == feito:
                    continue
                f = float(v) / feito
                x, y, w, h = cx
                hw, hh = max(w * f, 1e-3) / 2.0, max(h * f, 1e-3) / 2.0
                pts = [DB.XYZ(x - hw, y - hh, 0), DB.XYZ(x + hw, y - hh, 0), DB.XYZ(x + hw, y + hh, 0), DB.XYZ(x - hw, y + hh, 0)]
                ids = []
                for a, b in zip(pts, pts[1:] + pts[:1]):
                    try:
                        c = doc.Create.NewDetailCurve(view, DB.Line.CreateBound(a, b))
                        L.paint(view, c.Id, COR_CAIXA)
                        ids.append(c.Id)
                    except Exception:
                        pass
                self.ids_caixa[ln["id"]] = ids
            t.Commit()
        except Exception as e:
            if t.HasStarted() and not t.HasEnded():
                t.RollBack()
            self.txtStatus.Text = u"Não foi possível desenhar as caixas: {}".format(e)

    def _apagar_caixas(self):
        todos = [i for ids in self.ids_caixa.values() for i in ids]
        self.ids_caixa = {}
        if not todos:
            return
        t = DB.Transaction(doc, u"Prévia: caixas")
        t.Start()
        vivos = [i for i in todos if doc.GetElement(i) is not None]
        if vivos:
            doc.Delete(List[DB.ElementId](vivos))
        t.Commit()

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
        self.ids_caixa = {}
        self.sujos = set()
        if self.tg is not None:
            try:
                if self.tg.HasStarted() and not self.tg.HasEnded():
                    self.tg.RollBack()
            except Exception:
                pass
            self.tg = None

    def atualizar(self):
        t_at = L.agora()
        if getattr(self, "aberta", False):
            del L.TEMPOS[:]
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
            if v != 100 and b and not it.get("extra"):
                fk = it["sym"].Family.Id.ToString()
                tamanhos.setdefault(fk, max(b) * v / 100.0)
        if not escolhidos:
            self.res = None
            self.txtStatus.Text = u"Nenhum item marcado."
            return
        self.tg = DB.TransactionGroup(doc, u"Prévia " + CFG["botao"])
        self.tg.Start()
        try:
            op = self._opcoes_tabela()
            op["tamanhos"] = tamanhos
            op["cor"] = self.cor
            op["config"] = self._config_json()
            res = L.atualizar_legenda(doc, escolhidos, op)
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
        porid = dict((L.chave_item(it), it) for it in res["items"])
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
                    "componente": u"componente de legenda", "extra": u"símbolo acrescentado"}.get(it.get("modo"), it.get("modo") or u"")
            ln["med"].ToolTip = u"Símbolo: {}{}".format(modo, u" · redimensionado pelo DWG" if it.get("redimensionado") else u"")
        self.pct_feito = pcts
        self.tabela_feita = self._opcoes_tabela()
        self.cor_feita = self.cor
        self.ordem_feita = self._ordem()
        try:
            t_p = L.agora()
            self.prev = PreviewControl(doc, view.Id)
            self.hostPrev.Children.Add(self.prev)
            self.txtPrevVazia.Visibility = Visibility.Collapsed
            L.tempo(u"abrir a prévia do Revit", t_p)
        except Exception as e:
            self.txtStatus.Text = u"Legenda gerada, mas a prévia não abriu: {}".format(e)
            return
        partes = [u"{} itens".format(len(res["items"]))]
        if res.get("cols") and res["cols"][1]:
            partes.append(u"2 colunas ({} + {})".format(res["cols"][0], res["cols"][1]))
        n_red = len([1 for it in res["items"] if it.get("redimensionado")])
        if n_red:
            partes.append(u"{} símbolo(s) redimensionado(s)".format(n_red))
        L.tempo(u"PRÉVIA COMPLETA", t_at)
        primeira = not getattr(self, "aberta", False)
        if primeira:
            L.tempo(u"TOTAL DA ABERTURA (desde o clique no botão)", 0.0 - _T0.Elapsed.TotalSeconds + L.agora())
        self.aberta = True
        detalhe = _log_tempos(u"abertura" if primeira else u"atualizar")
        seg = (_T0.Elapsed.TotalSeconds if primeira else L.agora() - t_at)
        partes.append(u"{} em {:.1f} s".format(u"aberta" if primeira else u"atualizada", seg).replace(".", ","))
        self.txtStatus.Text = u"Prévia: " + u" · ".join(partes) + u". Use a roda do mouse para aproximar."
        self.txtStatus.ToolTip = u"Tempo de cada etapa:\n" + detalhe
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
    del L.TEMPOS[:]
    t = L.agora()
    L.tempo(u"carregar o script e as bibliotecas (pyRevit)", t - _T0.Elapsed.TotalSeconds)
    t = L.agora()
    try:
        itens = L.collect_items(doc)
        for it in itens:
            it["padrao"] = it["label"]
        itens, desmarcados = L.aplicar_config(doc, itens)
        L.tempo(u"LER OS ITENS DO MODELO ({})".format(len(itens)), t)
    except Exception as e:
        forms.alert(u"Erro ao ler os itens: {}".format(e), title=TITULO)
        return
    if not itens:
        forms.alert(u"Nenhuma família '(LOG)' posicionada no modelo.", title=TITULO)
        return
    t = L.agora()
    j = Janela(itens, desmarcados)
    L.tempo(u"montar a janela", t)
    j.ShowDialog()
    if j.gravado and j.res is not None:
        try:
            uidoc.ActiveView = j.res["view"]
        except Exception:
            pass


if __name__ == "__main__":
    main()
