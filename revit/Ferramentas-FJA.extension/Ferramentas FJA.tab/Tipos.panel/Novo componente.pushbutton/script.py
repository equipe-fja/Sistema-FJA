# -*- coding: utf-8 -*-
"""Carrega uma família (.rfa) no modelo ligada a um código da biblioteca central do Sistema FJA
(elemento/família): Keynote, Type Mark, Descrição e vínculo em todos os tipos; opcionalmente
renomeia a família como (SIGLA) DESCRIÇÃO."""
__persistentengine__ = True
import os

from pyrevit import revit, DB, forms, script

import fja_sistema as F

from System.Windows import Visibility
from System.Windows.Controls import ListBoxItem

doc = revit.doc
cfg = script.get_config()
TITULO = u"Novo componente"


def aplicar_estilo(win):
    try:
        from System.Windows.Markup import XamlReader
        from System.IO import File
        caminho = os.path.join(os.path.dirname(F.__file__), "fja_estilo.xaml")
        win.Resources.MergedDictionaries.Add(XamlReader.Parse(File.ReadAllText(caminho)))
    except Exception as e:
        print(u"estilo: {}".format(e))


class Janela(forms.WPFWindow):
    def __init__(self):
        forms.WPFWindow.__init__(self, os.path.join(os.path.dirname(__file__), "janela.xaml"))
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
        self.tbArquivo.Text = caminho
        self.txtFamilia.Text = u"Família {} · categoria {} · {} tipo(s): {}".format(
            f["nome"], f["categoria"] or u"?", len(f["tipos"]), u", ".join(f["tipos"][:8]) + (u"…" if len(f["tipos"]) > 8 else u""))
        self.preencher()

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
        ren = bool(self.ckRenomear.IsChecked)
        nome = self.tbNomeFam.Text if self._nome_editado else None
        if self.rbNovo.IsChecked:
            i = self.cbSigla.SelectedIndex
            form = {"sigla": self.siglas[i]["sigla"] if 0 <= i < len(self.siglas) else u"",
                    "desc": self.tbDesc.Text.strip(), "texto": self.tbTexto.Text.strip()}
            return F.plano_componente_novo(doc, d, f, form, ren, nome)
        i = self.lbItens.SelectedIndex
        if i < 0 or i >= len(self.lista):
            return None
        return F.plano_componente(doc, d, self.lista[i], f, ren, nome)

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
        self.txtPrevia.Text = u"Família \"{}\" · {} tipo(s) com Keynote e Type Mark {} · {} · descrição \"{}\"".format(
            fam_nome, len(self.fam["tipos"]), p["kn"] or u"(sem keynote)", p["item"].get("fja") or u"", (p["desc"] or u"").upper())
        self.txtAvisos.Text = u"\n".join(u"• " + a for a in p["avisos"])
        self.txtProblemas.Text = u"\n".join(u"• " + a for a in p["problemas"])
        self.btInserir.IsEnabled = not p["problemas"]

    # ------------------------------------------------------------ ações
    def inserir(self, sender, args):
        p = self.plano
        if p is None or p["problemas"]:
            return
        try:
            with revit.Transaction(u"Novo componente: {}".format(p["nomeFamilia"] if p["renomear"] else self.fam["nome"])):
                familia, avisos = F.inserir_componente(doc, self.dados, p)
                nome = familia.Name
        except Exception as e:
            forms.alert(u"Não foi possível inserir a família:\n{}".format(e), title=TITULO)
            return
        msg = u"Família no modelo: {}\nKeynote {} em {} tipo(s).".format(nome, p["kn"] or u"—", len(list(familia.GetFamilySymbolIds())))
        if p.get("novo") or p.get("reservas"):
            msg += u"\n\nPedido enviado ao sistema: o código e o keynote ficam definitivos quando alguém abrir o sistema."
        if avisos:
            msg += u"\n\n" + u"\n".join(u"• " + a for a in avisos)
        forms.alert(msg, title=TITULO)
        self.fam = None
        self.tbArquivo.Text = u""
        self.txtFamilia.Text = u"Família inserida. Escolha outro arquivo para continuar."
        self.preencher()

    def fechar(self, sender, args):
        self.Close()


if doc is None or doc.IsFamilyDocument:
    forms.alert(u"Abra um modelo de projeto para usar esta ferramenta.", title=TITULO)
else:
    Janela().ShowDialog()
