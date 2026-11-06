# -*- coding: utf-8 -*-
"""Atualiza a suíte Ferramentas FJA a partir do pacote publicado pelo Sistema FJA (banco) e recarrega o pyRevit."""
import json

from pyrevit import forms

import fja_nuvem as N
import fja_suite as U

TITULO = u"Atualizar Ferramentas FJA"


def ler(caminho):
    """Documento do banco, pedindo login se preciso. None = não existe; False = a pessoa desistiu."""
    for tentativa in range(3):
        try:
            return N.ler(caminho)
        except N.SemLogin as e:
            aviso = u"" if tentativa == 0 and not N.email_atual() else u"{}".format(e)
            if not N.pedir_login(aviso):
                return False
    return False


def principal():
    ext = U.pasta_extensao()
    local = U.versao_local(ext)
    try:
        info = ler("revitPub/extensao-versao")
    except N.ErroNuvem as e:
        forms.alert(u"Não foi possível consultar o Sistema FJA:\n{}".format(e), title=TITULO)
        return
    if info is False:
        return
    if not info or not info.get("versao"):
        forms.alert(u"O Sistema FJA ainda não publicou o pacote da extensão.\n\n"
                    u"Abra o sistema (sistema-fja.web.app) uma vez com a versão nova e tente de novo.", title=TITULO)
        return
    nova = info["versao"]
    c = U.comparar(nova, local)
    if c < 0:
        forms.alert(u"Esta pasta já tem a versão {}, mais nova que a publicada ({}). Nada a fazer.".format(local, nova),
                    title=TITULO)
        return
    if c == 0:
        if forms.alert(u"Você já tem a versão mais recente ({}).".format(local), title=TITULO,
                       options=[u"Fechar", u"Reinstalar os arquivos"]) != u"Reinstalar os arquivos":
            return
    else:
        compart = u"G:" in ext.upper() or u"DRIVES COMPARTILHADOS" in ext.upper()
        if not forms.alert(u"Atualizar a aba Ferramentas FJA da versão {} para a {}?".format(local, nova),
                           sub_msg=(u"Pasta: {}\n\n".format(ext) +
                                    (u"É a pasta compartilhada do G:: a atualização vale para todos do escritório "
                                     u"(cada um só precisa de pyRevit › Reload)." if compart else
                                     u"A atualização vale só para este computador.")),
                           title=TITULO, yes=True, no=True):
            return
    try:
        pac = ler("revitPub/extensao")
        if not pac:
            return
        pacote = json.loads(pac.get("json") or "{}")
        arqs = U.decodificar(pacote)
        g, iguais, apag = U.aplicar(ext, arqs)
    except Exception as e:
        forms.alert(u"A atualização não foi concluída:\n{}".format(e), title=TITULO)
        return
    msg = u"Ferramentas FJA {} instalada: {} arquivo(s) gravado(s), {} já estavam iguais, {} removido(s).".format(
        pacote.get("versao") or nova, g, iguais, apag)
    if forms.alert(msg, sub_msg=u"Recarregar o pyRevit agora para usar a versão nova?", title=TITULO,
                   yes=True, no=True):
        from pyrevit.loader import sessionmgr
        sessionmgr.reload_pyrevit()


principal()
