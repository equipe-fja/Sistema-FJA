using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Cotagem
{
    /// <summary>Tipos de vista da cotagem (cada um dá prioridade a itens diferentes). Ver docs/cotagem.md.</summary>
    public enum TipoVista { Indefinido, PlantaBaixa, TetoRefletido, Pontos, Paginacao, Corte, Fachada, VistaInterna, Implantacao }

    /// <summary>Faixas de escala com regras próprias: 1:500, 1:200, 1:75 (vale para 1:100 e 1:50) e 1:25.</summary>
    public enum Faixa { F500, F200, F75, F25 }

    /// <summary>O que a ferramenta cota numa vista (tipo × escala).</summary>
    public sealed class Regra
    {
        public bool Eixos, EixosTotal, Niveis, NiveisTotal;
        public bool Fachada;     // planta 1:75: panos e vãos de fachada + total, nos quatro lados
        public bool Contorno;    // planta 1:200: só o total do contorno do edifício
        public bool Ambientes;   // planta 1:200: duas dimensões gerais (perpendiculares) por ambiente
        public string Descricao = "";
        public bool Vazia => !Eixos && !Niveis && !Fachada && !Contorno && !Ambientes;
    }

    public static class Regras
    {
        public static readonly TipoVista[] Lista =
        {
            TipoVista.PlantaBaixa, TipoVista.TetoRefletido, TipoVista.Pontos, TipoVista.Paginacao,
            TipoVista.Corte, TipoVista.Fachada, TipoVista.VistaInterna, TipoVista.Implantacao,
        };

        public static string Nome(TipoVista t)
        {
            switch (t)
            {
                case TipoVista.PlantaBaixa: return "Planta baixa";
                case TipoVista.TetoRefletido: return "Planta de teto refletido";
                case TipoVista.Pontos: return "Planta de pontos";
                case TipoVista.Paginacao: return "Paginação de piso";
                case TipoVista.Corte: return "Corte";
                case TipoVista.Fachada: return "Fachada";
                case TipoVista.VistaInterna: return "Vista/corte interno";
                case TipoVista.Implantacao: return "Implantação";
                default: return "— escolher —";
            }
        }

        public static Faixa FaixaDe(int escala)
        {
            if (escala >= 400) return Faixa.F500;
            if (escala >= 150) return Faixa.F200;
            if (escala > 40) return Faixa.F75;
            return Faixa.F25;
        }

        public static string NomeFaixa(Faixa f)
        {
            switch (f)
            {
                case Faixa.F500: return "1:500";
                case Faixa.F200: return "1:200";
                case Faixa.F75: return "1:75 / 1:50";
                default: return "1:25";
            }
        }

        /// <summary>
        /// Regras: cadeias de eixos e de níveis (com total); planta 1:75, panos e vãos de fachada; planta 1:200, ambientes
        /// e contorno. Cadeias internas, terreno, luminárias, pontos e paginação ficam para as próximas versões (docs/cotagem.md).
        /// </summary>
        public static Regra Para(TipoVista t, int escala)
        {
            var f = FaixaDe(escala);
            var r = new Regra();
            switch (t)
            {
                case TipoVista.PlantaBaixa:
                    if (f == Faixa.F25) { r.Descricao = "1:25: cota faces de parede, não eixos (próxima versão)"; break; }
                    if (f == Faixa.F500) { r.Descricao = "1:500: lote e afastamentos (próxima versão)"; break; }
                    r.Eixos = r.EixosTotal = true;
                    if (f == Faixa.F75) { r.Fachada = true; r.Descricao = "panos e vãos de fachada + total; eixo a eixo + total"; break; }
                    r.Contorno = r.Ambientes = true;
                    r.Descricao = "dimensões gerais dos ambientes; contorno do edifício; eixo a eixo + total";
                    break;
                case TipoVista.Corte:
                case TipoVista.Fachada:
                    if (f == Faixa.F25) { r.Niveis = r.NiveisTotal = true; r.Descricao = "níveis piso a piso + total"; break; }
                    r.Eixos = r.EixosTotal = r.Niveis = r.NiveisTotal = true;
                    r.Descricao = "eixo a eixo + total; níveis piso a piso + total";
                    break;
                case TipoVista.VistaInterna:
                    r.Niveis = r.NiveisTotal = true;
                    r.Descricao = "níveis piso a piso + total";
                    break;
                case TipoVista.Indefinido:
                    r.Descricao = "escolha o tipo da vista";
                    break;
                default:
                    r.Descricao = "sem cotagem automática nesta versão";
                    break;
            }
            return r;
        }

        // ------------------------------------------------------------ identificação do tipo da vista

        /// <summary>Tipo sugerido para a vista e de onde veio (gravado na vista, template, nome ou tipo do Revit).</summary>
        public static TipoVista Sugerir(View v, out string origem)
        {
            var g = Marca.TipoGravado(v);
            if (g != TipoVista.Indefinido) { origem = "escolhido antes"; return g; }

            View tpl = v.ViewTemplateId != ElementId.InvalidElementId ? v.Document.GetElement(v.ViewTemplateId) as View : null;
            if (tpl != null)
            {
                var m = Mapa();
                if (m.TryGetValue(tpl.Name, out var t)) { origem = "template " + tpl.Name; return t; }
                t = PorPalavra(tpl.Name);
                if (t != TipoVista.Indefinido) { origem = "template " + tpl.Name; return t; }
            }

            var pn = PorPalavra(v.Name);
            if (pn != TipoVista.Indefinido) { origem = "nome da vista"; return pn; }

            switch (v.ViewType)
            {
                case ViewType.FloorPlan: case ViewType.AreaPlan: origem = "tipo da vista no Revit"; return TipoVista.PlantaBaixa;
                case ViewType.CeilingPlan: origem = "tipo da vista no Revit"; return TipoVista.TetoRefletido;
                case ViewType.Section: origem = "tipo da vista no Revit"; return TipoVista.Corte;
                case ViewType.Elevation: origem = "tipo da vista no Revit"; return TipoVista.Fachada;
            }
            origem = tpl == null ? "sem template" : "template sem correspondência";
            return TipoVista.Indefinido;
        }

        /// <summary>Palavras-chave no nome do template (ou da vista). A ordem importa: "planta de teto" é teto, não planta.</summary>
        public static TipoVista PorPalavra(string nome)
        {
            string s = SemAcento(nome ?? "").ToUpperInvariant();
            if (s.Contains("IMPLANT") || s.Contains("SITUAC")) return TipoVista.Implantacao;
            if (s.Contains("TETO") || s.Contains("FORRO") || s.Contains("REFLET") || s.Contains("RCP")) return TipoVista.TetoRefletido;
            if (s.Contains("PONTO")) return TipoVista.Pontos;
            if (s.Contains("PAGINA")) return TipoVista.Paginacao;
            if (s.Contains("VISTA INT") || s.Contains("INTERN") || s.Contains("AMPLIA") && s.Contains("CORTE")) return TipoVista.VistaInterna;
            if (s.Contains("FACHADA") || s.Contains("ELEVA")) return TipoVista.Fachada;
            if (s.Contains("CORTE") || s.Contains("SECAO")) return TipoVista.Corte;
            if (s.Contains("PLANTA") || s.Contains("LAYOUT") || s.Contains("PAVIMENTO")) return TipoVista.PlantaBaixa;
            return TipoVista.Indefinido;
        }

        public static string SemAcento(string s)
        {
            var n = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in n)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Correspondência template → tipo da vista, em lib/fja_cotagem.json da extensão:
        /// {"templates": {"FJA_PLANTA DE PONTOS": "Pontos", ...}} (valores = nomes do enum TipoVista).
        /// </summary>
        static Dictionary<string, TipoVista> Mapa()
        {
            var r = new Dictionary<string, TipoVista>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string ext = AppDomain.CurrentDomain.GetData("FJA.Extensao") as string;
                string arq = ext != null ? Path.Combine(ext, "lib", "fja_cotagem.json") : null;
                if (arq == null || !File.Exists(arq)) return r;
                var d = Json.LerObjeto(File.ReadAllText(arq, Encoding.UTF8)).Obj("templates");
                foreach (var kv in d)
                    if (Enum.TryParse(Convert.ToString(kv.Value), true, out TipoVista t)) r[kv.Key] = t;
            }
            catch { }
            return r;
        }
    }

    /// <summary>Preferências da pessoa: %APPDATA%\FJA\cotagem.json.</summary>
    public sealed class OpcoesCotagem
    {
        public string TipoCota = "";
        public double EspacamentoMm = 8;
        public bool Refazer;

        static string Arquivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "cotagem.json");

        public static OpcoesCotagem Carregar()
        {
            var o = new OpcoesCotagem();
            try
            {
                if (!File.Exists(Arquivo)) return o;
                var d = Json.LerObjeto(File.ReadAllText(Arquivo, Encoding.UTF8));
                o.TipoCota = d.Txt("tipoCota") ?? "";
                if (double.TryParse(d.Txt("espacamentoMm"), NumberStyles.Float, CultureInfo.InvariantCulture, out double e) && e >= 2 && e <= 30) o.EspacamentoMm = e;
            }
            catch { }
            return o;
        }

        public void Gravar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                File.WriteAllText(Arquivo, Json.Gravar(new Dictionary<string, object>
                {
                    ["tipoCota"] = TipoCota,
                    ["espacamentoMm"] = EspacamentoMm,
                }), Encoding.UTF8);
            }
            catch { }
        }
    }
}
