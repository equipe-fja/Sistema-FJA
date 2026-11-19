using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Legendas
{
    /// <summary>
    /// Cor da disciplina no projeto do Sistema FJA a que o modelo pertence.
    /// Lê revitPub/biblioteca do banco (Firebase) com o login guardado pelas ferramentas em Python
    /// (%APPDATA%\FJA\nuvem.json, só o token de renovação); sem login, usa a cópia biblioteca.json do G:.
    /// </summary>
    public static class CorSistema
    {
        const string API_KEY = "AIzaSyD05OgqpjI0mcEQSBh8srC96QyVvwPuh-M";
        const string BASE = "https://firestore.googleapis.com/v1/projects/sistema-fja/databases/(default)/documents/";
        const string PASTA_G = @"G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT";

        static string ArqLogin => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "nuvem.json");

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

        static string Token()
        {
            var cfg = File.Exists(ArqLogin) ? Json.LerObjeto(File.ReadAllText(ArqLogin, Encoding.UTF8)) : null;
            string refresh = cfg.Txt("refresh");
            if (string.IsNullOrEmpty(refresh)) return null;
            var r = Json.LerObjeto(Http("POST", "https://securetoken.googleapis.com/v1/token?key=" + API_KEY,
                "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refresh), null, "application/x-www-form-urlencoded"));
            return r.Txt("id_token");
        }

        static Dictionary<string, object> BibliotecaDoBanco(out string rotulo)
        {
            rotulo = "banco do sistema";
            string tk = Token();
            if (tk == null) return null;
            var d = Json.LerObjeto(Http("GET", BASE + "revitPub/biblioteca", null, tk));
            string json = d.Obj("fields").Obj("json").Txt("stringValue");
            return string.IsNullOrEmpty(json) ? null : Json.LerObjeto(json);
        }

        static Dictionary<string, object> BibliotecaDoG(out string rotulo)
        {
            rotulo = "cópia do G:";
            if (!Directory.Exists(PASTA_G)) return null;
            var rx = new Regex(@"^biblioteca( \(\d+\))?\.json$", RegexOptions.IgnoreCase);
            var arq = Directory.GetFiles(PASTA_G).Where(f => rx.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            return arq == null ? null : Json.LerObjeto(File.ReadAllText(arq, Encoding.UTF8));
        }

        /// <summary>(rgb ou null, texto dizendo de onde veio ou por que não achou).</summary>
        public static Tuple<byte[], string> Ler(Document doc, string disciplina)
        {
            Dictionary<string, object> bib = null;
            string rotulo = "", erro = null;
            try { bib = BibliotecaDoBanco(out rotulo); }
            catch (Exception e) { erro = e.Message; }
            if (bib == null)
            {
                try { bib = BibliotecaDoG(out rotulo); } catch { }
            }
            if (bib == null)
                return Tuple.Create<byte[], string>(null, erro != null
                    ? "não consegui ler o sistema (" + erro + ")"
                    : "sem login no sistema neste computador: entre por uma ferramenta de Tipos (Nova composição) e tente de novo");
            if (!bib.ContainsKey("coresProjetos"))
                return Tuple.Create<byte[], string>(null, "o sistema ainda não publicou as cores dos projetos (" + rotulo + ")");
            var lista = bib.Lista("coresProjetos").OfType<Dictionary<string, object>>().ToList();
            string titulo = doc.Title ?? "";
            // projeto do modelo: a ligação gravada no modelo ou as pistas (Nucleo/ProjetoDoModelo), como nas demais ferramentas
            var proj = ProjetoDoModelo.Resolver(doc, lista, out string origemProj, out _, out _);
            if (proj == null) return Tuple.Create<byte[], string>(null, "projeto do modelo '" + titulo + "': " + origemProj);
            string nomeP = ((proj.Txt("codigo") ?? "") + " " + (proj.Txt("nome") ?? "")).Trim();
            string dsc = (disciplina ?? "").ToUpperInvariant();
            foreach (var l in proj.Lista("disciplinas").OfType<Dictionary<string, object>>())
            {
                if ((l.Txt("cod") ?? "").ToUpperInvariant() == dsc || (l.Txt("sigla") ?? "").ToUpperInvariant() == dsc)
                {
                    var rgb = Cores.Hex(l.Txt("cor"));
                    if (rgb != null) return Tuple.Create(rgb, (l.Txt("sigla") ?? dsc) + " no projeto " + nomeP);
                }
            }
            return Tuple.Create<byte[], string>(null, "a disciplina " + dsc + " não está nas disciplinas do projeto " + nomeP);
        }
    }
}
