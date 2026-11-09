# -*- coding: utf-8 -*-
"""Suíte › Sobre: versão da extensão e da biblioteca C#. Primeiro botão que chama a DLL (FJA.Revit.Comandos.Sobre)."""
from pyrevit import forms

import fja_dotnet as D

TITULO = u"Ferramentas FJA"
uiapp = __revit__  # noqa: F821

ext = D.versao_extensao()
try:
    dll = D.versao_dll(uiapp)
except D.ErroDotnet as e:
    forms.alert(u"Ferramentas FJA {}.\n\n{}".format(ext, e), title=TITULO)
else:
    if dll.split("+")[0] != ext:
        forms.alert(u"A aba está na versão {}, mas a biblioteca C# está na {}.".format(ext, dll.split("+")[0]),
                    sub_msg=u"Use Suíte › Atualizar para alinhar as duas.", title=TITULO)
    D.rodar_botao("Sobre", uiapp, TITULO)
