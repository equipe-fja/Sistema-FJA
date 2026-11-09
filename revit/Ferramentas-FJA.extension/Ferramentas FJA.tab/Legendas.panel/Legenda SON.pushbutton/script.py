# -*- coding: utf-8 -*-
"""Legenda SON: familias '(SON)' ou '(SNZ)' visiveis nas pranchas, na cor do projeto.
A logica fica em lib/fja_legenda_disciplina.py (igual para LOG, AUT e SON)."""
from fja_legenda_disciplina import main

CFG = {
    "botao": u"Legenda SON",
    "alvo": u"SON - LEGENDA DE PONTOS DE SONORIZA\u00c7\u00c3O",
    "titulo": u"LEGENDA DE PONTOS DE SONORIZA\u00c7\u00c3O",
    "familias": ("(SON)", "(SNZ)"),
    "vistas": ("SON", "SNZ"),
    "bases": ("SON - LEGENDA GRAFICA", "SNZ - LEGENDA GRAFICA", "SON - LEGENDA", "SNZ - LEGENDA"),
    "guid": "721db198-2a3c-40f5-bb77-93fd4d40a439",
    "schema": "FJA_LegendaSON",
    "mark": "LegendaSON",
    "cor": (140, 195, 25),
}

if __name__ == "__main__":
    main(CFG)
