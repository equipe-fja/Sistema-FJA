# -*- coding: utf-8 -*-
"""Cópia de elementos de modelos linkados para o modelo ativo, na mesma posição, por módulo
(Equipamentos, Estrutura, Arquitetura, Encaminhamentos), com registro de origem em cada cópia
(Extensible Storage `FJA_CopiaLink`) para o controle e o monitoramento das próximas etapas.

Não depende do pyRevit: recebe o documento (dá para testar pelo conector do Revit).

Regras confirmadas no Revit 2027 (06/11/2026):
- `ElementTransformUtils.CopyElements(docLink, ids, doc, transformDoLink, opções)` põe a cópia na posição
  e na rotação exatas, inclusive espelhados e elementos em níveis com cota diferente (o Revit associa o
  nível e ajusta o deslocamento).
- Famílias face based vêm sem hospedeiro, no mesmo lugar (não se perdem).
- Famílias hospedadas em parede/forro/piso (não face based) não copiam sem o hospedeiro.
"""
import io
import json
import os
import time

import System
from Autodesk.Revit import DB
from Autodesk.Revit.DB.ExtensibleStorage import Schema, SchemaBuilder, Entity, AccessLevel, \
    ExtensibleStorageFilter
from System.Collections.Generic import List

PASTA_LIB = os.path.dirname(os.path.abspath(__file__))
ARQ_MODULOS = os.path.join(PASTA_LIB, "fja_links_modulos.json")
TOL_PES = 0.01          # ~3 mm: tolerância para casar origem e cópia
LOTE = 150              # elementos por chamada de CopyElements


# ------------------------------------------------------------------ módulos
def modulos():
    with io.open(ARQ_MODULOS, encoding="utf-8") as f:
        dados = json.load(f)
    out = []
    for m in dados.get("modulos", []):
        m = dict(m)
        m.setdefault("ativo", True)
        out.append(m)
    return out


def ids_categorias(mod):
    """ElementIds das categorias do módulo que existem nesta versão do Revit."""
    out = []
    for nome in mod.get("categorias", []):
        bic = getattr(DB.BuiltInCategory, nome, None)
        if bic is None:
            continue
        try:
            out.append(DB.ElementId(bic))
        except Exception:
            pass
    return out


# ------------------------------------------------------------------ links
class Link(object):
    def __init__(self, inst):
        self.inst = inst
        self.doc = inst.GetLinkDocument()
        self.uid = inst.UniqueId
        self.nome = inst.Name
        try:
            tipo = inst.Document.GetElement(inst.GetTypeId())
            self.tipo = DB.Element.Name.__get__(tipo)
        except Exception:
            self.tipo = self.nome.split(" : ")[0]
        self.transf = inst.GetTotalTransform() if self.doc is not None else None

    @property
    def carregado(self):
        return self.doc is not None

    @property
    def rotulo(self):
        return self.nome if self.carregado else u"{} (não carregado)".format(self.nome)


def links(doc):
    out = [Link(i) for i in DB.FilteredElementCollector(doc).OfClass(DB.RevitLinkInstance)]
    out.sort(key=lambda l: (not l.carregado, l.nome.lower()))
    return out


# ------------------------------------------------------------------ registro (Extensible Storage)
SCHEMA_GUID = System.Guid("5b1e9c47-2d8a-4f63-9e05-c7a4d1b83f26")
CAMPOS = ("modulo", "link", "linkInst", "origem", "foto", "copiadoEm", "por")


def esquema():
    sc = Schema.Lookup(SCHEMA_GUID)
    if sc is not None:
        return sc
    sb = SchemaBuilder(SCHEMA_GUID)
    sb.SetSchemaName("FJA_CopiaLink")
    sb.SetReadAccessLevel(AccessLevel.Public)
    sb.SetWriteAccessLevel(AccessLevel.Public)
    sb.SetDocumentation("Elemento copiado de um link pelas Ferramentas FJA: origem e estado no momento da copia")
    for c in CAMPOS:
        sb.AddSimpleField(c, System.String)
    return sb.Finish()


def gravar_registro(elem, **valores):
    valores.setdefault("copiadoEm", time.strftime("%Y-%m-%dT%H:%M:%S"))
    ent = Entity(esquema())
    for c in CAMPOS:
        ent.Set[System.String](c, u"{}".format(valores.get(c) or u""))
    elem.SetEntity(ent)


def ler_registro(elem):
    sc = Schema.Lookup(SCHEMA_GUID)
    if sc is None:
        return None
    try:
        ent = elem.GetEntity(sc)
        if ent is None or not ent.IsValid():
            return None
        v = dict((c, ent.Get[System.String](c)) for c in CAMPOS)
        try:
            v["foto"] = json.loads(v.get("foto") or "{}")
        except Exception:
            v["foto"] = {}
        return v
    except Exception:
        return None


def copias(doc):
    """{(UniqueId da instância do link, UniqueId do elemento de origem): [ids das cópias no modelo]}"""
    out = {}
    if Schema.Lookup(SCHEMA_GUID) is None:
        return out
    col = DB.FilteredElementCollector(doc).WherePasses(ExtensibleStorageFilter(SCHEMA_GUID))
    for e in col:
        r = ler_registro(e)
        if r:
            out.setdefault((r["linkInst"], r["origem"]), []).append(e.Id)
    return out


# ------------------------------------------------------------------ elementos do link
def _nome(e):
    try:
        return DB.Element.Name.__get__(e)
    except Exception:
        return u""


_NIVEIS = {}


def _niveis(ldoc):
    k = ldoc.GetHashCode()
    if k not in _NIVEIS:
        _NIVEIS[k] = list(DB.FilteredElementCollector(ldoc).OfClass(DB.Level))
    return _NIVEIS[k]


def nome_nivel(ldoc, e):
    lid = e.LevelId
    if lid is None or lid == DB.ElementId.InvalidElementId:
        for bip in (DB.BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, DB.BuiltInParameter.FAMILY_LEVEL_PARAM):
            try:
                p = e.get_Parameter(bip)
                if p is not None and p.AsElementId() != DB.ElementId.InvalidElementId:
                    lid = p.AsElementId()
                    break
            except Exception:
                pass
    lv = ldoc.GetElement(lid) if lid is not None and lid != DB.ElementId.InvalidElementId else None
    if lv is None:
        # face based etc.: o nível mais alto abaixo do ponto de inserção
        try:
            z = e.GetTotalTransform().Origin.Z + 0.01
            abaixo = [l for l in _niveis(ldoc) if l.ProjectElevation <= z]
            if abaixo:
                lv = max(abaixo, key=lambda l: l.ProjectElevation)
        except Exception:
            pass
    return _nome(lv) if lv is not None else u""


def situacao_origem(e):
    """'ok' (copiável), 'hospedeiro' (precisa do hospedeiro) ou None (não entra: componente aninhado,
    opção de projeto secundária)."""
    try:
        if e.SuperComponent is not None:
            return None
    except Exception:
        pass
    try:
        op = e.DesignOption
        if op is not None and not op.IsPrimary:
            return None
    except Exception:
        pass
    host = e.Host
    if host is None or isinstance(host, DB.Level):
        return "ok"
    if e.HostFace is not None:
        return "ok"   # face based: vem sem hospedeiro, no mesmo lugar
    try:
        if e.Symbol.Family.FamilyPlacementType == DB.FamilyPlacementType.OneLevelBasedHosted:
            return "hospedeiro"
    except Exception:
        pass
    return "ok"


def _v(p):
    return [round(p.X, 6), round(p.Y, 6), round(p.Z, 6)]


def foto(e, nivel=u""):
    """Estado do elemento de origem (coordenadas do próprio link, em pés)."""
    t = e.GetTotalTransform()
    sym = e.Symbol
    return {
        "cat": e.Category.Name if e.Category else u"",
        "fam": sym.Family.Name if sym is not None else u"",
        "tipo": _nome(sym) if sym is not None else u"",
        "o": _v(t.Origin), "x": _v(t.BasisX), "z": _v(t.BasisZ),
        "esp": bool(e.Mirrored),
        "niv": nivel,
        "face": e.HostFace is not None and not isinstance(e.Host, DB.Level),
    }


class Origem(object):
    __slots__ = ("e", "uid", "cat", "fam", "tipo", "nivel", "sit", "copias")

    def __init__(self, e, ldoc):
        self.e = e
        self.uid = e.UniqueId
        self.cat = e.Category.Name if e.Category else u"?"
        sym = e.Symbol
        self.fam = sym.Family.Name if sym is not None else u"?"
        self.tipo = _nome(sym) if sym is not None else u"?"
        self.nivel = nome_nivel(ldoc, e)
        self.sit = situacao_origem(e)
        self.copias = []

    @property
    def estado(self):
        if self.sit == "hospedeiro":
            return "hospedeiro"
        return "copiado" if self.copias else "novo"


def elementos_do_link(doc, link, mod):
    """Elementos do link que pertencem ao módulo, já marcados com as cópias existentes no modelo."""
    if not link.carregado:
        return []
    cats = ids_categorias(mod)
    if not cats:
        return []
    filtro = DB.ElementMulticategoryFilter(List[DB.ElementId](cats))
    col = DB.FilteredElementCollector(link.doc).OfClass(DB.FamilyInstance).WherePasses(filtro) \
        .WhereElementIsNotElementType()
    ja = copias(doc)
    _NIVEIS.clear()
    out = []
    for e in col:
        o = Origem(e, link.doc)
        if o.sit is None:
            continue
        o.copias = [i for i in ja.get((link.uid, o.uid), []) if doc.GetElement(i) is not None]
        out.append(o)
    return out


def agrupar(origens):
    """Linhas da janela: uma por categoria + família + tipo + nível."""
    g = {}
    for o in origens:
        g.setdefault((o.cat, o.fam, o.tipo, o.nivel), []).append(o)
    linhas = []
    for (cat, fam, tipo, niv), lst in g.items():
        linhas.append({"cat": cat, "fam": fam, "tipo": tipo, "nivel": niv, "itens": lst,
                       "novos": len([o for o in lst if o.estado == "novo"]),
                       "copiados": len([o for o in lst if o.estado == "copiado"]),
                       "hospedeiro": len([o for o in lst if o.estado == "hospedeiro"])})
    linhas.sort(key=lambda l: (l["cat"].lower(), l["fam"].lower(), l["tipo"].lower(), l["nivel"].lower()))
    return linhas


# ------------------------------------------------------------------ cópia
class _UsarTiposDoModelo(DB.IDuplicateTypeNamesHandler):
    def OnDuplicateTypeNamesFound(self, args):
        return DB.DuplicateTypeAction.UseDestinationTypes


class Silenciar(DB.IFailuresPreprocessor):
    """Apaga avisos (não erros) durante a cópia, para o Revit não abrir caixas a cada lote."""
    def PreprocessFailures(self, fa):
        for f in fa.GetFailureMessages():
            if f.GetSeverity() == DB.FailureSeverity.Warning:
                fa.DeleteWarning(f)
        return DB.FailureProcessingResult.Continue


def opcoes_transacao(t):
    fo = t.GetFailureHandlingOptions()
    fo.SetFailuresPreprocessor(Silenciar())
    t.SetFailureHandlingOptions(fo)


def _instancias(doc, ids):
    out = []
    for i in ids:
        n = doc.GetElement(i)
        if isinstance(n, DB.FamilyInstance):
            try:
                if n.SuperComponent is not None:
                    continue
            except Exception:
                pass
            out.append(n)
    return out


def _casar(doc, link, lote, novos):
    """Liga cada origem à sua cópia: mesma família e tipo, origem transformada mais próxima."""
    livres = list(novos)
    pares, sem = [], []
    for o in lote:
        alvo = link.transf.OfPoint(o.e.GetTotalTransform().Origin)
        melhor, dist = None, None
        for n in livres:
            try:
                if n.Symbol.Family.Name != o.fam or _nome(n.Symbol) != o.tipo:
                    continue
            except Exception:
                continue
            d = n.GetTotalTransform().Origin.DistanceTo(alvo)
            if dist is None or d < dist:
                melhor, dist = n, d
        if melhor is None:
            sem.append(o)
            continue
        livres.remove(melhor)
        pares.append((o, melhor, dist))
    return pares, sem


def _copiar(doc, link, lote, opc):
    ids = DB.ElementTransformUtils.CopyElements(
        link.doc, List[DB.ElementId]([o.e.Id for o in lote]), doc, link.transf, opc)
    doc.Regenerate()
    return _instancias(doc, ids)


def copiar(doc, link, origens, mod, workset=None, por=u"", progresso=None):
    """Copia as origens para o modelo (dentro de uma Transaction já aberta pelo chamador).
    Devolve {"copiados": [(origem, nova)], "falhas": [(origem, msg)], "avisos": [(origem, nova, msg)]}."""
    opc = DB.CopyPasteOptions()
    opc.SetDuplicateTypeNamesHandler(_UsarTiposDoModelo())
    res = {"copiados": [], "falhas": [], "avisos": []}
    alvo = [o for o in origens if o.sit == "ok"]
    for o in origens:
        if o.sit == "hospedeiro":
            res["falhas"].append((o, u"precisa do hospedeiro (família baseada em parede/forro/piso)"))
    feitos = 0
    for i in range(0, len(alvo), LOTE):
        lote = alvo[i:i + LOTE]
        pares, sem = [], []
        st = DB.SubTransaction(doc)
        st.Start()
        try:
            pares, sem = _casar(doc, link, lote, _copiar(doc, link, lote, opc))
            st.Commit()
        except Exception:
            st.RollBack()
            # lote recusado: um a um, para separar o que falha
            for o in lote:
                st1 = DB.SubTransaction(doc)
                st1.Start()
                try:
                    p, s = _casar(doc, link, [o], _copiar(doc, link, [o], opc))
                    st1.Commit()
                    pares += p
                    sem += s
                except Exception as ex:
                    st1.RollBack()
                    res["falhas"].append((o, u"{}".format(ex)))
        for o in sem:
            res["falhas"].append((o, u"a cópia não foi encontrada depois de copiar"))
        for o, n, dist in pares:
            if workset is not None:
                try:
                    p = n.get_Parameter(DB.BuiltInParameter.ELEM_PARTITION_PARAM)
                    if p is not None and not p.IsReadOnly:
                        p.Set(workset.IntegerValue if hasattr(workset, "IntegerValue") else int(workset))
                except Exception:
                    pass
            gravar_registro(n, modulo=mod.get("id"), link=link.tipo, linkInst=link.uid, origem=o.uid,
                            foto=json.dumps(foto(o.e, o.nivel), ensure_ascii=False), por=por)
            res["copiados"].append((o, n))
            if dist > TOL_PES:
                res["avisos"].append((o, n, u"posição difere {:.0f} mm".format(dist * 304.8)))
        feitos += len(lote)
        if progresso:
            progresso(feitos, len(alvo))
    return res


# ------------------------------------------------------------------ worksets
def worksets(doc):
    if not doc.IsWorkshared:
        return []
    ws = DB.FilteredWorksetCollector(doc).OfKind(DB.WorksetKind.UserWorkset).ToWorksets()
    return sorted(ws, key=lambda w: w.Name.lower())


def workset_ativo(doc):
    if not doc.IsWorkshared:
        return None
    return doc.GetWorksetTable().GetActiveWorksetId()
