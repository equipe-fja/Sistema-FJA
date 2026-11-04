# -*- coding: utf-8 -*-
"""Integração Revit × Sistema FJA: leitura dos dados publicados no G: e criação de
tipos de composição (paredes, pisos, forros, coberturas) e materiais.

Os dados vêm de G:\\Drives compartilhados\\BI-BIBLIOTECA\\SISTEMA FJA\\REVIT
(desenvolvimento: REVIT\\DEV), publicados pelo sistema: biblioteca.json e
projeto-<código>.json. Ver docs/revit-tipos-sincronizados.md no repositório.

Compatível com IronPython 2.7/3.4 (pyRevit). Sem dependência do pyRevit: as
funções recebem o documento, para poderem ser testadas fora dele.
"""
import io
import json
import os
import re
import time

import System
from Autodesk.Revit import DB
from Autodesk.Revit.DB.ExtensibleStorage import Schema, SchemaBuilder, Entity, AccessLevel
from System.Collections.Generic import List

PASTA_REVIT = u"G:\\Drives compartilhados\\BI-BIBLIOTECA\\SISTEMA FJA\\REVIT"
PASTA_DEV = os.path.join(PASTA_REVIT, u"DEV")

MM_POR_PE = 304.8

FUNCOES = {
    "Finish1": DB.MaterialFunctionAssignment.Finish1,
    "Finish2": DB.MaterialFunctionAssignment.Finish2,
    "Substrate": DB.MaterialFunctionAssignment.Substrate,
    "Structure": DB.MaterialFunctionAssignment.Structure,
    "Insulation": DB.MaterialFunctionAssignment.Insulation,
    "Membrane": DB.MaterialFunctionAssignment.Membrane,
}

# cor de sombreamento dos materiais criados sem material-base, por sigla de 2 letras
CORES_SIGLA = {
    "GE": (236, 236, 230), "PI": (245, 245, 240), "BL": (175, 175, 170), "EM": (205, 198, 186),
    "IS": (240, 220, 140), "PO": (210, 210, 205), "CC": (190, 190, 185), "CA": (160, 160, 155),
    "MD": (190, 150, 110), "GN": (200, 200, 200), "VN": (215, 205, 190), "PC": (185, 185, 180),
}
COR_PADRAO = (200, 200, 200)


# ------------------------------------------------------------------ dados publicados
def _mais_recente(pasta, prefixo):
    """'biblioteca.json', 'biblioteca (3).json'… → o mais recente (o Drive para
    Desktop renomeia quando o anterior ainda está na pasta)."""
    if not os.path.isdir(pasta):
        return None
    achados = []
    rx = re.compile(r"^" + re.escape(prefixo) + r"( \(\d+\))?\.json$", re.I)
    for n in os.listdir(pasta):
        if rx.match(n):
            p = os.path.join(pasta, n)
            achados.append((os.path.getmtime(p), p))
    if not achados:
        return None
    achados.sort()
    return achados[-1][1]


def _norm(s):
    import unicodedata
    s = unicodedata.normalize("NFKD", u"{}".format(s or u""))
    s = u"".join(c for c in s if not unicodedata.combining(c)).upper()
    return re.sub(r"[^A-Z0-9]+", u" ", s).strip()


def _ler_json(caminho):
    with io.open(caminho, "r", encoding="utf-8") as f:
        return json.load(f)


class Dados(object):
    """Biblioteca central + especificação de um projeto, como publicados pelo sistema."""

    def __init__(self, dev=False):
        self.dev = dev
        self.pasta = PASTA_DEV if dev else PASTA_REVIT
        arq = _mais_recente(self.pasta, "biblioteca")
        if not arq:
            raise IOError(u"Não encontrei biblioteca.json em {}".format(self.pasta))
        self.arquivo = arq
        self.bib = _ler_json(arq)
        self.geradoEm = self.bib.get("geradoEm", "")
        self.itens = dict((x["id"], x) for x in self.bib.get("itens", []))
        self.siglas = dict((s["sigla"], s) for s in self.bib.get("siglas", []))
        self.projetos = self.bib.get("projetos", [])
        self.projeto = None
        self.espec = []          # itens da especificação do projeto
        self.porItem = {}        # id do item central → linha da especificação
        self.pendentes = []      # pedidos gravados pelo Revit e ainda não processados pelo sistema
        self.pastaPedidos = os.path.join(self.pasta, u"pedidos")

    def composicoes(self):
        return [x for x in self.bib.get("itens", []) if x.get("tipo") == "sistema"]

    def projeto_do_documento(self, titulo):
        """'131-MO-0001-R00' → projeto de código 131; senão, projeto cujo nome
        aparece no título do arquivo ('FJA-PROJETO TESTE' → 'Projeto Teste')."""
        m = re.match(r"^\s*([A-Za-z0-9]+)[-_ ]", titulo or "")
        cod = m.group(1) if m else ""
        for p in self.projetos:
            if cod and p.get("codigo") == cod:
                return p
        tit = _norm(titulo)
        for p in self.projetos:
            n = _norm(p.get("nome"))
            if len(n) >= 5 and n in tit:
                return p
        return None

    def carregar_projeto(self, proj):
        self.projeto = proj
        self.espec, self.porItem = [], {}
        self.pendentes = ler_pendentes(self.pastaPedidos)
        if not proj:
            return
        nome = os.path.splitext(proj.get("arquivo") or ("projeto-" + proj["codigo"] + ".json"))[0]
        arq = _mais_recente(self.pasta, nome)
        if not arq:
            raise IOError(u"Não encontrei {}.json em {}".format(nome, self.pasta))
        dados = _ler_json(arq)
        self.espec = dados.get("itens", [])
        for e in self.espec:
            if e.get("item"):
                self.porItem.setdefault(e["item"], e)

    def linha_projeto(self, item_id):
        """Linha da especificação do projeto; se ainda não existe mas há pedido pendente que
        reservou keynote para o item (ou material), devolve uma linha provisória."""
        ln = self.porItem.get(item_id)
        if ln or not self.projeto:
            return ln
        for p in self.pendentes:
            if (p.get("projeto") or {}).get("id") != self.projeto["id"]:
                continue
            r = p.get("reservas") or {}
            if r.get("kn") and ((p.get("item") or {}).get("id") == item_id or u"pedido:" + p.get("id", u"") == item_id):
                return {"id": u"", "kn": r["kn"], "pendente": p.get("id")}
            kn = (r.get("materiais") or {}).get(item_id)
            if kn:
                return {"id": u"", "kn": kn, "pendente": p.get("id")}
        return None

    def itens_pendentes(self):
        """Composições criadas no Revit (pedidos 'novo-item') que o sistema ainda não processou."""
        res = []
        ids = set(x.get("pedido") for x in self.bib.get("itens", []) if x.get("pedido"))
        for p in self.pendentes:
            if p.get("acao") == "novo-item" and p.get("id") not in ids:
                res.append(item_do_pedido(p))
        return res

    # ---------------------------------------------------------- reservas
    def prox_fja(self, sigla):
        """Próximo código FJ.<SIGLA>.NNNN (numeração própria de cada sigla)."""
        sg = (sigla or u"").upper()
        rx = re.compile(r"^FJ\.%s\.(\d+)$" % re.escape(sg))
        n = 0
        for x in self.bib.get("itens", []):
            m = rx.match(x.get("fja") or "")
            if m:
                n = max(n, int(m.group(1)))
        for p in self.pendentes:
            m = rx.match((p.get("reservas") or {}).get("fja") or "")
            if m:
                n = max(n, int(m.group(1)))
        return u"FJ.%s.%04d" % (sg, n + 1)

    def prox_kn(self, sigla, tambem=()):
        """Próximo keynote livre da sigla no projeto, contando a especificação publicada,
        as reservas dos pedidos pendentes e as já feitas neste mesmo pedido (tambem)."""
        sigla = (sigla or u"").upper()
        usados = set(u"{}".format(e.get("kn") or u"").upper() for e in self.espec)
        for p in self.pendentes:
            r = p.get("reservas") or {}
            usados.add(u"{}".format(r.get("kn") or u"").upper())
            for k in (r.get("materiais") or {}).values():
                usados.add(u"{}".format(k).upper())
        usados.update(u"{}".format(k).upper() for k in tambem)
        n = 0
        for k in usados:
            m = re.match(r"^\.?" + re.escape(sigla) + r"\.(\d+)$", k)
            if m:
                n = max(n, int(m.group(1)))
        while True:
            n += 1
            k = u"%s.%02d" % (sigla, n)
            if k not in usados:
                return k


# ------------------------------------------------------------------ pedidos ao sistema
def ler_pendentes(pasta):
    res = []
    if not os.path.isdir(pasta):
        return res
    for n in sorted(os.listdir(pasta)):
        if n.lower().endswith(".json"):
            try:
                res.append(_ler_json(os.path.join(pasta, n)))
            except Exception:
                pass
    return res


def autor():
    return os.environ.get("USERNAME") or os.environ.get("USER") or u"?"


def novo_id_pedido():
    import random
    return u"{}-{}-{:04d}".format(time.strftime("%Y%m%d-%H%M%S"), re.sub(r"[^A-Za-z0-9]", "", autor())[:12] or "rv",
                                  random.randint(0, 9999))


def gravar_pedido(dados, pedido):
    if not os.path.isdir(dados.pastaPedidos):
        os.makedirs(dados.pastaPedidos)
    arq = os.path.join(dados.pastaPedidos, u"pedido-{}.json".format(pedido["id"]))
    txt = json.dumps(pedido, ensure_ascii=False, indent=1)
    if not isinstance(txt, type(u"")):
        txt = txt.decode("utf-8")
    with io.open(arq, "w", encoding="utf-8") as f:
        f.write(txt)
    dados.pendentes.append(pedido)
    return arq


def item_do_pedido(p):
    """Item provisório (formato do biblioteca.json) a partir de um pedido 'novo-item'."""
    i = p.get("item") or {}
    cam = []
    for c in i.get("camadas", []):
        fn = c.get("funcao") or "Structure"
        cam.append({"funcao": FUNCOES_PT.get(fn, fn), "funcaoRevit": fn, "material": c.get("material"),
                    "esp": float(c.get("esp") or 0)})
    return {"id": u"pedido:" + p["id"], "pedido": p["id"], "pendente": True,
            "fja": (p.get("reservas") or {}).get("fja") or u"", "sigla": i.get("sigla") or u"", "tipo": "sistema",
            "desc": i.get("desc") or u"", "texto": i.get("texto") or u"", "rvCat": i.get("rvCat") or u"",
            "papel": i.get("papel") or u"", "camadas": cam, "espTotal": sum(c["esp"] for c in cam)}


FUNCOES_PT = {"Finish1": u"Acabamento 1", "Finish2": u"Acabamento 2", "Substrate": u"Substrato",
              "Structure": u"Estrutura", "Insulation": u"Isolamento", "Membrane": u"Membrana"}


def montar_pedido(dados, acao, item, reservas, modelo=u""):
    p = {"fja": "pedido", "formato": 1, "id": novo_id_pedido(), "acao": acao,
         "criadoEm": time.strftime("%Y-%m-%dT%H:%M:%S"), "autor": autor(), "modelo": modelo,
         "projeto": {"id": dados.projeto["id"], "codigo": dados.projeto.get("codigo")} if dados.projeto else None,
         "reservas": reservas}
    if acao in ("novo-item", "editar-item"):
        p["item"] = {"id": item["id"]} if acao == "editar-item" else {}
        p["item"].update({"sigla": item.get("sigla"), "rvCat": item.get("rvCat"), "papel": item.get("papel"),
                     "desc": item.get("desc"), "texto": item.get("texto") or u"", "fabricante": u"", "nbr": u"",
                     "camadas": [{"funcao": c.get("funcaoRevit"), "material": c.get("material"),
                                  "esp": float(c.get("esp") or 0)} for c in item.get("camadas", [])]})
    else:
        p["item"] = {"id": item["id"]}
    return p


# ------------------------------------------------------------------ nomes
def limpar_nome(s):
    s = (s or u"").replace(u">=", u"\u2265").replace(u"<=", u"\u2264")
    s = re.sub(r"[\\:{}\[\]|;<>?`~]", "-", s or "")
    return re.sub(r"\s+", " ", s).strip()


def nome_tipo(kn, desc, variacao_mm=None):
    n = u"{} - {}".format(kn, (desc or u"").upper())
    if variacao_mm:
        n += u" E={}".format(int(round(variacao_mm)))
    return limpar_nome(n)


def fmt_mm(v):
    v = round(float(v or 0), 1)
    return (u"%g" % v).replace(".", ",")


# ------------------------------------------------------------------ vínculo (Extensible Storage)
SCHEMA_GUID = System.Guid("3c6f1d2a-9b84-4e57-a1f0-6d2e8b4c7a15")
CAMPOS = ("fja", "itemCentral", "itemProjeto", "projeto", "variacao", "pedido", "sincronizadoEm")


def esquema():
    sc = Schema.Lookup(SCHEMA_GUID)
    if sc is not None:
        return sc
    sb = SchemaBuilder(SCHEMA_GUID)
    sb.SetSchemaName("FJA_SistemaTipo")
    sb.SetReadAccessLevel(AccessLevel.Public)
    sb.SetWriteAccessLevel(AccessLevel.Public)
    sb.SetDocumentation("Vinculo do tipo/material com o Sistema FJA")
    for c in CAMPOS:
        sb.AddSimpleField(c, System.String)
    return sb.Finish()


def gravar_vinculo(elem, **valores):
    sc = esquema()
    ent = Entity(sc)
    valores.setdefault("sincronizadoEm", time.strftime("%Y-%m-%dT%H:%M:%S"))
    for c in CAMPOS:
        ent.Set[System.String](c, u"{}".format(valores.get(c) or u""))
    elem.SetEntity(ent)


def ler_vinculo(elem):
    sc = Schema.Lookup(SCHEMA_GUID)
    if sc is None:
        return None
    try:
        ent = elem.GetEntity(sc)
        if ent is None or not ent.IsValid():
            return None
        return dict((c, ent.Get[System.String](c)) for c in CAMPOS)
    except Exception:
        return None


# ------------------------------------------------------------------ parâmetros
def _set(elem, bip, valor):
    try:
        p = elem.get_Parameter(bip)
        if p is not None and not p.IsReadOnly:
            p.Set(valor)
            return True
    except Exception:
        pass
    return False


def nome_elem(e):
    return DB.Element.Name.__get__(e)


# ------------------------------------------------------------------ materiais
def _sigla_kn(s):
    m = re.match(r"^\.?([A-Za-z]+)", s or "")
    return m.group(1).upper() if m else ""


def plano_material(doc, dados, mat_id, reservas=None):
    """Como o material da camada ficará no modelo: existente (vinculado ou com o
    mesmo nome) ou a criar. Devolve dict com nome, kn, desc, existente (Material|None)."""
    item = dados.itens.get(mat_id) or {}
    linha = dados.linha_projeto(mat_id)
    kn = (linha or {}).get("kn") or u""
    reservado = False
    if not kn and dados.projeto and reservas is not None and item.get("sigla"):
        kn = dados.prox_kn(item["sigla"], tambem=list(reservas.values()))
        reservas[mat_id] = kn
        reservado = True
    desc = ((linha or {}).get("desc") or item.get("desc") or u"").upper()
    codigo = kn or item.get("fja") or u"?"
    nome = limpar_nome(u"{} - {}".format(codigo, desc))
    existente = None
    for m in DB.FilteredElementCollector(doc).OfClass(DB.Material):
        v = ler_vinculo(m)
        if v and v.get("itemCentral") == mat_id and (not dados.projeto or v.get("projeto") in ("", dados.projeto["id"])):
            existente = m
            break
    if existente is None:
        for m in DB.FilteredElementCollector(doc).OfClass(DB.Material):
            if nome_elem(m) == nome:
                existente = m
                break
    return {"id": mat_id, "item": item, "linha": linha, "kn": kn, "desc": desc, "nome": nome,
            "existente": existente, "sigla": item.get("sigla") or _sigla_kn(kn), "reservado": reservado}


def garantir_material(doc, dados, plano):
    """Cria (ou reaproveita) o material e grava keynote, descrição e vínculo."""
    m = plano["existente"]
    if m is None:
        mid = DB.Material.Create(doc, plano["nome"])
        m = doc.GetElement(mid)
        cor = CORES_SIGLA.get(plano["sigla"], COR_PADRAO)
        try:
            m.Color = DB.Color(cor[0], cor[1], cor[2])
            m.UseRenderAppearanceForShading = False
        except Exception:
            pass
    if plano["kn"]:
        _set(m, DB.BuiltInParameter.KEYNOTE_PARAM, plano["kn"])
        _set(m, DB.BuiltInParameter.ALL_MODEL_MARK, plano["kn"])
    _set(m, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION, plano["desc"])
    gravar_vinculo(m, fja=plano["item"].get("fja"), itemCentral=plano["id"],
                   itemProjeto=(plano["linha"] or {}).get("id"),
                   projeto=dados.projeto["id"] if dados.projeto else "",
                   pedido=plano.get("pedido") or (plano["linha"] or {}).get("pendente") or u"")
    return m


# ------------------------------------------------------------------ tipos
CATEGORIAS = {
    "Parede": u"Paredes",
    "Piso": u"Pisos",
    "Forro": u"Forros",
    "Cobertura": u"Coberturas",
}


def _tem_camadas(t):
    try:
        return t.GetCompoundStructure() is not None
    except Exception:
        return False


def tipo_base(doc, cat):
    """Tipo de sistema com camadas, para duplicar."""
    if cat == "Parede":
        tipos = [t for t in DB.FilteredElementCollector(doc).OfClass(DB.WallType) if t.Kind == DB.WallKind.Basic]
    elif cat == "Piso":
        tipos = [t for t in DB.FilteredElementCollector(doc).OfClass(DB.FloorType)
                 if not getattr(t, "IsFoundationSlab", False) and _tem_camadas(t)]
    elif cat == "Forro":
        tipos = [t for t in DB.FilteredElementCollector(doc).OfClass(DB.CeilingType) if _tem_camadas(t)]
    elif cat == "Cobertura":
        tipos = [t for t in DB.FilteredElementCollector(doc).OfClass(DB.RoofType) if _tem_camadas(t)]
    else:
        tipos = []
    return tipos[0] if tipos else None


def tipos_da_categoria(doc, cat):
    cls = {"Parede": DB.WallType, "Piso": DB.FloorType, "Forro": DB.CeilingType, "Cobertura": DB.RoofType}.get(cat)
    return list(DB.FilteredElementCollector(doc).OfClass(cls)) if cls else []


def tipos_vinculados(doc):
    """{id do item central: [tipos do modelo vinculados]} em todas as categorias."""
    res = {}
    for cat in CATEGORIAS:
        for t in tipos_da_categoria(doc, cat):
            v = ler_vinculo(t)
            if v and v.get("itemCentral"):
                res.setdefault(v["itemCentral"], []).append(t)
            if v and v.get("pedido"):
                res.setdefault(u"pedido:" + v["pedido"], []).append(t)
    return res


def estrutura(doc, camadas, mats, base_cs=None):
    """Monta a CompoundStructure. camadas: lista do biblioteca.json; mats: {id material: Material}.
    Parte da estrutura do tipo (base_cs), trocando só as camadas, para manter as configurações
    próprias de cada categoria (ex.: EndCap dos pisos). Núcleo: camadas de Estrutura; sem
    estrutura, Substrato; sem os dois, todas."""
    minimo = DB.CompoundStructure.GetMinimumLayerThickness()
    avisos = []
    lista = List[DB.CompoundStructureLayer]()
    funcoes = []
    for c in camadas:
        fn = c.get("funcaoRevit") or "Structure"
        esp_mm = float(c.get("esp") or 0)
        larg = esp_mm / MM_POR_PE
        if fn == "Membrane":
            larg = 0.0
        elif larg < minimo:
            avisos.append(u"{} com {} mm é mais fina que o mínimo do Revit ({} mm): usado o mínimo.".format(
                c.get("funcao"), fmt_mm(esp_mm), fmt_mm(minimo * MM_POR_PE)))
            larg = minimo
        m = mats.get(c.get("material"))
        mid = m.Id if m is not None else DB.ElementId.InvalidElementId
        lista.Add(DB.CompoundStructureLayer(larg, FUNCOES.get(fn, DB.MaterialFunctionAssignment.Structure), mid))
        funcoes.append(fn)
    if base_cs is not None:
        cs = base_cs
        cs.SetLayers(lista)
    else:
        cs = DB.CompoundStructure.CreateSimpleCompoundStructure(lista)
    n = len(funcoes)
    nucleo = [i for i, f in enumerate(funcoes) if f == "Structure"] or \
             [i for i, f in enumerate(funcoes) if f == "Substrate"] or list(range(n))
    cs.SetNumberOfShellLayers(DB.ShellLayerType.Exterior, nucleo[0])
    cs.SetNumberOfShellLayers(DB.ShellLayerType.Interior, n - 1 - nucleo[-1])
    try:
        est = [i for i, f in enumerate(funcoes) if f == "Structure"]
        if est:
            cs.StructuralMaterialIndex = est[0]
    except Exception:
        pass
    return cs, avisos


def plano_tipo(doc, dados, item):
    """O que acontecerá ao criar o tipo do item no modelo (sem alterar nada)."""
    linha = dados.linha_projeto(item["id"])
    kn = (linha or {}).get("kn") or u""
    desc = (linha or {}).get("desc") or item.get("desc") or u""
    cat = item.get("rvCat") or ""
    p = {"item": item, "linha": linha, "kn": kn, "desc": desc, "cat": cat, "problemas": [], "avisos": [],
         "reservas": None}
    reservas_mat = {}
    pendente = item.get("pendente") or (linha or {}).get("pendente")
    if item.get("pendente"):
        p["avisos"].append(u"Composição criada no Revit e ainda não confirmada pelo sistema "
                           u"(o sistema confirma quando alguém o abre).")
    if not item.get("camadas"):
        p["problemas"].append(u"O item não tem camadas: defina-as no sistema (ficha do item na biblioteca).")
    if cat not in CATEGORIAS:
        p["problemas"].append(u"O item não tem categoria do Revit definida no sistema.")
    if not dados.projeto:
        p["problemas"].append(u"O modelo não está ligado a um projeto publicado pelo sistema.")
    elif not linha and item.get("sigla"):
        kn = dados.prox_kn(item["sigla"])
        p["kn"] = kn
        p["reservas"] = {"kn": kn}
        p["avisos"].append(u"O item ainda não está na especificação do projeto {}: entra como {} "
                           u"(o sistema confirma o keynote).".format(dados.projeto["codigo"], kn))
    elif not linha:
        p["problemas"].append(u"O item não está na especificação do projeto e não tem sigla.")
    p["nome"] = nome_tipo(kn or item.get("fja"), desc)
    p["materiais"] = []
    vistos = set()
    for c in item.get("camadas", []):
        mid = c.get("material")
        if not mid or mid in vistos:
            continue
        vistos.add(mid)
        pm = plano_material(doc, dados, mid, reservas_mat)
        if pm["reservado"]:
            p["avisos"].append(u"Material {} entra na especificação do projeto como {}.".format(
                pm["item"].get("fja") or mid, pm["kn"]))
        elif not pm["kn"]:
            p["avisos"].append(u"Material {} ainda não está na especificação do projeto: será criado com o "
                               u"código FJ no nome e sem keynote.".format(pm["item"].get("fja") or mid))
        p["materiais"].append(pm)
    if reservas_mat:
        p["reservas"] = dict(p["reservas"] or {}, materiais=reservas_mat)
    p["nome"] = nome_tipo(p["kn"] or item.get("fja"), desc)
    # tipo existente (vinculado ao item, ou com o mesmo nome)
    p["existente"] = None
    if cat in CATEGORIAS:
        for t in tipos_da_categoria(doc, cat):
            v = ler_vinculo(t)
            if (v and v.get("itemCentral") == item["id"]) or nome_elem(t) == p["nome"] or \
                    (v and item.get("pedido") and v.get("pedido") == item.get("pedido")):
                p["existente"] = t
                break
        if p["existente"] is None and tipo_base(doc, cat) is None:
            p["problemas"].append(u"Não há no modelo um tipo de {} com camadas para servir de base.".format(
                CATEGORIAS[cat].lower()))
    return p


def criar_tipo(doc, dados, p):
    """Cria (ou atualiza, se já existir) o tipo planejado. Precisa de transação aberta.
    Devolve (tipo, avisos)."""
    item, avisos = p["item"], list(p["avisos"])
    pedido_id = item.get("pedido") or u""
    if p.get("novo"):
        ped = montar_pedido(dados, "novo-item", item, dict(p.get("reservas") or {}, fja=item.get("fja")),
                            modelo=doc.Title)
        ped["id"] = item["pedido"]
        gravar_pedido(dados, ped)
    elif p.get("editar"):
        # alteração de uma composição já existente: o sistema atualiza o item central e a linha do projeto
        ped = montar_pedido(dados, "editar-item", dict(item, desc=p["desc"]), p.get("reservas") or {}, modelo=doc.Title)
        gravar_pedido(dados, ped)
        pedido_id = ped["id"]
    elif p.get("reservas"):
        ped = montar_pedido(dados, "incluir-no-projeto", item, p["reservas"], modelo=doc.Title)
        gravar_pedido(dados, ped)
        pedido_id = ped["id"]
    for pm in p["materiais"]:
        if pm.get("reservado"):
            pm["pedido"] = pedido_id
    mats = {}
    for pm in p["materiais"]:
        mats[pm["id"]] = garantir_material(doc, dados, pm)
    t = p["existente"]
    if t is None:
        t = tipo_base(doc, p["cat"]).Duplicate(p["nome"])
    elif nome_elem(t) != p["nome"]:
        try:
            t.Name = p["nome"]
        except Exception:
            avisos.append(u"Não foi possível renomear o tipo existente para '{}'.".format(p["nome"]))
    cs, av = estrutura(doc, item.get("camadas", []), mats, t.GetCompoundStructure())
    avisos += av
    t.SetCompoundStructure(cs)
    _set(t, DB.BuiltInParameter.ALL_MODEL_TYPE_MARK, p["kn"])
    _set(t, DB.BuiltInParameter.KEYNOTE_PARAM, p["kn"])
    _set(t, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION, (p["desc"] or u"").upper())
    _set(t, DB.BuiltInParameter.ALL_MODEL_TYPE_COMMENTS,
         u"{} · {}".format(item.get("fja", ""), item.get("papel") or item.get("sigla") or ""))
    gravar_vinculo(t, fja=item.get("fja"), itemCentral=u"" if item.get("pendente") else item["id"],
                   itemProjeto=(p["linha"] or {}).get("id"),
                   projeto=dados.projeto["id"] if dados.projeto else "",
                   pedido=pedido_id or (p["linha"] or {}).get("pendente") or u"")
    return t, avisos


def plano_novo(doc, dados, form):
    """Plano para uma composição nova criada no Revit. form: sigla, rvCat, papel, desc, texto,
    camadas [{funcaoRevit, material, esp}]. Reserva FJ e keynote; criar_tipo grava o pedido."""
    pid = novo_id_pedido()
    cam = [{"funcao": FUNCOES_PT.get(c["funcaoRevit"], c["funcaoRevit"]), "funcaoRevit": c["funcaoRevit"],
            "material": c["material"], "esp": float(c.get("esp") or 0)} for c in form.get("camadas", [])]
    item = {"id": u"pedido:" + pid, "pedido": pid, "pendente": True, "fja": dados.prox_fja(form.get("sigla")),
            "sigla": (form.get("sigla") or u"").upper(), "tipo": "sistema", "desc": form.get("desc") or u"",
            "texto": form.get("texto") or u"", "rvCat": form.get("rvCat") or u"", "papel": form.get("papel") or u"",
            "camadas": cam, "espTotal": sum(c["esp"] for c in cam)}
    p = plano_tipo(doc, dados, item)
    p["novo"] = True
    p["avisos"] = [a for a in p["avisos"] if not a.startswith(u"Composição criada no Revit")]
    p["avisos"].insert(0, u"Será criado na biblioteca central como {} (o sistema confirma o código).".format(item["fja"]))
    if not (form.get("desc") or u"").strip():
        p["problemas"].append(u"Informe a descrição.")
    for c in cam:
        if not c.get("material"):
            p["problemas"].append(u"Há camada sem material.")
            break
    return p


def plano_editar(doc, dados, item, form):
    """Plano para alterar uma composição existente (camadas, descrição, especificação, categoria, papel).
    O tipo do modelo é atualizado na hora e o sistema recebe um pedido 'editar-item'."""
    cam = [{"funcao": FUNCOES_PT.get(c["funcaoRevit"], c["funcaoRevit"]), "funcaoRevit": c["funcaoRevit"],
            "material": c["material"], "esp": float(c.get("esp") or 0)} for c in form.get("camadas", [])]
    novo = dict(item)
    novo.update({"desc": form.get("desc") or item.get("desc") or u"", "texto": form.get("texto") or u"",
                 "rvCat": form.get("rvCat") or item.get("rvCat") or u"", "papel": form.get("papel") or u"",
                 "camadas": cam, "espTotal": sum(c["esp"] for c in cam)})
    p = plano_tipo(doc, dados, novo)
    p["editar"] = True
    p["desc"] = novo["desc"]
    p["nome"] = nome_tipo(p["kn"] or novo.get("fja"), p["desc"])
    p["avisos"].insert(0, u"{} será alterado na biblioteca central quando alguém abrir o sistema; "
                          u"outros projetos que usam o item verão o aviso de texto central alterado.".format(novo.get("fja")))
    return p
