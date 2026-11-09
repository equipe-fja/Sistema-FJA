# -*- coding: utf-8 -*-
"""Atualização da suíte Ferramentas FJA a partir do pacote que o Sistema FJA publica no banco.

O sistema (sistema-fja.web.app) grava em revitPub/extensao-versao {versao, hash, binHash, binPartes} e em
revitPub/extensao {versao, hash, json} o mesmo pacote do botão "Baixar o instalador". O botão Suíte › Atualizar
lê daí, grava os arquivos na pasta da extensão em uso (no escritório, a pasta compartilhada do G:, que vale
para todos) e recarrega o pyRevit.

A biblioteca C# (pasta bin/, desde a 2.5.0) vai em documentos separados, revitPub/extensao-bin-0…N-1
{versao, hash, i, n, txt}, porque um documento do banco tem limite de 1 MB. Só é baixada quando o hash
publicado (binHash) difere do registrado em bin/pacote.json. As DLLs são carregadas a partir da memória
(lib/fja_dotnet.py), então podem ser substituídas com o Revit aberto.

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


def _decod(lista):
    arqs = {}
    for a in lista or []:
        nome = (a.get("nome") or u"").replace("\\", "/")
        partes = [p for p in nome.split("/") if p]
        if len(partes) < 2 or partes[0] != NOME_EXT or any(p in (".", "..") for p in partes):
            raise ValueError(u"caminho inesperado no pacote: {}".format(nome))
        arqs["/".join(partes[1:])] = base64.b64decode(a.get("b64") or "")
    return arqs


def decodificar(pacote):
    """{caminho relativo à extensão: bytes} a partir de {versao, arquivos:[{nome, b64}]}. Valida tudo antes."""
    arqs = _decod(pacote.get("arquivos"))
    if "extension.json" not in arqs:
        raise ValueError(u"o pacote não tem extension.json")
    return arqs


# ------------------------------------------------------------------ biblioteca C# (bin/)
ARQ_BIN = "bin/pacote.json"


def bin_hash_local(ext=None):
    """Hash do pacote bin/ instalado nesta pasta ('' se não houver)."""
    try:
        with io.open(os.path.join(ext or pasta_extensao(), *ARQ_BIN.split("/")), encoding="utf-8") as f:
            return json.load(f).get("hash") or u""
    except Exception:
        return u""


def juntar_bin(partes, info):
    """Junta os documentos revitPub/extensao-bin-<i> e devolve {caminho: bytes}, incluindo bin/pacote.json.
    partes = lista de documentos na ordem; info = revitPub/extensao-versao."""
    n = int(info.get("binPartes") or 0)
    h = info.get("binHash") or u""
    if len(partes) != n or not n:
        raise ValueError(u"pacote da biblioteca C# incompleto ({} de {} partes)".format(len(partes), n))
    for i, p in enumerate(partes):
        if not p or p.get("hash") != h or int(p.get("i", -1)) != i or int(p.get("n", 0)) != n:
            raise ValueError(u"a parte {} da biblioteca C# não confere (publicação em andamento? tente de novo)".format(i))
    pac = json.loads(u"".join(p.get("txt") or u"" for p in partes))
    arqs = _decod(pac.get("arquivos"))
    if any(not k.startswith("bin/") for k in arqs):
        raise ValueError(u"o pacote da biblioteca C# tem arquivos fora de bin/")
    arqs[ARQ_BIN] = json.dumps({"versao": pac.get("versao") or info.get("versao"), "hash": h}).encode("utf-8")
    return arqs


def aplicar(ext, arqs, preservar=()):
    """Grava os arquivos do pacote na pasta da extensão e apaga os que saíram do pacote (menos .pyc e os
    caminhos que começam por algum prefixo de `preservar`, ex.: "bin/" quando a biblioteca C# não veio junto).
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
            rel = os.path.relpath(p, ext).replace(os.sep, "/")
            if n.lower().endswith(".pyc") or os.path.normcase(p) in manter or rel.startswith(tuple(preservar)):
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
