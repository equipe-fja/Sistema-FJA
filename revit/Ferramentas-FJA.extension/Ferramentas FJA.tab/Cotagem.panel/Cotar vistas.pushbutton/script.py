# -*- coding: utf-8 -*-
"""Cotar vistas: cotagem automática pelas regras da FJA (tipo da vista x escala). Em C#
(revit/dotnet/FJA.Revit, comando CotarVistas); ver docs/cotagem.md."""
import fja_dotnet as D

D.rodar_botao("CotarVistas", __revit__, u"Cotagem")
