using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Links
{
    /// <summary>
    /// Módulo do Copiar do link (lib/fja_links_modulos.json): categorias, parâmetros acompanhados e o jeito de copiar.
    /// "pecas" = famílias soltas (Equipamentos); "redes" = tubos, dutos, eletrocalhas, eletrodutos e conexões (Encaminhamentos).
    /// </summary>
    public sealed class Modulo
    {
        public string Id, Nome, Descricao, Tipo = "pecas";
        public bool Ativo = true;
        public List<string> Categorias = new List<string>();
        public List<string> Parametros = new List<string>();
        public bool Redes => Tipo == "redes";

        public List<ElementId> IdsCategorias()
        {
            var r = new List<ElementId>();
            foreach (var n in Categorias)
                if (Enum.TryParse(n, out BuiltInCategory bic))
                {
                    try { r.Add(new ElementId(bic)); } catch { }
                }
            return r;
        }

        public static List<Modulo> Todos()
        {
            var lista = new List<Modulo>();
            try
            {
                string ext = AppDomain.CurrentDomain.GetData("FJA.Extensao") as string;
                string arq = ext != null ? Path.Combine(ext, "lib", "fja_links_modulos.json") : null;
                if (arq != null && File.Exists(arq))
                {
                    var d = Json.LerObjeto(File.ReadAllText(arq, Encoding.UTF8));
                    foreach (var o in d.Lista("modulos"))
                    {
                        var m = o as Dictionary<string, object>;
                        if (m == null) continue;
                        var md = new Modulo
                        {
                            Id = m.Txt("id"), Nome = m.Txt("nome"), Descricao = m.Txt("descricao"),
                            Tipo = string.IsNullOrEmpty(m.Txt("tipo")) ? "pecas" : m.Txt("tipo"),
                            Ativo = !(m.ContainsKey("ativo") && m["ativo"] is bool b && !b),
                        };
                        foreach (var c in m.Lista("categorias")) if (c is string s) md.Categorias.Add(s);
                        if (m.ContainsKey("parametros"))
                        {
                            foreach (var c in m.Lista("parametros")) if (c is string s) md.Parametros.Add(s);
                        }
                        else md.Parametros.AddRange(new[] { "ALL_MODEL_MARK", "ALL_MODEL_INSTANCE_COMMENTS" });
                        lista.Add(md);
                    }
                }
            }
            catch { }
            if (lista.Count == 0) lista.Add(new Modulo { Id = "equipamentos", Nome = "Equipamentos", Categorias = { "OST_Furniture", "OST_LightingFixtures" } });
            return lista;
        }

        public static Modulo PorId(string id) => Todos().FirstOrDefault(m => m.Id == id);
    }

    /// <summary>Instância de link do Revit (carregada ou não).</summary>
    public sealed class LinkInfo
    {
        public RevitLinkInstance Inst;
        public Document Doc;
        public string Uid, Nome, Tipo;
        public Transform Transf;
        public bool Carregado => Doc != null;
        public string Rotulo => Carregado ? Nome : Nome + " (não carregado)";

        public LinkInfo(RevitLinkInstance inst)
        {
            Inst = inst;
            Doc = inst.GetLinkDocument();
            Uid = inst.UniqueId;
            Nome = inst.Name;
            try { Tipo = inst.Document.GetElement(inst.GetTypeId()).Name; } catch { Tipo = Nome.Split(new[] { " : " }, StringSplitOptions.None)[0]; }
            Transf = Doc != null ? inst.GetTotalTransform() : null;
        }

        public static List<LinkInfo> Todos(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Select(i => new LinkInfo(i)).OrderBy(l => !l.Carregado).ThenBy(l => l.Nome, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>
    /// Registro de origem em cada cópia (Extensible Storage FJA_CopiaLink) — o mesmo esquema do Python (fja_links.py),
    /// para as cópias antigas continuarem reconhecidas. A "foto" é um JSON com o estado da origem no link.
    /// </summary>
    public static class Registro
    {
        public static readonly Guid GUID = new Guid("5b1e9c47-2d8a-4f63-9e05-c7a4d1b83f26");
        public static readonly string[] CAMPOS = { "modulo", "link", "linkInst", "origem", "foto", "copiadoEm", "por" };

        public static Schema Esquema()
        {
            Schema sc = Schema.Lookup(GUID);
            if (sc != null) return sc;
            var sb = new SchemaBuilder(GUID);
            sb.SetSchemaName("FJA_CopiaLink");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("Elemento copiado de um link pelas Ferramentas FJA: origem e estado no momento da copia");
            foreach (var c in CAMPOS) sb.AddSimpleField(c, typeof(string));
            return sb.Finish();
        }

        public static void Gravar(Element e, Dictionary<string, string> v)
        {
            if (!v.ContainsKey("copiadoEm")) v["copiadoEm"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            Schema sc = Esquema();
            var ent = new Entity(sc);
            foreach (var c in CAMPOS) ent.Set(sc.GetField(c), v.TryGetValue(c, out var s) && s != null ? s : "");
            e.SetEntity(ent);
        }

        public sealed class Dados
        {
            public string Modulo, Link, LinkInst, Origem, CopiadoEm, Por;
            public Dictionary<string, object> Foto;
        }

        public static Dados Ler(Element e)
        {
            Schema sc = Schema.Lookup(GUID);
            if (sc == null) return null;
            try
            {
                Entity ent = e.GetEntity(sc);
                if (ent == null || !ent.IsValid()) return null;
                string G(string c) { try { return ent.Get<string>(sc.GetField(c)) ?? ""; } catch { return ""; } }
                return new Dados
                {
                    Modulo = G("modulo"), Link = G("link"), LinkInst = G("linkInst"), Origem = G("origem"),
                    CopiadoEm = G("copiadoEm"), Por = G("por"), Foto = Json.LerObjeto(G("foto")),
                };
            }
            catch { return null; }
        }

        /// <summary>Elementos do modelo com registro (pelo filtro de Extensible Storage).</summary>
        public static IEnumerable<Element> Elementos(Document doc)
        {
            if (Schema.Lookup(GUID) == null) return Enumerable.Empty<Element>();
            return new FilteredElementCollector(doc).WherePasses(new ExtensibleStorageFilter(GUID)).ToElements();
        }
    }

    /// <summary>Utilidades de geometria e texto para fotos (coordenadas em pés, como no Python).</summary>
    public static class Geo
    {
        public const double TOL_POS = 1.0 / 304.8;           // 1 mm
        public const double TOL_ANG = 0.2 * Math.PI / 180.0;  // 0,2 grau

        public static List<object> V(XYZ p) => new List<object> { Math.Round(p.X, 6), Math.Round(p.Y, 6), Math.Round(p.Z, 6) };

        public static XYZ P(object o)
        {
            var l = o as List<object>;
            if (l == null || l.Count < 3) return null;
            return new XYZ(Num(l[0]), Num(l[1]), Num(l[2]));
        }

        public static double Num(object o)
        {
            if (o is double d) return d;
            if (o is int i) return i;
            if (o is long g) return g;
            double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double r);
            return r;
        }

        public static Dictionary<string, object> Pose(Transform t) => new Dictionary<string, object>
        {
            ["o"] = V(t.Origin), ["x"] = V(t.BasisX), ["y"] = V(t.BasisY), ["z"] = V(t.BasisZ),
        };

        public static Transform Transf(Dictionary<string, object> d)
        {
            var t = Transform.Identity;
            t.Origin = P(d.ContainsKey("o") ? d["o"] : null) ?? XYZ.Zero;
            t.BasisX = P(d.ContainsKey("x") ? d["x"] : null) ?? XYZ.BasisX;
            t.BasisZ = P(d.ContainsKey("z") ? d["z"] : null) ?? XYZ.BasisZ;
            t.BasisY = P(d.ContainsKey("y") ? d["y"] : null) ?? t.BasisZ.CrossProduct(t.BasisX);
            return t;
        }

        public static double Ang(XYZ a, XYZ b)
        {
            try { return a.Normalize().AngleTo(b.Normalize()); } catch { return 0; }
        }

        public static string Mm(double pes) => (pes * 304.8).ToString("0", CultureInfo.InvariantCulture) + " mm";
        public static string Graus(double rad) => (rad * 180 / Math.PI).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + "°";

        /// <summary>Diferenças entre duas poses {o,x,z}.</summary>
        public static List<string> DifPose(Dictionary<string, object> a, Dictionary<string, object> b, string rot = "rotação")
        {
            var r = new List<string>();
            XYZ pa = P(a.ContainsKey("o") ? a["o"] : null), pb = P(b.ContainsKey("o") ? b["o"] : null);
            if (pa != null && pb != null && pa.DistanceTo(pb) > TOL_POS) r.Add("posição (" + Mm(pa.DistanceTo(pb)) + ")");
            XYZ xa = P(a.ContainsKey("x") ? a["x"] : null), xb = P(b.ContainsKey("x") ? b["x"] : null);
            XYZ za = P(a.ContainsKey("z") ? a["z"] : null), zb = P(b.ContainsKey("z") ? b["z"] : null);
            if (za != null && zb != null && Ang(za, zb) > TOL_ANG) r.Add("orientação (" + Graus(Ang(za, zb)) + ")");
            else if (xa != null && xb != null && Ang(xa, xb) > TOL_ANG) r.Add(rot + " (" + Graus(Ang(xa, xb)) + ")");
            return r;
        }

        /// <summary>Diferenças entre duas listas de pontas (redes). Ordem indiferente.</summary>
        public static List<string> DifPontas(List<XYZ> a, List<XYZ> b)
        {
            var r = new List<string>();
            if (a == null || b == null) return r;
            if (a.Count != b.Count) { r.Add("pontas (" + a.Count + " → " + b.Count + ")"); return r; }
            double maior = 0;
            var livres = new List<XYZ>(b);
            foreach (var p in a)
            {
                XYZ m = null; double dm = double.MaxValue;
                foreach (var q in livres) { double d = p.DistanceTo(q); if (d < dm) { dm = d; m = q; } }
                if (m != null) { livres.Remove(m); maior = Math.Max(maior, dm); }
            }
            if (maior > TOL_POS) r.Add("posição (" + Mm(maior) + ")");
            return r;
        }

        public static List<XYZ> Pontos(object o)
        {
            var r = new List<XYZ>();
            if (o is List<object> l) foreach (var x in l) { var p = P(x); if (p != null) r.Add(p); }
            return r;
        }
    }
}
