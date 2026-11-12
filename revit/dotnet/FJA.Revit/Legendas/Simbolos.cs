using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace FJA.Revit.Legendas
{
    /// <summary>Anotação genérica aninhada numa família (como a família desenha o símbolo em planta).</summary>
    public sealed class Aninhada
    {
        public string Fam, Tipo, VisHost;
        public bool VisivelPropria;
        public XYZ Pt;
        public double Angulo;
        public bool Espelhada;
        public List<Tuple<string, string>> Pmap = new List<Tuple<string, string>>(); // (parâmetro aninhado, parâmetro do hospedeiro)
    }

    /// <summary>
    /// Resolve sozinho os avisos e erros de geometria ("Line is too short" etc.) nas transações das famílias de
    /// símbolo: avisos são apagados e erros que se resolvem apagando o elemento (linhas minúsculas do DWG
    /// reduzido) são resolvidos assim, sem abrir a janela de erros do Revit.
    /// </summary>
    public sealed class ResolverFalhas : IFailuresPreprocessor
    {
        public int Apagados;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            bool resolveu = false;
            foreach (FailureMessageAccessor f in fa.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning) { fa.DeleteWarning(f); continue; }
                if (f.HasResolutionOfType(FailureResolutionType.DeleteElements))
                {
                    Apagados += f.GetFailingElementIds().Count;
                    f.SetCurrentResolutionType(FailureResolutionType.DeleteElements);
                    fa.ResolveFailure(f);
                    resolveu = true;
                }
            }
            return resolveu ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }

        public static Transaction Iniciar(Document d, string nome, ResolverFalhas r = null)
        {
            var t = new Transaction(d, nome);
            var fo = t.GetFailureHandlingOptions();
            fo.SetFailuresPreprocessor(r ?? new ResolverFalhas());
            fo.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(fo);
            t.Start();
            return t;
        }
    }

    sealed class Sobrescrever : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

    sealed class NaoSobrescrever : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return false;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Project;
            overwriteParameterValues = false;
            return false;
        }
    }

    /// <summary>Símbolos da legenda: famílias "(XXX) (LEG) NOME", anotações aninhadas, desenho da planta e DWG.</summary>
    public static class Simbolos
    {
        public const double MM_PLAN_MAX = 12.0;   // maior medida de um símbolo copiado da planta
        public const double MM_PLAN_MIN = 4.5;    // menor medida

        public static readonly Dictionary<ViewDetailLevel, int> BitsDetalhe = new Dictionary<ViewDetailLevel, int>
        {
            { ViewDetailLevel.Coarse, 8192 }, { ViewDetailLevel.Medium, 16384 }, { ViewDetailLevel.Fine, 32768 },
        };

        static int? Inteiro(Element e, BuiltInParameter bip)
        {
            try { var p = e.get_Parameter(bip); return p != null ? p.AsInteger() : (int?)null; } catch { return null; }
        }

        public static HashSet<string> NomesFamilias(Document doc) =>
            new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(Family)).Select(f => f.Name));

        static bool AbertaNoEditor(Document doc, string nome)
        {
            foreach (Document d in doc.Application.Documents)
            {
                try { if (d.IsFamilyDocument && (d.Title == nome || d.Title == nome + ".rfa")) return true; } catch { }
            }
            return false;
        }

        /// <summary>Lê as anotações aninhadas da família (sem alterar nada nela) e carrega no projeto as famílias
        /// de símbolo que faltam. Fora de transação.</summary>
        /// <summary>Nomes dos parâmetros da família que controlam a visibilidade de algum elemento dela (anotações
        /// aninhadas, linhas simbólicas, regiões…): são eles que mudam o símbolo na vista. null se não der para abrir.</summary>
        public static HashSet<string> ParametrosDeVisibilidade(Document doc, Family family)
        {
            if (family == null || !family.IsEditable || AbertaNoEditor(doc, family.Name)) return null;
            var r = new HashSet<string>(StringComparer.Ordinal);
            Document fdoc = doc.EditFamily(family);
            try
            {
                var fm = fdoc.FamilyManager;
                foreach (Element e in new FilteredElementCollector(fdoc).WhereElementIsNotElementType())
                {
                    try
                    {
                        var pv = e.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
                        if (pv == null) continue;
                        var a = fm.GetAssociatedFamilyParameter(pv);
                        if (a != null) r.Add(a.Definition.Name);
                    }
                    catch { }
                }
            }
            finally { fdoc.Close(false); }
            return r;
        }

        public static List<Aninhada> LerAninhadas(Document doc, Family family, int bitDetalhe, HashSet<string> carregadas)
        {
            var specs = new List<Aninhada>();
            if (AbertaNoEditor(doc, family.Name))
                throw new Exception("família aberta no editor do Revit; feche-a e rode de novo");
            Document fdoc = doc.EditFamily(family);
            try
            {
                FamilyManager fm = fdoc.FamilyManager;
                var cats = new[] { new ElementId(BuiltInCategory.OST_GenericAnnotation), new ElementId(BuiltInCategory.OST_DetailComponents) };
                foreach (FamilyInstance e in new FilteredElementCollector(fdoc).OfClass(typeof(FamilyInstance)))
                {
                    if (e.Category == null || !cats.Contains(e.Category.Id)) continue;
                    int? bits = Inteiro(e, BuiltInParameter.GEOM_VISIBILITY_PARAM);
                    if (bits.HasValue && bits.Value >= 8192 && (bits.Value & bitDetalhe) == 0) continue;
                    FamilySymbol nsym = e.Symbol;
                    string visHost = null;
                    Parameter pvis = e.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
                    if (pvis != null)
                    {
                        var a = fm.GetAssociatedFamilyParameter(pvis);
                        if (a != null) visHost = a.Definition.Name;
                    }
                    var sp = new Aninhada
                    {
                        Fam = nsym.FamilyName, Tipo = nsym.Name, VisHost = visHost,
                        VisivelPropria = Inteiro(e, BuiltInParameter.IS_VISIBLE_PARAM) != 0,
                        Espelhada = e.Mirrored,
                    };
                    foreach (Parameter ip in e.Parameters)
                    {
                        if (pvis != null && ip.Id == pvis.Id) continue;
                        FamilyParameter a = null;
                        try { a = fm.GetAssociatedFamilyParameter(ip); } catch { }
                        if (a != null) sp.Pmap.Add(Tuple.Create(ip.Definition.Name, a.Definition.Name));
                    }
                    Transform tr = e.GetTransform();
                    sp.Pt = tr.Origin;
                    sp.Angulo = Math.Atan2(tr.BasisX.Y, tr.BasisX.X);
                    specs.Add(sp);
                    if (!carregadas.Contains(nsym.FamilyName) && !AbertaNoEditor(doc, nsym.FamilyName))
                    {
                        Document ndoc = fdoc.EditFamily(nsym.Family);
                        try { ndoc.LoadFamily(doc, new NaoSobrescrever()); }
                        finally { ndoc.Close(false); }
                        carregadas.Add(nsym.FamilyName);
                    }
                }
            }
            finally { fdoc.Close(false); }
            return specs;
        }

        static Parameter ParamHospedeiro(Element rep, Element sym, string nome)
        {
            Parameter p = rep?.LookupParameter(nome);
            return p ?? sym?.LookupParameter(nome);
        }

        static void CopiarValor(Parameter src, Parameter dst, bool mesmoDoc = true)
        {
            if (src == null || dst == null || dst.IsReadOnly || src.StorageType != dst.StorageType) return;
            switch (src.StorageType)
            {
                case StorageType.Integer: dst.Set(src.AsInteger()); break;
                case StorageType.Double: dst.Set(src.AsDouble()); break;
                case StorageType.String: dst.Set(src.AsString() ?? ""); break;
                case StorageType.ElementId: if (mesmoDoc) dst.Set(src.AsElementId()); break;
            }
        }

        public static List<Aninhada> Visiveis(List<Aninhada> specs, Element rep, Element sym)
        {
            var r = new List<Aninhada>();
            foreach (var s in specs ?? new List<Aninhada>())
            {
                if (!string.IsNullOrEmpty(s.VisHost))
                {
                    var p = ParamHospedeiro(rep, sym, s.VisHost);
                    if (p == null || p.StorageType != StorageType.Integer || p.AsInteger() != 1) continue;
                }
                else if (!s.VisivelPropria) continue;
                r.Add(s);
            }
            return r;
        }

        static FamilySymbol AcharTipo(Document doc, string fam, string tipo) =>
            new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .FirstOrDefault(s => s.FamilyName == fam && s.Name == tipo);

        /// <summary>Cria as anotações na vista (na origem). Devolve os ids criados.</summary>
        public static List<ElementId> ColocarAninhadas(Document doc, View view, List<Aninhada> specs, Element rep, Element sym)
        {
            var ids = new List<ElementId>();
            foreach (var s in specs)
            {
                var nsym = AcharTipo(doc, s.Fam, s.Tipo);
                if (nsym == null) continue;
                if (!nsym.IsActive) { nsym.Activate(); doc.Regenerate(); }
                var p = new XYZ(s.Pt.X, s.Pt.Y, 0);
                FamilyInstance inst = doc.Create.NewFamilyInstance(p, nsym, view);
                foreach (var pm in s.Pmap) CopiarValor(ParamHospedeiro(rep, sym, pm.Item2), inst.LookupParameter(pm.Item1));
                if (Math.Abs(s.Angulo) > 1e-6)
                    ElementTransformUtils.RotateElement(doc, inst.Id, Line.CreateBound(p, new XYZ(p.X, p.Y, 1)), s.Angulo);
                if (s.Espelhada)
                    ElementTransformUtils.MirrorElements(doc, new List<ElementId> { inst.Id }, Plane.CreateByNormalAndOrigin(XYZ.BasisX, p), false);
                ids.Add(inst.Id);
            }
            return ids;
        }

        public static double[] Caixa(Document doc, IEnumerable<ElementId> ids, View view)
        {
            double[] bb = null;
            foreach (var i in ids)
            {
                var e = doc.GetElement(i);
                var b = e?.get_BoundingBox(view);
                if (b == null) continue;
                if (bb == null) bb = new[] { b.Min.X, b.Min.Y, b.Max.X, b.Max.Y };
                else bb = new[] { Math.Min(bb[0], b.Min.X), Math.Min(bb[1], b.Min.Y), Math.Max(bb[2], b.Max.X), Math.Max(bb[3], b.Max.Y) };
            }
            return bb;
        }

        // ------------------------------------------------------------ desenho copiado da planta
        static XYZ Plano(XYZ p) => new XYZ(p.X, p.Y, 0);

        /// <summary>Linhas do elemento como aparecem na vista (projetadas no plano), sem repetir.</summary>
        public static List<Curve> CurvasNaPlanta(FamilyInstance inst, View view)
        {
            var opt = new Options { View = view };
            GeometryElement geom = inst.get_Geometry(opt);
            var outl = new List<Curve>();
            var vistos = new HashSet<string>();
            double tol = 1.0 / 304.8 * 0.2;
            Func<XYZ, string> pk = p => Math.Round(p.X / tol) + ":" + Math.Round(p.Y / tol);
            Func<XYZ, XYZ, string, string> chave = (a, b, extra) =>
            {
                string ka = pk(a), kb = pk(b);
                return (string.CompareOrdinal(ka, kb) < 0 ? ka + "|" + kb : kb + "|" + ka) + "|" + extra;
            };
            Action<Curve> add = null;
            add = c =>
            {
                try
                {
                    if (c is Arc arc0 && !c.IsBound)
                    {
                        if (Math.Abs(arc0.Normal.Z) < 0.5) return;
                        string k = "circ" + pk(arc0.Center) + ":" + Math.Round(arc0.Radius / tol);
                        if (vistos.Add(k)) outl.Add(Arc.Create(Plano(arc0.Center), arc0.Radius, 0.0, 2.0 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                        return;
                    }
                    if (!c.IsBound) return;
                    IList<XYZ> pts;
                    if (c is Line) pts = new List<XYZ> { c.GetEndPoint(0), c.GetEndPoint(1) };
                    else if (c is Arc arc && Math.Abs(arc.Normal.Z) > 0.99)
                    {
                        XYZ a = Plano(c.GetEndPoint(0)), b = Plano(c.GetEndPoint(1)), m = Plano(c.Evaluate(0.5, true));
                        if (a.DistanceTo(b) < tol) return;
                        if (vistos.Add(chave(a, b, pk(m)))) outl.Add(Arc.Create(a, b, m));
                        return;
                    }
                    else pts = c.Tessellate();
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        XYZ a = Plano(pts[i]), b = Plano(pts[i + 1]);
                        if (a.DistanceTo(b) < tol) continue;
                        if (vistos.Add(chave(a, b, ""))) outl.Add(Line.CreateBound(a, b));
                    }
                }
                catch { }
            };
            Action<GeometryElement> walk = null;
            walk = gs =>
            {
                if (gs == null) return;
                foreach (GeometryObject g in gs)
                {
                    if (g is GeometryInstance gi) walk(gi.GetInstanceGeometry());
                    else if (g is Solid so) { foreach (Edge ed in so.Edges) add(ed.AsCurve()); }
                    else if (g is Curve cu) add(cu);
                    else if (g is PolyLine pl)
                    {
                        var p = pl.GetCoordinates();
                        for (int i = 0; i < p.Count - 1; i++)
                            if (p[i].DistanceTo(p[i + 1]) > 1e-9) add(Line.CreateBound(p[i], p[i + 1]));
                    }
                }
            };
            walk(geom);
            // o símbolo na legenda fica na orientação da família: desfaz a rotação do elemento na planta
            try
            {
                Transform tr = inst.GetTransform();
                double ang = Math.Atan2(tr.BasisX.Y, tr.BasisX.X);
                if (Math.Abs(ang) > 1e-6)
                {
                    Transform rot = Transform.CreateRotationAtPoint(XYZ.BasisZ, -ang, Plano(tr.Origin));
                    outl = outl.Select(c => { try { return c.CreateTransformed(rot); } catch { return null; } }).Where(c => c != null).ToList();
                }
            }
            catch { }
            return outl;
        }

        static void Limites(IEnumerable<Curve> curvas, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue; maxX = maxY = double.MinValue;
            foreach (var c in curvas)
                foreach (var p in c.Tessellate())
                {
                    minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
                }
        }

        /// <summary>Desenha na legenda, com linhas de detalhe, o elemento como aparece na planta
        /// (reduzido para caber em MM_PLAN_MAX de papel).</summary>
        public static List<ElementId> CopiarDaPlanta(Document doc, View alvo, Item it)
        {
            var ids = new List<ElementId>();
            if (it.Geo == null || it.GeoInst == null) return ids;
            var curvas = CurvasNaPlanta(it.GeoInst, it.Geo);
            if (curvas.Count == 0) return ids;
            Limites(curvas, out double x0, out double y0, out double x1, out double y1);
            double dim = Math.Max(x1 - x0, y1 - y0);
            double lim = MM_PLAN_MAX * alvo.Scale / 304.8;
            if (dim > lim)
            {
                var ctr = new XYZ((x0 + x1) / 2, (y0 + y1) / 2, 0);
                Transform tf = Transform.CreateTranslation(ctr).Multiply(Transform.Identity.ScaleBasis(lim / dim)).Multiply(Transform.CreateTranslation(ctr.Negate()));
                curvas = curvas.Select(c => { try { return c.CreateTransformed(tf); } catch { return null; } }).Where(c => c != null).ToList();
                it.Reduzido = true;
            }
            foreach (var c in curvas)
            {
                try { ids.Add(doc.Create.NewDetailCurve(alvo, c).Id); } catch { }
            }
            return ids;
        }

        // ------------------------------------------------------------ famílias (LEG)
        public static string NomeLeg(Disciplina d, Coleta col, FamilySymbol sym) => d.Prefixo + " (LEG) " + col.RotuloItem(sym, false);

        public static Family FamiliaPorNome(Document doc, string nome)
        {
            string alvo = Texto.Norm(nome);
            return new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => Texto.Norm(f.Name) == alvo);
        }

        public static FamilySymbol PrimeiroTipo(Document doc, Family f)
        {
            foreach (var i in f.GetFamilySymbolIds()) return doc.GetElement(i) as FamilySymbol;
            return null;
        }

        static string ModeloAnotacao(Autodesk.Revit.ApplicationServices.Application app)
        {
            var achados = new List<string>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(app.FamilyTemplatePath, "*.rft", SearchOption.AllDirectories))
                {
                    string l = Texto.Norm(Path.GetFileName(f));
                    if (l.Contains("GENERIC ANNOTATION") || l.Contains("ANOTACAO GENERICA")) achados.Add(f);
                }
            }
            catch { }
            return achados.OrderBy(p => Texto.Norm(p).Contains("PORTUGUESE") ? 0 : Texto.Norm(p).Contains("METRIC") ? 1 : 2).ThenBy(p => p).FirstOrDefault();
        }

        static View VistaDaFamilia(Document fdoc)
        {
            var ok = new[] { ViewType.FloorPlan, ViewType.DraftingView, ViewType.Detail, ViewType.Legend, ViewType.CeilingPlan, ViewType.EngineeringPlan };
            View resto = null;
            foreach (View v in new FilteredElementCollector(fdoc).OfClass(typeof(View)))
            {
                if (v.IsTemplate) continue;
                if (ok.Contains(v.ViewType)) return v;
                if (resto == null && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser
                    && v.ViewType != ViewType.Undefined && v.ViewType != ViewType.Internal) resto = v;
            }
            return resto;
        }

        static void CurvasDaGeometria(GeometryElement g, Transform tf, List<Curve> outl)
        {
            foreach (GeometryObject o in g)
            {
                if (o is GeometryInstance gi) CurvasDaGeometria(gi.GetSymbolGeometry(), tf.Multiply(gi.Transform), outl);
                else if (o is Curve c) { try { outl.Add(c.CreateTransformed(tf)); } catch { } }
                else if (o is PolyLine pl)
                {
                    var pts = pl.GetCoordinates().Select(p => tf.OfPoint(p)).ToList();
                    for (int i = 0; i < pts.Count - 1; i++)
                        if (pts[i].DistanceTo(pts[i + 1]) > 1e-6) outl.Add(Line.CreateBound(pts[i], pts[i + 1]));
                }
            }
        }

        static void Salvar(Document fdoc, Document doc, string pasta, string nome)
        {
            Directory.CreateDirectory(pasta);
            fdoc.SaveAs(Path.Combine(pasta, nome + ".rfa"), new SaveAsOptions { OverwriteExistingFile = true });
            fdoc.LoadFamily(doc, new NaoSobrescrever());
        }

        /// <summary>Cria e carrega no projeto a família de anotação 'nome' com o símbolo do item.
        /// Fora de transação. Devolve a família carregada (ou null).</summary>
        public static Family CriarFamiliaLeg(Document doc, Item it, List<Aninhada> specs, string nome, string pasta)
        {
            var app = doc.Application;
            string tpl = ModeloAnotacao(app) ?? throw new Exception("modelo de família 'Anotação genérica' não encontrado");
            double escala = (it.Geo ?? doc.ActiveView)?.Scale ?? 50;
            if (escala <= 0) escala = 50;
            Document fdoc = app.NewFamilyDocument(tpl);
            try
            {
                var carregadas = new HashSet<string>();
                foreach (var sp in specs)
                {
                    if (carregadas.Contains(sp.Fam)) continue;
                    var psym = AcharTipo(doc, sp.Fam, sp.Tipo);
                    if (psym == null) continue;
                    Document ndoc = doc.EditFamily(psym.Family);
                    try { ndoc.LoadFamily(fdoc, new NaoSobrescrever()); }
                    finally { ndoc.Close(false); }
                    carregadas.Add(sp.Fam);
                }
                int n = 0;
                using (var t = ResolverFalhas.Iniciar(fdoc, "Simbolo de legenda"))
                {
                    View view = VistaDaFamilia(fdoc);
                    foreach (var tn in new FilteredElementCollector(fdoc).OfClass(typeof(TextNote)).ToElementIds().ToList())
                        fdoc.Delete(tn);
                    foreach (var sp in specs)
                    {
                        var nsym = AcharTipo(fdoc, sp.Fam, sp.Tipo);
                        if (nsym == null) continue;
                        try
                        {
                            if (!nsym.IsActive) { nsym.Activate(); fdoc.Regenerate(); }
                            var pt = new XYZ(sp.Pt.X / escala, sp.Pt.Y / escala, 0);
                            FamilyInstance inst = fdoc.FamilyCreate.NewFamilyInstance(pt, nsym, view);
                            foreach (var pm in sp.Pmap) CopiarValor(ParamHospedeiro(it.Rep, it.Sym, pm.Item2), inst.LookupParameter(pm.Item1), false);
                            if (Math.Abs(sp.Angulo) > 1e-6)
                                ElementTransformUtils.RotateElement(fdoc, inst.Id, Line.CreateBound(pt, new XYZ(pt.X, pt.Y, 1)), sp.Angulo);
                            if (sp.Espelhada)
                                ElementTransformUtils.MirrorElements(fdoc, new List<ElementId> { inst.Id }, Plane.CreateByNormalAndOrigin(XYZ.BasisX, pt), false);
                            n++;
                        }
                        catch { }
                    }
                    if (n == 0 && it.Geo != null && it.GeoInst != null)
                    {
                        // sem anotação aninhada: linhas do elemento como aparece na planta, no tamanho do papel
                        var curvas = CurvasNaPlanta(it.GeoInst, it.Geo);
                        if (curvas.Count > 0)
                        {
                            Limites(curvas, out double x0, out double y0, out double x1, out double y1);
                            var ctr = new XYZ((x0 + x1) / 2, (y0 + y1) / 2, 0);
                            double dim = Math.Max(x1 - x0, y1 - y0) / escala;
                            double f = 1.0 / escala, lim = MM_PLAN_MAX / 304.8, mini = MM_PLAN_MIN / 304.8;
                            if (dim > lim) { f *= lim / dim; it.Reduzido = true; }
                            else if (dim > 0 && dim < mini) { f *= mini / dim; it.Ampliado = true; }
                            Transform tf = Transform.Identity.ScaleBasis(f).Multiply(Transform.CreateTranslation(ctr.Negate()));
                            foreach (var c in curvas)
                            {
                                try { fdoc.FamilyCreate.NewDetailCurve(view, c.CreateTransformed(tf)); n++; } catch { }
                            }
                        }
                    }
                    t.Commit();
                }
                if (n == 0) return null;
                Salvar(fdoc, doc, pasta, nome);
            }
            finally { fdoc.Close(false); }
            return FamiliaPorNome(doc, nome);
        }

        /// <summary>Parâmetro "Fator de escala" (Scale Factor) do tipo de um DWG importado.</summary>
        static Parameter FatorDeEscala(Element tipo)
        {
            if (tipo == null) return null;
            if (Enum.TryParse("IMPORT_SCALE", out BuiltInParameter bip))
            {
                var p = tipo.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.Double && !p.IsReadOnly) return p;
            }
            foreach (Parameter p in tipo.Parameters)
            {
                string n = Texto.Norm(p.Definition?.Name);
                if (p.StorageType == StorageType.Double && !p.IsReadOnly && (n == "SCALE FACTOR" || n == "FATOR DE ESCALA" || n.Contains("ESCALA") || n.Contains("SCALE")))
                    return p;
            }
            return null;
        }

        /// <summary>Família (LEG) feita de DWG importado: muda só o fator de escala do tipo do DWG dentro da
        /// família (sem exportar de novo) e recarrega no projeto. Fora de transação. Devolve false se a família
        /// não tem DWG (então é preciso convertê-la pelo caminho completo).</summary>
        public static bool Reescalar(Document doc, Family leg, double tamMm, Item it)
        {
            string nomeFam = leg.Name;
            if (AbertaNoEditor(doc, nomeFam)) throw new Exception("família aberta no editor do Revit; feche-a e atualize de novo");
            Document fdoc = doc.EditFamily(leg);
            try
            {
                var imp = new FilteredElementCollector(fdoc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().FirstOrDefault();
                if (imp == null) return false;
                Parameter fator = FatorDeEscala(fdoc.GetElement(imp.GetTypeId()));
                if (fator == null) return false;
                View view = VistaDaFamilia(fdoc);
                double alvo = tamMm / 304.8;
                using (var t = ResolverFalhas.Iniciar(fdoc, "Escala do símbolo de legenda"))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var bb = imp.get_BoundingBox(view) ?? imp.get_BoundingBox(null);
                        double m = bb != null ? Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y) : 0;
                        if (m <= 1e-12) break;
                        if (Math.Abs(m - alvo) / alvo < 0.005) break;
                        fator.Set(fator.AsDouble() * alvo / m);
                        fdoc.Regenerate();
                    }
                    t.Commit();
                }
                fdoc.LoadFamily(doc, new Sobrescrever());
            }
            finally { fdoc.Close(false); }
            it.Redimensionado = true;
            return true;
        }

        /// <summary>Redimensiona um símbolo pelo DWG: a família original numa vista de desenho 1:1 é exportada
        /// para DWG e importada na família 'nome' na escala que deixa a maior medida igual a tamMm.
        /// Fora de transação.</summary>
        public static Family ViaDwg(Document doc, Family famOrig, string nome, string pasta, double tamMm, Item it)
        {
            FamilySymbol sym = PrimeiroTipo(doc, famOrig);
            return sym == null ? null : ViaDwg(doc, sym, nome, pasta, tamMm, it);
        }

        /// <summary>Nome da família (LEG) de um símbolo acrescentado à mão, quando a escala dele é ajustada.</summary>
        // ------------------------------------------------------------ símbolos fixos (ex.: eletrodutos, ELE)
        public static string NomeFixo(Disciplina d, Fixo fx) => d.Prefixo + " (FIX) " + fx.Rotulo;

        /// <summary>Tipo do símbolo fixo: a família do projeto que bate com 'Procura' ou a "(SIGLA) (FIX) RÓTULO"
        /// criada pela ferramenta. null se nenhuma existir.</summary>
        public static FamilySymbol SimboloFixo(Document doc, Disciplina d, Fixo fx)
        {
            if (fx.Procura != null && fx.Procura.Length == 2)
            {
                var ids = new[] { new ElementId(BuiltInCategory.OST_GenericAnnotation), new ElementId(BuiltInCategory.OST_DetailComponents) };
                foreach (FamilySymbol s in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                {
                    try
                    {
                        if (s.Category == null || !ids.Contains(s.Category.Id)) continue;
                        if (Texto.Norm(s.FamilyName).Contains(fx.Procura[0]) && Texto.Norm(s.Name).Contains(fx.Procura[1])) return s;
                    }
                    catch { }
                }
            }
            var f = FamiliaPorNome(doc, NomeFixo(d, fx));
            return f != null ? PrimeiroTipo(doc, f) : null;
        }

        /// <summary>Cria (e carrega) as famílias dos símbolos fixos que faltam no projeto. Fora de transação.
        /// Devolve os erros.</summary>
        public static List<string> GarantirFixos(Document doc, Disciplina d, string pasta)
        {
            var erros = new List<string>();
            if (d.Fixos == null) return erros;
            foreach (var fx in d.Fixos)
            {
                if (SimboloFixo(doc, d, fx) != null) continue;
                try { CriarFixo(doc, d, fx, pasta); }
                catch (Exception e) { erros.Add(fx.Rotulo + ": " + e.Message); }
            }
            return erros;
        }

        static readonly byte[] COR_ELE = { 65, 100, 215 }, COR_ELETROCALHA = { 189, 242, 253 };
        // seta de eletroduto (mm de papel, como na família "Eletroduto que sobe, desce ou passa")
        static readonly double[][] SetaSobe = { new[] { 4.16, 3.04 }, new[] { 6.25, 6.25 }, new[] { 3.04, 4.16 }, new[] { 3.60, 3.60 } };
        static readonly double[][] SetaDesce = { new[] { 2.74, 3.85 }, new[] { 0.66, 0.66 }, new[] { 3.85, 2.74 }, new[] { 3.30, 3.30 } };

        static void CriarFixo(Document doc, Disciplina d, Fixo fx, string pasta)
        {
            var app = doc.Application;
            string tpl = ModeloAnotacao(app) ?? throw new Exception("modelo de família 'Anotação genérica' não encontrado");
            string nome = NomeFixo(d, fx);
            Document fdoc = app.NewFamilyDocument(tpl);
            try
            {
                using (var t = ResolverFalhas.Iniciar(fdoc, "Simbolo fixo"))
                {
                    View v = VistaDaFamilia(fdoc);
                    foreach (var tn in new FilteredElementCollector(fdoc).OfClass(typeof(TextNote)).ToElementIds().ToList())
                        fdoc.Delete(tn);
                    // linhas grossas numa subcategoria própria
                    GraphicsStyle grossa = null;
                    try
                    {
                        var cat = fdoc.OwnerFamily.FamilyCategory;
                        var sub = cat.SubCategories.Contains("FJA Linha grossa") ? cat.SubCategories.get_Item("FJA Linha grossa")
                            : fdoc.Settings.Categories.NewSubcategory(cat, "FJA Linha grossa");
                        sub.SetLineWeight(4, GraphicsStyleType.Projection);
                        grossa = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                    }
                    catch { }
                    const double k = 1.0 / 304.8;
                    Func<double, double, XYZ> P = (x, y) => new XYZ(x * k, y * k, 0);
                    Action<double, double, double, double, bool> L = (x0, y0, x1, y1, g) =>
                    {
                        var c = fdoc.FamilyCreate.NewDetailCurve(v, Line.CreateBound(P(x0, y0), P(x1, y1)));
                        if (g && grossa != null) { try { c.LineStyle = grossa; } catch { } }
                    };
                    Action<double[][], byte[]> R = (pts, rgb) =>
                    {
                        var tipo = TipoSolido(fdoc, rgb);
                        if (tipo == null) return;
                        var loop = new CurveLoop();
                        for (int i = 0; i < pts.Length; i++)
                        {
                            var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                            loop.Append(Line.CreateBound(P(a[0], a[1]), P(b[0], b[1])));
                        }
                        try
                        {
                            var fr = FilledRegion.Create(fdoc, tipo, v.Id, new List<CurveLoop> { loop });
                            var inv = FilledRegion.GetValidLineStyleIdsForFilledRegion(fdoc)
                                .FirstOrDefault(i => (fdoc.GetElement(i) as GraphicsStyle)?.GraphicsStyleCategory?.Id == new ElementId(BuiltInCategory.OST_InvisibleLines));
                            if (inv != null) fr.SetLineStyleId(inv);
                        }
                        catch { }
                    };
                    double h = 12.5 / 2;
                    switch (fx.Id)
                    {
                        case "sobe":
                        case "desce":
                            fdoc.FamilyCreate.NewDetailCurve(v, Arc.Create(XYZ.Zero, 0.94 * k, 0, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                            L(0.66, 0.66, 6.25, 6.25, false);
                            var seta = fx.Id == "sobe" ? SetaSobe : SetaDesce;
                            R(seta, COR_ELE);
                            for (int i = 0; i < 4; i++) L(seta[i][0], seta[i][1], seta[(i + 1) % 4][0], seta[(i + 1) % 4][1], false);
                            break;
                        case "teto":   // tracejado 3 mm / 3 mm
                            L(-h, 0, -4.5, 0, true); L(-1.5, 0, 1.5, 0, true); L(4.5, 0, h, 0, true);
                            break;
                        case "piso":   // traço longo - curto - longo
                            L(-h, 0, -1.86, 0, true); L(-0.24, 0, 1.13, 0, true); L(2.62, 0, h, 0, true);
                            break;
                        case "eletrocalha":
                            double e = 1.735;
                            R(new[] { new[] { -h, -e }, new[] { h, -e }, new[] { h, e }, new[] { -h, e } }, COR_ELETROCALHA);
                            L(-h, e, h, e, true); L(-h, -e, h, -e, true);
                            break;
                        default:
                            throw new Exception("símbolo fixo desconhecido: " + fx.Id);
                    }
                    t.Commit();
                }
                Salvar(fdoc, doc, pasta, nome);
            }
            finally { fdoc.Close(false); }
        }

        /// <summary>Tipo de região preenchida sólida na cor, na família (null se o modelo não tiver nenhum para copiar).</summary>
        static ElementId TipoSolido(Document fdoc, byte[] rgb)
        {
            string nome = $"FJA - SOLIDO {rgb[0]:000}-{rgb[1]:000}-{rgb[2]:000}";
            var tipos = new FilteredElementCollector(fdoc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
            var achado = tipos.FirstOrDefault(x => x.Name == nome);
            if (achado != null) return achado.Id;
            if (tipos.Count == 0) return null;
            var solido = new FilteredElementCollector(fdoc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(fp => { try { var p = fp.GetFillPattern(); return p.IsSolidFill && p.Target == FillPatternTarget.Drafting; } catch { return false; } });
            if (solido == null) return null;
            var novo = tipos[0].Duplicate(nome) as FilledRegionType;
            novo.ForegroundPatternId = solido.Id;
            novo.ForegroundPatternColor = new Color(rgb[0], rgb[1], rgb[2]);
            novo.BackgroundPatternId = ElementId.InvalidElementId;
            try { novo.IsMasking = false; } catch { }
            return novo.Id;
        }

        public static string NomeLegExtra(Disciplina d, FamilySymbol sym)
        {
            string fam = Regex.Replace(sym.FamilyName.Trim(), @"^\([A-Z]+\)\s*", "").ToUpperInvariant();
            bool varios = sym.Family != null && sym.Family.GetFamilySymbolIds().Count > 1;
            return d.Prefixo + " (LEG) " + fam + (varios ? " - " + sym.Name.Trim().ToUpperInvariant() : "");
        }

        public static Family ViaDwg(Document doc, FamilySymbol sym, string nome, string pasta, double tamMm, Item it)
        {
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting) ?? throw new Exception("o projeto não tem tipo de vista de desenho");
            Directory.CreateDirectory(pasta);
            string nomeDwg = "leg_" + Regex.Replace(Texto.Norm(nome), "[^A-Za-z0-9]+", "_").Trim('_');
            string caminho = Path.Combine(pasta, nomeDwg + ".dwg");
            try { if (File.Exists(caminho)) File.Delete(caminho); } catch { }
            ViewDrafting v;
            using (var t = new Transaction(doc, "Símbolo de legenda (DWG)"))
            {
                t.Start();
                v = ViewDrafting.Create(doc, vft.Id);
                v.Scale = 1;
                if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }
                doc.Create.NewFamilyInstance(XYZ.Zero, sym, v);
                t.Commit();
            }
            try
            {
                var opts = new DWGExportOptions();
                try { opts.TargetUnit = ExportUnit.Millimeter; } catch { }
                doc.Export(pasta, nomeDwg, new List<ElementId> { v.Id }, opts);
            }
            finally
            {
                using (var t = new Transaction(doc, "Símbolo de legenda (DWG)"))
                {
                    t.Start();
                    doc.Delete(v.Id);
                    t.Commit();
                }
            }
            if (!File.Exists(caminho)) throw new Exception("a exportação para DWG não gerou o arquivo");

            var app = doc.Application;
            Document fdoc = app.NewFamilyDocument(ModeloAnotacao(app) ?? throw new Exception("modelo de família 'Anotação genérica' não encontrado"));
            double m;
            try
            {
                using (var t = ResolverFalhas.Iniciar(fdoc, "Simbolo de legenda (DWG)"))
                {
                    View view = VistaDaFamilia(fdoc);
                    foreach (var tn in new FilteredElementCollector(fdoc).OfClass(typeof(TextNote)).ToElementIds().ToList())
                        fdoc.Delete(tn);
                    double alvo = tamMm / 304.8;
                    Func<double, Element> importar = esc =>
                    {
                        var o = new DWGImportOptions
                        {
                            Placement = ImportPlacement.Centered, ThisViewOnly = true,
                            ColorMode = ImportColorMode.BlackAndWhite, Unit = ImportUnit.Custom, CustomScale = esc,
                        };
                        fdoc.Import(caminho, o, view, out ElementId id);
                        fdoc.Regenerate();
                        return fdoc.GetElement(id);
                    };
                    Func<Element, double> medida = e =>
                    {
                        var bb = e?.get_BoundingBox(view);
                        return bb != null ? Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y) : 0.0;
                    };
                    // a unidade do DWG e o significado exato da escala variam: importa, mede e corrige
                    double escala = 1.0 / 304.8;
                    Element imp = importar(escala);
                    m = medida(imp);
                    for (int i = 0; i < 3; i++)
                    {
                        if (m <= 1e-12) throw new Exception("o DWG importado ficou vazio");
                        if (Math.Abs(m - alvo) / alvo < 0.01) break;
                        escala *= alvo / m;
                        fdoc.Delete(imp.Id);
                        imp = importar(escala);
                        m = medida(imp);
                    }
                    t.Commit();
                }
                Salvar(fdoc, doc, pasta, nome);
            }
            finally { fdoc.Close(false); }
            it.Redimensionado = true;
            return FamiliaPorNome(doc, nome);
        }
    }
}
