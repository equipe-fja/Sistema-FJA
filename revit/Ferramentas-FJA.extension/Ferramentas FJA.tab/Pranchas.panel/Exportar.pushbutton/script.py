# -*- coding: utf-8 -*-
"""Exportar pranchas: PDF e DWG com o nome do campo Prancha do carimbo. Em C#
(revit/dotnet/FJA.Revit, comando ExportarPranchas); ver docs/exportar-pranchas.md."""
import fja_dotnet as D

D.rodar_botao("ExportarPranchas", __revit__, u"Exportar pranchas")
