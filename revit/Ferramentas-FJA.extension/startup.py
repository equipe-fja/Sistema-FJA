# -*- coding: utf-8 -*-
"""Ferramentas FJA — roda quando o pyRevit carrega a extensão (abertura do Revit e Reload).

O pyRevit roda este arquivo antes de montar a aba; por isso a arrumação fica para o primeiro momento
ocioso do Revit, quando a aba já existe. O painel Suíte (Atualizar, Sobre) vai sempre para o fim da aba —
o layout do bundle.yaml da aba só vale ao abrir o Revit; num Reload os painéis que já existem não mudam
de lugar."""
ABA = u"Ferramentas FJA"
SUITE = u"Su\xedte"


def suite_no_fim():
    import clr
    clr.AddReference("AdWindows")
    import Autodesk.Windows as AW
    for t in AW.ComponentManager.Ribbon.Tabs:
        if t.Title != ABA:
            continue
        for p in [p for p in t.Panels if p.Source is not None and p.Source.Title == SUITE]:
            if t.Panels.IndexOf(p) != t.Panels.Count - 1:
                t.Panels.Remove(p)
                t.Panels.Add(p)


def _ocioso(sender, args):
    try:
        sender.Idling -= _ocioso
    except Exception:
        pass
    try:
        suite_no_fim()
    except Exception:
        pass


try:
    __revit__.Idling += _ocioso  # noqa: F821
except Exception:
    pass
