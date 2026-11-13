# -*- coding: utf-8 -*-
"""Trocar lado: troca o lado da bolha dos eixos e da cabeca dos niveis selecionados, em 2D, na vista ativa.
Em C# (revit/dotnet/FJA.Revit, comando TrocarLadoIndicacao); ver docs/cotagem.md."""
import fja_dotnet as D

D.rodar_botao("TrocarLadoIndicacao", __revit__, u"Trocar lado da indicação")
