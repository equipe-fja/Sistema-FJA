using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace FJA.Revit.Nucleo
{
    /// <summary>
    /// Banco do Sistema FJA (Firebase), com o login guardado pelas ferramentas em Python (lib/fja_nuvem.py):
    /// %APPDATA%\FJA\nuvem.json (e-mail e token de renovação; a senha nunca é guardada).
    /// Leitura de revitPub/&lt;nome&gt; (biblioteca e projetos publicados) e pedidos em revitPedidos.
    /// </summary>
    public static class Nuvem
    {
        const string API_KEY = "AIzaSyD05OgqpjI0mcEQSBh8srC96QyVvwPuh-M";
        const string BASE = "https://firestore.googleapis.com/v1/projects/sistema-fja/databases/(default)/documents/";

        static string ArqLogin => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "nuvem.json");

        static string tokenCache;
        static DateTime tokenAte = DateTime.MinValue;

#pragma warning disable SYSLIB0014 // WebClient: o mesmo nas duas gerações (.NET Framework 4.8 e .NET 10)
        static string Http(string metodo, string url, string corpo = null, string token = null, string tipo = "application/json")
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                wc.Headers.Add("Content-Type", tipo);
                if (token != null) wc.Headers.Add("Authorization", "Bearer " + token);
                return metodo == "GET" ? wc.DownloadString(url) : wc.UploadString(url, metodo, corpo ?? "");
            }
        }
#pragma warning restore SYSLIB0014

        static Dictionary<string, object> Config()
        {
            try { return File.Exists(ArqLogin) ? Json.LerObjeto(File.ReadAllText(ArqLogin, Encoding.UTF8)) : null; }
            catch { return null; }
        }

        /// <summary>Há login guardado neste computador.</summary>
        public static bool TemLogin => !string.IsNullOrEmpty(Config().Txt("refresh"));

        public static string Email => Config().Txt("email") ?? "";

        /// <summary>Token de acesso (renovado a cada 50 minutos); null sem login.</summary>
        public static string Token()
        {
            if (tokenCache != null && DateTime.UtcNow < tokenAte) return tokenCache;
            string refresh = Config().Txt("refresh");
            if (string.IsNullOrEmpty(refresh)) return null;
            var r = Json.LerObjeto(Http("POST", "https://securetoken.googleapis.com/v1/token?key=" + API_KEY,
                "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refresh), null, "application/x-www-form-urlencoded"));
            tokenCache = r.Txt("id_token");
            tokenAte = DateTime.UtcNow.AddMinutes(50);
            return tokenCache;
        }

        static bool NaoAchou(WebException e) =>
            e.Response is HttpWebResponse h && h.StatusCode == HttpStatusCode.NotFound;

        /// <summary>Conteúdo de biblioteca.json / projeto-*.json como o sistema gravou em revitPub/&lt;nome&gt;; null se não existe.</summary>
        public static Dictionary<string, object> Publicado(string nome)
        {
            string tk = Token();
            if (tk == null) return null;
            try
            {
                var d = Json.LerObjeto(Http("GET", BASE + "revitPub/" + Uri.EscapeDataString(nome), null, tk));
                string json = d.Obj("fields").Obj("json").Txt("stringValue");
                return string.IsNullOrEmpty(json) ? null : Json.LerObjeto(json);
            }
            catch (WebException e) when (NaoAchou(e)) { return null; }
        }

        static Dictionary<string, object> V(string s) => new Dictionary<string, object> { ["stringValue"] = s ?? "" };

        /// <summary>Pedidos do Revit ainda não processados pelo sistema (para as reservas de keynote).</summary>
        public static List<Dictionary<string, object>> PedidosPendentes()
        {
            var res = new List<Dictionary<string, object>>();
            string tk = Token();
            if (tk == null) return res;
            var q = new Dictionary<string, object>
            {
                ["structuredQuery"] = new Dictionary<string, object>
                {
                    ["from"] = new List<object> { new Dictionary<string, object> { ["collectionId"] = "revitPedidos" } },
                    ["where"] = new Dictionary<string, object>
                    {
                        ["fieldFilter"] = new Dictionary<string, object>
                        {
                            ["field"] = new Dictionary<string, object> { ["fieldPath"] = "status" },
                            ["op"] = "IN",
                            ["value"] = new Dictionary<string, object>
                            {
                                ["arrayValue"] = new Dictionary<string, object> { ["values"] = new List<object> { V("pendente"), V("processando") } },
                            },
                        },
                    },
                },
            };
            var r = Json.Ler(Http("POST", BASE.TrimEnd('/') + ":runQuery", Json.Gravar(q), tk)) as List<object>;
            foreach (var x in r ?? new List<object>())
            {
                var doc = (x as Dictionary<string, object>).Obj("document");
                string json = doc.Obj("fields").Obj("json").Txt("stringValue");
                if (string.IsNullOrEmpty(json)) continue;
                try { res.Add(Json.LerObjeto(json)); } catch { }
            }
            return res;
        }

        /// <summary>Grava o pedido em revitPedidos/&lt;id&gt; (o sistema processa quando alguém está com ele aberto).</summary>
        public static void EnviarPedido(Dictionary<string, object> pedido)
        {
            string tk = Token() ?? throw new InvalidOperationException("sem login no banco do sistema");
            long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var corpo = new Dictionary<string, object>
            {
                ["fields"] = new Dictionary<string, object>
                {
                    ["json"] = V(Json.Gravar(pedido)),
                    ["status"] = V("pendente"),
                    ["acao"] = V(pedido.Txt("acao")),
                    ["autor"] = V(pedido.Txt("autor")),
                    ["email"] = V(Email),
                    ["modelo"] = V(pedido.Txt("modelo")),
                    ["em"] = new Dictionary<string, object> { ["integerValue"] = ms.ToString() },
                },
            };
            Http("POST", BASE + "revitPedidos?documentId=" + Uri.EscapeDataString(pedido.Txt("id")), Json.Gravar(corpo), tk);
        }

        // ------------------------------------------------------------ documentos do banco (emissões, 2.38.0)

        /// <summary>uid de quem está logado (o "user_id" do token), para criadoPor/atualizadoPor.</summary>
        public static string Uid()
        {
            try
            {
                string tk = Token();
                if (tk == null) return "";
                var partes = tk.Split('.');
                string p = partes[1].Replace('-', '+').Replace('_', '/');
                p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
                var d = Json.LerObjeto(Encoding.UTF8.GetString(Convert.FromBase64String(p)));
                return d.Txt("user_id") ?? d.Txt("sub") ?? "";
            }
            catch { return ""; }
        }

        static string Exigir() => Token() ?? throw new InvalidOperationException("sem login no sistema neste computador");

        /// <summary>Documento <colecao>/<id> como objeto comum (null se não existe).</summary>
        public static Dictionary<string, object> Documento(string colecao, string id)
        {
            try
            {
                var d = Json.LerObjeto(Http("GET", BASE + colecao + "/" + Uri.EscapeDataString(id), null, Exigir()));
                var r = Fs.Campos(d.Obj("fields"));
                r["id"] = id;
                return r;
            }
            catch (WebException e) when (NaoAchou(e)) { return null; }
        }

        /// <summary>Documentos da coleção com campo == valor (texto).</summary>
        public static List<Dictionary<string, object>> Consulta(string colecao, string campo, string valor)
        {
            var q = new Dictionary<string, object>
            {
                ["structuredQuery"] = new Dictionary<string, object>
                {
                    ["from"] = new List<object> { new Dictionary<string, object> { ["collectionId"] = colecao } },
                    ["where"] = new Dictionary<string, object>
                    {
                        ["fieldFilter"] = new Dictionary<string, object>
                        {
                            ["field"] = new Dictionary<string, object> { ["fieldPath"] = campo },
                            ["op"] = "EQUAL",
                            ["value"] = V(valor),
                        },
                    },
                },
            };
            var res = new List<Dictionary<string, object>>();
            var r = Json.Ler(Http("POST", BASE.TrimEnd('/') + ":runQuery", Json.Gravar(q), Exigir())) as List<object>;
            foreach (var x in r ?? new List<object>())
            {
                var doc = (x as Dictionary<string, object>)?.Obj("document");
                if (doc == null || doc.Count == 0) continue;
                var c = Fs.Campos(doc.Obj("fields"));
                string nome = doc.Txt("name") ?? "";
                c["id"] = nome.Substring(nome.LastIndexOf('/') + 1);
                res.Add(c);
            }
            return res;
        }

        /// <summary>Cria <colecao>/<id>; false se o id já existe (não sobrescreve).</summary>
        public static bool Criar(string colecao, string id, Dictionary<string, object> dados)
        {
            try
            {
                Http("POST", BASE + colecao + "?documentId=" + Uri.EscapeDataString(id),
                     Json.Gravar(new Dictionary<string, object> { ["fields"] = Fs.Mapa(dados) }), Exigir());
                return true;
            }
            catch (WebException e) when (e.Response is HttpWebResponse h && (int)h.StatusCode == 409) { return false; }
        }

        /// <summary>Atualiza só os campos dados de <colecao>/<id> (o documento precisa existir).</summary>
        public static void Atualizar(string colecao, string id, Dictionary<string, object> campos)
        {
            var mask = string.Join("&", campos.Keys.Select(k => "updateMask.fieldPaths=" + Uri.EscapeDataString(k)));
            Http("PATCH", BASE + colecao + "/" + Uri.EscapeDataString(id) + "?currentDocument.exists=true&" + mask,
                 Json.Gravar(new Dictionary<string, object> { ["fields"] = Fs.Mapa(campos) }), Exigir());
        }
    }

    /// <summary>Conversão entre os valores tipados do Firestore (REST) e objetos comuns (texto, número, bool, lista, dicionário).</summary>
    public static class Fs
    {
        public static Dictionary<string, object> Campos(Dictionary<string, object> fields)
        {
            var r = new Dictionary<string, object>();
            foreach (var kv in fields ?? new Dictionary<string, object>()) r[kv.Key] = Valor(kv.Value as Dictionary<string, object>);
            return r;
        }

        public static object Valor(Dictionary<string, object> v)
        {
            if (v == null) return null;
            if (v.ContainsKey("stringValue")) return Convert.ToString(v["stringValue"]);
            if (v.ContainsKey("integerValue")) return long.TryParse(Convert.ToString(v["integerValue"]), out long n) ? n : 0L;
            if (v.ContainsKey("doubleValue")) return Convert.ToDouble(v["doubleValue"], System.Globalization.CultureInfo.InvariantCulture);
            if (v.ContainsKey("booleanValue")) return v["booleanValue"] is bool b ? b : string.Equals(Convert.ToString(v["booleanValue"]), "true", StringComparison.OrdinalIgnoreCase);
            if (v.ContainsKey("timestampValue")) return Convert.ToString(v["timestampValue"]);
            if (v.ContainsKey("mapValue")) return Campos((v["mapValue"] as Dictionary<string, object>)?.Obj("fields"));
            if (v.ContainsKey("arrayValue"))
            {
                var l = new List<object>();
                foreach (var x in (v["arrayValue"] as Dictionary<string, object>)?.Lista("values") ?? new List<object>()) l.Add(Valor(x as Dictionary<string, object>));
                return l;
            }
            return null;
        }

        public static Dictionary<string, object> Mapa(Dictionary<string, object> d)
        {
            var r = new Dictionary<string, object>();
            foreach (var kv in d) r[kv.Key] = Tipado(kv.Value);
            return r;
        }

        public static Dictionary<string, object> Tipado(object v)
        {
            switch (v)
            {
                case null: return new Dictionary<string, object> { ["nullValue"] = null };
                case string s: return new Dictionary<string, object> { ["stringValue"] = s };
                case bool b: return new Dictionary<string, object> { ["booleanValue"] = b };
                case int i: return new Dictionary<string, object> { ["integerValue"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                case long n: return new Dictionary<string, object> { ["integerValue"] = n.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                case double x: return new Dictionary<string, object> { ["doubleValue"] = x };
                case Dictionary<string, object> m: return new Dictionary<string, object> { ["mapValue"] = new Dictionary<string, object> { ["fields"] = Mapa(m) } };
                case System.Collections.IEnumerable e:
                    var l = new List<object>();
                    foreach (var x in e) l.Add(Tipado(x));
                    return new Dictionary<string, object> { ["arrayValue"] = new Dictionary<string, object> { ["values"] = l } };
                default: return new Dictionary<string, object> { ["stringValue"] = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) };
            }
        }
    }
}
