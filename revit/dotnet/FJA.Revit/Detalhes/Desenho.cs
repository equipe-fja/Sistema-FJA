using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Detalhes
{
    public enum TipoPeca { Curva, Regiao, Texto, Componente }

    /// <summary>Estilo de linha de origem (subcategoria de Linhas no projeto).</summary>
    public sealed class EstiloLinha
    {
        public ElementId Id;            // GraphicsStyle no projeto
        public string Nome;
        public byte R, G, B;
        public int Peso = 1;
        public ElementId Padrao;        // LinePatternElement (InvalidElementId = sólido)
        public string NomePadrao;
        public bool Invisivel;
        public bool Tracejado => Padrao != null && Padrao != ElementId.InvalidElementId && Padrao != LinePatternElement.GetSolidPatternId();
    }

    /// <summary>Um elemento 2D lido da vista, em coordenadas da vista (X = direita, Y = cima, Z = 0), sem escala.</summary>
    public sealed class Peca
    {
        public TipoPeca Tipo;
        public ElementId Origem;
        // curva
        public Curve Curva;
        public EstiloLinha Estilo;
        // região
        public List<CurveLoop> Loops;
        public ElementId TipoRegiao;
        public byte[] CorRegiao;        // cor do padrão de frente (ou de fundo)
        public bool Solida, Mascara;
        // texto / componente
        public XYZ Ponto;
        public Curve LinhaBase;         // componente baseado em linha
        public string Texto;
        public double AlturaTexto;      // altura do texto no modelo (papel × escala da vista)
        public double Angulo;
        public string Nome;             // família : tipo (componente)
        // prévia: traços (polilinhas) em coordenadas da vista
        public List<List<XYZ>> Tracos = new List<List<XYZ>>();
    }

    /// <summary>
    /// Desenho 2D selecionado numa vista: linhas de detalhe, regiões preenchidas e de mascaramento, textos,
    /// componentes de detalhe e anotações genéricas (grupos de detalhe são abertos). Tudo o mais fica de fora,
    /// listado nos avisos. Só leitura.
    /// </summary>
    public sealed class Desenho
    {
        public Document Doc;
        public View Vista;
        public Transform Quadro;        // vista → modelo
        public Transform ParaLocal;     // modelo → vista
        public double Z0;               // profundidade do plano do desenho (coordenada local)
        public int Escala;
        public readonly List<Peca> Pecas = new List<Peca>();
        public readonly List<ElementId> Originais = new List<ElementId>();  // o que foi selecionado (grupos inteiros)
        public readonly Dictionary<ElementId, EstiloLinha> Estilos = new Dictionary<ElementId, EstiloLinha>();
        public readonly List<string> Avisos = new List<string>();
        public double MinX, MinY, MaxX, MaxY;
        bool temZ;

        public static readonly BuiltInCategory[] CategoriasComponente = { BuiltInCategory.OST_DetailComponents, BuiltInCategory.OST_GenericAnnotation };

        public static bool VistaAceita(View v)
        {
            if (v == null || v.IsTemplate) return false;
            switch (v.ViewType)
            {
                case ViewType.FloorPlan: case ViewType.CeilingPlan: case ViewType.EngineeringPlan: case ViewType.AreaPlan:
                case ViewType.Section: case ViewType.Elevation: case ViewType.Detail: case ViewType.DraftingView:
                case ViewType.Legend: case ViewType.DrawingSheet:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Elemento 2D que a ferramenta sabe ler (fora grupos).</summary>
        public static bool Suportado(Element e, View v)
        {
            if (e == null || !e.ViewSpecific || e.OwnerViewId != v.Id) return false;
            if (e is CurveElement ce) return ce.CurveElementType == CurveElementType.DetailCurve;
            if (e is FilledRegion || e is TextNote) return true;
            if (e is FamilyInstance fi && fi.Category != null) return CategoriasComponente.Contains(fi.Category.BuiltInCategory);
            return false;
        }

        public static bool Selecionavel(Element e, View v) =>
            Suportado(e, v) || (e is Group g && g.ViewSpecific && g.OwnerViewId == v.Id);

        public XYZ Local(XYZ p)
        {
            var q = ParaLocal.OfPoint(p);
            if (!temZ) { Z0 = q.Z; temZ = true; }
            return new XYZ(q.X, q.Y, 0);
        }

        public XYZ ParaModelo(XYZ local) => Quadro.OfPoint(new XYZ(local.X, local.Y, Z0));

        static double ZDe(Curve c)
        {
            try { return c.GetEndPoint(0).Z; }
            catch { }
            try { if (c is Arc a) return a.Center.Z; if (c is Ellipse el) return el.Center.Z; } catch { }
            return c.Evaluate(0, false).Z;
        }

        public Curve Local(Curve c)
        {
            var c2 = c.CreateTransformed(ParaLocal);
            double z = ZDe(c2);
            if (!temZ) { Z0 = z; temZ = true; }
            return Math.Abs(z) < 1e-9 ? c2 : c2.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, -z)));
        }

        CurveLoop Local(CurveLoop loop)
        {
            var l2 = CurveLoop.CreateViaTransform(loop, ParaLocal);
            double z = 0;
            foreach (Curve c in l2) { z = ZDe(c); break; }
            if (!temZ) { Z0 = z; temZ = true; }
            return Math.Abs(z) < 1e-9 ? l2 : CurveLoop.CreateViaTransform(l2, Transform.CreateTranslation(new XYZ(0, 0, -z)));
        }

        static List<XYZ> Pontos(Curve c)
        {
            try { return c.Tessellate().Select(p => new XYZ(p.X, p.Y, 0)).ToList(); }
            catch { return new List<XYZ>(); }
        }

        public static string NomeDe(Element e)
        {
            try { return e?.Name ?? ""; } catch { return ""; }
        }

        EstiloLinha Estilo(ElementId gsId)
        {
            if (gsId == null || gsId == ElementId.InvalidElementId) return null;
            if (Estilos.TryGetValue(gsId, out var ja)) return ja;
            var gs = Doc.GetElement(gsId) as GraphicsStyle;
            var cat = gs?.GraphicsStyleCategory;
            if (cat == null) return null;
            var es = new EstiloLinha { Id = gsId, Nome = cat.Name };
            try { var c = cat.LineColor; if (c != null && c.IsValid) { es.R = c.Red; es.G = c.Green; es.B = c.Blue; } } catch { }
            try { es.Peso = cat.GetLineWeight(GraphicsStyleType.Projection) ?? 1; } catch { }
            try
            {
                es.Padrao = cat.GetLinePatternId(GraphicsStyleType.Projection);
                if (es.Tracejado) es.NomePadrao = NomeDe(Doc.GetElement(es.Padrao));
            }
            catch { es.Padrao = ElementId.InvalidElementId; }
            try { es.Invisivel = cat.BuiltInCategory == BuiltInCategory.OST_InvisibleLines; } catch { }
            Estilos[gsId] = es;
            return es;
        }

        void Abranger(XYZ p)
        {
            MinX = Math.Min(MinX, p.X); MinY = Math.Min(MinY, p.Y);
            MaxX = Math.Max(MaxX, p.X); MaxY = Math.Max(MaxY, p.Y);
        }

        public bool Vazio => Pecas.Count == 0;
        public double Largura => MaxX - MinX;
        public double Altura => MaxY - MinY;
        public XYZ Centro => new XYZ((MinX + MaxX) / 2, (MinY + MaxY) / 2, 0);
        public XYZ InferiorEsquerdo => new XYZ(MinX, MinY, 0);

        public static Desenho Ler(Document doc, View v, IEnumerable<ElementId> selecionados)
        {
            var d = new Desenho { Doc = doc, Vista = v, Escala = Math.Max(1, v.Scale) };
            d.MinX = d.MinY = double.MaxValue; d.MaxX = d.MaxY = double.MinValue;
            var q = Transform.Identity;
            q.Origin = v.Origin; q.BasisX = v.RightDirection; q.BasisY = v.UpDirection; q.BasisZ = v.ViewDirection;
            d.Quadro = q;
            d.ParaLocal = q.Inverse;

            var fora = new Dictionary<string, int>();
            var vistos = new HashSet<ElementId>();
            Action<ElementId, bool> visitar = null;
            visitar = (id, topo) =>
            {
                if (!vistos.Add(id)) return;
                var e = doc.GetElement(id);
                if (e == null) return;
                if (e is Group g && g.ViewSpecific && g.OwnerViewId == v.Id)
                {
                    if (topo) d.Originais.Add(id);
                    foreach (var m in g.GetMemberIds()) visitar(m, false);
                    return;
                }
                if (!Suportado(e, v))
                {
                    string k = e.Category?.Name ?? e.GetType().Name;
                    if (!e.ViewSpecific) k += " (elemento de modelo)";
                    fora[k] = fora.TryGetValue(k, out int n) ? n + 1 : 1;
                    return;
                }
                if (topo) d.Originais.Add(id);
                try { d.LerElemento(e); }
                catch (Exception ex) { d.Avisos.Add("Não foi possível ler " + (e.Category?.Name ?? "um elemento") + ": " + ex.Message); }
            };
            foreach (var id in selecionados) visitar(id, true);

            foreach (var kv in fora.OrderByDescending(x => x.Value))
                d.Avisos.Add("Fica de fora: " + kv.Key + " (" + kv.Value + ")");
            if (d.Pecas.Count == 0) { d.MinX = d.MinY = d.MaxX = d.MaxY = 0; }
            return d;
        }

        void LerElemento(Element e)
        {
            if (e is CurveElement ce)
            {
                var c = Local(ce.GeometryCurve);
                var p = new Peca { Tipo = TipoPeca.Curva, Origem = e.Id, Curva = c, Estilo = Estilo(ce.LineStyle?.Id) };
                p.Tracos.Add(Pontos(c));
                Juntar(p);
            }
            else if (e is FilledRegion fr)
            {
                var tipo = Doc.GetElement(fr.GetTypeId()) as FilledRegionType;
                var p = new Peca { Tipo = TipoPeca.Regiao, Origem = e.Id, TipoRegiao = fr.GetTypeId(), Loops = new List<CurveLoop>() };
                foreach (var l in fr.GetBoundaries())
                {
                    var ll = Local(l);
                    p.Loops.Add(ll);
                    var pts = new List<XYZ>();
                    foreach (Curve c in ll) { var t = Pontos(c); if (pts.Count > 0 && t.Count > 0) t.RemoveAt(0); pts.AddRange(t); }
                    p.Tracos.Add(pts);
                }
                if (tipo != null)
                {
                    p.Mascara = tipo.IsMasking;
                    bool frente = tipo.ForegroundPatternId != ElementId.InvalidElementId;
                    var cor = frente ? tipo.ForegroundPatternColor : tipo.BackgroundPatternColor;
                    if (cor != null && cor.IsValid) p.CorRegiao = new[] { cor.Red, cor.Green, cor.Blue };
                    var fpId = frente ? tipo.ForegroundPatternId : tipo.BackgroundPatternId;
                    try { p.Solida = (Doc.GetElement(fpId) as FillPatternElement)?.GetFillPattern()?.IsSolidFill ?? false; } catch { }
                }
                p.Estilo = EstiloDoContorno(fr);
                Juntar(p);
            }
            else if (e is TextNote tn)
            {
                var p = new Peca { Tipo = TipoPeca.Texto, Origem = e.Id, Ponto = Local(tn.Coord), Texto = tn.Text ?? "" };
                try { p.AlturaTexto = Doc.GetElement(tn.GetTypeId()).get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble() * Escala; } catch { p.AlturaTexto = 2.5 / 304.8 * Escala; }
                try { var b = ParaLocal.OfVector(tn.BaseDirection); p.Angulo = Math.Atan2(b.Y, b.X); } catch { }
                // prévia e limites: a caixa do texto na vista
                var bb = tn.get_BoundingBox(Vista);
                if (bb != null)
                {
                    var cantos = new[] { bb.Min, new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z), bb.Max, new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z) }.Select(x => Local(x)).ToList();
                    foreach (var c in cantos) Abranger(c);
                }
                Abranger(p.Ponto);
                Pecas.Add(p);
            }
            else if (e is FamilyInstance fi)
            {
                var p = new Peca { Tipo = TipoPeca.Componente, Origem = e.Id };
                try { p.Nome = fi.Symbol.FamilyName + " : " + fi.Symbol.Name; } catch { p.Nome = "componente"; }
                if (fi.Location is LocationCurve lc) { p.LinhaBase = Local(lc.Curve); p.Ponto = p.LinhaBase.GetEndPoint(0); }
                else if (fi.Location is LocationPoint lp) p.Ponto = Local(lp.Point);
                var geo = fi.get_Geometry(new Options { View = Vista });
                if (geo != null) Tracar(geo, p.Tracos);
                if (p.Tracos.Count == 0)
                {
                    var bb = fi.get_BoundingBox(Vista);
                    if (bb != null)
                    {
                        XYZ a = Local(bb.Min), b = Local(bb.Max);
                        p.Tracos.Add(new List<XYZ> { a, new XYZ(b.X, a.Y, 0), b, new XYZ(a.X, b.Y, 0), a });
                    }
                }
                if (p.Ponto != null) Abranger(p.Ponto);
                Juntar(p);
            }
        }

        void Tracar(GeometryElement g, List<List<XYZ>> saida)
        {
            foreach (GeometryObject o in g)
            {
                try
                {
                    if (o is GeometryInstance gi) Tracar(gi.GetInstanceGeometry(), saida);
                    else if (o is Curve c) saida.Add(Pontos(Local(c)));
                    else if (o is PolyLine pl) saida.Add(pl.GetCoordinates().Select(x => Local(x)).ToList());
                    else if (o is Solid s && s.Faces.Size > 0)
                        foreach (Edge ed in s.Edges) saida.Add(Pontos(Local(ed.AsCurve())));
                }
                catch { }
            }
        }

        /// <summary>Estilo do contorno da região (a API não tem leitura direta: vem da geometria).</summary>
        EstiloLinha EstiloDoContorno(FilledRegion fr)
        {
            try
            {
                var conta = new Dictionary<ElementId, int>();
                Action<GeometryElement> andar = null;
                andar = g =>
                {
                    foreach (GeometryObject o in g)
                    {
                        if (o is GeometryInstance gi) { andar(gi.GetInstanceGeometry()); continue; }
                        if (o is Curve c && c.GraphicsStyleId != ElementId.InvalidElementId)
                            conta[c.GraphicsStyleId] = conta.TryGetValue(c.GraphicsStyleId, out int n) ? n + 1 : 1;
                        // a região vem como sólido: o estilo do contorno está nas arestas
                        if (o is Solid s)
                            foreach (Edge ed in s.Edges)
                                if (ed.GraphicsStyleId != ElementId.InvalidElementId)
                                    conta[ed.GraphicsStyleId] = conta.TryGetValue(ed.GraphicsStyleId, out int m) ? m + 1 : 1;
                    }
                };
                var geo = fr.get_Geometry(new Options { View = Vista });
                if (geo != null) andar(geo);
                foreach (var kv in conta.OrderByDescending(x => x.Value))
                {
                    var es = Estilo(kv.Key);
                    if (es != null && (Doc.GetElement(kv.Key) as GraphicsStyle)?.GraphicsStyleCategory?.Parent?.BuiltInCategory == BuiltInCategory.OST_Lines)
                        return es;
                }
            }
            catch { }
            return null;
        }

        void Juntar(Peca p)
        {
            foreach (var t in p.Tracos) foreach (var q in t) Abranger(q);
            Pecas.Add(p);
        }

        public int Conta(TipoPeca t) => Pecas.Count(p => p.Tipo == t);

        public string Resumo()
        {
            var partes = new List<string>();
            int n;
            if ((n = Conta(TipoPeca.Curva)) > 0) partes.Add(n + (n == 1 ? " linha" : " linhas"));
            if ((n = Conta(TipoPeca.Regiao)) > 0) partes.Add(n + (n == 1 ? " região" : " regiões"));
            if ((n = Conta(TipoPeca.Texto)) > 0) partes.Add(n + (n == 1 ? " texto" : " textos"));
            if ((n = Conta(TipoPeca.Componente)) > 0) partes.Add(n + (n == 1 ? " componente" : " componentes"));
            return partes.Count == 0 ? "nada" : string.Join(", ", partes);
        }

        /// <summary>Nome sugerido: o da família, quando a seleção é um componente só.</summary>
        public string NomeSugerido()
        {
            var comps = Pecas.Where(p => p.Tipo == TipoPeca.Componente).ToList();
            if (comps.Count == 1 && Pecas.Count == 1)
            {
                var fi = Doc.GetElement(comps[0].Origem) as FamilyInstance;
                try { return fi?.Symbol?.FamilyName ?? ""; } catch { }
            }
            return "";
        }
    }
}
