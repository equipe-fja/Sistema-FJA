using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Links
{
    /// <summary>
    /// Reconectar as cópias das redes depois de copiadas.
    ///  1. Como no link: cada cópia sabe de que elemento veio; as ligações que existem no link entre duas origens que
    ///     têm cópia são refeitas entre as cópias (mesmo copiadas em dias diferentes). Não cria nem move nada.
    ///  2. Pontas coincidentes: conectores livres no mesmo ponto (≤ 1 mm), direções opostas, mesmo domínio, forma e
    ///     dimensão — inclusive com peças do próprio modelo. Também não cria nem move nada.
    /// Folgas (peças próximas mas separadas) ficam para uma etapa seguinte; aparecem na lista de pontas soltas.
    /// </summary>
    public static class Reconexao
    {
        public sealed class Solta
        {
            public ElementId Elemento;
            public XYZ Ponto;
            public string Motivo;
        }

        public sealed class Relatorio
        {
            public int ComoNoLink, Coincidentes, JaLigadas;
            public readonly List<Solta> Soltas = new List<Solta>();
            public readonly List<string> Erros = new List<string>();
            public string Resumo()
            {
                var p = new List<string>();
                p.Add(ComoNoLink + (ComoNoLink == 1 ? " ligação refeita como no link" : " ligações refeitas como no link"));
                p.Add(Coincidentes + (Coincidentes == 1 ? " por ponta coincidente" : " por pontas coincidentes"));
                if (Soltas.Count > 0) p.Add(Soltas.Count + (Soltas.Count == 1 ? " ponta continua solta" : " pontas continuam soltas"));
                return string.Join(" · ", p);
            }
        }

        static bool Fisico(Connector c)
        {
            try { return c.ConnectorType == ConnectorType.End || c.ConnectorType == ConnectorType.Curve || c.ConnectorType == ConnectorType.Physical; }
            catch { return false; }
        }

        static bool Ponta(Connector c)
        {
            try { return c.ConnectorType == ConnectorType.End || c.ConnectorType == ConnectorType.Physical; } catch { return false; }
        }

        static IEnumerable<Connector> Lista(Element e)
        {
            var cm = Motor.Conectores(e);
            if (cm == null) yield break;
            foreach (Connector c in cm.Connectors) yield return c;
        }

        /// <summary>Conector da cópia que corresponde ao conector da origem (mesmo índice; senão, o mais próximo).</summary>
        static Connector Correspondente(Element copia, Connector cs, Transform t)
        {
            var cm = Motor.Conectores(copia);
            if (cm == null) return null;
            try { var c = cm.Lookup(cs.Id); if (c != null && c.Origin.DistanceTo(t.OfPoint(cs.Origin)) < 0.05) return c; } catch { }
            XYZ alvo = t.OfPoint(cs.Origin);
            Connector m = null; double dm = 0.05;
            foreach (Connector c in cm.Connectors)
            {
                if (!Ponta(c)) continue;
                double d = c.Origin.DistanceTo(alvo);
                if (d < dm) { dm = d; m = c; }
            }
            return m;
        }

        /// <summary>Ligações (físicas) da origem no link: (conector da origem, conector do outro lado).</summary>
        static IEnumerable<KeyValuePair<Connector, Connector>> LigacoesNoLink(Element src)
        {
            foreach (var c in Lista(src))
            {
                if (!Fisico(c)) continue;
                bool lig; try { lig = c.IsConnected; } catch { lig = false; }
                if (!lig) continue;
                foreach (Connector r in Refs(c))
                    if (Outro(r, src)) yield return new KeyValuePair<Connector, Connector>(c, r);
            }
        }

        static List<Connector> Refs(Connector c)
        {
            var l = new List<Connector>();
            try { foreach (Connector r in c.AllRefs) l.Add(r); } catch { }
            return l;
        }

        static bool Outro(Connector r, Element src)
        {
            try { return r.Owner != null && r.Owner.Id != src.Id && Fisico(r) && (r.Owner is MEPCurve || r.Owner is FamilyInstance); }
            catch { return false; }
        }

        static bool Ligados(Connector a, Connector b)
        {
            try
            {
                if (!a.IsConnected) return false;
                foreach (Connector r in a.AllRefs) if (r.Owner != null && r.Owner.Id == b.Owner.Id && r.Id == b.Id) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Quantas ligações do link (com as duas pontas copiadas) estão desfeitas nesta cópia.</summary>
        public static int Faltando(Element src, Element copia, LinkInfo lk, Dictionary<string, Element> copiaPorOrigem)
        {
            int n = 0;
            if (Motor.Conectores(src) == null || Motor.Conectores(copia) == null) return 0;
            foreach (var kv in LigacoesNoLink(src))
            {
                if (!copiaPorOrigem.TryGetValue(lk.Uid + "|" + kv.Value.Owner.UniqueId, out var outra)) continue;
                var a = Correspondente(copia, kv.Key, lk.Transf);
                var b = Correspondente(outra, kv.Value, lk.Transf);
                if (a != null && b != null && !Ligados(a, b)) n++;
            }
            return n;
        }

        /// <summary>Mapa (instância do link | origem) → cópia, para todas as cópias do modelo.</summary>
        static Dictionary<string, Element> MapaCopias(Document doc)
        {
            var m = new Dictionary<string, Element>();
            foreach (var e in Registro.Elementos(doc))
            {
                var r = Registro.Ler(e);
                if (r != null) m[r.LinkInst + "|" + r.Origem] = e;
            }
            return m;
        }

        /// <summary>Nível 1: refaz entre as cópias dadas (e as cópias vizinhas) as ligações que existem no link.</summary>
        public static Relatorio ComoNoLink(Document doc, IList<Element> copias, Relatorio rel = null)
        {
            rel = rel ?? new Relatorio();
            var lks = LinkInfo.Todos(doc).Where(l => l.Carregado).ToDictionary(l => l.Uid);
            var mapa = MapaCopias(doc);
            foreach (var e in copias)
            {
                if (e == null || !e.IsValidObject || Motor.Conectores(e) == null) continue;
                var r = Registro.Ler(e);
                if (r == null || !lks.TryGetValue(r.LinkInst ?? "", out var lk)) continue;
                var src = lk.Doc.GetElement(r.Origem);
                if (src == null || Motor.Conectores(src) == null) continue;
                foreach (var kv in LigacoesNoLink(src))
                {
                    if (!mapa.TryGetValue(lk.Uid + "|" + kv.Value.Owner.UniqueId, out var outra) || !outra.IsValidObject) continue;
                    var a = Correspondente(e, kv.Key, lk.Transf);
                    var b = Correspondente(outra, kv.Value, lk.Transf);
                    if (a == null || b == null) continue;
                    if (Ligados(a, b)) { rel.JaLigadas++; continue; }
                    bool aLivre, bLivre;
                    try { aLivre = !a.IsConnected || a.ConnectorType == ConnectorType.Curve; bLivre = !b.IsConnected || b.ConnectorType == ConnectorType.Curve; } catch { continue; }
                    if (!aLivre || !bLivre) continue;
                    if (a.Origin.DistanceTo(b.Origin) > Geo.TOL_POS) continue;
                    try { a.ConnectTo(b); rel.ComoNoLink++; }
                    catch (Exception ex) { rel.Erros.Add(ex.Message); }
                }
            }
            return rel;
        }

        static string Celula(XYZ p, double passo) =>
            Math.Floor(p.X / passo) + ";" + Math.Floor(p.Y / passo) + ";" + Math.Floor(p.Z / passo);

        static bool Compativeis(Connector a, Connector b, out string motivo)
        {
            motivo = null;
            try
            {
                if (a.Domain != b.Domain) { motivo = "domínios diferentes"; return false; }
                if (a.CoordinateSystem.BasisZ.DotProduct(b.CoordinateSystem.BasisZ) > -0.99) { motivo = "direções não opostas"; return false; }
                if (a.Shape != b.Shape) { motivo = "formas diferentes"; return false; }
                if (a.Shape == ConnectorProfileType.Round)
                {
                    if (Math.Abs(a.Radius - b.Radius) > Geo.TOL_POS) { motivo = "diâmetros diferentes"; return false; }
                }
                else if (Math.Abs(a.Width - b.Width) > Geo.TOL_POS || Math.Abs(a.Height - b.Height) > Geo.TOL_POS)
                {
                    if (Math.Abs(a.Width - b.Height) > Geo.TOL_POS || Math.Abs(a.Height - b.Width) > Geo.TOL_POS) { motivo = "dimensões diferentes"; return false; }
                }
                return true;
            }
            catch (Exception ex) { motivo = ex.Message; return false; }
        }

        /// <summary>Nível 2: liga conectores livres das cópias a conectores livres coincidentes (cópias ou peças do modelo).</summary>
        public static Relatorio Coincidentes(Document doc, IList<Element> copias, Relatorio rel = null)
        {
            rel = rel ?? new Relatorio();
            const double passo = 10.0 / 304.8;
            var grade = new Dictionary<string, List<Connector>>();
            var todos = new FilteredElementCollector(doc).WherePasses(new LogicalOrFilter(
                new ElementClassFilter(typeof(MEPCurve)), new ElementClassFilter(typeof(FamilyInstance)))).WhereElementIsNotElementType();
            foreach (var e in todos)
            {
                if (Motor.Conectores(e) == null) continue;
                foreach (var c in Lista(e))
                {
                    bool livre; try { livre = Ponta(c) && !c.IsConnected; } catch { continue; }
                    if (!livre) continue;
                    string k = Celula(c.Origin, passo);
                    if (!grade.TryGetValue(k, out var l)) grade[k] = l = new List<Connector>();
                    l.Add(c);
                }
            }
            foreach (var e in copias)
            {
                if (e == null || !e.IsValidObject || Motor.Conectores(e) == null) continue;
                foreach (var a in Lista(e).ToList())
                {
                    bool livre; try { livre = Ponta(a) && !a.IsConnected; } catch { continue; }
                    if (!livre) continue;
                    XYZ p = a.Origin;
                    Connector melhor = null; double dm = Geo.TOL_POS; string motivo = null;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                string k = Celula(p + new XYZ(dx * passo, dy * passo, dz * passo), passo);
                                if (!grade.TryGetValue(k, out var l)) continue;
                                foreach (var b in l)
                                {
                                    try
                                    {
                                        if (b.Owner.Id == e.Id || b.IsConnected) continue;
                                        double d = b.Origin.DistanceTo(p);
                                        if (d > dm) continue;
                                        if (!Compativeis(a, b, out string m)) { motivo = m; continue; }
                                        melhor = b; dm = d;
                                    }
                                    catch { }
                                }
                            }
                    if (melhor == null)
                    {
                        if (motivo != null) rel.Soltas.Add(new Solta { Elemento = e.Id, Ponto = p, Motivo = "ponta coincidente com " + motivo });
                        continue;
                    }
                    try { a.ConnectTo(melhor); rel.Coincidentes++; }
                    catch (Exception ex) { rel.Erros.Add(ex.Message); }
                }
            }
            return rel;
        }

        /// <summary>Pontas das cópias que estão ligadas no link e continuam soltas no modelo.</summary>
        public static void Soltas(Document doc, IList<Element> copias, Relatorio rel)
        {
            var lks = LinkInfo.Todos(doc).Where(l => l.Carregado).ToDictionary(l => l.Uid);
            var mapa = MapaCopias(doc);
            var ja = new HashSet<string>(rel.Soltas.Select(s => s.Elemento + "|" + Math.Round(s.Ponto.X, 3) + ";" + Math.Round(s.Ponto.Y, 3) + ";" + Math.Round(s.Ponto.Z, 3)));
            foreach (var e in copias)
            {
                if (e == null || !e.IsValidObject || Motor.Conectores(e) == null) continue;
                var r = Registro.Ler(e);
                if (r == null || !lks.TryGetValue(r.LinkInst ?? "", out var lk)) continue;
                var src = lk.Doc.GetElement(r.Origem);
                if (src == null || Motor.Conectores(src) == null) continue;
                foreach (var kv in LigacoesNoLink(src))
                {
                    var a = Correspondente(e, kv.Key, lk.Transf);
                    if (a == null) continue;
                    bool lig; try { lig = a.IsConnected; } catch { continue; }
                    if (lig) continue;
                    string k = e.Id + "|" + Math.Round(a.Origin.X, 3) + ";" + Math.Round(a.Origin.Y, 3) + ";" + Math.Round(a.Origin.Z, 3);
                    if (ja.Contains(k)) continue;
                    ja.Add(k);
                    var outro = kv.Value.Owner;
                    string nome = (outro.Category != null ? outro.Category.Name : "elemento") + " " + (outro.Document.GetElement(outro.GetTypeId())?.Name ?? "");
                    rel.Soltas.Add(new Solta
                    {
                        Elemento = e.Id, Ponto = a.Origin,
                        Motivo = mapa.ContainsKey(lk.Uid + "|" + outro.UniqueId) ? "não foi possível ligar a " + nome.Trim() : "ligado no link a " + nome.Trim() + ", que não foi copiado",
                    });
                }
            }
        }

        /// <summary>Níveis 1 e 2 e a lista de pontas soltas (dentro de uma Transaction aberta).</summary>
        public static Relatorio Reconectar(Document doc, IList<Element> copias)
        {
            var rel = ComoNoLink(doc, copias);
            doc.Regenerate();
            Coincidentes(doc, copias, rel);
            doc.Regenerate();
            Soltas(doc, copias, rel);
            return rel;
        }

        /// <summary>Todas as cópias de redes (com conectores) registradas no modelo.</summary>
        public static List<Element> CopiasComConectores(Document doc) =>
            Registro.Elementos(doc).Where(e => Motor.Conectores(e) != null).ToList();
    }
}
