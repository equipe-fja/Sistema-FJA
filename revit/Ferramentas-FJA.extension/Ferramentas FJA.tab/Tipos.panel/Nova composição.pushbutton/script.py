# -*- coding: utf-8 -*-
"""Cria no modelo tipos de parede, piso, forro e cobertura a partir das composições da
biblioteca central do Sistema FJA (especificação técnica), com camadas, materiais,
Type Mark, Keynote, Descrição e vínculo com o sistema."""
__persistentengine__ = True  # evita a limpeza lenta do motor do pyRevit ao fechar a janela (~7 s medidos)
import os

from pyrevit import revit, DB, forms, script

import fja_sistema as F

from System.Windows import Thickness, GridLength, GridUnitType, TextWrapping, FontWeights, VerticalAlignment, \
    HorizontalAlignment, CornerRadius as System_CornerRadius
from System.Windows.Controls import Grid, ColumnDefinition, TextBlock, StackPanel, Border, ListBoxItem, Orientation, \
    ComboBox, ComboBoxItem, TextBox, Button
from System.Windows.Media import SolidColorBrush, Color

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()

COR_FN = {"Finish1": (207, 224, 238), "Finish2": (230, 216, 239), "Substrate": (228, 223, 213),
          "Structure": (199, 193, 182), "Insulation": (243, 227, 168), "Membrane": (74, 74, 72)}
COLS = [("FJ", 100), ("Keynote", 70), (u"Descrição", -1), ("mm", 54), (u"Selos", 190)]


def aplicar_estilo(win):
    """Aplica a estética do Sistema FJA (lib/fja_estilo.xaml) à janela."""
    try:
        from System.Windows.Markup import XamlReader
        from System.IO import File
        caminho = os.path.join(os.path.dirname(F.__file__), "fja_estilo.xaml")
        win.Resources.MergedDictionaries.Add(XamlReader.Parse(File.ReadAllText(caminho)))
    except Exception as e:
        print(u"estilo: {}".format(e))


def pincel(rgb):
    return SolidColorBrush(Color.FromRgb(rgb[0], rgb[1], rgb[2]))


CINZA = pincel((94, 94, 92))
CLARO = pincel((143, 143, 140))
VERDE = pincel((46, 115, 64))
VERMELHO = pincel((179, 38, 30))
AMBAR = pincel((138, 93, 5))


def texto(t, cor=None, peso=None, tam=None, quebra=False, margem=None):
    tb = TextBlock()
    tb.Text = t or u""
    if cor is not None:
        tb.Foreground = cor
    if peso is not None:
        tb.FontWeight = peso
    if tam:
        tb.FontSize = tam
    if quebra:
        tb.TextWrapping = TextWrapping.Wrap
    else:
        tb.TextTrimming = System_TextTrimming()
    if margem:
        tb.Margin = Thickness(*margem)
    tb.VerticalAlignment = VerticalAlignment.Center
    return tb


def System_TextTrimming():
    from System.Windows import TextTrimming
    return TextTrimming.CharacterEllipsis


def linha_grade(valores, peso=None, cor=None):
    g = Grid()
    for i, (_, w) in enumerate(COLS):
        cd = ColumnDefinition()
        cd.Width = GridLength(1, GridUnitType.Star) if w < 0 else GridLength(w)
        g.ColumnDefinitions.Add(cd)
    for i, v in enumerate(valores):
        tb = v if not isinstance(v, (str, type(u""), type(None), int, float)) else texto(v, cor=cor, peso=peso, margem=(0, 0, 6, 0))
        Grid.SetColumn(tb, i)
        g.Children.Add(tb)
    return g


FN_OPC = [(u"Acabamento 1", "Finish1"), (u"Acabamento 2", "Finish2"), (u"Substrato", "Substrate"),
          (u"Estrutura", "Structure"), (u"Isolamento", "Insulation"), (u"Membrana", "Membrane")]


def num(s):
    try:
        return float(u"{}".format(s or u"").strip().replace(",", "."))
    except Exception:
        return None


class FormNova(forms.WPFWindow):
    """Formulário de composição (mesmos campos do sistema). modo: 'novo', 'editar' ou 'duplicar'."""

    def __init__(self, dados, cat=None, item=None, modo="novo"):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "formulario.xaml"))
        aplicar_estilo(self)
        self.dados = dados
        self.modo = modo
        self.item = item
        self.resultado = None
        self.linhas = []
        self._pronto = False
        self.siglas = sorted([s for s in dados.bib.get("siglas", []) if s.get("tipo") == "sistema"],
                             key=lambda s: s["sigla"])
        for s in self.siglas:
            self.cbSigla.Items.Add(u"{} · {}".format(s["sigla"], s.get("nome") or u""))
        self.mats = sorted([x for x in dados.bib.get("itens", []) if x.get("tipo") == "material"],
                           key=lambda x: (x.get("sigla") or u"", x.get("fja") or u""))
        proj = (dados.projeto or {}).get("codigo", u"?")
        if modo == "editar":
            self.Title = u"Editar composição — Ferramentas FJA"
            self.txtTitulo.Text = u"Editar {} · {}".format(item.get("fja"), item.get("desc") or u"")
            self.txtSub.Text = u"Projeto {} · as alterações vão para a biblioteca central (o sistema aplica quando alguém o abrir) " \
                               u"e o tipo do modelo é atualizado agora.".format(proj)
            self.btOk.Content = u"Salvar e atualizar o tipo"
        else:
            self.txtSub.Text = u"Projeto {} · o item é criado no sistema (biblioteca central e especificação do projeto) " \
                               u"e o tipo é criado no modelo agora mesmo.".format(proj)
            if modo == "duplicar":
                self.txtTitulo.Text = u"Nova composição a partir de {}".format(item.get("fja"))
        self._cat_ini = cat
        self._pronto = True
        if item:
            self._preencher(item)
        else:
            if self.siglas:
                self.cbSigla.SelectedIndex = 0
            self.adicionar(None, None)

    def _preencher(self, it):
        sig = [s["sigla"] for s in self.siglas]
        if it.get("sigla") in sig:
            self.cbSigla.SelectedIndex = sig.index(it["sigla"])
        if self.modo == "editar":
            self.cbSigla.IsEnabled = False  # trocar a sigla mudaria o keynote: crie uma composição nova
        cats = ["Parede", "Piso", "Forro", "Cobertura"]
        if it.get("rvCat") in cats:
            self.cbCat.SelectedIndex = cats.index(it["rvCat"])
        if it.get("papel") in (u"Vedação", u"Acabamento"):
            self.cbPapel.SelectedIndex = [u"Vedação", u"Acabamento"].index(it["papel"])
        linha = self.dados.linha_projeto(it["id"]) or {}
        desc = linha.get("desc") or it.get("desc") or u""
        self.tbDesc.Text = desc + (u" (cópia)" if self.modo == "duplicar" else u"")
        self.tbTexto.Text = linha.get("texto") or it.get("texto") or u""
        self.linhas = [{"funcaoRevit": c.get("funcaoRevit") or "Structure", "material": c.get("material") or u"",
                        "esp": F.fmt_mm(c.get("esp"))} for c in it.get("camadas", [])]
        if not self.linhas:
            self.linhas.append({"funcaoRevit": "Structure", "material": u"", "esp": u""})
        self.desenhar()

    def _rot_mat(self, m):
        kn = (self.dados.linha_projeto(m["id"]) or {}).get("kn")
        return u"{} · {}".format(kn or m.get("fja"), m.get("desc") or u"")

    def _sigla(self):
        i = self.cbSigla.SelectedIndex
        return self.siglas[i] if 0 <= i < len(self.siglas) else None

    def sigla_mudou(self, sender, args):
        s = self._sigla()
        if s is None:
            return
        cats = ["Parede", "Piso", "Forro", "Cobertura"]
        cat = s.get("rvCat") or self._cat_ini
        if cat in cats:
            self.cbCat.SelectedIndex = cats.index(cat)
        if s.get("papel") in (u"Vedação", u"Acabamento"):
            self.cbPapel.SelectedIndex = [u"Vedação", u"Acabamento"].index(s["papel"])
        self.atualizar(None, None)

    # ------------------------------------------------------- camadas
    def adicionar(self, sender, args):
        self.linhas.append({"funcaoRevit": "Structure" if not self.linhas else "Finish1", "material": u"", "esp": u""})
        self.desenhar()

    def _mover(self, i, d):
        j = i + d
        if 0 <= j < len(self.linhas):
            self.linhas[i], self.linhas[j] = self.linhas[j], self.linhas[i]
            self.desenhar()

    def _remover(self, i):
        del self.linhas[i]
        self.desenhar()

    def _soltar(self, de, para):
        if de == para or not (0 <= de < len(self.linhas)):
            return
        ln = self.linhas.pop(de)
        self.linhas.insert(min(para, len(self.linhas)), ln)
        self.desenhar()

    # ---------------------------------------------- arrastar pelo grip (sem DragDrop do WPF)
    def _alvo(self, y):
        """Índice da posição de inserção para a coordenada y (relativa a spCamadas)."""
        from System.Windows import Point
        filhos = list(self.spCamadas.Children)
        for k, ch in enumerate(filhos):
            topo = ch.TranslatePoint(Point(0, 0), self.spCamadas).Y
            if y < topo + ch.ActualHeight / 2.0:
                return k
        return len(filhos)

    def _marcar(self, de, alvo):
        filhos = list(self.spCamadas.Children)
        for k, ch in enumerate(filhos):
            ch.Opacity = 0.45 if k == de else 1.0
            # linha vermelha indicando onde a camada vai entrar
            ch.Margin = Thickness(0, 3 if k == alvo else 0, 0, 3 if (alvo == len(filhos) and k == len(filhos) - 1) else 6)
            ch.Background = None
        if 0 <= alvo < len(filhos):
            filhos[alvo].Background = self._faixa_topo
        elif filhos and alvo == len(filhos):
            filhos[-1].Background = self._faixa_base

    def _ligar_arraste(self, g, alca, i):
        from System.Windows.Input import Cursors, Mouse
        from System.Windows.Media import LinearGradientBrush, GradientStop, GradientStopCollection, Color
        from System.Windows import Point
        if not hasattr(self, "_faixa_topo"):
            def faixa(ini, fim):
                v = Color.FromRgb(142, 27, 27)
                t = Color.FromArgb(0, 255, 255, 255)
                st = GradientStopCollection()
                for c, o in ((t, 0.0), (t, ini), (v, ini), (v, fim), (t, fim), (t, 1.0)):
                    st.Add(GradientStop(c, o))
                return LinearGradientBrush(st, Point(0, 0), Point(0, 1))
            self._faixa_topo = faixa(0.0, 0.07)
            self._faixa_base = faixa(0.93, 1.0)
        alca.Cursor = Cursors.SizeNS
        alca.ToolTip = u"Arraste para cima ou para baixo para mudar a ordem"
        estado = {"ativo": False, "alvo": i}

        def desce(s, e):
            estado["ativo"] = True
            estado["alvo"] = i
            alca.CaptureMouse()
            self._marcar(i, i)
            e.Handled = True

        def move(s, e):
            if not estado["ativo"]:
                return
            y = e.GetPosition(self.spCamadas).Y
            a = self._alvo(y)
            if a != estado["alvo"]:
                estado["alvo"] = a
                self._marcar(i, a)

        def sobe(s, e):
            if not estado["ativo"]:
                return
            estado["ativo"] = False
            alca.ReleaseMouseCapture()
            a = estado["alvo"]
            para = a - 1 if a > i else a  # ajusta pela remoção da própria camada
            if para != i:
                ln = self.linhas.pop(i)
                self.linhas.insert(para, ln)
            self.desenhar()

        def perdeu(s, e):
            if estado["ativo"]:
                estado["ativo"] = False
                self.desenhar()

        alca.MouseLeftButtonDown += desce
        alca.MouseMove += move
        alca.MouseLeftButtonUp += sobe
        alca.LostMouseCapture += perdeu

    def _duplicar(self, i):
        self.linhas.insert(i + 1, dict(self.linhas[i]))
        self.desenhar()

    # ------------------------------------------------------- material com busca
    def _combo_material(self, i, ln):
        cm = ComboBox()
        cm.Margin = Thickness(0, 0, 8, 0)
        cm.IsEditable = True
        cm.IsTextSearchEnabled = False
        cm.StaysOpenOnEdit = True
        cm.MaxDropDownHeight = 320
        estado = {"lista": list(self.mats), "mudando": False}

        def carregar(lista):
            estado["mudando"] = True
            cm.Items.Clear()
            for m in lista:
                cm.Items.Add(self._rot_mat(m))
            estado["lista"] = lista
            estado["mudando"] = False

        carregar(self.mats)
        atual = [m for m in self.mats if m["id"] == ln["material"]]
        if atual:
            estado["mudando"] = True
            cm.SelectedIndex = self.mats.index(atual[0])
            estado["mudando"] = False

        def escolheu(s, e):
            if estado["mudando"] or cm.SelectedIndex < 0:
                return
            m = estado["lista"][cm.SelectedIndex]
            self._set(i, "material", m["id"])

        def digitou(s, e):
            k = u"{}".format(e.Key)
            if k in ("Up", "Down", "Enter", "Return", "Tab", "Escape", "Left", "Right"):
                return
            txt = cm.Text or u""
            q = F._norm(txt)
            lista = [m for m in self.mats if not q or q in F._norm(self._rot_mat(m) + u" " + (m.get("fja") or u""))]
            carregar(lista)
            estado["mudando"] = True
            cm.Text = txt
            estado["mudando"] = False
            try:
                tb = cm.Template.FindName("PART_EditableTextBox", cm)
                tb.CaretIndex = len(txt)
            except Exception:
                pass
            cm.IsDropDownOpen = True

        def saiu(s, e):
            # ao sair sem escolher, volta a mostrar o material escolhido (ou limpa)
            mid = self.linhas[i]["material"] if i < len(self.linhas) else u""
            m = [x for x in self.mats if x["id"] == mid]
            if (cm.Text or u"") != (self._rot_mat(m[0]) if m else u""):
                carregar(self.mats)
                estado["mudando"] = True
                cm.SelectedIndex = self.mats.index(m[0]) if m else -1
                if not m:
                    cm.Text = u""
                estado["mudando"] = False

        cm.SelectionChanged += escolheu
        cm.KeyUp += digitou
        cm.LostKeyboardFocus += saiu
        return cm

    def desenhar(self):
        sp = self.spCamadas
        sp.Children.Clear()
        for i, ln in enumerate(self.linhas):
            g = Grid()
            g.Margin = Thickness(0, 0, 0, 6)
            for w in (20, 140, -1, 80, 76):
                cd = ColumnDefinition()
                cd.Width = GridLength(1, GridUnitType.Star) if w < 0 else GridLength(w)
                g.ColumnDefinitions.Add(cd)
            q = Border()
            q.Width = 8
            q.Margin = Thickness(2, 1, 0, 1)
            q.CornerRadius = System_CornerRadius(4)
            q.Background = pincel(COR_FN.get(ln["funcaoRevit"], (220, 220, 220)))
            q.BorderBrush = pincel((150, 150, 146))
            q.BorderThickness = Thickness(1)
            q.HorizontalAlignment = HorizontalAlignment.Left
            q.VerticalAlignment = VerticalAlignment.Stretch
            Grid.SetColumn(q, 0)
            g.Children.Add(q)
            self._ligar_arraste(g, q, i)
            cf = ComboBox()
            cf.Margin = Thickness(0, 0, 8, 0)
            for rot, cod in FN_OPC:
                cf.Items.Add(rot)
            cf.SelectedIndex = [c for _, c in FN_OPC].index(ln["funcaoRevit"])
            cf.SelectionChanged += (lambda s, e, i=i: (self._set(i, "funcaoRevit", FN_OPC[s.SelectedIndex][1]), self.desenhar()))
            Grid.SetColumn(cf, 1)
            g.Children.Add(cf)
            cm = self._combo_material(i, ln)
            Grid.SetColumn(cm, 2)
            g.Children.Add(cm)
            te = TextBox()
            te.Text = ln["esp"]
            te.ToolTip = u"Espessura em mm"
            te.TextChanged += (lambda s, e, i=i: self._set(i, "esp", s.Text))
            Grid.SetColumn(te, 3)
            g.Children.Add(te)
            bts = StackPanel()
            bts.Orientation = Orientation.Horizontal
            bts.Margin = Thickness(8, 0, 0, 0)
            for rot, fn, dica in ((u"\u29C9", lambda s, e, i=i: self._duplicar(i), u"Duplicar camada"),
                                  (u"\u2715", lambda s, e, i=i: self._remover(i), u"Remover")):
                b = Button()
                b.Content = rot
                b.ToolTip = dica
                b.Click += fn
                try:
                    b.Style = self.FindResource("Discreto")
                except Exception:
                    b.Width = 28
                bts.Children.Add(b)
            Grid.SetColumn(bts, 4)
            g.Children.Add(bts)
            sp.Children.Add(g)
        self.atualizar(None, None)

    def _set(self, i, k, v):
        if 0 <= i < len(self.linhas):
            self.linhas[i][k] = v
            self.atualizar(None, None)

    # ------------------------------------------------------- prévia e confirmação
    def _form(self):
        s = self._sigla()
        return {"sigla": s["sigla"] if s else u"", "rvCat": ["Parede", "Piso", "Forro", "Cobertura"][max(0, self.cbCat.SelectedIndex)],
                "papel": [u"Vedação", u"Acabamento"][max(0, self.cbPapel.SelectedIndex)],
                "desc": (self.tbDesc.Text or u"").strip(), "texto": (self.tbTexto.Text or u"").strip(),
                "camadas": [{"funcaoRevit": ln["funcaoRevit"], "material": ln["material"], "esp": num(ln["esp"]) or 0}
                            for ln in self.linhas]}

    def atualizar(self, sender, args):
        if not self._pronto:
            return
        f = self._form()
        papel = f["papel"]
        self.txtOrdem.Text = u"da face acabada para a vedação" if papel == u"Acabamento" else \
            (u"de fora para dentro" if f["rvCat"] == "Parede" else u"de cima para baixo")
        total = sum(c["esp"] for c in f["camadas"])
        self.txtTotal.Text = u"Total: {} mm".format(F.fmt_mm(total))
        d = self.dados
        if not f["sigla"]:
            self.txtPrevia.Text = u""
            return
        if self.modo == "editar":
            kn = (d.linha_projeto(self.item["id"]) or {}).get("kn") or self.item.get("fja")
            self.txtPrevia.Text = u"Tipo no modelo: \"{}\" · {} mm".format(F.nome_tipo(kn, f["desc"] or u"…"), F.fmt_mm(total))
            return
        kn = d.prox_kn(f["sigla"])
        self.txtPrevia.Text = u"Será criado como {} na biblioteca e {} no projeto {} → tipo \"{}\"".format(
            d.prox_fja(f["sigla"]), kn, (d.projeto or {}).get("codigo", u"?"), F.nome_tipo(kn, f["desc"] or u"…"))

    def confirmar(self, sender, args):
        f = self._form()
        erros = []
        if not f["sigla"]:
            erros.append(u"Escolha a sigla.")
        if not f["desc"]:
            erros.append(u"Informe a descrição.")
        if not f["camadas"]:
            erros.append(u"Adicione pelo menos uma camada.")
        for k, c in enumerate(f["camadas"]):
            if not c["material"]:
                erros.append(u"Camada {}: escolha o material.".format(k + 1))
            if c["funcaoRevit"] != "Membrane" and not c["esp"] > 0:
                erros.append(u"Camada {}: informe a espessura.".format(k + 1))
        if erros:
            forms.alert(u"\n".join(erros), title=u"Nova composição")
            return
        self.resultado = f
        self.Close()

    def cancelar(self, sender, args):
        self.Close()


class Janela(forms.WPFWindow):
    def __init__(self):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        aplicar_estilo(self)
        self.dados = None
        self.item_atual = None
        self.plano = None
        self.vinc = {}
        self._pronto = False
        self.gCab.Children.Add(linha_grade([c for c, _ in COLS], peso=FontWeights.SemiBold, cor=CINZA))
        tem_oficial = F._mais_recente(F.PASTA_REVIT, "biblioteca") is not None
        self.cbFonte.SelectedIndex = 1 if cfg.get_option("dev", not tem_oficial) else 0
        self.cbCategoria.SelectedIndex = self._categoria_da_selecao()
        self.cbPapel.SelectedIndex = 0
        self._pronto = True
        self.carregar()

    # ---------------------------------------------------------------- dados
    def _categoria_da_selecao(self):
        try:
            ids = list(uidoc.Selection.GetElementIds())
            if ids:
                e = doc.GetElement(ids[0])
                if isinstance(e, DB.Wall):
                    return 1
                if isinstance(e, DB.Floor):
                    return 2
                if isinstance(e, DB.Ceiling):
                    return 3
                if isinstance(e, DB.RoofBase):
                    return 4
        except Exception:
            pass
        return 0

    def carregar(self):
        dev = self.cbFonte.SelectedIndex == 1
        try:
            self.dados = F.Dados(dev=dev)
        except Exception as e:
            self.dados = None
            self.txtFonte.Text = u"{}".format(e)
            self.lbItens.Items.Clear()
            self.mostrar(None)
            return
        d = self.dados
        self.txtFonte.Text = u"{} · publicado em {} · {} composições".format(
            u"Desenvolvimento (REVIT\\DEV)" if dev else u"Oficial", (d.geradoEm or u"")[:16].replace("T", " "),
            len(d.composicoes()))
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
            forms.alert(u"{}".format(e), title=u"Nova composição")
            self.dados.carregar_projeto(None)
        self.vinc = F.tipos_vinculados(doc)
        self.preencher()

    # ---------------------------------------------------------------- lista
    # selos da lista: podem aparecer juntos
    SELOS = {"projeto": (u"Projeto", (237, 237, 235), (94, 94, 92)),
             "tipo": (u"Tipo", (228, 241, 230), (46, 115, 64)),
             "camadas": (u"Sem camadas", (251, 231, 229), (179, 38, 30)),
             "categoria": (u"Sem categoria", (251, 231, 229), (179, 38, 30)),
             "aguardando": (u"Aguardando", (248, 239, 217), (138, 93, 5))}

    DICAS = {"projeto": u"Já está na especificação técnica deste projeto, com keynote.",
             "tipo": u"Já existe um tipo criado a partir desta composição no modelo aberto.",
             "camadas": u"A composição não tem camadas: não pode virar tipo até ser corrigida.",
             "categoria": u"Falta definir a categoria (parede, piso, forro ou cobertura): não pode virar tipo até ser corrigida.",
             "aguardando": u"Criada ou alterada pelo Revit; o sistema confirma os códigos quando alguém o abrir."}

    def _selos(self, it, linha):
        r = []
        if it.get("pendente"):
            r.append("aguardando")
        if linha:
            r.append("projeto")
        if it["id"] in self.vinc or (it.get("pedido") and u"pedido:" + it["pedido"] in self.vinc):
            r.append("tipo")
        if not it.get("camadas"):
            r.append("camadas")
        if not it.get("rvCat"):
            r.append("categoria")
        return r

    def _painel_selos(self, chaves):
        from System.Windows.Controls import WrapPanel
        wp = WrapPanel()
        wp.VerticalAlignment = VerticalAlignment.Center
        for k in chaves:
            rot, fundo, cor = self.SELOS[k]
            b = Border()
            b.CornerRadius = System_CornerRadius(9)
            b.Background = pincel(fundo)
            b.Padding = Thickness(7, 1, 7, 1)
            b.Margin = Thickness(0, 1, 4, 1)
            b.Child = texto(rot, cor=pincel(cor), tam=11)
            b.ToolTip = self.DICAS[k]
            wp.Children.Add(b)
        return wp

    def preencher(self, manter=None):
        self.lbItens.Items.Clear()
        d = self.dados
        if d is None:
            return
        cat = [None, "Parede", "Piso", "Forro", "Cobertura"][max(0, self.cbCategoria.SelectedIndex)]
        papel = [None, u"Vedação", u"Acabamento"][max(0, self.cbPapel.SelectedIndex)]
        busca = F._norm(self.tbBusca.Text)
        so_proj = bool(self.ckProjeto.IsChecked)
        lst = []
        for it in d.composicoes() + d.itens_pendentes():
            if cat and it.get("rvCat") != cat:
                continue
            if papel and it.get("papel") != papel:
                continue
            linha = d.linha_projeto(it["id"])
            if so_proj and not linha:
                continue
            if busca:
                alvo = [it.get("fja"), it.get("sigla"), it.get("desc"), (linha or {}).get("kn"), (linha or {}).get("desc")]
                alvo += [(d.itens.get(c.get("material")) or {}).get("desc") for c in it.get("camadas", [])]
                if busca not in F._norm(u" ".join(u"{}".format(a or u"") for a in alvo)):
                    continue
            lst.append((it.get("sigla") or u"", (linha or {}).get("kn") or u"~", it.get("fja") or u"", it, linha))
        lst.sort(key=lambda x: (x[0], x[1], x[2]))
        escolhido = None
        for _, _, _, it, linha in lst:
            li = ListBoxItem()
            li.Tag = it["id"]
            li.Padding = Thickness(4, 3, 4, 3)
            li.Content = linha_grade([it.get("fja"), (linha or {}).get("kn") or u"—",
                                      (linha or {}).get("desc") or it.get("desc"),
                                      F.fmt_mm(it.get("espTotal")), self._painel_selos(self._selos(it, linha))])
            li.ToolTip = u"{} · {} · {}".format(it.get("rvCat") or u"sem categoria", it.get("papel") or u"sem papel",
                                                  it.get("desc"))
            self.lbItens.Items.Add(li)
            if manter and it["id"] == manter:
                escolhido = li
        pend = len(d.pendentes)
        self.txtRodape.Text = u"{} de {} composições{}".format(len(lst), len(d.composicoes()),
                                                               u" · {} pedido(s) aguardando o sistema".format(pend) if pend else u"")
        if escolhido is not None:
            self.lbItens.SelectedItem = escolhido
        else:
            self.mostrar(None)

    # ---------------------------------------------------------------- detalhe
    def mostrar(self, item):
        sp = self.spDetalhe
        sp.Children.Clear()
        self.plano = None
        self.btCriar.IsEnabled = False
        self.btCriar.Content = u"Criar no modelo"
        self.item_atual = item
        self.btEditar.IsEnabled = bool(item) and not item.get("pendente") and bool(self.dados and self.dados.projeto)
        self.btDuplicar.IsEnabled = bool(item) and bool(self.dados and self.dados.projeto)
        if item is None:
            sp.Children.Add(texto(u"Escolha uma composição na lista.", cor=CLARO))
            return
        d = self.dados
        p = F.plano_tipo(doc, d, item)
        self.plano = p
        sp.Children.Add(texto(p["nome"], peso=FontWeights.SemiBold, tam=14, quebra=True, margem=(0, 0, 0, 2)))
        sp.Children.Add(texto(u"{} · {} · {} · {} mm".format(item.get("fja"), item.get("rvCat") or u"sem categoria",
                                                              item.get("papel") or u"sem papel",
                                                              F.fmt_mm(item.get("espTotal"))),
                              cor=CINZA, quebra=True, margem=(0, 0, 0, 10)))
        cam = item.get("camadas", [])
        total = sum(float(c.get("esp") or 0) for c in cam) or 1.0
        if cam:
            barra = Grid()
            barra.Height = 30
            barra.Margin = Thickness(0, 0, 0, 4)
            for i, c in enumerate(cam):
                cd = ColumnDefinition()
                peso = float(c.get("esp") or 0) / total * 100.0
                cd.Width = GridLength(max(peso, 1.5), GridUnitType.Star)
                barra.ColumnDefinitions.Add(cd)
                b = Border()
                b.Background = pincel(COR_FN.get(c.get("funcaoRevit"), (220, 220, 220)))
                b.BorderBrush = pincel((150, 150, 145))
                b.BorderThickness = Thickness(0.5)
                b.ToolTip = u"{} · {} mm".format(c.get("funcao"), F.fmt_mm(c.get("esp")))
                Grid.SetColumn(b, i)
                barra.Children.Add(b)
            sp.Children.Add(barra)
            orient = u"da face acabada para a vedação" if item.get("papel") == u"Acabamento" else \
                (u"de fora para dentro" if item.get("rvCat") == "Parede" else u"de cima para baixo")
            sp.Children.Add(texto(u"Camadas " + orient, cor=CLARO, margem=(0, 0, 0, 6)))
            planos = dict((pm["id"], pm) for pm in p["materiais"])
            for c in cam:
                pm = planos.get(c.get("material")) or {}
                lin = StackPanel()
                lin.Orientation = Orientation.Horizontal
                lin.Margin = Thickness(0, 1, 0, 1)
                q = Border()
                q.Width = 10
                q.Height = 10
                q.Margin = Thickness(0, 0, 6, 0)
                q.Background = pincel(COR_FN.get(c.get("funcaoRevit"), (220, 220, 220)))
                lin.Children.Add(q)
                t1 = texto(c.get("funcao"), cor=CINZA)
                t1.Width = 92
                lin.Children.Add(t1)
                t2 = texto(pm.get("kn") or (pm.get("item") or {}).get("fja") or u"?", peso=FontWeights.SemiBold)
                t2.Width = 64
                lin.Children.Add(t2)
                t3 = texto(pm.get("desc") or u"", quebra=False)
                t3.Width = 250
                t3.ToolTip = pm.get("nome")
                lin.Children.Add(t3)
                lin.Children.Add(texto(F.fmt_mm(c.get("esp")) + u" mm", cor=CINZA))
                sp.Children.Add(lin)
            novos = [pm for pm in p["materiais"] if pm["existente"] is None]
            if novos:
                sp.Children.Add(texto(u"Materiais a criar: " + u", ".join(pm["nome"] for pm in novos),
                                      cor=CINZA, quebra=True, margem=(0, 8, 0, 0)))
        if p["existente"] is not None:
            sp.Children.Add(texto(u"Já existe no modelo: {}. Criar de novo atualiza camadas, materiais e parâmetros.".format(
                F.nome_elem(p["existente"])), cor=VERDE, quebra=True, margem=(0, 8, 0, 0)))
            self.btCriar.Content = u"Atualizar tipo no modelo"
        for x in p["problemas"]:
            sp.Children.Add(texto(x, cor=VERMELHO, quebra=True, margem=(0, 8, 0, 0)))
        for x in p["avisos"]:
            sp.Children.Add(texto(x, cor=AMBAR, quebra=True, margem=(0, 6, 0, 0)))
        txt = (d.linha_projeto(item["id"]) or {}).get("texto") or item.get("texto")
        if txt:
            sp.Children.Add(texto(u"Especificação", peso=FontWeights.SemiBold, margem=(0, 12, 0, 2)))
            sp.Children.Add(texto(txt, cor=CINZA, quebra=True))
        self.btCriar.IsEnabled = not p["problemas"]

    # ---------------------------------------------------------------- eventos
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

    def filtro_mudou(self, sender, args):
        if self._pronto:
            self.preencher()

    def _item(self, iid):
        it = self.dados.itens.get(iid)
        if it is None:
            for x in self.dados.itens_pendentes():
                if x["id"] == iid:
                    return x
        return it

    def item_mudou(self, sender, args):
        li = self.lbItens.SelectedItem
        self.mostrar(self._item(li.Tag) if li is not None else None)

    def _abrir_form(self, item=None, modo="novo"):
        d = self.dados
        if d is None or not d.projeto:
            forms.alert(u"Escolha o projeto (no alto da janela) antes.", title=u"Nova composição")
            return None
        cat = [None, "Parede", "Piso", "Forro", "Cobertura"][max(0, self.cbCategoria.SelectedIndex)]
        f = FormNova(d, cat, item=item, modo=modo)
        try:
            f.Owner = self
        except Exception:
            pass
        f.ShowDialog()
        return f.resultado

    def editar(self, sender, args):
        it = self.item_atual
        if not it or it.get("pendente"):
            return
        r = self._abrir_form(it, "editar")
        if not r:
            return
        p = F.plano_editar(doc, self.dados, it, r)
        if p["problemas"]:
            forms.alert(u"\n".join(p["problemas"]), title=u"Editar composição")
            return
        self._executar(p)
        # mostra já a versão editada enquanto o sistema não republica
        self.dados.itens[it["id"]] = p["item"]
        self.preencher(manter=it["id"])

    def duplicar(self, sender, args):
        it = self.item_atual
        if not it:
            return
        r = self._abrir_form(it, "duplicar")
        if not r:
            return
        p = F.plano_novo(doc, self.dados, r)
        if p["problemas"]:
            forms.alert(u"\n".join(p["problemas"]), title=u"Nova composição")
            return
        self._executar(p)

    def nova(self, sender, args):
        d = self.dados
        if d is None or not d.projeto:
            forms.alert(u"Escolha o projeto (no alto da janela) antes de criar uma composição nova.",
                        title=u"Nova composição")
            return
        cat = [None, "Parede", "Piso", "Forro", "Cobertura"][max(0, self.cbCategoria.SelectedIndex)]
        f = FormNova(d, cat)
        try:
            f.Owner = self
        except Exception:
            pass
        f.ShowDialog()
        if not f.resultado:
            return
        p = F.plano_novo(doc, d, f.resultado)
        if p["problemas"]:
            forms.alert(u"\n".join(p["problemas"]), title=u"Nova composição")
            return
        self._executar(p)

    def fechar(self, sender, args):
        self.Close()

    def criar(self, sender, args):
        p = self.plano
        if p is None or p["problemas"]:
            return
        self._executar(p)

    def _executar(self, p):
        item_id = p["item"]["id"]
        try:
            with revit.Transaction(u"Nova composição: {}".format(p["nome"])):
                tipo, avisos = F.criar_tipo(doc, self.dados, p)
                nome = F.nome_elem(tipo)
        except Exception as e:
            forms.alert(u"Não foi possível criar o tipo:\n{}".format(e), title=u"Nova composição")
            return
        msg = u"Tipo pronto no modelo:\n{}".format(nome)
        if p.get("editar"):
            msg += u"\n\nAlteração enviada ao sistema: a biblioteca central e a especificação do projeto são " \
                   u"atualizadas quando alguém abrir o sistema."
        elif p.get("novo") or p.get("reservas"):
            msg += u"\n\nPedido enviado ao sistema. Os códigos reservados ficam definitivos quando alguém abrir " \
                   u"o sistema; se algum mudar, use Atualizar nesta janela."
        if avisos:
            msg += u"\n\n" + u"\n".join(u"• " + a for a in avisos)
        forms.alert(msg, title=u"Nova composição")
        self.vinc = F.tipos_vinculados(doc)
        self.preencher(manter=item_id)


if doc is None or doc.IsFamilyDocument:
    forms.alert(u"Abra um modelo de projeto para usar esta ferramenta.", title=u"Nova composição")
else:
    Janela().ShowDialog()
