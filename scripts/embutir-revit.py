#!/usr/bin/env python3
"""Troca o marcador null/*EXT_REVIT*/ pelos arquivos da extensão do pyRevit (revit/Ferramentas-FJA.extension).

A biblioteca C# (Ferramentas-FJA.extension/bin/, compilada pelo GitHub Actions) vai numa lista à parte, "bin":
no banco ela é gravada em documentos separados (limite de 1 MB por documento); no instalador entra junto."""
import base64, json, os, sys
raiz = os.path.join(os.path.dirname(__file__), "..", "revit")
ext = os.path.join(raiz, "Ferramentas-FJA.extension")
versao = json.load(open(os.path.join(ext, "extension.json"), encoding="utf-8")).get("version", "?")
arqs, binarios = [], []
for d, _, fs in sorted(os.walk(ext)):
    for f in sorted(fs):
        if f.endswith((".pyc", ".log")) or f.startswith("."):
            continue
        cam = os.path.join(d, f)
        rel = os.path.relpath(cam, raiz).replace(os.sep, "/")
        (binarios if rel.startswith("Ferramentas-FJA.extension/bin/") else arqs).append({"nome": os.path.relpath(cam, raiz).replace(os.sep, "/"),
                     "b64": base64.b64encode(open(cam, "rb").read()).decode()})
instalar = open(os.path.join(raiz, "instalar.ps1"), encoding="utf-8").read().replace("__VERSAO__", versao)
dados = json.dumps({"versao": versao, "arquivos": arqs, "bin": binarios, "instalar": instalar}, ensure_ascii=False, separators=(",", ":"))
for p in sys.argv[1:]:
    s = open(p, encoding="utf-8").read()
    assert s.count("null/*EXT_REVIT*/") == 1, p
    open(p, "w", encoding="utf-8").write(s.replace("null/*EXT_REVIT*/", dados))
print("extensão v{} embutida ({} arquivos + {} da biblioteca C#)".format(versao, len(arqs), len(binarios)))
