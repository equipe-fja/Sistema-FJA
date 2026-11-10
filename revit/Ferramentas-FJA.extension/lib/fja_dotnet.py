# -*- coding: utf-8 -*-
"""Ponte entre os botões do pyRevit e a biblioteca C# da suíte (FJA.Revit.dll).

A DLL fica em <extensão>/bin/<geração>/FJA.Revit.dll:
    bin/R2024  -> Revit 2024 (.NET Framework 4.8)
    bin/R2027  -> Revit 2027 em diante (.NET 10)

Ela é carregada a partir da memória (Assembly.Load com os bytes do arquivo), nunca direto do arquivo.
Assim o Windows não trava a DLL: o Suíte › Atualizar pode substituí-la com o Revit aberto, e quem desenvolve
pode recompilar e testar de novo sem reiniciar o Revit.

Cada versão da DLL é carregada uma vez por sessão do Revit (cache no AppDomain, chave = caminho + tamanho +
data do arquivo). Se o arquivo mudar, a versão nova é carregada no próximo clique; a anterior continua na
memória até o Revit fechar (o .NET não descarrega DLLs).

Não depende do pyRevit (dá para testar pelo conector do Revit):
    import sys; sys.path.append(r"<extensão>\\lib"); import fja_dotnet
    print(fja_dotnet.executar("Sobre", uiapp))
"""
import io
import json
import os

import clr  # noqa: F401  (IronPython)
from System import AppDomain, Array, Object
from System.IO import File

NOME_DLL = "FJA.Revit.dll"


class ErroDotnet(Exception):
    pass


def pasta_extensao():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def versao_extensao():
    try:
        with io.open(os.path.join(pasta_extensao(), "extension.json"), encoding="utf-8") as f:
            return json.load(f).get("version") or u"?"
    except Exception:
        return u"?"


def geracao(uiapp):
    """Pasta da DLL para a versão do Revit em uso."""
    v = int(uiapp.Application.VersionNumber)
    if v <= 2024:
        return "R2024"
    if v >= 2027:
        return "R2027"
    raise ErroDotnet(u"As ferramentas em C# ainda não têm versão para o Revit {} (só 2024 e 2027 em diante).".format(v))


def caminho_dll(uiapp):
    return os.path.join(pasta_extensao(), "bin", geracao(uiapp), NOME_DLL)


def carregar(dll):
    """Assembly carregado da memória; reaproveita o mesmo enquanto o arquivo não mudar."""
    if not os.path.isfile(dll):
        raise ErroDotnet(u"A biblioteca C# não está instalada nesta pasta:\n{}\n\n"
                         u"Use Suíte › Atualizar para baixar a versão mais recente.".format(dll))
    st = os.stat(dll)
    chave = u"FJA.Revit|{}|{}|{}".format(os.path.normcase(dll), st.st_size, int(st.st_mtime))
    dom = AppDomain.CurrentDomain
    asm = dom.GetData(chave)
    if asm is None:
        from System.Reflection import Assembly
        dados = File.ReadAllBytes(dll)
        pdb = dll[:-4] + ".pdb"
        if os.path.isfile(pdb):
            asm = Assembly.Load(dados, File.ReadAllBytes(pdb))  # com o .pdb, os erros mostram a linha do código
        else:
            asm = Assembly.Load(dados)
        dom.SetData(chave, asm)
    return asm


def executar(comando, uiapp, dll=None):
    """Roda FJA.Revit.Comandos.<comando>. Devolve o Resultado (Status: ok / cancelado / erro, Mensagem, Detalhe)."""
    asm = carregar(dll or caminho_dll(uiapp))
    try:
        # pasta da extensão para o C# (a DLL vem da memória e não sabe de onde saiu): estilo e logs
        AppDomain.CurrentDomain.SetData("FJA.Extensao", pasta_extensao())
    except Exception:
        pass
    tipo = asm.GetType("FJA.Revit.Entrada")
    if tipo is None:
        raise ErroDotnet(u"A DLL não tem a classe FJA.Revit.Entrada.")
    metodo = tipo.GetMethod("Executar")
    try:
        return metodo.Invoke(None, Array[Object]([comando, uiapp]))
    except Exception as e:
        interno = getattr(getattr(e, "clsException", None), "InnerException", None)
        raise ErroDotnet(u"{}".format(interno.Message if interno is not None else e))


def versao_dll(uiapp, dll=None):
    asm = carregar(dll or caminho_dll(uiapp))
    return u"{}".format(asm.GetType("FJA.Revit.Entrada").GetProperty("Versao").GetValue(None, None))


def rodar_botao(comando, uiapp, titulo=u"Ferramentas FJA"):
    """Para usar no script.py de um botão: roda o comando e mostra o erro, se houver."""
    from pyrevit import forms
    try:
        r = executar(comando, uiapp)
    except ErroDotnet as e:
        forms.alert(u"{}".format(e), title=titulo)
        return None
    if r.Status == "erro":
        forms.alert(r.Mensagem or u"O comando falhou.", sub_msg=r.Detalhe or None, title=titulo)
    return r
