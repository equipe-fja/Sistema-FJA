using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FJA.Revit.Cotagem;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Grids, Níveis e Cortes › Estender/Alinhar (2.25.0). Como o Aparar/Estender do Revit, para eixos, níveis e
    /// cortes: primeiro a linha de referência (linha de detalhe ou de modelo, eixo, nível, plano de referência ou
    /// corte); depois cada eixo, nível ou corte clicado tem a ponta levada até a reta da referência — se a reta
    /// cruza o elemento, fica o lado clicado e a outra ponta é aparada; se não cruza, a ponta mais próxima é
    /// estendida. Eixos, níveis e cortes já selecionados ao abrir são alinhados de uma vez (ponta mais próxima).
    /// Cada ponta muda no tipo de extensão em que já está (3D ou 2D), sem trocar o tipo; no corte, muda a largura
    /// (recorte da vista do corte). Desde 2.26.8 os cortes não são alvo (a API não move as cabeças da linha do
    /// corte, só o recorte, que não muda o desenho na planta); continuam aceitos como referência. AplicarCorte
    /// fica no código para quando houver como. Ver docs/grids-niveis-cortes.md.
    /// </summary>
    public class EstenderAlinhar : ComandoFJA
    {
        const string Titulo = "Estender/Alinhar";
        const double TOL = 1.0 / 304.8;   // 1 mm
        const string JaNaLinha = "já está na linha de referência";
        const string CortesFora = "Cortes não são ajustados: o Revit não dá acesso às pontas da linha do corte (cabeças), só à largura do recorte, que não muda o desenho na planta. Um corte ainda pode servir de linha de referência.";

        /// <summary>Reta na vista (coordenadas da tela: X = RightDirection, Y = UpDirection).</summary>
        struct Reta { public double Ax, Ay, Dx, Dy; }

        sealed class FiltroLimite : ISelectionFilter
        {
            public bool AllowElement(Element e) =>
                (e is CurveElement ce && ce.GeometryCurve is Line) || (e is Grid g && !g.IsCurved) || e is Level ||
                e is ReferencePlane || EhMarcador(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        sealed class FiltroAlvo : ISelectionFilter
        {
            public ElementId Limite;
            public bool AllowElement(Element e) =>
                e.Id != Limite && ((e is Grid g && !g.IsCurved) || e is Level);   // cortes fora desde 2.26.8
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        /// <summary>Corte: o marcador na planta (OST_Viewers), a categoria Cortes ou a própria vista de corte
        /// (2.26.1: no clique o Revit pode entregar o corte de outra forma que na seleção normal).</summary>
        static bool EhMarcador(Element e)
        {
            if (e is ViewSection vs) return vs.ViewType == ViewType.Section && !vs.IsTemplate;
            try
            {
                var c = e.Category?.BuiltInCategory;
                return c == BuiltInCategory.OST_Viewers || c == BuiltInCategory.OST_Sections;
            }
            catch { return false; }
        }

        /// <summary>Classe e categoria do elemento, para dizer o que foi clicado quando não é aceito.</summary>
        static string Descrever(Element e)
        {
            string cat = "";
            try { cat = e.Category != null ? e.Category.Name : "sem categoria"; } catch { }
            return "\"" + e.Name + "\" (" + e.GetType().Name + ", " + cat + ")";
        }

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            View v = doc.ActiveView;
            if (v == null || v is ViewSheet || v.ViewType == ViewType.ThreeD || v.ViewType == ViewType.Schedule)
            {
                TaskDialog.Show(Titulo, "Abra uma planta, corte ou elevação.");
                return Result.Cancelled;
            }
            XYZ R = v.RightDirection, U = v.UpDirection;
            var fAlvo = new FiltroAlvo();
            var pre = uidoc.Selection.GetElementIds().Select(i => doc.GetElement(i)).Where(e => e != null && !(e is View) && fAlvo.AllowElement(e)).ToList();   // vistas do navegador não entram

            // 1) linha de referência
            Reta lim;
            ElementId idLim;
            while (true)
            {
                Reference r;
                try { r = uidoc.Selection.PickObject(ObjectType.Element, Titulo + ": clique na linha de referência (Esc para sair)"); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                var e = doc.GetElement(r);
                if (e == null) continue;
                if (!new FiltroLimite().AllowElement(e)) { TaskDialog.Show(Titulo, "Não serve como referência: " + Descrever(e) + ". Use uma linha reta, eixo, nível, plano de referência ou corte."); continue; }
                string erro = LerLimite(doc, v, e, R, U, out lim);
                if (erro == null) { idLim = e.Id; break; }
                TaskDialog.Show(Titulo, erro);
            }
            fAlvo.Limite = idLim;

            // 2) os já selecionados, de uma vez
            if (pre.Count > 0)
            {
                var erros = new List<string>();
                int feitos = 0;
                using (var t = new Transaction(doc, Titulo))
                {
                    t.Start();
                    foreach (var e in pre.Where(x => x.Id != idLim))
                        using (var st = new SubTransaction(doc))
                        {
                            st.Start();
                            string erro = Aplicar(doc, v, e, lim, null, R, U);
                            if (erro == null) { st.Commit(); feitos++; } else { st.RollBack(); erros.Add(e.Name + ": " + erro); }
                        }
                    if (feitos > 0) { t.Commit(); uidoc.RefreshActiveView(); } else t.RollBack();
                }
                if (erros.Count > 0) TaskDialog.Show(Titulo, string.Join("\n", erros));
            }

            // 3) um a um, como o Aparar/Estender do Revit (Esc termina). Sem filtro no clique (2.26.1): o filtro
            // recusava o corte sem aviso; agora qualquer clique responde e o que não serve é explicado.
            while (true)
            {
                Reference r;
                try { r = uidoc.Selection.PickObject(ObjectType.Element, Titulo + ": clique no eixo ou nível, do lado que fica (Esc para terminar)"); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                var e = doc.GetElement(r);
                if (e == null) continue;
                if (e.Id == idLim) { TaskDialog.Show(Titulo, "Esse é a própria linha de referência. Clique num eixo ou nível."); continue; }
                if (EhMarcador(e)) { TaskDialog.Show(Titulo, CortesFora); continue; }
                if (!fAlvo.AllowElement(e)) { TaskDialog.Show(Titulo, "Não é eixo nem nível: " + Descrever(e) + "."); continue; }
                using (var t = new Transaction(doc, Titulo))
                {
                    t.Start();
                    string erro;
                    try { erro = Aplicar(doc, v, e, lim, r.GlobalPoint, R, U); }
                    catch (Exception ex) { erro = ex.Message; }
                    if (erro == null) { t.Commit(); uidoc.RefreshActiveView(); }
                    else { t.RollBack(); TaskDialog.Show(Titulo, e.Name + ": " + erro); }
                }
            }
            return Result.Succeeded;
        }

        // ------------------------------------------------------------ geometria na tela

        static void Tela(XYZ p, XYZ R, XYZ U, out double x, out double y) { x = p.DotProduct(R); y = p.DotProduct(U); }

        static string RetaDe(XYZ p0, XYZ p1, XYZ R, XYZ U, out Reta r)
        {
            Tela(p0, R, U, out double x0, out double y0);
            Tela(p1, R, U, out double x1, out double y1);
            r = new Reta { Ax = x0, Ay = y0, Dx = x1 - x0, Dy = y1 - y0 };
            return Math.Sqrt(r.Dx * r.Dx + r.Dy * r.Dy) < TOL ? "a linha aparece como um ponto nesta vista" : null;
        }

        /// <summary>Parâmetro t (0 em p0, 1 em p1) onde o segmento p0–p1, prolongado, cruza a reta. null se paralelos.</summary>
        static double? Cruzamento(XYZ p0, XYZ p1, Reta lim, XYZ R, XYZ U)
        {
            Tela(p0, R, U, out double ax, out double ay);
            Tela(p1, R, U, out double bx, out double by);
            double dx = bx - ax, dy = by - ay;
            double den = dx * lim.Dy - dy * lim.Dx;
            double n1 = Math.Sqrt(dx * dx + dy * dy), n2 = Math.Sqrt(lim.Dx * lim.Dx + lim.Dy * lim.Dy);
            if (n1 < TOL || Math.Abs(den) < 1e-6 * n1 * n2) return null;
            return ((lim.Ax - ax) * lim.Dy - (lim.Ay - ay) * lim.Dx) / den;
        }

        static double Param(XYZ p0, XYZ p1, XYZ q, XYZ R, XYZ U)
        {
            Tela(p0, R, U, out double ax, out double ay);
            Tela(p1, R, U, out double bx, out double by);
            Tela(q, R, U, out double qx, out double qy);
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            return l2 < 1e-12 ? 0 : ((qx - ax) * dx + (qy - ay) * dy) / l2;
        }

        /// <summary>Qual ponta vai até o cruzamento t: 0 (início) ou 1 (fim). null se já está lá.</summary>
        static int? Ponta(double t, double? clique, double comp)
        {
            double tol = comp > 0 ? TOL / comp : 1e-6;
            if (t > tol && t < 1 - tol)
            {
                // a reta cruza: fica o lado clicado (sem clique, a ponta mais próxima é aparada)
                if (clique != null) return clique.Value > t ? 0 : 1;
                return t < 0.5 ? 0 : 1;
            }
            if (t <= 0) return Math.Abs(t) < tol ? (int?)null : 0;
            return Math.Abs(t - 1) < tol ? (int?)null : 1;
        }

        // ------------------------------------------------------------ referência

        static string LerLimite(Document doc, View v, Element e, XYZ R, XYZ U, out Reta r)
        {
            r = default(Reta);
            switch (e)
            {
                case CurveElement ce:
                    if (!(ce.GeometryCurve is Line l)) return "Escolha uma linha reta.";
                    return RetaDe(l.GetEndPoint(0), l.GetEndPoint(1), R, U, out r);
                case DatumPlane dp when !(dp is Grid g0 && g0.IsCurved):
                    var lv = Extensao2D.Visivel(dp, v);
                    if (lv == null) return "Não foi possível ler a linha desse elemento nesta vista.";
                    return RetaDe(lv.GetEndPoint(0), lv.GetEndPoint(1), R, U, out r);
                case ReferencePlane rp:
                    return RetaDe(rp.BubbleEnd, rp.FreeEnd, R, U, out r);
            }
            if (EhMarcador(e))
            {
                var vs = SecaoDoMarcador(doc, e);
                if (vs == null) return "Só cortes (não chamadas nem elevações).";
                Extremos(vs.CropBox, out XYZ a, out XYZ b);
                return RetaDe(a, b, R, U, out r);
            }
            return "Escolha uma linha, eixo, nível, plano de referência ou corte.";
        }

        // ------------------------------------------------------------ aplicação

        static string Aplicar(Document doc, View v, Element e, Reta lim, XYZ clique, XYZ R, XYZ U)
        {
            if (e is DatumPlane dp) return AplicarDatum(dp, v, lim, clique, R, U);
            if (EhMarcador(e)) return CortesFora;
            return "elemento não aceito";
        }

        /// <summary>Eixo ou nível: a ponta escolhida vai até a reta, no tipo de extensão em que ela já está.</summary>
        static string AplicarDatum(DatumPlane dp, View v, Reta lim, XYZ clique, XYZ R, XYZ U)
        {
            if (dp is Grid g && g.IsCurved) return "eixo curvo";
            var vis = Extensao2D.Visivel(dp, v);
            if (vis == null) return "não aparece nesta vista";
            XYZ v0 = vis.GetEndPoint(0), v1 = vis.GetEndPoint(1);
            var t = Cruzamento(v0, v1, lim, R, U);
            if (t == null) return "é paralelo à linha de referência";
            var qual = Ponta(t.Value, clique != null ? Param(v0, v1, clique, R, U) : (double?)null, vis.Length);
            if (qual == null) return JaNaLinha;
            var ponta = qual == 0 ? DatumEnds.End0 : DatumEnds.End1;

            var tipo = dp.GetDatumExtentTypeInView(ponta, v);
            var c = dp.GetCurvesInView(tipo, v)?.OfType<Line>().FirstOrDefault();
            if (c == null) return "não foi possível ler a extensão";
            XYZ c0 = c.GetEndPoint(0), c1 = c.GetEndPoint(1);
            var tc = Cruzamento(c0, c1, lim, R, U);
            if (tc == null) return "é paralelo à linha de referência";
            XYZ np = c0 + (c1 - c0) * tc.Value;
            XYZ a = qual == 0 ? np : c0, b = qual == 0 ? c1 : np;
            if (a.DistanceTo(b) < 10 * TOL) return "ficaria curto demais";
            if ((b - a).DotProduct(c1 - c0) <= 0) return "a linha de referência passa além da outra ponta";
            dp.SetCurveInView(tipo, v, Line.CreateBound(a, b));
            return null;
        }

        /// <summary>Corte: a largura (recorte da vista do corte) vai até a reta.</summary>
        static string AplicarCorte(Document doc, Element marcador, Reta lim, XYZ clique, XYZ R, XYZ U)
        {
            var vs = SecaoDoMarcador(doc, marcador);
            if (vs == null) return "só cortes (não chamadas nem elevações)";
            var cb = vs.CropBox;
            Extremos(cb, out XYZ p0, out XYZ p1);
            var t = Cruzamento(p0, p1, lim, R, U);
            if (t == null) return "é paralelo à linha de referência";
            double w = cb.Max.X - cb.Min.X;
            var qual = Ponta(t.Value, clique != null ? Param(p0, p1, clique, R, U) : (double?)null, w);
            if (qual == null) return JaNaLinha;
            double nx = cb.Min.X + t.Value * w;
            if (qual == 0)
            {
                if (cb.Max.X - nx < 10 * TOL) return "a linha de referência passa além da outra ponta";
                cb.Min = new XYZ(nx, cb.Min.Y, cb.Min.Z);
            }
            else
            {
                if (nx - cb.Min.X < 10 * TOL) return "a linha de referência passa além da outra ponta";
                cb.Max = new XYZ(nx, cb.Max.Y, cb.Max.Z);
            }
            vs.CropBox = cb;
            return null;
        }

        /// <summary>Pontas da linha do corte (no plano do corte, z = 0 da caixa), em coordenadas do modelo.</summary>
        static void Extremos(BoundingBoxXYZ cb, out XYZ a, out XYZ b)
        {
            var T = cb.Transform;
            a = T.OfPoint(new XYZ(cb.Min.X, 0, 0));
            b = T.OfPoint(new XYZ(cb.Max.X, 0, 0));
        }

        /// <summary>A vista de corte de um marcador (o marcador tem o nome da vista).</summary>
        static ViewSection SecaoDoMarcador(Document doc, Element marcador)
        {
            if (marcador is ViewSection proprio) return proprio.ViewType == ViewType.Section && !proprio.IsTemplate ? proprio : null;
            string nome = null;
            try { nome = marcador.get_Parameter(BuiltInParameter.VIEW_NAME)?.AsString(); } catch { }
            if (string.IsNullOrEmpty(nome)) nome = marcador.Name;
            return new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                .FirstOrDefault(x => !x.IsTemplate && x.ViewType == ViewType.Section && x.Name == nome);
        }
    }
}
