# -*- coding: utf-8 -*-
"""Atualiza a vista 'ELE - LEGENDA DE LUMINÁRIAS' preservando elementos criados manualmente pelo usuário."""
import math
import re
from System.Collections.Generic import List
from pyrevit import revit, DB, forms


LINEAR_PREFIXES = (
    "LIN.01", "TET.02", "TET.03", "PEN.02", "PEN.03", "NLI.02", "NLI.03",
    "LIN-01", "TET-02", "TET-03", "PEN-02", "PEN-03", "NLI-02", "NLI-03",
)
ITEM_TEXT_REGEX = re.compile(r"^[A-Z]{2,4}[.-]\d{2,3}\s*-")


def norm_ascii(s):
    if not s:
        return ""
    res = []
    for ch in s.upper().strip():
        o = ord(ch)
        if o < 128:
            res.append(ch)
        elif ch in u"\u00c0\u00c1\u00c2\u00c3\u00c4":
            res.append("A")
        elif ch in u"\u00c8\u00c9\u00ca\u00cb":
            res.append("E")
        elif ch in u"\u00cc\u00cd\u00ce\u00cf":
            res.append("I")
        elif ch in u"\u00d2\u00d3\u00d4\u00d5\u00d6":
            res.append("O")
        elif ch in u"\u00d9\u00da\u00db\u00dc":
            res.append("U")
        elif ch == u"\u00c7":
            res.append("C")
    return "".join(res)


def auto_classify(name_ascii):
    """Retorna (prefixo, grupo_comentario) para novas famílias sem Type Mark."""
    if " NAO LISTAD" in name_ascii or name_ascii.startswith("LUMINARIA NAO LISTADA"):
        return ("NLI", u"7. Lumin\u00e1rias N\u00e3o Listadas - A Definir (NLI)")
    if "FITA" in name_ascii or "PERFIL" in name_ascii or "ARCO LED" in name_ascii:
        return ("LIN", u"1. Ilumina\u00e7\u00e3o Linear e Perfis de LED (LIN)")
    if "SPOT" in name_ascii or "FOCO" in name_ascii:
        return ("SPT", u"2. Spots e Focos de Destaque (SPT)")
    if "ARANDELA" in name_ascii:
        return ("ARA", u"4. Arandelas - Parede e Piscina (ARA)")
    if "BALIZADOR" in name_ascii or "POSTE" in name_ascii or "REFLETOR" in name_ascii or "EXTERNA" in name_ascii:
        return ("EXT", u"5. Ilumina\u00e7\u00e3o Externa e Balizamento (EXT)")
    if "PENDENTE" in name_ascii:
        return ("PEN", u"6. Pendentes Decorativos e Lineares (PEN)")
    return ("TET", u"3. Lumin\u00e1rias Gerais e T\u00e9cnicas de Teto (TET)")


def clean_default_desc(raw_name):
    if not raw_name:
        return u"LUMIN\u00c1RIA"
    txt = raw_name.strip()
    if txt.upper().startswith("(LUM)"):
        txt = txt[5:].strip()
    return txt.upper()


def is_linear_item(code, name_ascii, cw, ch):
    if code in LINEAR_PREFIXES:
        return True
    if any(k in name_ascii for k in ("LINEAR", "TUBULAR", "FITA", "PERFIL")):
        return True
    return False


def is_script_generated_element(elem, view):
    """Retorna True APENAS para elementos gerados automaticamente pelo script (preserva criações manuais)."""
    if isinstance(elem, DB.TextNote):
        txt = norm_ascii(elem.Text)
        return bool(ITEM_TEXT_REGEX.match(txt))

    if isinstance(elem, DB.CurveElement):
        try:
            ogs = view.GetElementOverrides(elem.Id)
            if ogs and ogs.ProjectionLineColor and ogs.ProjectionLineColor.IsValid:
                c = ogs.ProjectionLineColor
                if int(c.Red) == 255 and int(c.Green) == 0 and int(c.Blue) == 128:
                    return True
        except Exception:
            pass
        return False

    return False


def extract_primitives_in_view(elem, view):
    prims = []
    opt = DB.Options()
    if view is not None:
        opt.View = view
    else:
        opt.DetailLevel = DB.ViewDetailLevel.Fine
        opt.IncludeNonVisibleObjects = True
    geom = elem.get_Geometry(opt)
    if not geom:
        return prims

    def walk(g_enum):
        for g in g_enum:
            if isinstance(g, DB.Arc):
                if not g.IsBound:
                    c = g.Center
                    r = g.Radius
                    if r > 1e-4 and abs(g.Normal.Z) > 0.5:
                        prims.append(("circle", c.X, c.Y, r))
                else:
                    p0 = g.GetEndPoint(0)
                    p1 = g.GetEndPoint(1)
                    pm = g.Evaluate(0.5, True)
                    if math.hypot(p1.X - p0.X, p1.Y - p0.Y) > 1e-4:
                        prims.append(("arc", p0.X, p0.Y, p1.X, p1.Y, pm.X, pm.Y))
            elif isinstance(g, DB.Ellipse):
                c = g.Center
                r = 0.5 * (g.RadiusX + g.RadiusY)
                if r > 1e-4:
                    prims.append(("circle", c.X, c.Y, r))
            elif isinstance(g, DB.Curve):
                if g.IsBound:
                    p0 = g.GetEndPoint(0)
                    p1 = g.GetEndPoint(1)
                    if math.hypot(p1.X - p0.X, p1.Y - p0.Y) > 1e-4:
                        prims.append(("line", p0.X, p0.Y, p1.X, p1.Y))
            elif isinstance(g, DB.PolyLine):
                pts = g.GetCoordinates()
                for i in range(len(pts) - 1):
                    if math.hypot(pts[i + 1].X - pts[i].X, pts[i + 1].Y - pts[i].Y) > 1e-4:
                        prims.append(("line", pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y))
            elif isinstance(g, DB.GeometryInstance):
                walk(g.GetInstanceGeometry())
            elif isinstance(g, DB.Solid) and g.Edges.Size > 0:
                for edge in g.Edges:
                    ec = edge.AsCurve()
                    if isinstance(ec, DB.Arc) and not ec.IsBound:
                        if ec.Radius > 1e-4 and abs(ec.Normal.Z) > 0.5:
                            prims.append(("circle", ec.Center.X, ec.Center.Y, ec.Radius))
                    elif ec and ec.IsBound:
                        p0 = ec.GetEndPoint(0)
                        p1 = ec.GetEndPoint(1)
                        if math.hypot(p1.X - p0.X, p1.Y - p0.Y) > 1e-4:
                            prims.append(("line", p0.X, p0.Y, p1.X, p1.Y))

    walk(geom)
    return prims


def main():
    doc = revit.doc
    uidoc = revit.uidoc

    all_views = DB.FilteredElementCollector(doc).OfClass(DB.View).ToElements()
    src_leg = None
    target_leg = None
    target_leg_name = u"ELE - LEGENDA DE LUMIN\u00c1RIAS"

    for v in all_views:
        try:
            if v.ViewType == DB.ViewType.Legend and not v.IsTemplate:
                vname = norm_ascii(DB.Element.Name.__get__(v))
                if vname == "ELE - LEGENDA GRAFICA":
                    src_leg = v
                elif vname == "ELE - LEGENDA DE LUMINARIAS":
                    target_leg = v
        except Exception:
            pass

    if not src_leg:
        for v in all_views:
            if v.ViewType == DB.ViewType.Legend and not v.IsTemplate and (not target_leg or v.Id != target_leg.Id):
                src_leg = v
                break

    if not src_leg and not target_leg:
        forms.alert(u"Nenhuma vista de Legenda base foi encontrada no projeto.", title=u"Legenda LUM")
        return

    ref_view = src_leg if src_leg else target_leg
    ref_elems = DB.FilteredElementCollector(doc, ref_view.Id).ToElements()

    title_type_id = None
    body_type_id = None
    header_line_style = None
    ref_underline_len = None

    text_notes = [e for e in ref_elems if isinstance(e, DB.TextNote)]
    for tn in text_notes:
        txt = norm_ascii(tn.Text)
        if "LEGENDA GRAFICA" in txt or txt == "NOTAS":
            title_type_id = tn.GetTypeId()
        elif "TOMADA" in txt or "INTERRUPTOR" in txt or " - " in txt:
            body_type_id = tn.GetTypeId()

    if not body_type_id and text_notes:
        body_type_id = text_notes[-1].GetTypeId()
    if not title_type_id:
        title_type_id = body_type_id

    min_x = None
    max_x = None
    for e in ref_elems:
        if isinstance(e, DB.CurveElement):
            c = e.GeometryCurve
            p0 = c.GetEndPoint(0)
            p1 = c.GetEndPoint(1)
            if abs(p0.Y - p1.Y) < 1e-4:
                lx = min(p0.X, p1.X)
                rx = max(p0.X, p1.X)
                length = rx - lx
                if min_x is None or lx < min_x:
                    min_x = lx
                if max_x is None or rx > max_x:
                    max_x = rx
                if 8.0 < length < 22.0:
                    header_line_style = e.LineStyle
                    ref_underline_len = length

    total_width = (max_x - min_x) if (min_x is not None and max_x is not None and (max_x - min_x) > 20.0) else 56.82

    src_comp = None
    if src_leg:
        src_comps = (
            DB.FilteredElementCollector(doc, src_leg.Id)
            .OfCategory(DB.BuiltInCategory.OST_LegendComponents)
            .WhereElementIsNotElementType()
            .ToElements()
        )
        if src_comps:
            src_comp = src_comps[0]

    # Coleta apenas tipos de luminárias com pelo menos 1 instância inserida no projeto
    placed_instances = (
        DB.FilteredElementCollector(doc)
        .OfCategory(DB.BuiltInCategory.OST_LightingFixtures)
        .WhereElementIsNotElementType()
        .ToElements()
    )
    placed_type_ids = set()
    for inst in placed_instances:
        try:
            tid = inst.GetTypeId()
            if tid and tid != DB.ElementId.InvalidElementId:
                placed_type_ids.add(tid)
        except Exception:
            pass

    all_symbols = (
        DB.FilteredElementCollector(doc)
        .OfCategory(DB.BuiltInCategory.OST_LightingFixtures)
        .WhereElementIsElementType()
        .ToElements()
    )
    symbols = [s for s in all_symbols if s.Id in placed_type_ids]

    used_numbers = {}
    for sym in symbols:
        p_mark = sym.get_Parameter(DB.BuiltInParameter.ALL_MODEL_TYPE_MARK)
        mk = p_mark.AsString().strip() if (p_mark and p_mark.HasValue and p_mark.AsString()) else ""
        if "." in mk or "-" in mk:
            parts = re.split(r"[.-]", mk, 1)
            if len(parts) == 2 and parts[1].isdigit():
                used_numbers[parts[0]] = max(used_numbers.get(parts[0], 0), int(parts[1]))

    prefix_order = {"LIN": 1, "SPT": 2, "TET": 3, "ARA": 4, "EXT": 5, "PEN": 6, "NLI": 7}

    t = DB.Transaction(doc, "Atualizar Legenda LUM")
    t.Start()

    try:
        items_data = []
        for sym in symbols:
            try:
                fam_name = sym.Family.Name
            except Exception:
                fam_name = DB.Element.Name.__get__(sym)
            type_name = DB.Element.Name.__get__(sym)
            raw_name = fam_name or type_name
            name_ascii = norm_ascii(raw_name)

            p_mark = sym.get_Parameter(DB.BuiltInParameter.ALL_MODEL_TYPE_MARK)
            code = p_mark.AsString().strip() if (p_mark and p_mark.HasValue and p_mark.AsString()) else ""

            if not code:
                pref, grp_comm = auto_classify(name_ascii)
                nxt = used_numbers.get(pref, 0) + 1
                used_numbers[pref] = nxt
                code = "{}.{:02d}".format(pref, nxt)
                if p_mark and not p_mark.IsReadOnly:
                    p_mark.Set(code)
                p_comm = sym.get_Parameter(DB.BuiltInParameter.ALL_MODEL_TYPE_COMMENTS)
                if p_comm and not p_comm.IsReadOnly and not p_comm.AsString():
                    p_comm.Set(grp_comm)

            p_desc = sym.get_Parameter(DB.BuiltInParameter.ALL_MODEL_DESCRIPTION)
            desc = p_desc.AsString().strip() if (p_desc and p_desc.HasValue and p_desc.AsString()) else ""
            if not desc:
                desc = clean_default_desc(raw_name)
                if p_desc and not p_desc.IsReadOnly:
                    p_desc.Set(desc)
            else:
                desc = desc.upper()

            pref_key = re.split(r"[.-]", code)[0] if ("." in code or "-" in code) else code[:3]
            sort_tuple = (prefix_order.get(pref_key, 99), code)
            items_data.append((sort_tuple, code, name_ascii, desc, sym))

        items_data.sort(key=lambda x: x[0])

        is_newly_duplicated = False
        if not target_leg:
            new_leg_id = src_leg.Duplicate(DB.ViewDuplicateOption.WithDetailing)
            target_leg = doc.GetElement(new_leg_id)
            target_leg.Name = target_leg_name
            is_newly_duplicated = True

        try:
            target_leg.DetailLevel = DB.ViewDetailLevel.Fine
        except Exception:
            pass

        # Apaga SOMENTE os elementos gerados anteriormente pelo script (preservando criações manuais!)
        existing_elems = DB.FilteredElementCollector(doc, target_leg.Id).ToElements()
        template_comp = None
        ids_to_delete = List[DB.ElementId]()
        previous_items = set()

        for e in existing_elems:
            if e.Category is None:
                continue
            if e.Category.Id == DB.ElementId(DB.BuiltInCategory.OST_LegendComponents):
                if template_comp is None:
                    template_comp = e
                elif is_newly_duplicated:
                    ids_to_delete.Add(e.Id)
            elif is_newly_duplicated or is_script_generated_element(e, target_leg):
                if not is_newly_duplicated and isinstance(e, DB.TextNote) and e.Text:
                    previous_items.add(e.Text.strip())
                ids_to_delete.Add(e.Id)

        if ids_to_delete.Count > 0:
            doc.Delete(ids_to_delete)

        if template_comp is None and src_comp is not None and src_leg is not None:
            copied_ids = DB.ElementTransformUtils.CopyElements(
                src_leg,
                List[DB.ElementId]([src_comp.Id]),
                target_leg,
                DB.Transform.Identity,
                DB.CopyPasteOptions(),
            )
            if copied_ids and copied_ids.Count > 0:
                template_comp = doc.GetElement(copied_ids[0])

        # Verifica se o usuário já criou um título manualmente (ex: "LEGENDA DE LUMINÁRIAS")
        remaining_elems = DB.FilteredElementCollector(doc, target_leg.Id).ToElements()
        user_title_note = None
        user_underline_y = None
        user_x0 = None

        for e in remaining_elems:
            if isinstance(e, DB.TextNote):
                txt = norm_ascii(e.Text)
                if "LEGENDA" in txt or "LUMINARIA" in txt:
                    user_title_note = e
                    break

        x0 = min_x if min_x is not None else 0.0
        row_step = total_width * 0.0345

        if user_title_note is not None:
            doc.Regenerate()
            t_bb = user_title_note.get_BoundingBox(target_leg)
            if t_bb:
                # Procura se o usuário já desenhou a linha horizontal embaixo do título
                for e in remaining_elems:
                    if isinstance(e, DB.CurveElement):
                        c = e.GeometryCurve
                        p0 = c.GetEndPoint(0)
                        p1 = c.GetEndPoint(1)
                        if abs(p0.Y - p1.Y) < 1e-4 and (t_bb.Min.Y - 3.0 <= p0.Y <= t_bb.Min.Y + 0.5):
                            user_underline_y = p0.Y
                            user_x0 = min(p0.X, p1.X)
                            break

                if user_x0 is not None:
                    x0 = user_x0
                else:
                    x0 = t_bb.Min.X - total_width * 0.018

                if user_underline_y is None:
                    # Cria a linha embaixo do título do usuário no mesmo padrão do LEGENDA GRÁFICA
                    user_underline_y = t_bb.Min.Y - 0.45
                    ulen = ref_underline_len if ref_underline_len else (total_width * 0.24)
                    uline = DB.Line.CreateBound(
                        DB.XYZ(x0, user_underline_y, 0.0),
                        DB.XYZ(x0 + ulen, user_underline_y, 0.0),
                    )
                    dline = doc.Create.NewDetailCurve(target_leg, uline)
                    if header_line_style:
                        dline.LineStyle = header_line_style

                first_row_y = user_underline_y - row_step * 0.90
            else:
                first_row_y = -row_step * 0.85
        else:
            # Se ainda não houver título na vista, cria "LEGENDA DE LUMINÁRIAS" + sublinhado no padrão "LEGENDA GRÁFICA"
            y0 = 0.0
            title_pt = DB.XYZ(x0 + total_width * 0.018, y0, 0.0)
            title_note = DB.TextNote.Create(
                doc, target_leg.Id, title_pt, u"LEGENDA DE LUMIN\u00c1RIAS", title_type_id
            )
            doc.Regenerate()
            t_bb = title_note.get_BoundingBox(target_leg)
            line_y = (t_bb.Min.Y - 0.45) if t_bb else (y0 - 1.5)
            ulen = ref_underline_len if ref_underline_len else (total_width * 0.24)
            uline = DB.Line.CreateBound(DB.XYZ(x0, line_y, 0.0), DB.XYZ(x0 + ulen, line_y, 0.0))
            dline = doc.Create.NewDetailCurve(target_leg, uline)
            if header_line_style:
                dline.LineStyle = header_line_style
            first_row_y = line_y - row_step * 0.90

        col_w = total_width * 0.49
        col_gap = total_width * 0.02
        sym_offset_x = total_width * 0.048
        text_offset_x = total_width * 0.105
        text_box_w = col_w - text_offset_x - (total_width * 0.008)

        global_scale = 1.80
        linear_target_len = (text_offset_x - sym_offset_x) * 1.12
        max_sym_h = row_step * 0.78

        magenta_color = DB.Color(255, 0, 128)
        ogs_magenta = DB.OverrideGraphicSettings()
        ogs_magenta.SetProjectionLineColor(magenta_color)

        total_items = len(items_data)
        rows_per_col = int(math.ceil(total_items / 2.0)) if total_items > 0 else 15

        for idx, (_, code, name_ascii, desc_pt, sym) in enumerate(items_data):
            col = 0 if idx < rows_per_col else 1
            row = idx if idx < rows_per_col else (idx - rows_per_col)

            col_x = x0 + col * (col_w + col_gap)
            row_y = first_row_y - row * row_step
            target_sym_pt = DB.XYZ(col_x + sym_offset_x, row_y, 0.0)

            prims = []
            if sym and template_comp:
                copied = DB.ElementTransformUtils.CopyElement(doc, template_comp.Id, DB.XYZ(0, 0, 0))
                comp_id = copied[0]
                comp = doc.GetElement(comp_id)
                p_lc = comp.get_Parameter(DB.BuiltInParameter.LEGEND_COMPONENT)
                p_vd = comp.get_Parameter(DB.BuiltInParameter.LEGEND_COMPONENT_VIEW)

                try:
                    p_lc.Set(sym.Id)
                except Exception:
                    pass

                for vdir in [-8, -7, -5]:
                    try:
                        if p_vd:
                            p_vd.Set(vdir)
                        doc.Regenerate()
                        cand = extract_primitives_in_view(comp, target_leg)
                        if len(cand) >= 1:
                            prims = cand
                            break
                    except Exception:
                        pass

                code_norm = code.replace(".", "-")
                if code_norm == "SPT-02" or len(prims) == 0:
                    try:
                        if p_vd:
                            p_vd.Set(-8)
                        doc.Regenerate()
                        cand_3d = extract_primitives_in_view(comp, None)
                        if len(cand_3d) > len(prims):
                            prims = cand_3d
                    except Exception:
                        pass

                doc.Delete(comp.Id)

            if len(prims) == 0 and sym:
                prims = extract_primitives_in_view(sym, None)

            xs = []
            ys = []
            for p in prims:
                if p[0] == "circle":
                    _, cx, cy, r = p
                    xs.extend([cx - r, cx + r])
                    ys.extend([cy - r, cy + r])
                elif p[0] == "line":
                    _, lx0, ly0, lx1, ly1 = p
                    xs.extend([lx0, lx1])
                    ys.extend([ly0, ly1])
                elif p[0] == "arc":
                    _, ax0, ay0, ax1, ay1, axm, aym = p
                    xs.extend([ax0, ax1, axm])
                    ys.extend([ay0, ay1, aym])

            created_curves = []
            if len(xs) >= 2:
                c_min_x, c_max_x = min(xs), max(xs)
                c_min_y, c_max_y = min(ys), max(ys)
                cw = c_max_x - c_min_x
                ch = c_max_y - c_min_y
                c_cx = 0.5 * (c_min_x + c_max_x)
                c_cy = 0.5 * (c_min_y + c_max_y)

                code_norm = code.replace(".", "-")
                is_true_linear = is_linear_item(code_norm, name_ascii, cw, ch)
                rotate_90 = (ch > cw * 1.35) and (
                    is_true_linear or code_norm in ["EXT-01", "EXT-03", "EXT-04", "EXT-05", "LIN-02", "NLI-06"]
                )
                if rotate_90:
                    cw, ch = ch, cw

                s = global_scale
                max_dim = max(cw, ch)

                if code_norm == "SPT-01" or "MINI SPOT" in name_ascii:
                    s = (0.38 / max_dim) if max_dim > 1e-4 else global_scale
                elif code_norm.startswith("ARA-") or "ARANDELA" in name_ascii:
                    s = (0.95 / max_dim) if max_dim > 1e-4 else global_scale
                elif code_norm in ["EXT-01", "EXT-03", "EXT-04", "EXT-05"]:
                    s = (1.45 / max_dim) if max_dim > 1e-4 else global_scale
                elif not is_true_linear:
                    if max_dim * s < 0.72 and max_dim > 1e-4:
                        s = 0.72 / max_dim
                    if ch * s > max_sym_h:
                        s = max_sym_h / ch
                    if cw * s > max_sym_h * 1.35:
                        s = min(s, (max_sym_h * 1.35) / cw)
                else:
                    if ch * s > 0.78 and ch > 1e-4:
                        s = 0.78 / ch
                    elif ch * s < 0.22 and ch > 1e-4:
                        s = 0.22 / ch

                if is_true_linear and cw > 1e-4:
                    scale_x = linear_target_len / cw
                    scale_y = s
                else:
                    scale_x = s
                    scale_y = s

                def tx_pt(px, py):
                    dx = px - c_cx
                    dy = py - c_cy
                    if rotate_90:
                        dx, dy = dy, -dx
                    return DB.XYZ(target_sym_pt.X + dx * scale_x, target_sym_pt.Y + dy * scale_y, 0.0)

                for p in prims[:150]:
                    try:
                        if p[0] == "circle":
                            _, cx, cy, r = p
                            nc = tx_pt(cx, cy)
                            nr = max(r * scale_y, 0.04)
                            dc = doc.Create.NewDetailCurve(
                                target_leg,
                                DB.Arc.Create(nc, nr, 0.0, 2.0 * math.pi, DB.XYZ.BasisX, DB.XYZ.BasisY),
                            )
                            created_curves.append(dc)
                        elif p[0] == "arc":
                            _, ax0, ay0, ax1, ay1, axm, aym = p
                            np0 = tx_pt(ax0, ay0)
                            np1 = tx_pt(ax1, ay1)
                            npm = tx_pt(axm, aym)
                            if np0.DistanceTo(np1) > 0.005:
                                dc = doc.Create.NewDetailCurve(target_leg, DB.Arc.Create(np0, np1, npm))
                                created_curves.append(dc)
                        elif p[0] == "line":
                            _, lx0, ly0, lx1, ly1 = p
                            np0 = tx_pt(lx0, ly0)
                            np1 = tx_pt(lx1, ly1)
                            if np0.DistanceTo(np1) > 0.005:
                                dc = doc.Create.NewDetailCurve(target_leg, DB.Line.CreateBound(np0, np1))
                                created_curves.append(dc)
                    except Exception:
                        pass

            for dc in created_curves:
                try:
                    target_leg.SetElementOverrides(dc.Id, ogs_magenta)
                except Exception:
                    pass

            label_text = u"{} - {}".format(code, desc_pt)
            txt_pt = DB.XYZ(col_x + text_offset_x, row_y + 0.35, 0.0)
            tn = DB.TextNote.Create(doc, target_leg.Id, txt_pt, label_text, body_type_id)
            try:
                tn.Width = text_box_w
            except Exception:
                pass

        if template_comp:
            doc.Delete(template_comp.Id)

        t.Commit()

        current_items = [u"{} - {}".format(code, desc_pt) for _, code, _, desc_pt, _ in items_data]
        current_set = set(current_items)
        added_items = [it for it in current_items if it not in previous_items] if previous_items else []
        removed_items = sorted([it for it in previous_items if it not in current_set]) if previous_items else []

        report_lines = [
            u"Legenda '{}' atualizada com sucesso!".format(target_leg_name),
            u"Total de tipos na legenda: {}".format(total_items),
        ]

        if added_items or removed_items:
            report_lines.append(u"")
            report_lines.append(u"Relatório de alterações:")
            for it in added_items:
                report_lines.append(u"  + Adicionado: {}".format(it))
            for it in removed_items:
                report_lines.append(u"  - Removido: {}".format(it))
        elif previous_items:
            report_lines.append(u"Alterações: Nenhuma inclusão ou remoção detectada.")

        report_lines.append(u"")
        report_lines.append(u"Quer abrir a vista?")

        abrir_vista = forms.alert(
            u"\n".join(report_lines),
            title=u"Legenda LUM",
            ok=False,
            yes=True,
            no=True,
        )

        if abrir_vista:
            try:
                uidoc.ActiveView = target_leg
            except Exception:
                pass

    except Exception as ex:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        forms.alert(u"Erro ao atualizar legenda:\n{}".format(str(ex)), title=u"Erro")


if __name__ == "__main__":
    main()
