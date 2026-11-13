# -*- coding: utf-8 -*-
"""Estender/Alinhar: leva as pontas de eixos, niveis e cortes ate uma linha de referencia (como o Aparar/Estender do Revit).
Em C# (revit/dotnet/FJA.Revit, comando EstenderAlinhar); ver docs/grids-niveis-cortes.md."""
import fja_dotnet as D

D.rodar_botao("EstenderAlinhar", __revit__, u"Estender/Alinhar")
