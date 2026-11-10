# -*- coding: utf-8 -*-
"""Prévia LOG: a legenda de pontos de lógica numa janela com prévia do próprio Revit.
Desde a 2.6.0 em C# (revit/dotnet/FJA.Revit, comando PreviaLegendaLOG); ver docs/revit-csharp.md."""
import fja_dotnet as D

D.rodar_botao("PreviaLegendaLOG", __revit__, u"Legenda LOG · prévia")
