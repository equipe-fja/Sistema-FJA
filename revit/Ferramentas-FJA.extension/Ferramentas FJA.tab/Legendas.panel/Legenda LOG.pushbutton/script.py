# -*- coding: utf-8 -*-
"""Legenda LOG: familias '(LOG)' visiveis nas pranchas, na cor do projeto.
A logica fica em lib/fja_legenda_disciplina.py (igual para LOG, AUT e SON)."""
from fja_legenda_disciplina import main

CFG = {
    "botao": u"Legenda LOG",
    "alvo": u"LOG - LEGENDA DE PONTOS DE L\u00d3GICA",
    "titulo": u"LEGENDA DE PONTOS DE L\u00d3GICA",
    "familias": ("(LOG)",),
    "vistas": ("LOG",),
    "bases": ("LOG - LEGENDA GRAFICA", "LOG - LEGENDA"),
    "guid": "9a4e7c21-5b3d-4e8f-a6c2-3d1f8b7e5a92",
    "schema": "FJA_LegendaLOG",
    "mark": "LegendaLOG",
    "cor": (121, 86, 169),
}

if __name__ == "__main__":
    main(CFG)
