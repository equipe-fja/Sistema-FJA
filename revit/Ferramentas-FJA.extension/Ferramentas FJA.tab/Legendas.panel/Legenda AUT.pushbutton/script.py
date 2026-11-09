# -*- coding: utf-8 -*-
"""Legenda AUT: familias '(AUT)' visiveis nas pranchas, na cor do projeto.
A logica fica em lib/fja_legenda_disciplina.py (igual para LOG, AUT e SON)."""
from fja_legenda_disciplina import main

CFG = {
    "botao": u"Legenda AUT",
    "alvo": u"AUT - LEGENDA DE PONTOS DE AUTOMA\u00c7\u00c3O",
    "titulo": u"LEGENDA DE PONTOS DE AUTOMA\u00c7\u00c3O",
    "familias": ("(AUT)",),
    "vistas": ("AUT",),
    "bases": ("AUT - LEGENDA GRAFICA", "AUT - LEGENDA"),
    "guid": "1d86c717-cc28-4626-b9c1-efb5663f1b10",
    "schema": "FJA_LegendaAUT",
    "mark": "LegendaAUT",
    "cor": (255, 110, 45),
}

if __name__ == "__main__":
    main(CFG)
