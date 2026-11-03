# -*- coding: utf-8 -*-
"""Renomeia todos os tipos de uma categoria selecionada para o padrão 'Type Mark - Descrição'."""
import re
from pyrevit import revit, DB, forms


def sanitize_revit_name(name):
    """Substitui caracteres proibidos em nomes de elementos do Revit."""
    if not name:
        return ""
    return re.sub(r'[\\:\{\}\[\]\|;<>?`~]', "-", name).strip()


def get_elem_id_int(elem_id):
    try:
        return int(elem_id.Value)
    except Exception:
        return int(elem_id.IntegerValue)


class CategoryOption(forms.TemplateListItem):
    """Item de categoria para exibição no SelectFromList do pyRevit."""

    @property
    def name(self):
        return u"{} ({} tipos)".format(self.item["name"], len(self.item["types"]))


def main():
    doc = revit.doc

    all_types = (
        DB.FilteredElementCollector(doc)
        .WhereElementIsElementType()
        .ToElements()
    )

    cat_map = {}
    for et in all_types:
        cat = et.Category
        if cat is None or not cat.Name:
            continue
        # Ignora categorias internas de sistema que não possuem Type Mark / Description editáveis
        p_mark = et.get_Parameter(DB.BuiltInParameter.ALL_MODEL_TYPE_MARK)
        p_desc = et.get_Parameter(DB.BuiltInParameter.ALL_MODEL_DESCRIPTION)
        if p_mark is None or p_desc is None:
            continue

        cid = get_elem_id_int(cat.Id)
        if cid not in cat_map:
            cat_map[cid] = {
                "id": cid,
                "name": cat.Name,
                "category": cat,
                "types": [],
            }
        cat_map[cid]["types"].append(et)

    if not cat_map:
        forms.alert(
            u"Nenhuma categoria com tipos compatíveis foi encontrada no modelo.",
            title=u"Renomear Tipos",
        )
        return

    sorted_cats = sorted(cat_map.values(), key=lambda c: c["name"].upper())
    options = [CategoryOption(c) for c in sorted_cats]

    selected = forms.SelectFromList.show(
        options,
        title=u"Selecione a Categoria para Renomear os Tipos (Type Mark - Descrição)",
        button_name=u"Renomear Tipos",
        multiselect=False,
         width=520,
        height=550,
    )

    if not selected:
        return

    cat_info = selected if isinstance(selected, dict) else getattr(selected, "item", selected)
    cat_name = cat_info["name"]
    elem_types = cat_info["types"]

    renamed_list = []
    unchanged_count = 0
    skipped_list = []

    t = DB.Transaction(doc, u"Renomear Tipos - {}".format(cat_name))
    t.Start()

    try:
        for et in elem_types:
            old_name = DB.Element.Name.__get__(et)

            p_mark = et.get_Parameter(DB.BuiltInParameter.ALL_MODEL_TYPE_MARK)
            p_desc = et.get_Parameter(DB.BuiltInParameter.ALL_MODEL_DESCRIPTION)

            type_mark = (
                p_mark.AsString().strip()
                if (p_mark and p_mark.HasValue and p_mark.AsString())
                else u""
            )
            desc = (
                p_desc.AsString().strip()
                if (p_desc and p_desc.HasValue and p_desc.AsString())
                else u""
            )

            if not type_mark or not desc:
                missing = []
                if not type_mark:
                    missing.append(u"Type Mark")
                if not desc:
                    missing.append(u"Descrição")
                skipped_list.append((old_name, u"sem " + u" e ".join(missing)))
                continue

            # Evita duplicar o código caso a descrição já comece com "CÓDIGO - "
            prefix = u"{} - ".format(type_mark)
            if desc.upper().startswith(prefix.upper()):
                desc = desc[len(prefix):].strip()

            new_name = sanitize_revit_name(u"{} - {}".format(type_mark, desc))
            if not new_name:
                skipped_list.append((old_name, u"nome resultante vazio"))
                continue

            if old_name == new_name:
                unchanged_count += 1
                continue

            try:
                et.Name = new_name
            except Exception:
                # Caso já exista outro tipo na mesma família com o mesmo nome
                new_name = sanitize_revit_name(
                    u"{} - {} ({})".format(type_mark, desc, get_elem_id_int(et.Id))
                )
                if old_name == new_name:
                    unchanged_count += 1
                    continue
                et.Name = new_name

            renamed_list.append((old_name, new_name))

        t.Commit()

    except Exception as ex:
        if t.HasStarted() and not t.HasEnded():
            t.RollBack()
        forms.alert(
            u"Erro ao renomear os tipos da categoria '{}':\n{}".format(cat_name, str(ex)),
            title=u"Erro - Renomear Tipos",
        )
        return

    report = [
        u"Categoria: {}".format(cat_name),
        u"Total de tipos analisados: {}".format(len(elem_types)),
        u"  • Renomeados: {}".format(len(renamed_list)),
        u"  • Já estavam no padrão: {}".format(unchanged_count),
        u"  • Ignorados (parâmetros vazios): {}".format(len(skipped_list)),
    ]

    if renamed_list:
        report.append(u"")
        report.append(u"Tipos renomeados:")
        for old_n, new_n in renamed_list[:20]:
            report.append(u"  • '{}'  ->  '{}'".format(old_n, new_n))
        if len(renamed_list) > 20:
            report.append(u"  ... e mais {} tipo(s).".format(len(renamed_list) - 20))

    if skipped_list:
        report.append(u"")
        report.append(u"Tipos ignorados (preencha Type Mark e Descrição):")
        for old_n, reason in skipped_list[:15]:
            report.append(u"  • '{}' ({})".format(old_n, reason))
        if len(skipped_list) > 15:
            report.append(u"  ... e mais {} tipo(s).".format(len(skipped_list) - 15))

    forms.alert(u"\n".join(report), title=u"Renomear Tipos")


if __name__ == "__main__":
    main()
