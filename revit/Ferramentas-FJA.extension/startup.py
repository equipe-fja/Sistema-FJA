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
    # informa ao Sistema FJA a versão instalada (aviso de atualização na página inicial); em segundo plano
    try:
        from System.Threading import Thread, ThreadStart
        t = Thread(ThreadStart(_registrar_versao))
        t.IsBackground = True
        t.Start()
    except Exception:
        pass


def _registrar_versao():
    try:
        import fja_nuvem
        import fja_suite
        try:
            rv = __revit__.Application.VersionNumber  # noqa: F821
        except Exception:
            rv = u""
        fja_nuvem.registrar_versao(fja_suite.versao_local(), rv)
    except Exception:
        pass


try:
    __revit__.Idling += _ocioso  # noqa: F821
except Exception:
    pass


# painéis encaixáveis da biblioteca C# (Tipos › Componentes): o Revit só aceita registrar na abertura
try:
    import fja_dotnet as _D
    _D.registrar_paineis(__revit__)  # noqa: F821
except Exception:
    pass
