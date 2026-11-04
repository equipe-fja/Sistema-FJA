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
            "fja": (p.get("reservas") or {}).get("fja") or u"", "sigla": i.get("sigla") or u"", "tipo": i.get("tipo") or "sistema",
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
        p["item"].update({"sigla": item.get("sigla"), "tipo": item.get("tipo") or "sistema",
                     "rvCat": item.get("rvCat"), "papel": item.get("papel"),
                     "desc": item.get("desc"), "texto": item.get("texto") or u"", "fabricante": u"", "nbr": u"",
                     "camadas": [{"funcao": c.get("funcaoRevit"), "material": c.get("material"),
                                  "esp": float(c.get("esp") or 0)} for c in item.get("camadas", [])]})
    else:
        p["item"] = {"id": item["id"]}
    if item.get("siglaNova"):
        p["item"]["siglaNova"] = item["siglaNova"]  # o sistema cadastra a sigla antes de criar o item
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


# versão 2 do vínculo: guarda também o keynote e a descrição gravados na última sincronização,
# para o diagnóstico distinguir "o sistema mudou" de "alguém alterou no Revit".
SCHEMA_GUID2 = System.Guid("8d2e4b61-5f3a-4c09-b7e2-1a9c6f0d3e58")
CAMPOS2 = CAMPOS + ("kn", "desc")


def esquema2():
    sc = Schema.Lookup(SCHEMA_GUID2)
    if sc is not None:
        return sc
    sb = SchemaBuilder(SCHEMA_GUID2)
    sb.SetSchemaName("FJA_SistemaTipo2")
    sb.SetReadAccessLevel(AccessLevel.Public)
    sb.SetWriteAccessLevel(AccessLevel.Public)
    sb.SetDocumentation("Vinculo do tipo/material com o Sistema FJA (com valores sincronizados)")
    for c in CAMPOS2:
        sb.AddSimpleField(c, System.String)
    return sb.Finish()


def _param_txt(elem, bip):
    try:
        p = elem.get_Parameter(bip)
        return (p.AsString() or u"") if p is not None else u""
    except Exception:
        return u""


def gravar_vinculo(elem, **valores):
    valores.setdefault("sincronizadoEm", time.strftime("%Y-%m-%dT%H:%M:%S"))
    valores.setdefault("kn", _param_txt(elem, DB.BuiltInParameter.KEYNOTE_PARAM))
    valores.setdefault("desc", _param_txt(elem, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION))
    ent = Entity(esquema2())
    for c in CAMPOS2:
        ent.Set[System.String](c, u"{}".format(valores.get(c) or u""))
    elem.SetEntity(ent)


def ler_vinculo(elem):
    for guid, campos in ((SCHEMA_GUID2, CAMPOS2), (SCHEMA_GUID, CAMPOS)):
        sc = Schema.Lookup(guid)
        if sc is None:
            continue
        try:
            ent = elem.GetEntity(sc)
            if ent is None or not ent.IsValid():
                continue
            v = dict((c, ent.Get[System.String](c)) for c in campos)
            v["_versao"] = 2 if guid == SCHEMA_GUID2 else 1
            return v
        except Exception:
            continue
    return None


def apagar_vinculo(elem):
    for guid in (SCHEMA_GUID2, SCHEMA_GUID):
        sc = Schema.Lookup(guid)
        if sc is not None:
            try:
                elem.DeleteEntity(sc)
            except Exception:
                pass


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


def doc_ativo_ok(doc):
    """A ferramenta só age sobre o modelo que estava ativo quando foi aberta."""
    try:
        from pyrevit import HOST_APP
        atual = HOST_APP.uidoc.Document
        return atual is not None and atual.Equals(doc)
    except Exception:
        return True


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
    elif nome_elem(t) != p["nome"] and not p.get("manterNome"):
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
            "camadas": cam, "espTotal": sum(c["esp"] for c in cam), "siglaNova": form.get("siglaNova")}
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


# ------------------------------------------------------------------ componentes (famílias carregáveis)
def pedir_sigla_nova(dados, tipo, titulo=u"Nova sigla"):
    """Pergunta sigla (3 letras) e nome da tabela. Devolve dict da sigla nova ou None."""
    from pyrevit import forms
    rot = u"composição" if tipo == "sistema" else u"elemento/família"
    sg = forms.ask_for_string(prompt=u"Sigla da nova tabela de {} (3 letras):".format(rot), title=titulo)
    if not sg:
        return None
    sg = re.sub(r"[^A-Za-z]", "", sg).upper()
    if len(sg) != 3:
        forms.alert(u"A sigla de {} tem 3 letras.".format(rot), title=titulo)
        return None
    if sg in dados.siglas:
        forms.alert(u"A sigla {} já existe ({}).".format(sg, dados.siglas[sg].get("nome") or u""), title=titulo)
        return None
    nome = forms.ask_for_string(prompt=u"Nome da tabela {} (ex.: Portas de madeira):".format(sg), title=titulo)
    if not nome or not nome.strip():
        return None
    s = {"sigla": sg, "nome": nome.strip(), "tipo": tipo, "nova": True}
    dados.siglas[sg] = s
    dados.bib.setdefault("siglas", []).append(s)
    return s


def elementos(dados):
    """Itens da biblioteca de elemento/família (siglas de 3 letras do tipo elemento)."""
    return [x for x in dados.bib.get("itens", []) if x.get("tipo") == "elemento"]


def siglas_elemento(dados):
    return sorted([s for s in dados.bib.get("siglas", []) if s.get("tipo") == "elemento"], key=lambda s: s["sigla"])


def ler_familia(app, caminho):
    """Abre o .rfa em segundo plano e devolve {nome, categoria, tipos}."""
    fdoc = app.OpenDocumentFile(caminho)
    try:
        if not fdoc.IsFamilyDocument:
            raise ValueError(u"O arquivo não é uma família (.rfa).")
        cat = fdoc.OwnerFamily.FamilyCategory
        tipos = [t.Name for t in fdoc.FamilyManager.Types if t.Name and t.Name.strip()]
        return {"caminho": caminho, "nome": os.path.splitext(os.path.basename(caminho))[0],
                "categoria": cat.Name if cat else u"", "tipos": tipos,
                "tiposInfo": [{"nome": t, "id": None, "inst": None, "vinculo": {}, "kn": u""} for t in tipos]}
    finally:
        fdoc.Close(False)


def _idv(eid):
    """Número do ElementId (Revit 2024+ usa .Value; versões antigas, .IntegerValue)."""
    v = getattr(eid, "Value", None)
    return int(v) if v is not None else int(eid.IntegerValue)


def familia_do_elemento(doc, elem):
    """Família de um elemento já inserido no modelo (instância ou tipo de família carregável)."""
    sim = None
    if isinstance(elem, DB.FamilyInstance):
        sim = elem.Symbol
    elif isinstance(elem, DB.FamilySymbol):
        sim = elem
    if sim is None:
        raise ValueError(u"O elemento selecionado não é de uma família carregável.")
    familia = sim.Family
    if familia.IsInPlace:
        raise ValueError(u"Famílias criadas no local (in-place) não podem ser readequadas.")
    tipos = [doc.GetElement(i) for i in familia.GetFamilySymbolIds()]
    cont = {}
    for inst in DB.FilteredElementCollector(doc).OfClass(DB.FamilyInstance):
        try:
            if inst.Symbol.Family.Id == familia.Id:
                k = _idv(inst.Symbol.Id)
                cont[k] = cont.get(k, 0) + 1
        except Exception:
            pass
    vinc = ler_vinculo(sim) or {}
    kn = u""
    try:
        par = sim.get_Parameter(DB.BuiltInParameter.KEYNOTE_PARAM)
        kn = par.AsString() or u"" if par else u""
    except Exception:
        pass
    info = []
    for t in tipos:
        kt = u""
        try:
            pr = t.get_Parameter(DB.BuiltInParameter.KEYNOTE_PARAM)
            kt = (pr.AsString() or u"") if pr else u""
        except Exception:
            pass
        info.append({"nome": nome_elem(t), "id": t.Id, "inst": cont.get(_idv(t.Id), 0),
                     "vinculo": ler_vinculo(t) or {}, "kn": kt})
    return {"modelo": True, "familia": familia, "simbolo": sim, "nome": familia.Name, "tiposInfo": info,
            "categoria": familia.FamilyCategory.Name if familia.FamilyCategory else u"",
            "tipos": [nome_elem(t) for t in tipos],
            "instancias": dict((nome_elem(t), cont.get(_idv(t.Id), 0)) for t in tipos),
            "vinculo": vinc, "knAtual": kn, "tipoAtual": nome_elem(sim)}


def nome_familia(sigla, desc):
    return limpar_nome(u"({}) {}".format((sigla or u"").upper(), (desc or u"").upper()))


def plano_componente(doc, dados, item, fam, renomear=True, nome=None):
    """O que acontecerá ao inserir a família com o código do item (sem alterar nada)."""
    linha = dados.linha_projeto(item["id"])
    kn = (linha or {}).get("kn") or u""
    desc = (linha or {}).get("desc") or item.get("desc") or u""
    p = {"item": item, "linha": linha, "kn": kn, "desc": desc, "fam": fam, "problemas": [], "avisos": [],
         "reservas": None, "renomear": renomear}
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
    if not (desc or u"").strip():
        p["problemas"].append(u"Informe a descrição.")
    p["nomeFamilia"] = limpar_nome(nome) if nome else nome_familia(item.get("sigla"), desc)
    if renomear and not p["nomeFamilia"]:
        p["problemas"].append(u"Informe o nome da família.")
    if item.get("pendente"):
        p["avisos"].insert(0, u"Será criado na biblioteca central como {} (o sistema confirma o código).".format(
            item.get("fja")))
    return p


def plano_componente_novo(doc, dados, fam, form, renomear=True, nome=None):
    pid = novo_id_pedido()
    item = {"id": u"pedido:" + pid, "pedido": pid, "pendente": True, "fja": dados.prox_fja(form.get("sigla")),
            "sigla": (form.get("sigla") or u"").upper(), "tipo": "elemento", "desc": form.get("desc") or u"",
            "siglaNova": form.get("siglaNova"),
            "texto": form.get("texto") or u"", "rvCat": fam.get("categoria") or u"", "papel": u"", "camadas": []}
    p = plano_componente(doc, dados, item, fam, renomear, nome)
    p["novo"] = True
    if not item["sigla"]:
        p["problemas"].append(u"Escolha a sigla.")
    return p


class _OpcoesCarga(DB.IFamilyLoadOptions):
    def OnFamilyFound(self, familyInUse, overwriteParameterValues):
        overwriteParameterValues.Value = False
        return True

    def OnSharedFamilyFound(self, sharedFamily, familyInUse, source, overwriteParameterValues):
        source.Value = DB.FamilySource.Family
        overwriteParameterValues.Value = False
        return True


def _familia_por_nome(doc, nome):
    for f in DB.FilteredElementCollector(doc).OfClass(DB.Family):
        if f.Name == nome:
            return f
    return None


def inserir_componente(doc, dados, p):
    """Carrega a família, grava keynote/Type Mark/descrição/vínculo em todos os tipos e renomeia se pedido.
    Grava o pedido ao sistema. Precisa de transação aberta. Devolve (familia, avisos)."""
    import clr
    item, fam, avisos = p["item"], p["fam"], list(p["avisos"])
    pedido_id = item.get("pedido") or u""
    if p.get("novo"):
        ped = montar_pedido(dados, "novo-item", item, dict(p.get("reservas") or {}, fja=item.get("fja")),
                            modelo=doc.Title)
        ped["id"] = item["pedido"]
        gravar_pedido(dados, ped)
    elif p.get("reservas"):
        ped = montar_pedido(dados, "incluir-no-projeto", item, p["reservas"], modelo=doc.Title)
        gravar_pedido(dados, ped)
        pedido_id = ped["id"]
    if fam.get("modelo"):
        familia = fam["familia"]  # readequar: família já no modelo, sem recarregar
    else:
        ref = clr.Reference[DB.Family]()
        doc.LoadFamily(fam["caminho"], _OpcoesCarga(), ref)
        familia = ref.Value or _familia_por_nome(doc, fam["nome"])
    if familia is None:
        raise ValueError(u"Não foi possível carregar a família.")
    if p.get("renomear") and familia.Name != p["nomeFamilia"]:
        if _familia_por_nome(doc, p["nomeFamilia"]) is not None:
            avisos.append(u"Já existe uma família chamada '{}': o nome não foi trocado.".format(p["nomeFamilia"]))
        else:
            try:
                familia.Name = p["nomeFamilia"]
            except Exception as e:
                avisos.append(u"Não foi possível renomear a família: {}".format(e))
    alvos = [fam["simbolo"].Id] if fam.get("modelo") and p.get("soTipo") else list(familia.GetFamilySymbolIds())
    p["nTipos"] = len(alvos)
    for sid in alvos:
        t = doc.GetElement(sid)
        _set(t, DB.BuiltInParameter.ALL_MODEL_TYPE_MARK, p["kn"])
        _set(t, DB.BuiltInParameter.KEYNOTE_PARAM, p["kn"])
        _set(t, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION, (p["desc"] or u"").upper())
        _set(t, DB.BuiltInParameter.ALL_MODEL_TYPE_COMMENTS, item.get("fja") or u"")
        gravar_vinculo(t, fja=item.get("fja"), itemCentral=u"" if item.get("pendente") else item["id"],
                       itemProjeto=(p["linha"] or {}).get("id"),
                       projeto=dados.projeto["id"] if dados.projeto else "",
                       pedido=pedido_id or (p["linha"] or {}).get("pendente") or u"")
    return familia, avisos


# ------------------------------------------------------------------ componentes: código por tipo
def plano_codigo(doc, dados, cod, tambem):
    """Plano de um código usado por um ou mais tipos. cod: {"item": item} (existente) ou
    {"novo": {"sigla","desc","texto","siglaNova"}} (novo). tambem: keynotes/FJs já reservados neste lote."""
    if cod.get("novo"):
        f = cod["novo"]
        sg = (f.get("sigla") or u"").upper()
        if "item" not in cod:  # o item novo é criado uma vez e reutilizado por todos os tipos que o usam
            pid = novo_id_pedido()
            fja = dados.prox_fja(sg)
            while fja in tambem:
                n = int(fja.rsplit(".", 1)[1]) + 1
                fja = u"FJ.%s.%04d" % (sg, n)
            tambem.add(fja)
            cod["item"] = {"id": u"pedido:" + pid, "pedido": pid, "pendente": True, "fja": fja, "sigla": sg,
                           "tipo": "elemento", "desc": f.get("desc") or u"", "texto": f.get("texto") or u"",
                           "rvCat": cod.get("cat") or u"", "papel": u"", "camadas": [], "siglaNova": f.get("siglaNova")}
    item = cod["item"]
    linha = dados.linha_projeto(item["id"])
    kn = (linha or {}).get("kn") or u""
    desc = (linha or {}).get("desc") or item.get("desc") or u""
    p = {"item": item, "linha": linha, "kn": kn, "desc": desc, "reservas": None, "novo": bool(cod.get("novo")),
         "problemas": [], "avisos": []}
    if not dados.projeto:
        p["problemas"].append(u"O modelo não está ligado a um projeto publicado pelo sistema.")
    elif not linha and item.get("sigla"):
        kn = dados.prox_kn(item["sigla"], tambem)
        tambem.add(kn)
        p["kn"] = kn
        p["reservas"] = {"kn": kn}
    elif not linha:
        p["problemas"].append(u"{} não está na especificação do projeto e não tem sigla.".format(item.get("fja")))
    if not (desc or u"").strip():
        p["problemas"].append(u"Código sem descrição.")
    return p


def nome_tipo_comp(kn, desc, original, repetido):
    n = u"{} - {}".format(kn, (desc or u"").upper())
    if repetido:
        n += u" - {}".format(original)
    return limpar_nome(n)


def aplicar_componente(doc, dados, fam, grupos, ren_fam, nome_fam, ren_tipos):
    """grupos: [{"plano": plano_codigo, "tipos": [nomes]}]. Carrega a família (se vier de arquivo), grava
    keynote/Type Mark/descrição/vínculo por tipo, renomeia tipos e família se pedido e envia os pedidos ao
    sistema. Precisa de transação aberta. Devolve (familia, avisos)."""
    import clr
    avisos = []
    for g in grupos:
        p, item = g["plano"], g["plano"]["item"]
        g["pedido"] = item.get("pedido") or u""
        if p.get("novo"):
            ped = montar_pedido(dados, "novo-item", item, dict(p.get("reservas") or {}, fja=item.get("fja")),
                                modelo=doc.Title)
            ped["id"] = item["pedido"]
            gravar_pedido(dados, ped)
        elif p.get("reservas"):
            ped = montar_pedido(dados, "incluir-no-projeto", item, p["reservas"], modelo=doc.Title)
            gravar_pedido(dados, ped)
            g["pedido"] = ped["id"]
    if fam.get("modelo"):
        familia = fam["familia"]
    else:
        ref = clr.Reference[DB.Family]()
        doc.LoadFamily(fam["caminho"], _OpcoesCarga(), ref)
        familia = ref.Value or _familia_por_nome(doc, fam["nome"])
    if familia is None:
        raise ValueError(u"Não foi possível carregar a família.")
    simb = dict((nome_elem(doc.GetElement(i)), doc.GetElement(i)) for i in familia.GetFamilySymbolIds())
    for g in grupos:
        p, item = g["plano"], g["plano"]["item"]
        rep = len(g["tipos"]) > 1
        for nome in g["tipos"]:
            t = simb.get(nome)
            if t is None:
                avisos.append(u"Tipo '{}' não encontrado na família carregada.".format(nome))
                continue
            _set(t, DB.BuiltInParameter.ALL_MODEL_TYPE_MARK, p["kn"])
            _set(t, DB.BuiltInParameter.KEYNOTE_PARAM, p["kn"])
            _set(t, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION, (p["desc"] or u"").upper())
            _set(t, DB.BuiltInParameter.ALL_MODEL_TYPE_COMMENTS, item.get("fja") or u"")
            gravar_vinculo(t, fja=item.get("fja"), itemCentral=u"" if item.get("pendente") else item["id"],
                           itemProjeto=(p["linha"] or {}).get("id"),
                           projeto=dados.projeto["id"] if dados.projeto else "",
                           pedido=g["pedido"] or (p["linha"] or {}).get("pendente") or u"")
            if ren_tipos:
                novo = nome_tipo_comp(p["kn"] or item.get("fja"), p["desc"], nome, rep)
                if novo != nome:
                    try:
                        t.Name = novo
                    except Exception as e:
                        avisos.append(u"Não foi possível renomear o tipo '{}': {}".format(nome, e))
    if ren_fam and nome_fam and familia.Name != nome_fam:
        if _familia_por_nome(doc, nome_fam) is not None:
            avisos.append(u"Já existe uma família chamada '{}': o nome não foi trocado.".format(nome_fam))
        else:
            try:
                familia.Name = nome_fam
            except Exception as e:
                avisos.append(u"Não foi possível renomear a família: {}".format(e))
    return familia, avisos


# ------------------------------------------------------------------ diagnóstico da sincronização
def elementos_vinculados(doc):
    """Tipos (sistema e famílias) e materiais com vínculo do Sistema FJA."""
    res = []
    for cls in (DB.WallType, DB.FloorType, DB.CeilingType, DB.RoofType, DB.FamilySymbol, DB.Material):
        for e in DB.FilteredElementCollector(doc).OfClass(cls):
            v = ler_vinculo(e)
            if v and (v.get("itemCentral") or v.get("pedido") or v.get("fja")):
                res.append((e, v))
    return res


def _cat_elem(e):
    if isinstance(e, DB.Material):
        return u"Material"
    if isinstance(e, DB.FamilySymbol):
        try:
            return e.Family.FamilyCategory.Name
        except Exception:
            return u"Família"
    return {"WallType": u"Paredes", "FloorType": u"Pisos", "CeilingType": u"Forros", "RoofType": u"Coberturas"}.get(
        e.GetType().Name, e.GetType().Name)


def _mm(pes):
    return round(pes * 304.8, 1)


def _camadas_modelo(doc, t):
    try:
        cs = t.GetCompoundStructure()
    except Exception:
        cs = None
    if cs is None:
        return None
    res = []
    for ly in cs.GetLayers():
        mid = u""
        m = doc.GetElement(ly.MaterialId) if ly.MaterialId and ly.MaterialId != DB.ElementId.InvalidElementId else None
        if m is not None:
            mid = (ler_vinculo(m) or {}).get("itemCentral") or u""
        res.append((_mm(ly.Width), mid))
    return res


def diagnosticar(doc, dados, e, v):
    """Compara o elemento vinculado com o sistema. Devolve dict com estado e diferenças."""
    tipo = "material" if isinstance(e, DB.Material) else ("componente" if isinstance(e, DB.FamilySymbol) else "composicao")
    d = {"elem": e, "vinc": v, "tipo": tipo, "nome": nome_elem(e), "cat": _cat_elem(e), "item": None, "linha": None,
         "estado": "ok", "difs": [], "kn": u"", "desc": u""}
    item = dados.itens.get(v.get("itemCentral")) if v.get("itemCentral") else None
    if item is None and v.get("pedido"):
        item = next((x for x in dados.bib.get("itens", []) if x.get("pedido") == v["pedido"]), None)
        if item is None:
            pend = [p for p in dados.pendentes if p.get("id") == v["pedido"]]
            if pend:
                d["estado"] = "aguardando"
                d["fja"] = v.get("fja")
                return d
    if item is None:
        d["estado"] = "orfao"
        return d
    d["item"] = item
    linha = dados.linha_projeto(item["id"])
    d["linha"] = linha
    if dados.projeto and not linha:
        d["estado"] = "fora"
        return d
    if (linha or {}).get("pendente"):
        d["estado"] = "aguardando"
        return d
    kn = (linha or {}).get("kn") or u""
    desc = ((linha or {}).get("desc") or item.get("desc") or u"").upper()
    d["kn"], d["desc"] = kn, desc
    atual_kn = _param_txt(e, DB.BuiltInParameter.KEYNOTE_PARAM)
    atual_desc = _param_txt(e, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION)
    if kn and atual_kn != kn:
        d["difs"].append((u"Keynote", atual_kn, kn))
    if tipo != "material":
        tm = _param_txt(e, DB.BuiltInParameter.ALL_MODEL_TYPE_MARK)
        if kn and tm != kn:
            d["difs"].append((u"Type Mark", tm, kn))
    if desc and atual_desc.upper() != desc:
        d["difs"].append((u"Descrição", atual_desc, desc))
    if tipo == "composicao" and item.get("camadas"):
        mod = _camadas_modelo(doc, e) or []
        sis = [(round(float(c.get("esp") or 0), 1), c.get("material") or u"") for c in item["camadas"]]
        if [x[0] for x in mod] != [x[0] for x in sis]:
            d["difs"].append((u"Camadas (mm)", u" / ".join(fmt_mm(x[0]) for x in mod), u" / ".join(fmt_mm(x[0]) for x in sis)))
        elif [x[1] for x in mod] != [x[1] for x in sis]:
            d["difs"].append((u"Materiais das camadas", u"diferentes", u"conforme a composição"))
    if d["difs"]:
        # com o vínculo v2 dá para saber quem mudou: se o Revit ainda tem o valor sincronizado, quem mudou foi o sistema
        if v.get("_versao") == 2:
            mexeu = (atual_kn != (v.get("kn") or u"")) or (atual_desc != (v.get("desc") or u""))
            d["estado"] = "revit" if mexeu else "desatualizado"
        else:
            d["estado"] = "desatualizado"
    return d


def ressincronizar(doc, dados, d, renomear=False):
    """Traz para o Revit os dados do sistema (precisa de transação aberta). Devolve lista de avisos."""
    e, item, avisos = d["elem"], d["item"], []
    if d["tipo"] == "composicao":
        p = plano_tipo(doc, dados, item)
        p["existente"] = e
        p["manterNome"] = not renomear
        if [x for x in p["problemas"] if u"tipo de" not in x]:
            return [u"{}: {}".format(d["nome"], u"; ".join(p["problemas"]))]
        _, av = criar_tipo(doc, dados, p)
        return av
    _set(e, DB.BuiltInParameter.KEYNOTE_PARAM, d["kn"])
    if d["tipo"] == "material":
        _set(e, DB.BuiltInParameter.ALL_MODEL_MARK, d["kn"])
    else:
        _set(e, DB.BuiltInParameter.ALL_MODEL_TYPE_MARK, d["kn"])
    _set(e, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION, d["desc"])
    if renomear and d["kn"]:
        novo = limpar_nome(u"{} - {}".format(d["kn"], d["desc"]))
        if d["tipo"] == "componente":
            irmaos = [nome_elem(doc.GetElement(i)) for i in e.Family.GetFamilySymbolIds() if i != e.Id]
            if novo in irmaos:
                orig = d["nome"].rsplit(u" - ", 1)[-1] if d["nome"].startswith(d["kn"]) else d["nome"]
                novo = limpar_nome(u"{} - {}".format(novo, orig))
        if novo != d["nome"]:
            try:
                e.Name = novo
            except Exception as ex:
                avisos.append(u"Não foi possível renomear '{}': {}".format(d["nome"], ex))
    v = d["vinc"]
    gravar_vinculo(e, fja=item.get("fja"), itemCentral=item["id"], itemProjeto=(d["linha"] or {}).get("id"),
                   projeto=dados.projeto["id"] if dados.projeto else v.get("projeto"), pedido=v.get("pedido"))
    return avisos


def incluir_no_projeto(doc, dados, d):
    """Item fora do projeto: reserva keynote, envia pedido 'incluir-no-projeto' e grava no Revit."""
    item = d["item"]
    kn = dados.prox_kn(item.get("sigla"))
    ped = montar_pedido(dados, "incluir-no-projeto", item, {"kn": kn}, modelo=doc.Title)
    gravar_pedido(dados, ped)
    d["_pedido"] = ped["id"]
    d2 = dict(d, kn=kn, desc=(item.get("desc") or u"").upper(), linha={"id": u"", "kn": kn, "pendente": ped["id"]})
    d2["vinc"] = dict(d["vinc"], pedido=ped["id"])
    e = d["elem"]
    _set(e, DB.BuiltInParameter.KEYNOTE_PARAM, kn)
    _set(e, DB.BuiltInParameter.ALL_MODEL_MARK if d["tipo"] == "material" else DB.BuiltInParameter.ALL_MODEL_TYPE_MARK, kn)
    _set(e, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION, d2["desc"])
    gravar_vinculo(e, fja=item.get("fja"), itemCentral=item["id"], itemProjeto=u"",
                   projeto=dados.projeto["id"] if dados.projeto else u"", pedido=ped["id"])
    return kn


def instancias_do_tipo(doc, e):
    if isinstance(e, DB.Material):
        return []
    ids = []
    for cls in (DB.Wall, DB.Floor, DB.Ceiling, DB.RoofBase, DB.FamilyInstance):
        for x in DB.FilteredElementCollector(doc).OfClass(cls).WhereElementIsNotElementType():
            try:
                if x.GetTypeId() == e.Id:
                    ids.append(x.Id)
            except Exception:
                pass
    return ids


def gravar_valores(doc, dados, d, kn, desc, pedido=u""):
    """Grava keynote/Type Mark/descrição e vínculo (sem mexer em camadas nem no nome)."""
    e, item = d["elem"], d["item"]
    _set(e, DB.BuiltInParameter.KEYNOTE_PARAM, kn)
    _set(e, DB.BuiltInParameter.ALL_MODEL_MARK if d["tipo"] == "material" else DB.BuiltInParameter.ALL_MODEL_TYPE_MARK, kn)
    _set(e, DB.BuiltInParameter.ALL_MODEL_DESCRIPTION, desc)
    gravar_vinculo(e, fja=item.get("fja"), itemCentral=item["id"], itemProjeto=u"",
                   projeto=dados.projeto["id"] if dados.projeto else u"", pedido=pedido)
