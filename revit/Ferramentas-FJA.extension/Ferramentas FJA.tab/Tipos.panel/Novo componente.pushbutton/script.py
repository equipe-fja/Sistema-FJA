# -*- coding: utf-8 -*-
"""Carrega uma família (.rfa) no modelo ligada a um código da biblioteca central do Sistema FJA
(elemento/família): Keynote, Type Mark, Descrição e vínculo em todos os tipos; opcionalmente
renomeia a família como (SIGLA) DESCRIÇÃO."""
__persistentengine__ = True
import os

from pyrevit import revit, DB, forms, script

import fja_sistema as F

from System.Windows import Visibility
from Autodesk.Revit.UI.Selection import ObjectType
from System.Windows.Controls import ListBoxItem

doc = revit.doc
uidoc = revit.uidoc
cfg = script.get_config()
TITULO = u"Componentes"

def _pincel(r, g, b):
    from System.Windows.Media import SolidColorBrush, Color
    return SolidColorBrush(Color.FromRgb(r, g, b))


F_COR = {"cinza": _pincel(94, 94, 92), "verde": _pincel(46, 115, 64), "ambar": _pincel(138, 93, 5),
         "vermelho": _pincel(179, 38, 30)}


def aplicar_estilo(win):
    try:
        from System.Windows.Markup import XamlReader
        from System.IO import File
        caminho = os.path.join(os.path.dirname(F.__file__), "fja_estilo.xaml")
        win.Resources.MergedDictionaries.Add(XamlReader.Parse(File.ReadAllText(caminho)))
    except Exception as e:
        print(u"estilo: {}".format(e))


class Janela(forms.WPFWindow):
    def __init__(self, elemento=None):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
        self.pedir_selecao = False
        aplicar_estilo(self)
        self.dados = None
        self.fam = None
        self.lista = []
        self.plano = None
        self._nome_editado = False
        self._pronto = False
        tem_oficial = F._mais_recente(F.PASTA_REVIT, "biblioteca") is not None
        self.cbFonte.SelectedIndex = 1 if cfg.get_option("dev", not tem_oficial) else 0
        self._pronto = True
        self.carregar()
        # readequar: elemento escolhido em "Selecionar no modelo" ou já selecionado ao abrir a ferramenta
        try:
            if elemento is None:
                ids = list(uidoc.Selection.GetElementIds())
                elemento = doc.GetElement(ids[0]) if ids else None
            if elemento is not None:
                self._usar_elemento(elemento, avisar=True)
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
        self.txtFonte.Text = u"{} · publicado em {} · {} elementos/famílias na biblioteca".format(
            u"Desenvolvimento (REVIT\\DEV)" if dev else u"Oficial", (d.geradoEm or u"")[:16].replace("T", " "),
            len(F.elementos(d)))
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
        self._pronto = True
        self._usar_projeto(d.projetos[sel - 1] if sel else None)

    def _usar_projeto(self, proj):
        try:
            self.dados.carregar_projeto(proj)
        except Exception as e:
            forms.alert(u"{}".format(e), title=TITULO)
            self.dados.carregar_projeto(None)
        self.preencher()

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

    # ------------------------------------------------------------ família
    def escolher(self, sender, args):
        caminho = forms.pick_file(file_ext="rfa", title=u"Escolha a família (.rfa)")
        if not caminho:
            return
        try:
            self.fam = F.ler_familia(doc.Application, caminho)
        except Exception as e:
            forms.alert(u"Não foi possível ler a família:\n{}".format(e), title=TITULO)
            return
        f = self.fam
        self.spAbr.Visibility = Visibility.Collapsed
        self.ckRenomear.IsEnabled = True
        self._mostrar_vinculo()
        self.tbArquivo.Text = caminho
        self.txtFamilia.Text = u"Família {} · categoria {} · {} tipo(s): {}".format(
            f["nome"], f["categoria"] or u"?", len(f["tipos"]), u", ".join(f["tipos"][:8]) + (u"…" if len(f["tipos"]) > 8 else u""))
        self.preencher()

    def selecionar(self, sender, args):
        # A seleção no modelo precisa acontecer com a janela fechada: esconder uma janela modal encerra o
        # contexto da API do Revit (e as transações seguintes falham). Fecha, seleciona e reabre.
        self.pedir_selecao = True
        self.Close()

    def _usar_elemento(self, el, avisar=True):
        try:
            f = F.familia_do_elemento(doc, el)
        except Exception as e:
            if avisar:
                forms.alert(u"{}".format(e), title=TITULO)
            return
        self.fam = f
        self.tbArquivo.Text = u"No modelo: {}".format(f["nome"])
        n = sum(f["instancias"].values())
        self.txtFamilia.Text = u"Readequar família {} · categoria {} · {} tipo(s), {} instância(s) no modelo".format(
            f["nome"], f["categoria"] or u"?", len(f["tipos"]), n)
        self.rbFamilia.Content = u"a família inteira ({} tipos)".format(len(f["tipos"]))
        self.rbTipo.Content = u"só o tipo \"{}\" ({} instância(s))".format(f["tipoAtual"], f["instancias"].get(f["tipoAtual"], 0))
        self.rbFamilia.IsChecked = True
        self.spAbr.Visibility = Visibility.Visible if len(f["tipos"]) > 1 else Visibility.Collapsed
        self._mostrar_vinculo()
        self.rbExistente.IsChecked = True
        self.preencher()
        # já vem marcado o código atual, se houver
        iid = (f.get("vinculo") or {}).get("itemCentral")
        for k, it in enumerate(self.lista):
            if iid and it["id"] == iid:
                self.lbItens.SelectedIndex = k
                self.lbItens.ScrollIntoView(self.lbItens.SelectedItem)
                break

    def _mostrar_vinculo(self):
        f = self.fam or {}
        if not f.get("modelo"):
            self.txtVinculo.Text = u""
            return
        v = f.get("vinculo") or {}
        it = self.dados.itens.get(v.get("itemCentral")) if self.dados and v.get("itemCentral") else None
        if not v.get("itemCentral") and not v.get("pedido"):
            self.txtVinculo.Text = u"Sem vínculo com o sistema" + (u" (keynote atual: {})".format(f["knAtual"]) if f["knAtual"] else u"")
            self.txtVinculo.Foreground = F_COR["cinza"]
            return
        ln = self.dados.linha_projeto(v["itemCentral"]) if self.dados and v.get("itemCentral") else None
        kn_ok = (ln or {}).get("kn") or u""
        if it is None:
            self.txtVinculo.Text = u"Vinculada a {} (aguardando o sistema ou fora desta biblioteca)".format(v.get("fja") or u"?")
            self.txtVinculo.Foreground = F_COR["ambar"]
        elif kn_ok and kn_ok != f["knAtual"]:
            self.txtVinculo.Text = u"Vínculo divergente: {} · keynote no modelo {} · no sistema {}".format(it.get("fja"), f["knAtual"] or u"—", kn_ok)
            self.txtVinculo.Foreground = F_COR["vermelho"]
        else:
            self.txtVinculo.Text = u"Já vinculada a {} · {} · keynote {}".format(it.get("fja"), it.get("desc"), f["knAtual"] or u"—")
            self.txtVinculo.Foreground = F_COR["verde"]

    def abr_mudou(self, sender, args):
        if not self._pronto:
            return
        so = bool(self.rbTipo.IsChecked)
        self.ckRenomear.IsChecked = not so
        self.ckRenomear.IsEnabled = not so
        self.tbNomeFam.IsEnabled = not so
        self.atualizar()

    # ------------------------------------------------------------ código
    def modo_mudou(self, sender, args):
        if not self._pronto:
            return
        novo = bool(self.rbNovo.IsChecked)
        self.spExistente.Visibility = Visibility.Collapsed if novo else Visibility.Visible
        self.gNovo.Visibility = Visibility.Visible if novo else Visibility.Collapsed
        self._nome_editado = False
        self.atualizar()

    def busca_mudou(self, sender, args):
        if self._pronto:
            self.preencher()

    def preencher(self):
        self.lbItens.Items.Clear()
        self.lista = []
        d = self.dados
        if d is None:
            self.atualizar()
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
            mesma = 0 if cat and F._norm(it.get("rvCat")) == cat else 1
            res.append((mesma, 0 if ln else 1, it.get("sigla") or u"", it.get("fja") or u"", it, ln))
        res.sort(key=lambda x: x[:4])
        for mesma, _, _, _, it, ln in res[:400]:
            li = ListBoxItem()
            li.Content = u"{}   {}   {}{}{}".format(it.get("fja") or u"", ln.get("kn") or u"—", ln.get("desc") or it.get("desc") or u"",
                                                  u"   · " + it["rvCat"] if it.get("rvCat") else u"",
                                                  u"   (aguardando sistema)" if it.get("pendente") else u"")
            li.Tag = it["id"]
            self.lbItens.Items.Add(li)
            self.lista.append(it)
        self.txtRodape.Text = u"{} de {} elementos/famílias{}".format(
            len(self.lista), len(itens), u" · mesma categoria primeiro" if cat else u"")
        self.atualizar()

    def item_mudou(self, sender, args):
        self._nome_editado = False
        self.atualizar()

    def novo_mudou(self, sender, args):
        if self._pronto:
            self._nome_editado = False
            self.atualizar()

    def renomear_mudou(self, sender, args):
        self.tbNomeFam.IsEnabled = bool(self.ckRenomear.IsChecked)
        self.atualizar()

    def nome_mudou(self, sender, args):
        if self._pronto and self.tbNomeFam.IsFocused:
            self._nome_editado = True
            self.atualizar(manter_nome=True)

    # ------------------------------------------------------------ plano
    def _plano(self):
        d, f = self.dados, self.fam
        if d is None or f is None:
            return None
        ren = bool(self.ckRenomear.IsChecked) and self.ckRenomear.IsEnabled
        nome = self.tbNomeFam.Text if self._nome_editado else None
        if self.rbNovo.IsChecked:
            i = self.cbSigla.SelectedIndex
            form = {"sigla": self.siglas[i]["sigla"] if 0 <= i < len(self.siglas) else u"",
                    "desc": self.tbDesc.Text.strip(), "texto": self.tbTexto.Text.strip()}
            p = F.plano_componente_novo(doc, d, f, form, ren, nome)
        else:
            i = self.lbItens.SelectedIndex
            if i < 0 or i >= len(self.lista):
                return None
            p = F.plano_componente(doc, d, self.lista[i], f, ren, nome)
        p["soTipo"] = bool(f.get("modelo") and self.spAbr.Visibility == Visibility.Visible and self.rbTipo.IsChecked)
        return p

    def atualizar(self, manter_nome=False):
        p = self.plano = self._plano()
        if p is None:
            self.txtPrevia.Text = u"Escolha o arquivo da família e o código." if self.fam is None else u"Escolha o código."
            self.txtAvisos.Text = self.txtProblemas.Text = u""
            self.btInserir.IsEnabled = False
            return
        if not manter_nome:
            self._pronto = False
            self.tbNomeFam.Text = p["nomeFamilia"]
            self._pronto = True
        fam_nome = p["nomeFamilia"] if p["renomear"] else self.fam["nome"]
        n = 1 if p.get("soTipo") else len(self.fam["tipos"])
        self.txtPrevia.Text = u"{} \"{}\" · {} tipo(s) com Keynote e Type Mark {} · {} · descrição \"{}\"".format(
            u"Readequar família" if self.fam.get("modelo") else u"Família", fam_nome, n, p["kn"] or u"(sem keynote)",
            p["item"].get("fja") or u"", (p["desc"] or u"").upper())
        self.txtAvisos.Text = u"\n".join(u"• " + a for a in p["avisos"])
        self.txtProblemas.Text = u"\n".join(u"• " + a for a in p["problemas"])
        self.btInserir.IsEnabled = not p["problemas"]

    # ------------------------------------------------------------ ações
    def inserir(self, sender, args):
        p = self.plano
        if p is None or p["problemas"]:
            return
        f = self.fam
        atual = (f.get("vinculo") or {}).get("itemCentral") if f.get("modelo") else None
        if atual and atual != p["item"]["id"]:
            ant = self.dados.itens.get(atual) or {}
            if not forms.alert(u"Esta família já está vinculada a {} ({}).\n\nTrocar para {} ({})?".format(
                    ant.get("fja") or (f["vinculo"].get("fja") or u"?"), ant.get("desc") or u"",
                    p["item"].get("fja") or u"novo código", (p["desc"] or u"").upper()),
                    title=TITULO, yes=True, no=True):
                return
        try:
            with revit.Transaction(u"Componentes: {}".format(p["nomeFamilia"] if p["renomear"] else self.fam["nome"])):
                familia, avisos = F.inserir_componente(doc, self.dados, p)
                nome = familia.Name
        except Exception as e:
            forms.alert(u"Não foi possível inserir a família:\n{}".format(e), title=TITULO)
            return
        msg = u"Família no modelo: {}\nKeynote {} em {} tipo(s).".format(nome, p["kn"] or u"—", p.get("nTipos") or 0)
        if p.get("novo") or p.get("reservas"):
            msg += u"\n\nPedido enviado ao sistema: o código e o keynote ficam definitivos quando alguém abrir o sistema."
        if avisos:
            msg += u"\n\n" + u"\n".join(u"• " + a for a in avisos)
        forms.alert(msg, title=TITULO)
        self.fam = None
        self.tbArquivo.Text = u""
        self.spAbr.Visibility = Visibility.Collapsed
        self.ckRenomear.IsEnabled = True
        self.txtVinculo.Text = u""
        self.txtFamilia.Text = u"Pronto. Escolha outro arquivo ou selecione outro elemento para continuar."
        self.preencher()

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
        try:
            ref = uidoc.Selection.PickObject(ObjectType.Element, u"Clique num elemento da família a readequar (Esc cancela)")
            elemento = doc.GetElement(ref.ElementId)
        except Exception:
            pass  # Esc: reabre sem elemento
