# -*- coding: utf-8 -*-
"""Cria no modelo tipos de parede, piso, forro e cobertura a partir das composições da
biblioteca central do Sistema FJA (especificação técnica), com camadas, materiais,
Type Mark, Keynote, Descrição e vínculo com o sistema."""
import os

from pyrevit import revit, DB, forms, script

import fja_sistema as F

from System.Windows import Thickness, GridLength, GridUnitType, TextWrapping, FontWeights, VerticalAlignment
from System.Windows.Controls import Grid, ColumnDefinition, TextBlock, StackPanel, Border, ListBoxItem, Orientation, \
    ComboBox, ComboBoxItem, TextBox, Button
from System.Windows.Media import SolidColorBrush, Color

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()

COR_FN = {"Finish1": (207, 224, 238), "Finish2": (230, 216, 239), "Substrate": (228, 223, 213),
          "Structure": (199, 193, 182), "Insulation": (243, 227, 168), "Membrane": (74, 74, 72)}
COLS = [("FJ", 72), ("Keynote", 70), (u"Descrição", -1), ("mm", 54), (u"Situação", 120)]


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
        tb = v if isinstance(v, TextBlock) else texto(v, cor=cor, peso=peso, margem=(0, 0, 6, 0))
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
    """Formulário de composição nova (mesmos campos do sistema)."""

    def __init__(self, dados, cat=None):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "formulario.xaml"))
        self.dados = dados
        self.resultado = None
        self.linhas = []
        self._pronto = False
        self.siglas = sorted([s for s in dados.bib.get("siglas", []) if s.get("tipo") == "sistema"],
                             key=lambda s: s["sigla"])
        for s in self.siglas:
            self.cbSigla.Items.Add(u"{} · {}".format(s["sigla"], s.get("nome") or u""))
        self.mats = sorted([x for x in dados.bib.get("itens", []) if x.get("tipo") == "material"],
                           key=lambda x: (x.get("sigla") or u"", x.get("fja") or u""))
        self.txtSub.Text = u"Projeto {} · o item é criado no sistema (biblioteca central e especificação do projeto) " \
                           u"e o tipo é criado no modelo agora mesmo.".format(
                               (dados.projeto or {}).get("codigo", u"?"))
        self._cat_ini = cat
        self._pronto = True
        if self.siglas:
            self.cbSigla.SelectedIndex = 0
        self.adicionar(None, None)

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

    def desenhar(self):
        sp = self.spCamadas
        sp.Children.Clear()
        for i, ln in enumerate(self.linhas):
            g = Grid()
            g.Margin = Thickness(0, 0, 0, 4)
            for w in (24, 130, -1, 70, 96):
                cd = ColumnDefinition()
                cd.Width = GridLength(1, GridUnitType.Star) if w < 0 else GridLength(w)
                g.ColumnDefinitions.Add(cd)
            n = texto(u"{}".format(i + 1), cor=CLARO)
            Grid.SetColumn(n, 0)
            g.Children.Add(n)
            cf = ComboBox()
            cf.Margin = Thickness(0, 0, 6, 0)
            for rot, cod in FN_OPC:
                cf.Items.Add(rot)
            cf.SelectedIndex = [c for _, c in FN_OPC].index(ln["funcaoRevit"])
            cf.SelectionChanged += (lambda s, e, i=i: self._set(i, "funcaoRevit", FN_OPC[s.SelectedIndex][1]))
            Grid.SetColumn(cf, 1)
            g.Children.Add(cf)
            cm = ComboBox()
            cm.Margin = Thickness(0, 0, 6, 0)
            cm.IsTextSearchEnabled = True
            sel = -1
            for k, m in enumerate(self.mats):
                cm.Items.Add(self._rot_mat(m))
                if m["id"] == ln["material"]:
                    sel = k
            cm.SelectedIndex = sel
            cm.SelectionChanged += (lambda s, e, i=i: self._set(i, "material", self.mats[s.SelectedIndex]["id"]
                                                                if s.SelectedIndex >= 0 else u""))
            Grid.SetColumn(cm, 2)
            g.Children.Add(cm)
            te = TextBox()
            te.Text = ln["esp"]
            te.Height = 23
            te.ToolTip = u"Espessura em mm"
            te.TextChanged += (lambda s, e, i=i: self._set(i, "esp", s.Text))
            Grid.SetColumn(te, 3)
            g.Children.Add(te)
            bts = StackPanel()
            bts.Orientation = Orientation.Horizontal
            bts.Margin = Thickness(6, 0, 0, 0)
            for rot, fn, dica in ((u"\u2191", lambda s, e, i=i: self._mover(i, -1), u"Subir"),
                                  (u"\u2193", lambda s, e, i=i: self._mover(i, 1), u"Descer"),
                                  (u"\u2715", lambda s, e, i=i: self._remover(i), u"Remover")):
                b = Button()
                b.Content = rot
                b.Width = 26
                b.Margin = Thickness(0, 0, 3, 0)
                b.ToolTip = dica
                b.Click += fn
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
        kn = d.prox_kn(f["sigla"])
        self.txtPrevia.Text = u"Será criado como {} na biblioteca e {} no projeto {} → tipo \"{}\"".format(
            d.prox_fja(), kn, (d.projeto or {}).get("codigo", u"?"), F.nome_tipo(kn, f["desc"] or u"…"))

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
        self.dados = None
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
    def _situacao(self, it):
        if it["id"] in self.vinc or (it.get("pedido") and u"pedido:" + it["pedido"] in self.vinc):
            return u"no modelo", VERDE
        if it.get("pendente"):
            return u"aguardando sistema", AMBAR
        if not it.get("camadas"):
            return u"sem camadas", VERMELHO
        if not it.get("rvCat"):
            return u"sem categoria", VERMELHO
        if self.dados.projeto and not self.dados.linha_projeto(it["id"]):
            return u"entra no projeto", CINZA
        return u"pronto", CINZA

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
            sit, cor = self._situacao(it)
            li = ListBoxItem()
            li.Tag = it["id"]
            li.Padding = Thickness(4, 3, 4, 3)
            li.Content = linha_grade([it.get("fja"), (linha or {}).get("kn") or u"—",
                                      (linha or {}).get("desc") or it.get("desc"),
                                      F.fmt_mm(it.get("espTotal")), texto(sit, cor=cor)])
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
        if p.get("novo") or p.get("reservas"):
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
