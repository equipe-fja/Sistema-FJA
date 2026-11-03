# -*- coding: utf-8 -*-
"""Cria/atualiza a vista 'ELE - LEGENDA DE PONTOS ELETRICOS' com os pontos de
parede de eletrica presentes no modelo (um item por tipo + altura de instalacao),
os pontos de piso (um item por tipo, sem altura), os quadros e os simbolos fixos,
preservando elementos criados manualmente pelo usuario na vista."""
import math
import re
import System
from System.Collections.Generic import List
from Autodesk.Revit import DB
from Autodesk.Revit.DB.ExtensibleStorage import (
    Schema, SchemaBuilder, Entity, AccessLevel,
)

TARGET_NAME = u"ELE - LEGENDA DE PONTOS EL\u00c9TRICOS"
TITLE_TEXT = u"LEGENDA DE PONTOS EL\u00c9TRICOS"

# Marca gravada (Extensible Storage) nos elementos gerados pelo botao, para
# que uma nova execucao apague so o que o proprio botao criou.
SCHEMA_GUID = System.Guid("6f3b2c1e-8d4a-4f7b-9c2e-1a5d7e9b3c41")
SCHEMA_NAME = "FJA_LegendaELE"
FIELD = "Origem"
MARK = "LegendaELE"

# Categorias em que os pontos de parede podem estar
POINT_CATEGORIES = (
    DB.BuiltInCategory.OST_ElectricalFixtures,
    DB.BuiltInCategory.OST_LightingDevices,
    DB.BuiltInCategory.OST_CommunicationDevices,
    DB.BuiltInCategory.OST_TelephoneDevices,
    DB.BuiltInCategory.OST_DataDevices,
    DB.BuiltInCategory.OST_SecurityDevices,
    DB.BuiltInCategory.OST_FireAlarmDevices,
    DB.BuiltInCategory.OST_NurseCallDevices,
)
EXCLUDE_WORDS = ("NAO LISTAV",)
# Pontos de piso: familia com esta palavra no nome. Entram sem altura, num
# bloco proprio depois dos pontos de parede.
FLOOR_WORD = "PISO"
# Caixas de passagem (ex.: de alvenaria, no chao): tambem sem altura, em bloco proprio
BOX_WORDS = ("CAIXA DE PASSAGEM", "CAIXA PASSAGEM")


def point_block(fam_ascii):
    """0 = ponto de parede, 1 = ponto de piso, 2 = caixa de passagem."""
    if any(w in fam_ascii for w in BOX_WORDS):
        return 2
    if FLOOR_WORD in fam_ascii:
        return 1
    return 0
HEIGHT_PARAM = "ELEVACAO DO PONTO"
LEGEND_VIEW_PLAN = -8  # vista "Floor Plan" do componente de legenda
LEGEND_VIEW_FRONT = -7  # vista "Elevation: Front" (usada nos quadros)
# Triangulo do quadro: base e topo como fracao da altura do componente
# (familia ABB: de 6 a 169 mm numa caixa de 186,8 mm)
QUADRO_TRI = (6.0 / 186.8, 169.0 / 186.8)

# Quadros eletricos (Electrical Equipment): entram os tipos cujo nome tenha estas palavras
PANEL_WORDS = ("QUADRO", "PAINEL", "PANEL", "QDC", "QGBT")

# Cores (RGB)
COR_ELE = (65, 100, 215)          # cor da disciplina ELE
COR_ELETROCALHA = (189, 242, 253)  # preenchimento da eletrocalha
COR_SOBE_DESCE = COR_ELE           # linha e seta de eletroduto que sobe/desce
COR_SETA_CONTORNO = COR_ELE        # contorno da seta

# Layout em milimetros de papel (multiplicado pela escala da vista)
MM_ROW_STEP = 6.7       # distancia minima entre linhas
MM_ROW_PAD = 1.4        # folga acima/abaixo de simbolos altos
MM_SYM_X = 8.0          # centro do simbolo a partir do inicio da coluna
MM_TEXT_X = 16.0        # inicio do texto a partir do inicio da coluna
MM_COL_W = 84.0         # largura de cada coluna
MM_TEXT_W = 64.0        # largura da caixa de texto
MM_UNDERLINE = 41.4     # comprimento do sublinhado do titulo
MM_TITLE_LINE = 4.7     # insercao do titulo -> sublinhado (igual a legenda grafica)
MM_FIRST_ROW = 1.0      # distancia sublinhado -> topo da primeira linha
MAX_ROWS_PER_COL = 14
MM_LINHA = 12.5         # comprimento dos simbolos de eletroduto/eletrocalha

# Simbolos fixos (desenhados em 2D, iguais em qualquer projeto)
FIXED_ITEMS = [
    ("sobe", u"ELETRODUTO QUE SOBE"),
    ("desce", u"ELETRODUTO QUE DESCE"),
    ("teto", u"ELETRODUTO PELO TETO"),
    ("piso", u"ELETRODUTO PELO PISO"),
    ("eletrocalha", u"ELETROCALHA PERFURADA SEM TAMPA"),
]


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
    sc = Schema.Lookup(SCHEMA_GUID)
    if sc is not None:
        return sc
    sb = SchemaBuilder(SCHEMA_GUID)
    sb.SetSchemaName(SCHEMA_NAME)
    sb.SetReadAccessLevel(AccessLevel.Public)
    sb.SetWriteAccessLevel(AccessLevel.Public)
    sb.AddSimpleField(FIELD, System.String)
    return sb.Finish()


def mark(elem, schema):
    ent = Entity(schema)
    ent.Set[System.String](FIELD, MARK)
    elem.SetEntity(ent)


def is_marked(elem, schema):
    try:
        ent = elem.GetEntity(schema)
        return ent is not None and ent.IsValid() and ent.Get[System.String](FIELD) == MARK
    except Exception:
        return False


# ---------------------------------------------------------------- dados
def height_mm(inst):
    for p in inst.Parameters:
        try:
            if p.StorageType == DB.StorageType.Double and norm_ascii(p.Definition.Name) == HEIGHT_PARAM:
                return int(round(p.AsDouble() * 304.8 / 10.0)) * 10  # arredonda ao cm
        except Exception:
            pass
    return None


def group_order(name_ascii):
    has_t = "TOMADA" in name_ascii
    has_i = "INTERRUPTOR" in name_ascii
    if has_t and has_i:
        return 2
    if has_t:
        return 1
    if has_i:
        return 3
    return 4


_MULT_WORDS = {"DUPLA": 2, "DUPLO": 2, "DUPLAS": 2, "DUPLOS": 2,
               "TRIPLA": 3, "TRIPLO": 3, "TRIPLAS": 3, "TRIPLOS": 3}


def points_in_box(base_ascii):
    """Quantidade de pontos na mesma caixa, lida do texto do item.
    '2 TOMADAS ...' = 2; '1 INTERRUPTOR ... + 1 TOMADA ...' = 2; 'TOMADA ...' = 1.
    O '+' so separa pecas quando tem espaco dos dois lados (nao quebra '2P+T')."""
    total = 0
    for part in re.split(r"\s\+\s", base_ascii):
        part = part.strip()
        m = re.match(r"^(\d+)\s", part)
        n = int(m.group(1)) if m else 1
        for w in re.findall(r"[A-Z]+", part):
            if w in _MULT_WORDS:
                n *= _MULT_WORDS[w]
                break
        total += n
    return total


def name_without_count(base_ascii):
    return re.sub(r"^\d+\s+", "", base_ascii.strip())


def base_description(sym):
    """Texto base do tipo: Descricao, se for informativa; senao o nome do tipo."""
    p = sym.get_Parameter(DB.BuiltInParameter.ALL_MODEL_DESCRIPTION)
    desc = p.AsString().strip() if (p and p.HasValue and p.AsString()) else ""
    if len(desc) < 8 or re.match(r"^\d+\s*X\s*\d+\"?$", norm_ascii(desc)):
        desc = elem_name(sym)
    return desc.upper().strip()


def label_for(sym, h_mm):
    base = base_description(sym)
    caixa = None
    m = re.search(r"\s*-\s*(\d+\s*X\s*\d+)\"?\s*$", base)
    if m:
        caixa = m.group(1).replace(" ", "")
        base = base[:m.start()].strip()
    parts = [base]
    if h_mm is not None:
        parts.append(u"A {}M DO PISO".format(("%.2f" % (h_mm / 1000.0)).replace(".", ",")))
    if caixa:
        parts.append(u"EM CAIXA {}\"".format(caixa))
    return u", ".join(parts)


def collect_items(doc):
    combos = {}
    for bic in POINT_CATEGORIES:
        col = DB.FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType()
        for inst in col:
            if not isinstance(inst, DB.FamilyInstance) or inst.SuperComponent is not None:
                continue
            sym = inst.Symbol
            fam_ascii = norm_ascii(sym.FamilyName)
            if any(w in fam_ascii for w in EXCLUDE_WORDS):
                continue
            no_height = point_block(fam_ascii) > 0
            h = None if no_height else height_mm(inst)
            if h is None and not no_height:  # sem "Elevacao do Ponto" = nao e ponto de parede
                continue
            key = (sym.Id.ToString(), h)
            if key not in combos:
                combos[key] = [sym, h, 0, inst]  # inst = instancia representativa
            combos[key][2] += 1
    items = []
    for sym, h, n, rep in combos.values():
        name_ascii = norm_ascii(sym.FamilyName + " " + elem_name(sym))
        base_ascii = norm_ascii(label_for(sym, None))
        items.append({
            "sort": (point_block(norm_ascii(sym.FamilyName)), group_order(name_ascii), points_in_box(base_ascii),
                     name_without_count(base_ascii), h or 0),
            "sym": sym, "h": h, "count": n, "label": label_for(sym, h), "rep": rep,
            "kind": "ponto",
        })
    items.sort(key=lambda it: it["sort"])
    return items


def panel_label(sym):
    """'QLF-QUADRO LUZ E FORCA' -> 'QLF - QUADRO LUZ E FORCA'."""
    t = elem_name(sym).upper().strip()
    m = re.match(r"^([A-Z0-9]{2,6})\s*-\s*(.+)$", t)
    if m:
        return u"{} - {}".format(m.group(1), m.group(2).strip())
    return t


def collect_panels(doc):
    """Quadros eletricos (Electrical Equipment) com instancia no modelo, um item por tipo."""
    types = {}
    col = (DB.FilteredElementCollector(doc)
           .OfCategory(DB.BuiltInCategory.OST_ElectricalEquipment)
           .WhereElementIsNotElementType())
    for inst in col:
        if not isinstance(inst, DB.FamilyInstance) or inst.SuperComponent is not None:
            continue
        sym = inst.Symbol
        name = norm_ascii(sym.FamilyName + " " + elem_name(sym))
        if not any(w in name for w in PANEL_WORDS):
            continue
        key = sym.Id.ToString()
        if key not in types:
            types[key] = [sym, 0]
        types[key][1] += 1
    items = []
    for sym, n in types.values():
        items.append({"kind": "quadro", "sym": sym, "count": n, "label": panel_label(sym),
                      "sort": norm_ascii(elem_name(sym))})
    items.sort(key=lambda it: it["sort"])
    return items


# ---------------------------------------------------------------- desenho 2D
class Desenho(object):
    """Linhas, circulos e regioes preenchidas com cor propria, independentes
    de estilos ou familias do projeto (cria os tipos de regiao se faltarem)."""

    def __init__(self, doc, view, k, schema):
        self.doc, self.view, self.k, self.schema = doc, view, k, schema
        self._fr_types = {}
        self._invisible = None
        self._solid = None

    def pt(self, x, y):
        return DB.XYZ(x * self.k, y * self.k, 0)

    def _ogs(self, rgb, weight):
        o = DB.OverrideGraphicSettings()
        o.SetProjectionLineColor(DB.Color(rgb[0], rgb[1], rgb[2]))
        o.SetCutLineColor(DB.Color(rgb[0], rgb[1], rgb[2]))
        if weight:
            o.SetProjectionLineWeight(weight)
        return o

    def paint(self, elem_id, rgb):
        """Sobrepoe a cor em todas as linhas e padroes (hachuras) do elemento na vista."""
        c = DB.Color(rgb[0], rgb[1], rgb[2])
        o = self.view.GetElementOverrides(elem_id)
        o.SetProjectionLineColor(c)
        o.SetCutLineColor(c)
        for setter in ("SetSurfaceForegroundPatternColor", "SetSurfaceBackgroundPatternColor",
                       "SetCutForegroundPatternColor", "SetCutBackgroundPatternColor"):
            try:
                getattr(o, setter)(c)
            except Exception:
                pass
        self.view.SetElementOverrides(elem_id, o)

    def _curve(self, curve, rgb, weight):
        dc = self.doc.Create.NewDetailCurve(self.view, curve)
        self.view.SetElementOverrides(dc.Id, self._ogs(rgb, weight))
        return dc.Id

    def line(self, x0, y0, x1, y1, rgb, weight):
        return self._curve(DB.Line.CreateBound(self.pt(x0, y0), self.pt(x1, y1)), rgb, weight)

    def circle(self, cx, cy, r, rgb, weight):
        arc = DB.Arc.Create(self.pt(cx, cy), r * self.k, 0.0, 2.0 * math.pi, DB.XYZ.BasisX, DB.XYZ.BasisY)
        return self._curve(arc, rgb, weight)

    def _solid_pattern(self):
        if self._solid is None:
            for fpe in DB.FilteredElementCollector(self.doc).OfClass(DB.FillPatternElement):
                fp = fpe.GetFillPattern()
                if fp.IsSolidFill and fp.Target == DB.FillPatternTarget.Drafting:
                    self._solid = fpe.Id
                    break
        return self._solid

    def _invisible_style(self):
        if self._invisible is None:
            alvo = int(DB.BuiltInCategory.OST_InvisibleLines)
            for i in DB.FilledRegion.GetValidLineStyleIdsForFilledRegion(self.doc):
                gs = self.doc.GetElement(i)
                try:
                    if gs.GraphicsStyleCategory.Id.Value == alvo:
                        self._invisible = i
                        break
                except Exception:
                    pass
        return self._invisible

    def fill_type(self, rgb):
        name = "FJA - SOLIDO {:03d}-{:03d}-{:03d}".format(*rgb)
        if name in self._fr_types:
            return self._fr_types[name]
        found = None
        for t in DB.FilteredElementCollector(self.doc).OfClass(DB.FilledRegionType):
            if elem_name(t) == name:
                found = t
                break
        if found is None:
            base = DB.FilteredElementCollector(self.doc).OfClass(DB.FilledRegionType).FirstElement()
            found = base.Duplicate(name)
            found.ForegroundPatternId = self._solid_pattern()
            found.ForegroundPatternColor = DB.Color(rgb[0], rgb[1], rgb[2])
            found.BackgroundPatternId = DB.ElementId.InvalidElementId
            try:
                found.IsMasking = False
            except Exception:
                pass
        self._fr_types[name] = found.Id
        return found.Id

    def region(self, pts_mm, rgb):
        loop = DB.CurveLoop()
        pts = [self.pt(x, y) for x, y in pts_mm]
        for i in range(len(pts)):
            loop.Append(DB.Line.CreateBound(pts[i], pts[(i + 1) % len(pts)]))
        fr = DB.FilledRegion.Create(self.doc, self.fill_type(rgb), self.view.Id, List[DB.CurveLoop]([loop]))
        try:
            inv = self._invisible_style()
            if inv is not None:
                fr.SetLineStyleId(inv)
        except Exception:
            pass
        return fr.Id

    def region_xy(self, pts_ft, rgb):
        """Poligono em coordenadas do modelo (pes)."""
        k = self.k
        return self.region([(x / k, y / k) for x, y in pts_ft], rgb)


# Seta de eletroduto (mm de papel, como na familia "Eletroduto que sobe, desce ou passa")
_SETA_SOBE = [(4.16, 3.04), (6.25, 6.25), (3.04, 4.16), (3.60, 3.60)]
_SETA_DESCE = [(2.74, 3.85), (0.66, 0.66), (3.85, 2.74), (3.30, 3.30)]


def draw_fixed(d, kind):
    """Desenha um simbolo fixo em torno da origem e retorna os ids."""
    ids = []
    h = MM_LINHA / 2.0
    if kind in ("sobe", "desce"):
        ids.append(d.circle(0, 0, 0.94, COR_SOBE_DESCE, 2))
        ids.append(d.line(0.66, 0.66, 6.25, 6.25, COR_SOBE_DESCE, 2))
        seta = _SETA_SOBE if kind == "sobe" else _SETA_DESCE
        ids.append(d.region(seta, COR_SOBE_DESCE))
        for i in range(4):
            a, b = seta[i], seta[(i + 1) % 4]
            ids.append(d.line(a[0], a[1], b[0], b[1], COR_SETA_CONTORNO, 1))
    elif kind == "teto":   # tracejado 3 mm / 3 mm, centrado
        for x0, x1 in ((-h, -4.5), (-1.5, 1.5), (4.5, h)):
            ids.append(d.line(x0, 0, x1, 0, COR_ELE, 4))
    elif kind == "piso":   # traco longo - curto - longo
        for x0, x1 in ((-h, -1.86), (-0.24, 1.13), (2.62, h)):
            ids.append(d.line(x0, 0, x1, 0, COR_ELE, 4))
    elif kind == "eletrocalha":
        e = 1.735
        ids.append(d.region([(-h, -e), (h, -e), (h, e), (-h, e)], COR_ELETROCALHA))
        ids.append(d.line(-h, e, h, e, COR_ELE, 4))
        ids.append(d.line(-h, -e, h, -e, COR_ELE, 4))
    return ids


# ---------------------------------------------------------------- simbolos aninhados
# Os pontos desenham o simbolo em planta com anotacoes genericas aninhadas
# ("(ELE) (SMB) ..."), ligadas/desligadas por parametros da familia (ex.: Baixa,
# Media, Alta, calculados a partir da Elevacao do Ponto) e por nivel de detalhe.
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


def _copy_value(src, dst):
    if src is None or dst is None or dst.IsReadOnly or src.StorageType != dst.StorageType:
        return
    st = src.StorageType
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


# ---------------------------------------------------------------- vistas
def find_legends(doc):
    src = target = None
    others = []
    for v in DB.FilteredElementCollector(doc).OfClass(DB.View):
        try:
            if v.ViewType != DB.ViewType.Legend or v.IsTemplate:
                continue
        except Exception:
            continue
        n = norm_ascii(elem_name(v))
        if n == norm_ascii(TARGET_NAME):
            target = v
        elif n.startswith("ELE - LEGENDA GRAFICA"):
            src = v
        else:
            others.append(v)
    return src, target, others


def reference_styles(doc, ref_view):
    title_type = body_type = None
    for tn in DB.FilteredElementCollector(doc, ref_view.Id).OfClass(DB.TextNote):
        txt = norm_ascii(tn.Text)
        if "LEGENDA" in txt and title_type is None:
            title_type = tn.GetTypeId()
        elif ("TOMADA" in txt or "INTERRUPTOR" in txt) and body_type is None:
            body_type = tn.GetTypeId()
    default_txt = doc.GetDefaultElementTypeId(DB.ElementTypeGroup.TextNoteType)
    body_type = body_type or default_txt
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


# ---------------------------------------------------------------- principal
def atualizar_legenda(doc):
    """Executa tudo numa transacao. Retorna um dicionario com o resultado."""
    points = collect_items(doc)
    panels = collect_panels(doc)
    fixed = [{"kind": "fixo", "fixo": kd, "label": lb} for kd, lb in FIXED_ITEMS]
    items = points + panels + fixed

    src, target, others = find_legends(doc)
    base_view = src or target or (others[0] if others else None)
    if base_view is None:
        return {"ok": False, "msg": u"O projeto n\u00e3o tem nenhuma vista de legenda para servir de base."}
    comp_view, comp_src = any_legend_component(doc, [src, target] + others)
    if comp_src is None:
        return {"ok": False, "msg": u"Nenhum componente de legenda encontrado no projeto para usar como modelo."}

    title_type, body_type, line_style = reference_styles(doc, src or base_view)

    # Simbolos aninhados de cada familia (fora de transacao: abre as familias
    # so para leitura e carrega no projeto as familias de simbolo que faltarem)
    detail_bit = DETAIL_BITS.get((target or base_view).DetailLevel, 16384)
    loaded_names = _project_family_names(doc)
    nested = {}
    nested_errors = []
    for it in points:
        fam = it["sym"].Family
        key = fam.Id.ToString()
        if key in nested:
            continue
        try:
            nested[key] = read_nested_symbols(doc, fam, detail_bit, loaded_names)
        except Exception as ex:
            nested[key] = []
            nested_errors.append(u"{}: {}".format(fam.Name, ex))

    col_info = None
    t = DB.Transaction(doc, "Atualizar Legenda ELE")
    t.Start()
    try:
        schema = get_schema()
        created_view = False
        if target is None:
            target = doc.GetElement(base_view.Duplicate(DB.ViewDuplicateOption.Duplicate))
            target.Name = TARGET_NAME
            created_view = True
            # Duplicate sem detalhes pode trazer algo em alguns casos: limpa so
            # textos, linhas, componentes e anotacoes (nunca elementos internos da vista)
            ids = List[DB.ElementId]()
            for e in DB.FilteredElementCollector(doc, target.Id).WhereElementIsNotElementType():
                if e.OwnerViewId == target.Id and is_view_content(e):
                    ids.Add(e.Id)
            if ids.Count:
                doc.Delete(ids)

        # Componente modelo copiado para a vista de destino ANTES da limpeza: o
        # modelo pode ser um componente que o proprio botao criou na legenda
        # (ex.: o de um quadro), que a limpeza abaixo apaga. A copia nao e marcada.
        tpl_id = DB.ElementTransformUtils.CopyElements(
            comp_view, List[DB.ElementId]([comp_src.Id]), target,
            DB.Transform.Identity, DB.CopyPasteOptions())[0]
        try:
            doc.GetElement(tpl_id).DeleteEntity(schema)  # a copia pode herdar a marca
        except Exception:
            pass

        # Remove apenas o que o botao gerou antes; guarda os textos para o relatorio
        previous = set()
        ids = List[DB.ElementId]()
        for e in DB.FilteredElementCollector(doc, target.Id).WhereElementIsNotElementType():
            if e.Id != tpl_id and e.OwnerViewId == target.Id and is_marked(e, schema):
                if isinstance(e, DB.TextNote) and norm_ascii(e.Text) != norm_ascii(TITLE_TEXT):
                    previous.add(e.Text.strip())
                ids.Add(e.Id)
        if ids.Count:
            doc.Delete(ids)

        k = target.Scale / 304.8  # mm de papel -> pes no modelo

        # Titulo + sublinhado
        x0, y0 = 0.0, 0.0
        title = DB.TextNote.Create(doc, target.Id, DB.XYZ(x0 + 1.0 * k, y0, 0), TITLE_TEXT, title_type)
        mark(title, schema)
        line_y = y0 - MM_TITLE_LINE * k
        ul = doc.Create.NewDetailCurve(target, DB.Line.CreateBound(
            DB.XYZ(x0, line_y, 0), DB.XYZ(x0 + MM_UNDERLINE * k, line_y, 0)))
        if line_style is not None:
            ul.LineStyle = line_style
        mark(ul, schema)

        draw = Desenho(doc, target, k, schema)

        def legend_component(sym):
            comp = doc.GetElement(DB.ElementTransformUtils.CopyElement(doc, tpl_id, DB.XYZ.Zero)[0])
            comp.get_Parameter(DB.BuiltInParameter.LEGEND_COMPONENT).Set(sym.Id)
            try:
                comp.get_Parameter(DB.BuiltInParameter.LEGEND_COMPONENT_VIEW).Set(LEGEND_VIEW_PLAN)
            except Exception:
                pass
            return comp

        # 1a passada: cria simbolo e texto de cada item, mede a altura da linha e ja
        # posiciona tudo numa coluna so (provisoria). Cada simbolo tem de sair da
        # origem logo depois de criado: linhas de detalhe com pontas coincidentes
        # (ex.: eletroduto que sobe e que desce) ficam ligadas pelo Revit, e ao mover
        # uma a outra e arrastada junto.
        rows = []
        prov_top = line_y - MM_FIRST_ROW * k
        for it in items:
            sym_ids = []
            if it["kind"] == "ponto":
                # anotacoes aninhadas visiveis para esta altura; se a familia
                # nao tiver, usa o componente de legenda
                vspecs = visible_specs(nested.get(it["sym"].Family.Id.ToString(), []), it["rep"], it["sym"])
                if vspecs:
                    sym_ids = place_nested_symbols(doc, target, vspecs, it["rep"], it["sym"])
                    it["modo"] = "simbolos"
                if not sym_ids:
                    sym_ids = [legend_component(it["sym"]).Id]
                    it["modo"] = "componente"
                for i in sym_ids:  # linhas e hachuras na cor ELE
                    draw.paint(i, COR_ELE)
            elif it["kind"] == "quadro":
                # componente de legenda na vista frontal (como o quadro aparece em
                # planta) + triangulo solido ELE no canto inferior esquerdo, como a
                # regiao preenchida da familia (componente de legenda nao a mostra)
                comp = legend_component(it["sym"])
                try:
                    comp.get_Parameter(DB.BuiltInParameter.LEGEND_COMPONENT_VIEW).Set(LEGEND_VIEW_FRONT)
                except Exception:
                    pass
                draw.paint(comp.Id, COR_ELE)
                doc.Regenerate()
                cb = comp.get_BoundingBox(target)
                sym_ids = [comp.Id]
                if cb is not None:
                    h = cb.Max.Y - cb.Min.Y
                    yb = cb.Min.Y + h * QUADRO_TRI[0]
                    yt = cb.Min.Y + h * QUADRO_TRI[1]
                    sym_ids.append(draw.region_xy([(cb.Min.X, yt), (cb.Min.X, yb), (cb.Max.X, yb)], COR_ELE))
                it["modo"] = "quadro"
            else:
                sym_ids = draw_fixed(draw, it["fixo"])
                it["modo"] = "fixo"
            for i in sym_ids:
                mark(doc.GetElement(i), schema)

            # Texto criado provisoriamente na 1a coluna
            opts = DB.TextNoteOptions(body_type)
            tn = DB.TextNote.Create(doc, target.Id, DB.XYZ(x0 + MM_TEXT_X * k, line_y, 0),
                                    it["label"], opts)
            try:
                tn.Width = MM_TEXT_W / 304.8  # largura em papel (pes)
            except Exception:
                pass
            mark(tn, schema)
            doc.Regenerate()

            # Altura da linha = o maior entre simbolo e texto (texto pode quebrar)
            bb = union_bbox(doc, sym_ids, target)
            tbb = tn.get_BoundingBox(target)
            sym_h = (bb[3] - bb[1]) if bb else 0.0
            txt_h = (tbb.Max.Y - tbb.Min.Y) if tbb else 0.0
            row_h = max(MM_ROW_STEP * k, sym_h + MM_ROW_PAD * k, txt_h + MM_ROW_PAD * k)
            cy = prov_top - row_h / 2.0
            prov_top -= row_h
            if bb:
                cur = DB.XYZ((bb[0] + bb[2]) / 2.0, (bb[1] + bb[3]) / 2.0, 0)
                DB.ElementTransformUtils.MoveElements(
                    doc, List[DB.ElementId](sym_ids), DB.XYZ(x0 + MM_SYM_X * k, cy, 0) - cur)
            if tbb:
                DB.ElementTransformUtils.MoveElement(
                    doc, tn.Id, DB.XYZ(0, cy - (tbb.Min.Y + tbb.Max.Y) / 2.0, 0))
            rows.append((sym_ids, tn, cy, row_h))

        # Colunas: ate MAX_ROWS_PER_COL itens numa coluna; acima disso, duas colunas
        # equilibradas pela ALTURA. Os itens seguem a ordem da lista e a divisao fica
        # onde a coluna mais alta e a menor possivel (empate: 1a coluna maior).
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
        col_info = [sum(heights[:split]) / k, sum(heights[split:]) / k,
                    split, len(rows) - split]  # alturas em mm de papel e quantidades

        # 2a passada: leva para a 2a coluna (simbolo + texto juntos) as linhas que
        # ficaram depois do ponto de divisao; a 1a coluna ja esta no lugar
        col_tops = [line_y - MM_FIRST_ROW * k] * 2
        for idx, (sym_ids, tn, cy0, row_h) in enumerate(rows):
            col = 0 if idx < split else 1
            cy = col_tops[col] - row_h / 2.0
            col_tops[col] -= row_h
            d = DB.XYZ(col * MM_COL_W * k, cy - cy0, 0)
            if d.GetLength() > 1e-9:
                DB.ElementTransformUtils.MoveElements(
                    doc, List[DB.ElementId](list(sym_ids) + [tn.Id]), d)

        doc.Delete(tpl_id)
        t.Commit()
    except Exception:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        raise

    labels = [it["label"] for it in items]
    return {
        "ok": True, "view": target, "created": created_view, "items": items,
        "nested_errors": nested_errors, "cols": col_info,
        "added": [l for l in labels if l not in previous] if previous else [],
        "removed": sorted(l for l in previous if l not in set(labels)),
    }


def main():
    from pyrevit import revit, forms
    doc = revit.doc
    try:
        res = atualizar_legenda(doc)
    except Exception as ex:
        forms.alert(u"Erro ao atualizar a legenda:\n{}".format(ex), title=u"Legenda ELE")
        return
    if not res["ok"]:
        forms.alert(res["msg"], title=u"Legenda ELE")
        return

    lines = [
        u"Legenda '{}' {} com sucesso!".format(TARGET_NAME, u"criada" if res["created"] else u"atualizada"),
        u"Itens na legenda (tipo + altura): {}".format(len(res["items"])),
    ]
    if res["added"] or res["removed"]:
        lines += [u"", u"Altera\u00e7\u00f5es:"]
        lines += [u"  + {}".format(l) for l in res["added"]]
        lines += [u"  - {}".format(l) for l in res["removed"]]
    if res.get("nested_errors"):
        lines += [u"", u"Fam\u00edlias com s\u00edmbolo pelo componente de legenda (erro ao ler s\u00edmbolos):"]
        lines += [u"  ! {}".format(l) for l in res["nested_errors"]]
    lines += [u"", u"Quer abrir a vista?"]
    if forms.alert(u"\n".join(lines), title=u"Legenda ELE", ok=False, yes=True, no=True):
        try:
            revit.uidoc.ActiveView = res["view"]
        except Exception:
            pass


if __name__ == "__main__":
    main()
