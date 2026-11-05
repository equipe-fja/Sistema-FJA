# -*- coding: utf-8 -*-
"""Acesso direto ao banco do Sistema FJA (Firebase / Firestore), sem passar pelo G:.

- Login com o mesmo e-mail e senha do sistema (Firebase Authentication, API REST).
  O token de renovação fica em %APPDATA%\\FJA\\nuvem.json; a senha nunca é guardada.
- Leitura: revitPub/<nome> (o sistema grava ali o mesmo conteúdo de biblioteca.json e projeto-*.json).
- Pedidos: revitPedidos/<id> {json, status:'pendente'}; o sistema aberto em qualquer computador
  processa na hora e grava status 'ok'/'erro' e o resultado.

Só .NET (WebClient) e json: funciona no IronPython 2.7/3.4 do pyRevit.
"""
import io
import json
import os
import time

import System
from System.Net import WebClient, WebException, ServicePointManager, SecurityProtocolType
from System.Text import Encoding

API_KEY = "AIzaSyD05OgqpjI0mcEQSBh8srC96QyVvwPuh-M"
PROJETO = "sistema-fja"
BASE = "https://firestore.googleapis.com/v1/projects/{}/databases/(default)/documents/".format(PROJETO)
ARQ = os.path.join(os.environ.get("APPDATA") or os.path.expanduser("~"), "FJA", "nuvem.json")

try:
    ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12
except Exception:
    pass


class ErroNuvem(Exception):
    pass


class SemLogin(ErroNuvem):
    pass


_sessao = {"token": None, "exp": 0}


def _ler_cfg():
    try:
        with io.open(ARQ, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def _gravar_cfg(c):
    d = os.path.dirname(ARQ)
    if not os.path.isdir(d):
        os.makedirs(d)
    txt = json.dumps(c, ensure_ascii=False)
    if not isinstance(txt, type(u"")):
        txt = txt.decode("utf-8")
    with io.open(ARQ, "w", encoding="utf-8") as f:
        f.write(txt)


def _http(metodo, url, corpo=None, token=None, tipo="application/json"):
    wc = WebClient()
    wc.Encoding = Encoding.UTF8
    wc.Headers.Add("Content-Type", tipo)
    if token:
        wc.Headers.Add("Authorization", "Bearer " + token)
    try:
        if metodo == "GET":
            txt = wc.DownloadString(url)
        else:
            txt = wc.UploadString(url, metodo, corpo if corpo is not None else u"")
        return json.loads(txt) if txt else {}
    except WebException as e:
        msg, cod = u"{}".format(e.Message), 0
        try:
            cod = int(e.Response.StatusCode)
            leitor = System.IO.StreamReader(e.Response.GetResponseStream())
            j = json.loads(leitor.ReadToEnd())
            msg = (j.get("error") or {}).get("message") or msg
        except Exception:
            pass
        if cod in (401, 403):
            raise SemLogin(msg)
        raise ErroNuvem(msg)


ERROS_LOGIN = {
    "INVALID_LOGIN_CREDENTIALS": u"E-mail ou senha incorretos.", "INVALID_PASSWORD": u"E-mail ou senha incorretos.",
    "EMAIL_NOT_FOUND": u"E-mail ou senha incorretos.", "USER_DISABLED": u"Conta desativada.",
    "TOO_MANY_ATTEMPTS_TRY_LATER": u"Muitas tentativas. Aguarde alguns minutos.",
}


def entrar(email, senha):
    """Login com e-mail e senha do sistema; guarda só o token de renovação."""
    try:
        r = _http("POST", "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + API_KEY,
                  json.dumps({"email": email, "password": senha, "returnSecureToken": True}))
    except ErroNuvem as e:
        chave = u"{}".format(e).split(" ")[0].split(":")[0]
        raise SemLogin(ERROS_LOGIN.get(chave, u"{}".format(e)))
    _sessao["token"], _sessao["exp"] = r["idToken"], time.time() + int(r.get("expiresIn", 3600)) - 120
    _gravar_cfg({"email": r.get("email") or email, "refresh": r["refreshToken"]})
    return r.get("email") or email


def sair():
    _sessao["token"], _sessao["exp"] = None, 0
    try:
        os.remove(ARQ)
    except Exception:
        pass


def email_atual():
    return _ler_cfg().get("email") or u""


def token():
    if _sessao["token"] and time.time() < _sessao["exp"]:
        return _sessao["token"]
    c = _ler_cfg()
    if not c.get("refresh"):
        raise SemLogin(u"Entre com o seu e-mail e senha do Sistema FJA.")
    try:
        r = _http("POST", "https://securetoken.googleapis.com/v1/token?key=" + API_KEY,
                  "grant_type=refresh_token&refresh_token=" + c["refresh"], tipo="application/x-www-form-urlencoded")
    except ErroNuvem:
        raise SemLogin(u"A sessão expirou. Entre de novo com o seu e-mail e senha.")
    _sessao["token"], _sessao["exp"] = r["id_token"], time.time() + int(r.get("expires_in", 3600)) - 120
    if r.get("refresh_token") and r["refresh_token"] != c["refresh"]:
        c["refresh"] = r["refresh_token"]
        _gravar_cfg(c)
    return _sessao["token"]


# ------------------------------------------------------------------ conversão de valores do Firestore
def _de(v):
    if "stringValue" in v:
        return v["stringValue"]
    if "integerValue" in v:
        return int(v["integerValue"])
    if "doubleValue" in v:
        return v["doubleValue"]
    if "booleanValue" in v:
        return v["booleanValue"]
    if "nullValue" in v:
        return None
    if "mapValue" in v:
        return dict((k, _de(x)) for k, x in ((v["mapValue"].get("fields")) or {}).items())
    if "arrayValue" in v:
        return [_de(x) for x in (v["arrayValue"].get("values") or [])]
    return None


def _para(x):
    if x is None:
        return {"nullValue": None}
    if isinstance(x, bool):
        return {"booleanValue": x}
    if isinstance(x, (int, long if str is bytes else int)):
        return {"integerValue": str(x)}
    if isinstance(x, float):
        return {"doubleValue": x}
    if isinstance(x, dict):
        return {"mapValue": {"fields": dict((k, _para(v)) for k, v in x.items())}}
    if isinstance(x, (list, tuple)):
        return {"arrayValue": {"values": [_para(v) for v in x]}}
    return {"stringValue": u"{}".format(x)}


def _doc(j):
    d = dict((k, _de(v)) for k, v in (j.get("fields") or {}).items())
    d["_id"] = j.get("name", "").split("/")[-1]
    return d


# ------------------------------------------------------------------ leitura e pedidos
def ler(caminho):
    """Documento (dict) ou None se não existe."""
    try:
        return _doc(_http("GET", BASE + caminho, token=token()))
    except ErroNuvem as e:
        if "not found" in u"{}".format(e).lower() or "NOT_FOUND" in u"{}".format(e):
            return None
        raise


def publicado(nome):
    """Conteúdo de biblioteca.json / projeto-*.json como o sistema gravou em revitPub/<nome>."""
    d = ler("revitPub/" + nome)
    if not d or not d.get("json"):
        return None
    return json.loads(d["json"])


def pedidos_pendentes():
    """Pedidos ainda não processados (para as reservas de código e keynote)."""
    q = {"structuredQuery": {"from": [{"collectionId": "revitPedidos"}],
                             "where": {"fieldFilter": {"field": {"fieldPath": "status"}, "op": "IN",
                                                       "value": {"arrayValue": {"values": [{"stringValue": "pendente"}, {"stringValue": "processando"}]}}}}}}
    r = _http("POST", BASE[:-1] + ":runQuery", json.dumps(q), token=token())
    res = []
    for x in r if isinstance(r, list) else []:
        if x.get("document"):
            try:
                res.append(json.loads(_doc(x["document"]).get("json") or "{}"))
            except Exception:
                pass
    return res


def enviar_pedido(pedido):
    corpo = json.dumps({"fields": {"json": _para(json.dumps(pedido, ensure_ascii=False)), "status": _para("pendente"),
                                   "acao": _para(pedido.get("acao") or ""), "autor": _para(pedido.get("autor") or ""),
                                   "email": _para(email_atual()), "modelo": _para(pedido.get("modelo") or ""),
                                   "em": _para(int(time.time() * 1000))}}, ensure_ascii=False)
    _http("POST", BASE + "revitPedidos?documentId=" + pedido["id"], corpo, token=token())


def aguardar_pedido(pid, segundos=8):
    """Espera o sistema processar o pedido; devolve {'status','resultado'} ou None se ainda pendente."""
    fim = time.time() + segundos
    while time.time() < fim:
        try:
            d = ler("revitPedidos/" + pid)
        except ErroNuvem:
            return None
        if d and d.get("status") in ("ok", "erro"):
            return d
        time.sleep(1)
    return None


# ------------------------------------------------------------------ janela de login (WPF, sem pyRevit)
XAML = u"""<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Sistema FJA — entrar" Width="380" SizeToContent="Height"
 WindowStartupLocation="CenterScreen" ResizeMode="NoResize" FontFamily="Segoe UI" FontSize="13" Background="#F6F6F5">
 <StackPanel Margin="22,18">
  <TextBlock Text="Sistema FJA" FontSize="18" FontWeight="SemiBold"/>
  <TextBlock Text="Entre com o mesmo e-mail e senha do sistema (sistema-fja.web.app)." Foreground="#5E5E5C" TextWrapping="Wrap" Margin="0,4,0,14"/>
  <TextBlock Text="E-mail" Foreground="#5E5E5C" FontSize="12"/><TextBox Name="em" Height="30" Padding="6,4" Margin="0,2,0,10"/>
  <TextBlock Text="Senha" Foreground="#5E5E5C" FontSize="12"/><PasswordBox Name="sn" Height="30" Padding="6,4" Margin="0,2,0,6"/>
  <TextBlock Name="msg" Foreground="#B3261E" TextWrapping="Wrap" Margin="0,4,0,8"/>
  <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
   <Button Name="cancelar" Content="Agora não" Padding="14,6" Margin="0,0,8,0" Background="White"/>
   <Button Name="ok" Content="Entrar" Padding="18,6" Background="#8E1B1B" Foreground="White" IsDefault="True"/>
  </StackPanel>
 </StackPanel></Window>"""


def pedir_login(aviso=u""):
    """Abre a janela de login. Devolve o e-mail se entrou, None se a pessoa desistiu."""
    from System.Windows.Markup import XamlReader
    w = XamlReader.Parse(XAML)
    em, sn, msg = w.FindName("em"), w.FindName("sn"), w.FindName("msg")
    em.Text = email_atual()
    msg.Text = aviso or u""
    res = {"email": None}

    def ok(s, a):
        try:
            res["email"] = entrar(em.Text.strip().lower(), sn.Password)
            w.Close()
        except ErroNuvem as e:
            msg.Text = u"{}".format(e)

    def cancelar(s, a):
        w.Close()
    w.FindName("ok").Click += ok
    w.FindName("cancelar").Click += cancelar
    (sn if em.Text else em).Focus()
    w.ShowDialog()
    return res["email"]
