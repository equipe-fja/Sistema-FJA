# -*- coding: utf-8 -*-
"""Atualização da suíte Ferramentas FJA a partir do pacote que o Sistema FJA publica no banco.

O sistema (sistema-fja.web.app) grava em revitPub/extensao-versao {versao, hash} e em revitPub/extensao
{versao, hash, json} o mesmo pacote do botão "Baixar o instalador". O botão Suíte › Atualizar lê daí,
grava os arquivos na pasta da extensão em uso (no escritório, a pasta compartilhada do G:, que vale para
todos) e recarrega o pyRevit.

Não depende do pyRevit (dá para testar pelo conector do Revit).
"""
import base64
import io
import json
import os

NOME_EXT = "Ferramentas-FJA.extension"


def pasta_extensao():
    """Pasta Ferramentas-FJA.extension em uso (esta biblioteca fica em <extensão>/lib)."""
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def versao_local(ext=None):
    try:
        with io.open(os.path.join(ext or pasta_extensao(), "extension.json"), encoding="utf-8") as f:
            return json.load(f).get("version") or u"0"
    except Exception:
        return u"0"


def _num(v):
    out = []
    for p in u"{}".format(v or u"0").split("."):
        try:
            out.append(int(p))
        except ValueError:
            out.append(0)
    while len(out) < 3:
        out.append(0)
    return tuple(out)


def comparar(a, b):
    """-1, 0 ou 1 (a < b, a == b, a > b)."""
    x, y = _num(a), _num(b)
    return (x > y) - (x < y)


def decodificar(pacote):
    """{caminho relativo à extensão: bytes} a partir de {versao, arquivos:[{nome, b64}]}. Valida tudo antes."""
    arqs = {}
    for a in pacote.get("arquivos") or []:
        nome = (a.get("nome") or u"").replace("\\", "/")
        partes = [p for p in nome.split("/") if p]
        if len(partes) < 2 or partes[0] != NOME_EXT or any(p in (".", "..") for p in partes):
            raise ValueError(u"caminho inesperado no pacote: {}".format(nome))
        arqs["/".join(partes[1:])] = base64.b64decode(a.get("b64") or "")
    if "extension.json" not in arqs:
        raise ValueError(u"o pacote não tem extension.json")
    return arqs


def aplicar(ext, arqs):
    """Grava os arquivos do pacote na pasta da extensão e apaga os que saíram do pacote (menos .pyc).
    Devolve (gravados, iguais, apagados)."""
    if os.path.basename(os.path.normpath(ext)) != NOME_EXT or not os.path.isfile(os.path.join(ext, "extension.json")):
        raise ValueError(u"pasta da extensão inesperada: {}".format(ext))
    gravados = iguais = apagados = 0
    for rel, dados in sorted(arqs.items()):
        p = os.path.join(ext, *rel.split("/"))
        d = os.path.dirname(p)
        if not os.path.isdir(d):
            os.makedirs(d)
        try:
            with io.open(p, "rb") as f:
                if f.read() == dados:
                    iguais += 1
                    continue
        except IOError:
            pass
        with io.open(p, "wb") as f:
            f.write(dados)
        gravados += 1
    manter = set(os.path.normcase(os.path.join(ext, *rel.split("/"))) for rel in arqs)
    for raiz, pastas, nomes in os.walk(ext, topdown=False):
        for n in nomes:
            p = os.path.join(raiz, n)
            if n.lower().endswith(".pyc") or os.path.normcase(p) in manter:
                continue
            try:
                os.remove(p)
                apagados += 1
            except OSError:
                pass
        if raiz != ext:
            try:
                if not os.listdir(raiz):
                    os.rmdir(raiz)
            except OSError:
                pass
    return gravados, iguais, apagados
