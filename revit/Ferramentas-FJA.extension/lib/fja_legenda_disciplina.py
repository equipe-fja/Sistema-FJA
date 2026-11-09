# -*- coding: utf-8 -*-
"""Legenda de pontos por disciplina (LOG, AUT, SON...): cria/atualiza uma vista de
legenda com todas as familias cujo nome comeca com o prefixo da disciplina (ex.:
'(LOG)') e que aparecem em vistas colocadas em pranchas (um item por tipo). Cada
simbolo e pintado com a cor que o elemento tem nessas vistas (sobreposicao do
elemento, filtros da vista ou do modelo de vista, sobreposicao da categoria ou
estilo de objeto, nessa ordem). Elementos desenhados a mao na vista sao preservados.

Uso no script do botao:
    from fja_legenda_disciplina import main
    main({"botao": u"Legenda LOG", "alvo": u"LOG - LEGENDA DE PONTOS DE L\u00d3GICA", ...})
"""
import math
import os
import re
import System
from System.Collections.Generic import List
from Autodesk.Revit import DB
from Autodesk.Revit.DB.ExtensibleStorage import (
    Schema, SchemaBuilder, Entity, AccessLevel,
)

# Configuracao da disciplina (preenchida por configurar() a cada execucao):
#   botao    - nome do botao (titulo das janelas e da transacao)
#   alvo     - nome da vista de legenda criada/atualizada
#   titulo   - texto do titulo dentro da legenda
#   familias - prefixos do nome da familia que entram (ex.: ("(LOG)",))
#   vistas   - prefixos do nome das vistas preferidas para ler a cor
#   bases    - inicio do nome (sem acento) das legendas que servem de base, em ordem
#   guid, schema, mark - marca gravada nos elementos criados (Extensible Storage)
#   cor      - cor usada quando nao der para ler a do projeto
C = {}
FIELD = "Origem"
BASES_GERAIS = ("LOG - LEGENDA GRAFICA", "ELE - LEGENDA GRAFICA")


def configurar(cfg):
    C.clear()
    C.update(cfg)
    C["familias"] = tuple(norm_ascii(p) for p in cfg["familias"])
    C["vistas"] = tuple(norm_ascii(p) for p in cfg.get("vistas", ()))
    C["bases"] = tuple(norm_ascii(p) for p in cfg.get("bases", ())) + BASES_GERAIS
    # prefixo das familias de simbolo de legenda: "(SON) (LEG) ANTENA DE MICROFONE"
    C["prefixo_leg"] = cfg.get("prefixo_leg") or cfg["familias"][0]


LEGEND_VIEW_PLAN = -8

MM_ROW_STEP = 6.7
MM_ROW_PAD = 1.4
MM_SYM_X = 8.0
MM_TEXT_X = 16.0
MM_COL_W = 84.0
MM_TEXT_W = 64.0
MM_UNDERLINE = 41.4
MM_TITLE_LINE = 4.7
MM_FIRST_ROW = 1.0
MAX_ROWS_PER_COL = 14
MM_PLAN_MAX = 12.0      # maior medida de um simbolo copiado da planta
MM_PLAN_MIN = 4.5       # menor medida (simbolo tirado do modelo em escala real fica minusculo no papel)


def norm_ascii(s):
    if not s:
        return ""
    rep = {u"\u00c0": "A", u"\u00c1": "A", u"\u00c2": "A", u"\u00c3": "A", u"\u00c4": "A",
           u"\u00c8": "E", u"\u00c9": "E", u"\u00ca": "E", u"\u00cb": "E",
           u"\u00cc": "I", u"\u00cd": "I", u"\u00ce": "I", u"\u00cf": "I",
           u"\u00d2": "O", u"\u00d3": "O", u"\u00d4": "O", u"\u00d5": "O", u"\u00d6": "O",
           u"\u00d9": "U", u"\u00da": "U", u"\u00db": "U", u"\u00dc": "U", u"\u00c7": "C"}
    out = []
    for ch in s.upper().strip():
        if ord(ch) < 128:
            out.append(ch)
        elif ch in rep:
            out.append(rep[ch])
    return "".join(out)


def elem_name(e):
    return DB.Element.Name.__get__(e)


# ---------------------------------------------------------------- marca
def get_schema():
    guid = System.Guid(C["guid"])
    sc = Schema.Lookup(guid)
    if sc is not None:
        return sc
    sb = SchemaBuilder(guid)
    sb.SetSchemaName(C["schema"])
    sb.SetReadAccessLevel(AccessLevel.Public)
    sb.SetWriteAccessLevel(AccessLevel.Public)
    sb.AddSimpleField(FIELD, System.String)
    return sb.Finish()


def mark(elem, schema):
    ent = Entity(schema)
    ent.Set[System.String](FIELD, C["mark"])
    elem.SetEntity(ent)


def is_marked(elem, schema):
    try:
        ent = elem.GetEntity(schema)
        return ent is not None and ent.IsValid() and ent.Get[System.String](FIELD) == C["mark"]
    except Exception:
        return False



# ---------------------------------------------------------------- simbolos aninhados
# As familias desenham o simbolo em planta com anotacoes genericas aninhadas,
# ligadas/desligadas por parametros da familia e por nivel de detalhe.
# Um componente de legenda so mostra os valores padrao da familia, entao a legenda
# monta o simbolo com essas mesmas anotacoes, como numa instancia real.
DETAIL_BITS = {
    DB.ViewDetailLevel.Coarse: 8192,
    DB.ViewDetailLevel.Medium: 16384,
    DB.ViewDetailLevel.Fine: 32768,
}


class _NaoSobrescrever(DB.IFamilyLoadOptions):
    """Carrega familias novas; se ja existir uma com o mesmo nome, mantem a do projeto."""

    def OnFamilyFound(self, familyInUse, overwriteParameterValues):
        return False, False

    def OnSharedFamilyFound(self, sharedFamily, familyInUse, source, overwriteParameterValues):
        return False, DB.FamilySource.Project, False


def _int_param(elem, bip):
    try:
        p = elem.get_Parameter(bip)
        return p.AsInteger() if p is not None else None
    except Exception:
        return None


def _project_family_names(doc):
    return set(f.Name for f in DB.FilteredElementCollector(doc).OfClass(DB.Family))


def _is_open_in_editor(doc, fam_name):
    for d in doc.Application.Documents:
        try:
            if d.IsFamilyDocument and d.Title in (fam_name, fam_name + ".rfa"):
                return True
        except Exception:
            pass
    return False


def read_nested_symbols(doc, family, detail_bit, loaded_names):
    """Le as anotacoes genericas aninhadas da familia (sem alterar nada nela) e
    carrega no projeto as familias de simbolo que ainda nao existem. Deve rodar
    FORA de transacao. Retorna lista de especificacoes (pode ser vazia)."""
    specs = []
    if _is_open_in_editor(doc, family.Name):
        # EditFamily devolveria a janela aberta pelo usuario e o Close(False) abaixo a
        # fecharia sem salvar
        raise Exception(u"fam\u00edlia aberta no editor do Revit; feche-a e rode o bot\u00e3o de novo")
    fdoc = doc.EditFamily(family)
    try:
        fm = fdoc.FamilyManager
        sym_cats = (DB.ElementId(DB.BuiltInCategory.OST_GenericAnnotation),
                    DB.ElementId(DB.BuiltInCategory.OST_DetailComponents))
        for e in DB.FilteredElementCollector(fdoc).OfClass(DB.FamilyInstance):
            if e.Category is None or e.Category.Id not in sym_cats:
                continue
            bits = _int_param(e, DB.BuiltInParameter.GEOM_VISIBILITY_PARAM)
            if bits is not None and bits >= 8192 and not (bits & detail_bit):
                continue  # copia de outro nivel de detalhe
            nsym = e.Symbol
            vis_host = None
            pvis = e.get_Parameter(DB.BuiltInParameter.IS_VISIBLE_PARAM)
            if pvis is not None:
                a = fm.GetAssociatedFamilyParameter(pvis)
                if a is not None:
                    vis_host = a.Definition.Name
            pmap = []
            for ip in e.Parameters:
                if pvis is not None and ip.Id == pvis.Id:
                    continue
                try:
                    a = fm.GetAssociatedFamilyParameter(ip)
                except Exception:
                    a = None
                if a is not None:
                    pmap.append((ip.Definition.Name, a.Definition.Name))
            tr = e.GetTransform()
            specs.append({
                "fam": nsym.FamilyName, "type": elem_name(nsym),
                "vis_host": vis_host, "own_visible": _int_param(e, DB.BuiltInParameter.IS_VISIBLE_PARAM) != 0,
                "pt": tr.Origin, "angle": math.atan2(tr.BasisX.Y, tr.BasisX.X),
                "mirrored": bool(e.Mirrored), "pmap": pmap,
            })
            if nsym.FamilyName not in loaded_names and not _is_open_in_editor(doc, nsym.FamilyName):
                ndoc = fdoc.EditFamily(nsym.Family)
                try:
                    ndoc.LoadFamily(doc, _NaoSobrescrever())
                finally:
                    ndoc.Close(False)
                loaded_names.add(nsym.FamilyName)
    finally:
        fdoc.Close(False)
    return specs


def _host_param(rep, sym, name):
    p = rep.LookupParameter(name)
    if p is None:
        p = sym.LookupParameter(name)
    return p


def _copy_value(src, dst, mesmo_doc=True):
    if src is None or dst is None or dst.IsReadOnly or src.StorageType != dst.StorageType:
        return
    st = src.StorageType
    if st == DB.StorageType.ElementId and not mesmo_doc:
        return  # id de outro documento nao vale aqui
    if st == DB.StorageType.Integer:
        dst.Set(src.AsInteger())
    elif st == DB.StorageType.Double:
        dst.Set(src.AsDouble())
    elif st == DB.StorageType.String:
        dst.Set(src.AsString() or "")
    elif st == DB.StorageType.ElementId:
        dst.Set(src.AsElementId())


def visible_specs(specs, rep, sym):
    out = []
    for s in specs:
        if s["vis_host"]:
            p = _host_param(rep, sym, s["vis_host"])
            if p is None or p.StorageType != DB.StorageType.Integer or p.AsInteger() != 1:
                continue
        elif not s["own_visible"]:
            continue
        out.append(s)
    return out


def _find_symbol(doc, fam_name, type_name):
    for s in DB.FilteredElementCollector(doc).OfClass(DB.FamilySymbol):
        if s.FamilyName == fam_name and elem_name(s) == type_name:
            return s
    return None


def place_nested_symbols(doc, view, specs, rep, sym):
    """Cria as anotacoes na vista (na origem). Retorna os ids criados."""
    ids = []
    for s in specs:
        nsym = _find_symbol(doc, s["fam"], s["type"])
        if nsym is None:
            continue
        if not nsym.IsActive:
            nsym.Activate()
            doc.Regenerate()
        inst = doc.Create.NewFamilyInstance(DB.XYZ(s["pt"].X, s["pt"].Y, 0), nsym, view)
        for nested_name, host_name in s["pmap"]:
            _copy_value(_host_param(rep, sym, host_name), inst.LookupParameter(nested_name))
        if abs(s["angle"]) > 1e-6:
            axis = DB.Line.CreateBound(DB.XYZ(s["pt"].X, s["pt"].Y, 0), DB.XYZ(s["pt"].X, s["pt"].Y, 1))
            DB.ElementTransformUtils.RotateElement(doc, inst.Id, axis, s["angle"])
        if s["mirrored"]:
            plane = DB.Plane.CreateByNormalAndOrigin(DB.XYZ.BasisX, DB.XYZ(s["pt"].X, s["pt"].Y, 0))
            DB.ElementTransformUtils.MirrorElements(doc, List[DB.ElementId]([inst.Id]), plane, False)
        ids.append(inst.Id)
    return ids


def union_bbox(doc, ids, view):
    bb = None
    for i in ids:
        b = doc.GetElement(i).get_BoundingBox(view)
        if b is None:
            continue
        if bb is None:
            bb = [b.Min.X, b.Min.Y, b.Max.X, b.Max.Y]
        else:
            bb = [min(bb[0], b.Min.X), min(bb[1], b.Min.Y), max(bb[2], b.Max.X), max(bb[3], b.Max.Y)]
    return bb

def is_view_content(e):
    """Textos, linhas, componentes de legenda e anotacoes/detalhes da vista."""
    if isinstance(e, (DB.TextNote, DB.CurveElement, DB.FamilyInstance, DB.FilledRegion)):
        return True
    try:
        return e.Category is not None and e.Category.Id == DB.ElementId(DB.BuiltInCategory.OST_LegendComponents)
    except Exception:
        return False


def any_legend_component(doc, views):
    for v in views:
        if v is None:
            continue
        comps = (DB.FilteredElementCollector(doc, v.Id)
                 .OfCategory(DB.BuiltInCategory.OST_LegendComponents)
                 .WhereElementIsNotElementType().ToElements())
        if comps:
            return v, comps[0]
    return None, None



# ---------------------------------------------------------------- cor no projeto
def _rgb(c):
    try:
        if c is not None and c.IsValid:
            return (int(c.Red), int(c.Green), int(c.Blue))
    except Exception:
        pass
    return None


def _template_for(doc, view, bip):
    """Vista que controla o parametro (o modelo de vista, se ele controlar)."""
    try:
        tid = view.ViewTemplateId
        if tid is not None and tid != DB.ElementId.InvalidElementId:
            tpl = doc.GetElement(tid)
            if tpl is not None:
                nc = tpl.GetNonControlledTemplateParameterIds()
                if not any(i == DB.ElementId(bip) for i in nc):
                    return tpl
    except Exception:
        pass
    return view


def _filter_ids(v):
    try:
        return list(v.GetOrderedFilters())  # ordem de prioridade (Revit 2021+)
    except Exception:
        return list(v.GetFilters())


def _filter_matches(f, e):
    try:
        if not any(c == e.Category.Id for c in f.GetCategories()):
            return False
    except Exception:
        pass
    if isinstance(f, DB.SelectionFilterElement):
        return any(i == e.Id for i in f.GetElementIds())
    try:
        ef = f.GetElementFilter()
    except Exception:
        ef = None
    if ef is None:
        return True
    try:
        return ef.PassesFilter(e)
    except Exception:
        return False


def effective_color(doc, view, e, cache):
    """Cor das linhas do elemento na vista: sobreposicao do elemento > filtros
    (na ordem da vista) > sobreposicao da categoria > estilo de objeto."""
    c = _rgb(view.GetElementOverrides(e.Id).ProjectionLineColor)
    if c:
        return c, u"sobreposi\u00e7\u00e3o do elemento"
    vkey = view.Id.ToString()
    if vkey not in cache:
        fv = _template_for(doc, view, DB.BuiltInParameter.VIS_GRAPHICS_FILTERS)
        cv = _template_for(doc, view, DB.BuiltInParameter.VIS_GRAPHICS_MODEL)
        flist = []
        for fid in _filter_ids(fv):
            try:
                if hasattr(fv, "GetIsFilterEnabled") and not fv.GetIsFilterEnabled(fid):
                    continue
                if not fv.GetFilterVisibility(fid):
                    continue
                col = _rgb(fv.GetFilterOverrides(fid).ProjectionLineColor)
                if col:
                    flist.append((doc.GetElement(fid), col))
            except Exception:
                pass
        cache[vkey] = (flist, cv)
    flist, cv = cache[vkey]
    for f, col in flist:
        if f is not None and _filter_matches(f, e):
            return col, u"filtro '{}'".format(elem_name(f))
    cat = e.Category
    if cat is not None:
        try:
            c = _rgb(cv.GetCategoryOverrides(cat.Id).ProjectionLineColor)
            if c:
                return c, u"sobreposi\u00e7\u00e3o da categoria"
        except Exception:
            pass
        c = _rgb(cat.LineColor)
        if c:
            return c, u"estilo de objeto"
    return C["cor"], u"padr\u00e3o"


# ---------------------------------------------------------------- dados
SKIP_VIEW_TYPES = (DB.ViewType.Legend, DB.ViewType.Schedule, DB.ViewType.DraftingView,
                   DB.ViewType.DrawingSheet, DB.ViewType.PanelSchedule,
                   DB.ViewType.ColumnSchedule)


def sheet_views(doc):
    views = {}
    for sh in DB.FilteredElementCollector(doc).OfClass(DB.ViewSheet):
        try:
            if sh.IsTemplate or sh.IsPlaceholder:
                continue
            for vid in sh.GetAllPlacedViews():
                v = doc.GetElement(vid)
                if v is None or v.IsTemplate or v.ViewType in SKIP_VIEW_TYPES:
                    continue
                views[vid.ToString()] = v
        except Exception:
            pass
    return list(views.values())


def item_label(sym, multi):
    """Nome da familia sem o prefixo '(LOG)'; se a familia tiver mais de um tipo
    na legenda, acrescenta o nome do tipo."""
    nome = re.sub(r"\.\d{4}$", "", sym.FamilyName.strip())  # sufixo de copia de seguranca (.0001)
    for pf in C["familias"]:
        if norm_ascii(nome).startswith(pf):
            nome = nome[len(pf):].strip(u" -_")
            break
    if multi:
        nome = u"{} - {}".format(nome, elem_name(sym).strip())
    return nome.upper()


def collect_items(doc):
    """Um item por tipo de familia '(LOG)' visivel em alguma vista de prancha."""
    types = {}
    cache = {}
    for v in sheet_views(doc):
        prefer = norm_ascii(elem_name(v)).startswith(C["vistas"]) if C["vistas"] else False
        try:
            col = DB.FilteredElementCollector(doc, v.Id).OfClass(DB.FamilyInstance)
        except Exception:
            continue
        for inst in col:
            if inst.SuperComponent is not None:
                continue
            sym = inst.Symbol
            if not norm_ascii(sym.FamilyName).startswith(C["familias"]):
                continue
            key = sym.Id.ToString()
            it = types.get(key)
            if it is None:
                it = types[key] = {"sym": sym, "rep": inst, "inst": set(), "views": set(),
                                   "cores": {}}
            it["inst"].add(inst.Id.ToString())
            it["views"].add(elem_name(v))
            # vista de planta onde o elemento aparece (para copiar o desenho se a
            # familia nao tiver anotacao aninhada); prefere as vistas LOG
            if isinstance(v, DB.ViewPlan) and (it.get("geo") is None or (prefer and not it["geo_pref"])):
                it["geo"], it["geo_pref"], it["geo_inst"] = v, prefer, inst
            try:
                cor, origem = effective_color(doc, v, inst, cache)
            except Exception:
                cor, origem = C["cor"], u"padr\u00e3o"
            ck = (cor, origem)
            n = it["cores"].get(ck, [0, 0])
            n[0 if prefer else 1] += 1
            it["cores"][ck] = n
    fam_types = {}
    for it in types.values():
        fk = it["sym"].Family.Id.ToString()
        fam_types[fk] = fam_types.get(fk, 0) + 1
    items = []
    for it in types.values():
        # cor mais frequente nas vistas LOG; se nao aparecer nelas, nas demais
        best = sorted(it["cores"].items(), key=lambda kv: (-kv[1][0], -kv[1][1]))[0][0]
        sym = it["sym"]
        items.append({
            "sym": sym, "rep": it["rep"], "count": len(it["inst"]), "views": len(it["views"]),
            "cor": best[0], "cor_origem": best[1],
            "label": item_label(sym, fam_types[sym.Family.Id.ToString()] > 1),
            "geo": it.get("geo"), "geo_inst": it.get("geo_inst"),
        })
    for x in items:
        x["sort"] = (norm_ascii(x["sym"].Category.Name if x["sym"].Category else ""), norm_ascii(x["label"]))
    items.sort(key=lambda x: x["sort"])
    return items


# ---------------------------------------------------------------- desenho copiado da planta
def _flat(p):
    return DB.XYZ(p.X, p.Y, 0)


def _plan_curves(inst, view):
    """Linhas do elemento como aparecem na vista (projetadas no plano), sem repetir."""
    opt = DB.Options()
    opt.View = view
    geom = inst.get_Geometry(opt)
    out = []
    seen = set()
    tol = 1.0 / 304.8 * 0.2  # 0,2 mm

    def key(a, b, extra=""):
        ka = (round(a.X / tol), round(a.Y / tol))
        kb = (round(b.X / tol), round(b.Y / tol))
        return (min(ka, kb), max(ka, kb), extra)

    def add_curve(c):
        try:
            if isinstance(c, DB.Arc) and not c.IsBound:
                if abs(c.Normal.Z) < 0.5:
                    return
                k = ("circ", round(c.Center.X / tol), round(c.Center.Y / tol), round(c.Radius / tol))
                if k not in seen:
                    seen.add(k)
                    out.append(DB.Arc.Create(_flat(c.Center), c.Radius, 0.0, 2.0 * math.pi,
                                             DB.XYZ.BasisX, DB.XYZ.BasisY))
                return
            if not c.IsBound:
                return
            if isinstance(c, DB.Line):
                pts = [c.GetEndPoint(0), c.GetEndPoint(1)]
            elif isinstance(c, DB.Arc) and abs(c.Normal.Z) > 0.99:
                a, b, m = _flat(c.GetEndPoint(0)), _flat(c.GetEndPoint(1)), _flat(c.Evaluate(0.5, True))
                if a.DistanceTo(b) < tol:
                    return
                k = key(a, b, (round(m.X / tol), round(m.Y / tol)))
                if k not in seen:
                    seen.add(k)
                    out.append(DB.Arc.Create(a, b, m))
                return
            else:
                pts = list(c.Tessellate())
            for i in range(len(pts) - 1):
                a, b = _flat(pts[i]), _flat(pts[i + 1])
                if a.DistanceTo(b) < tol:
                    continue
                k = key(a, b)
                if k not in seen:
                    seen.add(k)
                    out.append(DB.Line.CreateBound(a, b))
        except Exception:
            pass

    def walk(gs):
        if gs is None:
            return
        for g in gs:
            if isinstance(g, DB.GeometryInstance):
                walk(g.GetInstanceGeometry())
            elif isinstance(g, DB.Solid):
                for e in g.Edges:
                    add_curve(e.AsCurve())
            elif isinstance(g, DB.Curve):
                add_curve(g)
            elif isinstance(g, DB.PolyLine):
                pts = g.GetCoordinates()
                for i in range(pts.Count - 1):
                    add_curve(DB.Line.CreateBound(pts[i], pts[i + 1]))

    walk(geom)
    return out


def draw_plan_copy(doc, target, it):
    """Desenha na legenda, com linhas de detalhe, o elemento como ele aparece na
    planta. Elementos grandes (ex.: rack) sao reduzidos para caber em
    MM_PLAN_MAX mm de papel. Retorna os ids criados."""
    if it.get("geo") is None or it.get("geo_inst") is None:
        return []
    curves = _plan_curves(it["geo_inst"], it["geo"])
    if not curves:
        return []
    xs, ys = [], []
    for c in curves:
        for p in c.Tessellate():
            xs.append(p.X)
            ys.append(p.Y)
    dim = max(max(xs) - min(xs), max(ys) - min(ys))
    lim = MM_PLAN_MAX * target.Scale / 304.8
    if dim > lim:
        ctr = DB.XYZ((max(xs) + min(xs)) / 2.0, (max(ys) + min(ys)) / 2.0, 0)
        tf = DB.Transform.CreateTranslation(ctr).Multiply(
            DB.Transform.Identity.ScaleBasis(lim / dim)).Multiply(
            DB.Transform.CreateTranslation(ctr.Negate()))
        out = []
        for c in curves:
            try:
                out.append(c.CreateTransformed(tf))
            except Exception:
                pass
        curves = out
        it["reduzido"] = True
    ids = []
    for c in curves:
        try:
            ids.append(doc.Create.NewDetailCurve(target, c).Id)
        except Exception:
            pass
    return ids


def paint(view, elem_id, rgb):
    c = DB.Color(rgb[0], rgb[1], rgb[2])
    o = view.GetElementOverrides(elem_id)
    o.SetProjectionLineColor(c)
    o.SetCutLineColor(c)
    for setter in ("SetSurfaceForegroundPatternColor", "SetSurfaceBackgroundPatternColor",
                   "SetCutForegroundPatternColor", "SetCutBackgroundPatternColor"):
        try:
            getattr(o, setter)(c)
        except Exception:
            pass
    view.SetElementOverrides(elem_id, o)


# ---------------------------------------------------------------- familias de simbolo de legenda
# Cada item da legenda usa uma familia de anotacao generica "(SON) (LEG) ANTENA DE
# MICROFONE" (prefixo da disciplina + (LEG) + nome sem prefixo). Se o projeto ja tiver
# uma com esse nome, ela e usada como esta (pode ter sido ajustada a mao); senao e
# criada a partir do simbolo da familia (anotacoes aninhadas ou desenho da planta).
def leg_name(sym):
    return u"{} (LEG) {}".format(C["prefixo_leg"], item_label(sym, False))


def _family_by_name(doc, name):
    alvo = norm_ascii(name)
    for f in DB.FilteredElementCollector(doc).OfClass(DB.Family):
        if norm_ascii(f.Name) == alvo:
            return f
    return None


def _first_symbol(doc, fam):
    for i in fam.GetFamilySymbolIds():
        return doc.GetElement(i)
    return None


def _template_anotacao(app):
    achados = []
    for root, dirs, files in os.walk(app.FamilyTemplatePath):
        for f in files:
            l = norm_ascii(f)
            if l.endswith(".RFT") and ("GENERIC ANNOTATION" in l or "ANOTACAO GENERICA" in l):
                achados.append(os.path.join(root, f))
    achados.sort(key=lambda p: (0 if "PORTUGUESE" in norm_ascii(p) else 1 if "METRIC" in norm_ascii(p) else 2, p))
    return achados[0] if achados else None


def _family_view(fdoc):
    ok = (DB.ViewType.FloorPlan, DB.ViewType.DraftingView, DB.ViewType.Detail, DB.ViewType.Legend,
          DB.ViewType.CeilingPlan, DB.ViewType.EngineeringPlan)
    resto = None
    for v in DB.FilteredElementCollector(fdoc).OfClass(DB.View):
        if v.IsTemplate:
            continue
        if v.ViewType in ok:
            return v
        if resto is None and v.ViewType not in (DB.ViewType.ProjectBrowser, DB.ViewType.SystemBrowser,
                                                 DB.ViewType.Undefined, DB.ViewType.Internal):
            resto = v
    return resto


def _curvas_da_geometria(g, tf, out):
    """Curvas (linhas, arcos, polilinhas) de uma geometria, ja transformadas."""
    for o in g:
        if isinstance(o, DB.GeometryInstance):
            _curvas_da_geometria(o.GetSymbolGeometry(), tf.Multiply(o.Transform), out)
        elif isinstance(o, DB.Curve):
            try:
                out.append(o.CreateTransformed(tf))
            except Exception:
                pass
        elif isinstance(o, DB.PolyLine):
            pts = [tf.OfPoint(p) for p in o.GetCoordinates()]
            for a, b in zip(pts, pts[1:]):
                if a.DistanceTo(b) > 1e-6:
                    out.append(DB.Line.CreateBound(a, b))


def criar_familia_leg(doc, it, specs, name, pasta, tamanho_mm=None):
    """Cria e carrega no projeto a familia de anotacao 'name' com o simbolo do item.
    Deve rodar FORA de transacao. Retorna a familia carregada (ou None).
    tamanho_mm: maior medida do simbolo no papel; None = tamanho original (anotacoes
    aninhadas como estao; desenho da planta limitado a MM_PLAN_MIN..MM_PLAN_MAX).
    Com tamanho, as anotacoes aninhadas viram linhas (preenchimentos e textos delas se perdem)."""
    app = doc.Application
    tpl = _template_anotacao(app)
    if not tpl:
        raise Exception(u"modelo de família 'Anotação genérica' não encontrado")
    escala = float((it.get("geo") or doc.ActiveView).Scale or 50)
    fdoc = app.NewFamilyDocument(tpl)
    try:
        # anotacoes aninhadas: carrega na familia nova as mesmas familias de simbolo
        carregadas = set()
        for sp in specs:
            if sp["fam"] in carregadas:
                continue
            psym = _find_symbol(doc, sp["fam"], sp["type"])
            if psym is None:
                continue
            ndoc = doc.EditFamily(psym.Family)
            try:
                ndoc.LoadFamily(fdoc, _NaoSobrescrever())
            finally:
                ndoc.Close(False)
            carregadas.add(sp["fam"])
        t = DB.Transaction(fdoc, u"Simbolo de legenda")
        t.Start()
        view = _family_view(fdoc)
        for tn in list(DB.FilteredElementCollector(fdoc).OfClass(DB.TextNote)):
            fdoc.Delete(tn.Id)  # texto de exemplo do modelo
        n = 0
        for sp in specs:
            nsym = _find_symbol(fdoc, sp["fam"], sp["type"])
            if nsym is None:
                continue
            try:
                if not nsym.IsActive:
                    nsym.Activate()
                    fdoc.Regenerate()
                # anotacao: distancias no papel = distancia no modelo / escala da vista
                pt = DB.XYZ(sp["pt"].X / escala, sp["pt"].Y / escala, 0)
                inst = fdoc.FamilyCreate.NewFamilyInstance(pt, nsym, view)
                for nested_name, host_name in sp["pmap"]:
                    _copy_value(_host_param(it["rep"], it["sym"], host_name), inst.LookupParameter(nested_name), False)
                if abs(sp["angle"]) > 1e-6:
                    axis = DB.Line.CreateBound(pt, DB.XYZ(pt.X, pt.Y, 1))
                    DB.ElementTransformUtils.RotateElement(fdoc, inst.Id, axis, sp["angle"])
                if sp["mirrored"]:
                    plane = DB.Plane.CreateByNormalAndOrigin(DB.XYZ.BasisX, pt)
                    DB.ElementTransformUtils.MirrorElements(fdoc, List[DB.ElementId]([inst.Id]), plane, False)
                n += 1
            except Exception:
                pass
        if n and tamanho_mm:
            # redimensiona: troca as anotacoes aninhadas pelas linhas delas, na escala pedida
            fdoc.Regenerate()
            opt = DB.Options()
            opt.View = view
            curvas, insts = [], []
            for fi in DB.FilteredElementCollector(fdoc, view.Id).OfClass(DB.FamilyInstance):
                insts.append(fi.Id)
                g = fi.get_Geometry(opt)
                if g is not None:
                    _curvas_da_geometria(g, DB.Transform.Identity, curvas)
            if curvas:
                xs, ys = [], []
                for c in curvas:
                    for p in c.Tessellate():
                        xs.append(p.X)
                        ys.append(p.Y)
                dim = max(max(xs) - min(xs), max(ys) - min(ys))
                if dim > 1e-9:
                    ctr = DB.XYZ((max(xs) + min(xs)) / 2.0, (max(ys) + min(ys)) / 2.0, 0)
                    tf = DB.Transform.Identity.ScaleBasis((tamanho_mm / 304.8) / dim).Multiply(DB.Transform.CreateTranslation(ctr.Negate()))
                    fdoc.Delete(List[DB.ElementId](insts))
                    n = 0
                    for c in curvas:
                        try:
                            fdoc.FamilyCreate.NewDetailCurve(view, c.CreateTransformed(tf))
                            n += 1
                        except Exception:
                            pass
                    it["redimensionado"] = True
        if n == 0 and it.get("geo") is not None and it.get("geo_inst") is not None:
            # sem anotacao aninhada: linhas do elemento como aparece na planta, no tamanho
            # do papel (limitado a MM_PLAN_MAX), centradas na origem da familia
            curves = _plan_curves(it["geo_inst"], it["geo"])
            if curves:
                xs, ys = [], []
                for c in curves:
                    for p in c.Tessellate():
                        xs.append(p.X)
                        ys.append(p.Y)
                ctr = DB.XYZ((max(xs) + min(xs)) / 2.0, (max(ys) + min(ys)) / 2.0, 0)
                dim = max(max(xs) - min(xs), max(ys) - min(ys)) / escala
                f = 1.0 / escala
                lim, mini = MM_PLAN_MAX / 304.8, MM_PLAN_MIN / 304.8
                if tamanho_mm and dim > 1e-9:
                    f *= (tamanho_mm / 304.8) / dim
                    it["redimensionado"] = True
                elif dim > lim:
                    f *= lim / dim
                    it["reduzido"] = True
                elif 0 < dim < mini:
                    f *= mini / dim
                    it["ampliado"] = True
                tf = DB.Transform.Identity.ScaleBasis(f).Multiply(DB.Transform.CreateTranslation(ctr.Negate()))
                for c in curves:
                    try:
                        fdoc.FamilyCreate.NewDetailCurve(view, c.CreateTransformed(tf))
                        n += 1
                    except Exception:
                        pass
        t.Commit()
        if n == 0:
            return None
        if not os.path.isdir(pasta):
            os.makedirs(pasta)
        caminho = os.path.join(pasta, name + ".rfa")
        so = DB.SaveAsOptions()
        so.OverwriteExistingFile = True
        fdoc.SaveAs(caminho, so)
        fdoc.LoadFamily(doc, _NaoSobrescrever())
    finally:
        fdoc.Close(False)
    return _family_by_name(doc, name)


# ---------------------------------------------------------------- vistas
def find_legends(doc):
    """(base, alvo, outras). Base = a primeira legenda que bate com C["bases"]."""
    target = None
    cands = {}
    others = []
    for v in DB.FilteredElementCollector(doc).OfClass(DB.View):
        try:
            if v.ViewType != DB.ViewType.Legend or v.IsTemplate:
                continue
        except Exception:
            continue
        n = norm_ascii(elem_name(v))
        if n == norm_ascii(C["alvo"]):
            target = v
            continue
        others.append(v)
        for i, b in enumerate(C["bases"]):
            if n.startswith(b) and i not in cands:
                cands[i] = v
    src = cands[min(cands)] if cands else None
    if src is not None:
        others.remove(src)
    return src, target, others


def reference_styles(doc, ref_view):
    """Tipo de texto do titulo ('LEGENDA ...'), do corpo (o mais usado nos demais
    textos) e estilo do sublinhado, lidos da legenda grafica do LOG."""
    title_type = None
    counts = {}
    for tn in DB.FilteredElementCollector(doc, ref_view.Id).OfClass(DB.TextNote):
        txt = norm_ascii(tn.Text)
        if txt.startswith("LEGENDA") and title_type is None:
            title_type = tn.GetTypeId()
        elif not txt.startswith("NOTAS"):
            k = tn.GetTypeId().ToString()
            counts[k] = (counts.get(k, (0, None))[0] + 1, tn.GetTypeId())
    body_type = None
    if counts:
        body_type = sorted(counts.values(), key=lambda x: -x[0])[0][1]
    body_type = body_type or doc.GetDefaultElementTypeId(DB.ElementTypeGroup.TextNoteType)
    title_type = title_type or body_type
    line_style = None
    for ce in DB.FilteredElementCollector(doc, ref_view.Id).OfClass(DB.CurveElement):
        try:
            c = ce.GeometryCurve
            p0, p1 = c.GetEndPoint(0), c.GetEndPoint(1)
            if abs(p0.Y - p1.Y) < 1e-4 and 8.0 < abs(p1.X - p0.X) < 22.0:
                line_style = ce.LineStyle
                break
        except Exception:
            pass
    return title_type, body_type, line_style


# ---------------------------------------------------------------- principal
def atualizar_legenda(doc, items=None, opcoes=None):
    """Executa tudo numa transacao. Retorna um dicionario com o resultado.
    opcoes (janela de previa): {"tamanho_mm": float|None, "redim": set(ids de familia) -
    familias (LEG) refeitas nesse tamanho, "passo_mm": altura minima da linha}"""
    opcoes = opcoes or {}
    passo = float(opcoes.get("passo_mm") or MM_ROW_STEP)
    redim = set(opcoes.get("redim") or ())
    if items is None:
        items = collect_items(doc)
    if not items:
        return {"ok": False, "msg": u"Nenhuma fam\u00edlia '{}' aparece nas vistas das pranchas.".format("' / '".join(C["familias"]))}

    src, target, others = find_legends(doc)
    base_view = src or target or (others[0] if others else None)
    if base_view is None:
        return {"ok": False, "msg": u"O projeto n\u00e3o tem nenhuma vista de legenda para servir de base."}
    # Componente de legenda (Revit nao cria um do zero pela API: copia um que ja
    # exista). So e necessario para familias sem anotacao aninhada.
    comp_view, comp_src = any_legend_component(doc, [src, target] + others)

    title_type, body_type, line_style = reference_styles(doc, src or base_view)

    # Familias de simbolo de legenda "(XXX) (LEG) NOME": usa as que ja existem no
    # projeto; as que faltam sao criadas a partir das anotacoes aninhadas da familia
    # (lidas fora de transacao, sem alterar nada nela) ou do desenho da planta
    detail_bit = DETAIL_BITS.get((target or base_view).DetailLevel, 16384)
    loaded_names = _project_family_names(doc)
    nested = {}
    nested_errors = []
    legs = {}
    leg_criadas, leg_existentes, leg_erros = [], [], []
    pasta_leg = os.path.join(System.IO.Path.GetTempPath(), "FJA_LEG")
    for it in items:
        fam = it["sym"].Family
        key = fam.Id.ToString()
        if key in legs:
            continue
        nome_leg = leg_name(it["sym"])
        lf = _family_by_name(doc, nome_leg)
        tam = opcoes.get("tamanho_mm") if key in redim else None
        if lf is not None and tam:
            # vai ser refeita no tamanho pedido: apaga a que existe
            td = DB.Transaction(doc, u"Refazer símbolo de legenda")
            td.Start()
            doc.Delete(lf.Id)
            td.Commit()
            lf = None
        if lf is not None:
            legs[key] = lf
            leg_existentes.append(lf.Name)
            continue
        try:
            nested[key] = read_nested_symbols(doc, fam, detail_bit, loaded_names)
        except Exception as ex:
            nested[key] = []
            nested_errors.append(u"{}: {}".format(fam.Name, ex))
        # redimensionar: o simbolo sai do desenho do elemento na planta (anotacoes aninhadas
        # incluidas), escalado para o tamanho pedido; sem planta, tenta as anotacoes
        specs_leg = visible_specs(nested[key], it["rep"], it["sym"])
        if tam and it.get("geo") is not None and it.get("geo_inst") is not None:
            specs_leg = []
        try:
            lf = criar_familia_leg(doc, it, specs_leg, nome_leg, pasta_leg, tam)
        except Exception as ex:
            lf = None
            leg_erros.append(u"{}: {}".format(nome_leg, ex))
        legs[key] = lf
        if lf is not None:
            leg_criadas.append(lf.Name)
        if tam and not it.get("redimensionado"):
            leg_erros.append(u"{}: não foi possível redimensionar (sem desenho na planta)".format(nome_leg))

    previous = set()
    col_info = None
    template_removido = None
    t = DB.Transaction(doc, u"Atualizar " + C["botao"])
    t.Start()
    try:
        schema = get_schema()
        created_view = False
        if target is None:
            target = doc.GetElement(base_view.Duplicate(DB.ViewDuplicateOption.Duplicate))
            target.Name = C["alvo"]
            created_view = True
            ids = List[DB.ElementId]()
            for e in DB.FilteredElementCollector(doc, target.Id).WhereElementIsNotElementType():
                if e.OwnerViewId == target.Id and is_view_content(e):
                    ids.Add(e.Id)
            if ids.Count:
                doc.Delete(ids)

        # Alguns simbolos aninhados sao DWG importados na familia: garante que a
        # categoria "Importados em familias" esteja visivel na legenda
        # (se um modelo de vista esconder essa categoria, a legenda deixa de usar o
        # modelo: o modelo herdado da legenda base nao e proprio para esta legenda)
        template_removido = None
        try:
            cid = DB.ElementId(DB.BuiltInCategory.OST_ImportObjectStyles)
            if target.GetCategoryHidden(cid):
                tid = target.ViewTemplateId
                if tid is not None and tid != DB.ElementId.InvalidElementId:
                    template_removido = elem_name(doc.GetElement(tid))
                    target.ViewTemplateId = DB.ElementId.InvalidElementId
                target.SetCategoryHidden(cid, False)
        except Exception:
            pass

        tpl_id = None
        if comp_src is not None:
            tpl_id = DB.ElementTransformUtils.CopyElements(
                comp_view, List[DB.ElementId]([comp_src.Id]), target,
                DB.Transform.Identity, DB.CopyPasteOptions())[0]
            try:
                doc.GetElement(tpl_id).DeleteEntity(schema)
            except Exception:
                pass

        ids = List[DB.ElementId]()
        for e in DB.FilteredElementCollector(doc, target.Id).WhereElementIsNotElementType():
            if e.Id != tpl_id and e.OwnerViewId == target.Id and is_marked(e, schema):
                if isinstance(e, DB.TextNote) and norm_ascii(e.Text) != norm_ascii(C["titulo"]):
                    previous.add(e.Text.strip())
                ids.Add(e.Id)
        if ids.Count:
            doc.Delete(ids)

        k = target.Scale / 304.8

        x0, y0 = 0.0, 0.0
        title = DB.TextNote.Create(doc, target.Id, DB.XYZ(x0 + 1.0 * k, y0, 0), C["titulo"], title_type)
        mark(title, schema)
        line_y = y0 - MM_TITLE_LINE * k
        ul = doc.Create.NewDetailCurve(target, DB.Line.CreateBound(
            DB.XYZ(x0, line_y, 0), DB.XYZ(x0 + MM_UNDERLINE * k, line_y, 0)))
        if line_style is not None:
            ul.LineStyle = line_style
        mark(ul, schema)

        def legend_component(sym):
            comp = doc.GetElement(DB.ElementTransformUtils.CopyElement(doc, tpl_id, DB.XYZ.Zero)[0])
            comp.get_Parameter(DB.BuiltInParameter.LEGEND_COMPONENT).Set(sym.Id)
            try:
                comp.get_Parameter(DB.BuiltInParameter.LEGEND_COMPONENT_VIEW).Set(LEGEND_VIEW_PLAN)
            except Exception:
                pass
            return comp

        rows = []
        text_dx = 0.0  # deslocamento comum dos textos (alinhados pela esquerda)
        prov_top = line_y - MM_FIRST_ROW * k
        for it in items:
            sym_ids = []
            lf = legs.get(it["sym"].Family.Id.ToString())
            lsym = _first_symbol(doc, lf) if lf is not None else None
            if lsym is not None:
                if not lsym.IsActive:
                    lsym.Activate()
                    doc.Regenerate()
                sym_ids = [doc.Create.NewFamilyInstance(DB.XYZ.Zero, lsym, target).Id]
                it["modo"] = "leg"
                it["leg"] = lf.Name
            vspecs = [] if sym_ids else visible_specs(nested.get(it["sym"].Family.Id.ToString(), []), it["rep"], it["sym"])
            if vspecs:
                sym_ids = place_nested_symbols(doc, target, vspecs, it["rep"], it["sym"])
                it["modo"] = "simbolos"
                doc.Regenerate()
                if sym_ids and union_bbox(doc, sym_ids, target) is None:
                    # anotacao sem nada visivel na legenda: descarta e tenta a planta
                    doc.Delete(List[DB.ElementId](sym_ids))
                    sym_ids = []
            if not sym_ids:
                sym_ids = draw_plan_copy(doc, target, it)
                it["modo"] = "planta"
            if not sym_ids and tpl_id is not None:
                sym_ids = [legend_component(it["sym"]).Id]
                it["modo"] = "componente"
            if not sym_ids:
                it["modo"] = "sem simbolo"
            for i in sym_ids:
                paint(target, i, it["cor"])
                mark(doc.GetElement(i), schema)

            opts = DB.TextNoteOptions(body_type)
            opts.HorizontalAlignment = DB.HorizontalTextAlignment.Left
            tn = DB.TextNote.Create(doc, target.Id, DB.XYZ(x0 + MM_TEXT_X * k, line_y, 0),
                                    it["label"], opts)
            try:
                tn.Width = MM_TEXT_W / 304.8
            except Exception:
                pass
            mark(tn, schema)
            doc.Regenerate()

            bb = union_bbox(doc, sym_ids, target)
            tbb = tn.get_BoundingBox(target)
            sym_h = (bb[3] - bb[1]) if bb else 0.0
            txt_h = (tbb.Max.Y - tbb.Min.Y) if tbb else 0.0
            row_h = max(passo * k, sym_h + MM_ROW_PAD * k, txt_h + MM_ROW_PAD * k)
            cy = prov_top - row_h / 2.0
            prov_top -= row_h
            dx_txt = 0.0
            if bb:
                # simbolo largo (ex.: camera com o cone de visao): encosta no inicio
                # da coluna e empurra o texto para a direita
                half = (bb[2] - bb[0]) / 2.0
                sx = x0 + max(MM_SYM_X * k, half)
                dx_txt = max(0.0, sx + half + 2.0 * k - (x0 + MM_TEXT_X * k))
                cur = DB.XYZ((bb[0] + bb[2]) / 2.0, (bb[1] + bb[3]) / 2.0, 0)
                DB.ElementTransformUtils.MoveElements(
                    doc, List[DB.ElementId](sym_ids), DB.XYZ(sx, cy, 0) - cur)
            if tbb:
                DB.ElementTransformUtils.MoveElement(
                    doc, tn.Id, DB.XYZ(0, cy - (tbb.Min.Y + tbb.Max.Y) / 2.0, 0))
            rows.append((sym_ids, tn, cy, row_h))
            it["sym_ids"] = list(sym_ids)
            text_dx = max(text_dx, dx_txt)

        heights = [r[3] for r in rows]
        split = len(rows)
        if len(rows) > MAX_ROWS_PER_COL:
            total = sum(heights)
            best = None
            acc = 0.0
            for s in range(1, len(rows)):
                acc += heights[s - 1]
                worst = max(acc, total - acc)
                if best is None or worst <= best + 1e-9:
                    best, split = worst, s
        col_info = [split, len(rows) - split]

        col_tops = [line_y - MM_FIRST_ROW * k] * 2
        # Textos alinhados pela esquerda: todos comecam no mesmo x, logo depois do
        # simbolo mais largo; a 2a coluna anda o mesmo tanto a mais
        if text_dx > 1e-9:
            DB.ElementTransformUtils.MoveElements(
                doc, List[DB.ElementId]([r[1].Id for r in rows]), DB.XYZ(text_dx, 0, 0))
        col_w = MM_COL_W * k + text_dx
        for idx, (sym_ids, tn, cy0, row_h) in enumerate(rows):
            col = 0 if idx < split else 1
            cy = col_tops[col] - row_h / 2.0
            col_tops[col] -= row_h
            d = DB.XYZ(col * col_w, cy - cy0, 0)
            if d.GetLength() > 1e-9:
                DB.ElementTransformUtils.MoveElements(
                    doc, List[DB.ElementId](list(sym_ids) + [tn.Id]), d)

        if tpl_id is not None:
            doc.Delete(tpl_id)
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise

    for it in items:
        bb = union_bbox(doc, it.get("sym_ids") or [], target) if it.get("sym_ids") else None
        it["mm"] = ((bb[2] - bb[0]) / target.Scale * 304.8, (bb[3] - bb[1]) / target.Scale * 304.8) if bb else None
    labels = [it["label"] for it in items]
    return {
        "ok": True, "view": target, "created": created_view, "items": items,
        "nested_errors": nested_errors, "cols": col_info, "template_removido": template_removido,
        "leg_criadas": leg_criadas, "leg_existentes": leg_existentes, "leg_erros": leg_erros,
        "added": [l for l in labels if l not in previous] if previous else [],
        "removed": sorted(l for l in previous if l not in set(labels)),
    }


def main(cfg):
    from pyrevit import revit, forms
    configurar(cfg)
    doc = revit.doc
    try:
        res = atualizar_legenda(doc)
    except Exception as ex:
        forms.alert(u"Erro ao atualizar a legenda:\n{}".format(ex), title=C["botao"])
        return
    if not res["ok"]:
        forms.alert(res["msg"], title=C["botao"])
        return

    lines = [
        u"Legenda '{}' {} com sucesso!".format(C["alvo"], u"criada" if res["created"] else u"atualizada"),
        u"Itens na legenda (um por tipo): {}".format(len(res["items"])),
    ]
    cores = {}
    for it in res["items"]:
        cores.setdefault((it["cor"], it["cor_origem"]), 0)
        cores[(it["cor"], it["cor_origem"])] += 1
    for (cor, origem), n in sorted(cores.items(), key=lambda kv: -kv[1]):
        lines.append(u"  Cor RGB {}-{}-{} ({}): {} item(ns)".format(cor[0], cor[1], cor[2], origem, n))
    if res["added"] or res["removed"]:
        lines += [u"", u"Altera\u00e7\u00f5es:"]
        lines += [u"  + {}".format(l) for l in res["added"]]
        lines += [u"  - {}".format(l) for l in res["removed"]]
    if res.get("leg_criadas"):
        lines += [u"", u"Famílias de símbolo de legenda criadas (ajuste-as à vontade: na próxima "
                  u"execução elas são usadas como estiverem):"]
        lines += [u"  + {}".format(l) for l in res["leg_criadas"]]
    if res.get("leg_existentes"):
        lines += [u"", u"Famílias de símbolo de legenda já existentes (usadas como estão): {}".format(len(res["leg_existentes"]))]
    if res.get("leg_erros"):
        lines += [u"", u"Não foi possível criar a família de legenda (símbolo desenhado na vista):"]
        lines += [u"  ! {}".format(l) for l in res["leg_erros"]]
    if res.get("template_removido"):
        lines += [u"", u"O modelo de vista '{}' foi retirado da legenda porque escondia os "
                  u"DWG importados nas fam\u00edlias (usados em alguns s\u00edmbolos).".format(res["template_removido"])]
    sem = [it["label"] for it in res["items"] if it.get("modo") == "sem simbolo"]
    if sem:
        lines += [u"", u"Itens sem s\u00edmbolo (a fam\u00edlia n\u00e3o tem anota\u00e7\u00e3o aninhada e o projeto "
                  u"n\u00e3o tem nenhum componente de legenda para servir de modelo; coloque um "
                  u"componente de legenda qualquer numa legenda e rode de novo, ou desenhe o "
                  u"s\u00edmbolo \u00e0 m\u00e3o):"]
        lines += [u"  ? {}".format(l) for l in sem]
    if res.get("nested_errors"):
        lines += [u"", u"Fam\u00edlias com s\u00edmbolo pelo componente de legenda (erro ao ler s\u00edmbolos):"]
        lines += [u"  ! {}".format(l) for l in res["nested_errors"]]
    lines += [u"", u"Quer abrir a vista?"]
    if forms.alert(u"\n".join(lines), title=C["botao"], ok=False, yes=True, no=True):
        try:
            revit.uidoc.ActiveView = res["view"]
        except Exception:
            pass


