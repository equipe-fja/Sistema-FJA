using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Cotagem
{
    /// <summary>
    /// Número das cotas criadas desviado de outros elementos da vista (2.26.0, pedido da FJA em 13/11/2026), sem
    /// perder a legibilidade. Para cada trecho, o retângulo do texto (altura pelo tipo de cota × escala, largura pelo
    /// número de caracteres) é testado contra os obstáculos: textos das outras cotas (existentes e novas), etiquetas,
    /// textos, símbolos de anotação, portas, janelas e pilares. Posições tentadas, nesta ordem: a do Revit; o outro
    /// lado da linha; deslocado ao longo do trecho (sem sair dele), dos dois lados; e, em trecho curto, além das
    /// pontas. A primeira livre é usada; sem nenhuma, fica a do Revit (contada no relatório).
    /// </summary>
    static class Textos
    {
        sealed class Caixa
        {
            public double X0, X1, Y0, Y1;
            public object Dono;
            public bool Cruza(Caixa o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
        }

        /// <summary>Um texto de cota: o trecho (ou a cota de um trecho só), com o que é preciso para movê-lo.</summary>
        sealed class Texto
        {
            public Dimension Dim;
            public DimensionSegment Seg;   // null: cota sem segmentos
            public XYZ Pos;                // posição atual do texto
            public double Larg, Alt;       // tamanho do texto no modelo
            public bool Hor;               // linha da cota horizontal na tela
            public double A, B, Linha;     // extensão do trecho ao longo da linha e posição da linha
            public Caixa Caixa;
        }

        public static void Ajustar(Document doc, View v, ResultadoVista res, XYZ R, XYZ U)
        {
            try { doc.Regenerate(); } catch { }
            var obst = Obstaculos(doc, v, R, U);
            var novas = new HashSet<ElementId>(res.Novas.Select(d => d.Id));

            // textos de todas as cotas da vista (as outras também são obstáculos)
            var todos = new List<Texto>();
            foreach (Dimension d in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)))
                todos.AddRange(TextosDa(d, v, R, U));
            foreach (var t in todos) obst.Add(t.Caixa);

            foreach (var t in todos.Where(x => novas.Contains(x.Dim.Id)))
            {
                if (!obst.Any(o => !ReferenceEquals(o, t.Caixa) && o.Cruza(t.Caixa))) continue;
                bool movido = false;
                foreach (var p in Candidatos(t))
                {
                    var c = CaixaEm(t, p, R, U);
                    if (obst.Any(o => !ReferenceEquals(o, t.Caixa) && o.Cruza(c))) continue;
                    if (!Mover(t, p)) break;
                    t.Caixa.X0 = c.X0; t.Caixa.X1 = c.X1; t.Caixa.Y0 = c.Y0; t.Caixa.Y1 = c.Y1;
                    t.Pos = p; movido = true;
                    break;
                }
                if (movido) res.TextosMovidos++; else res.TextosSemLugar++;
            }
        }

        // ------------------------------------------------------------ textos e caixas

        static IEnumerable<Texto> TextosDa(Dimension d, View v, XYZ R, XYZ U)
        {
            var r = new List<Texto>();
            try
            {
                if (d is SpotDimension || d.DimensionShape != DimensionShape.Linear || !(d.Curve is Line ln)) return r;
                XYZ dir = ln.Direction;
                bool hor = Math.Abs(dir.DotProduct(R)) >= Math.Abs(dir.DotProduct(U));
                double alt = AlturaTexto(d, v);
                XYZ eixo = hor ? R : U, outro = hor ? U : R;
                double linha = d.Origin.DotProduct(outro);
                if (d.NumberOfSegments == 0)
                {
                    if (d.Value == null) return r;
                    var t = Novo(d, null, d.TextPosition, d.ValueString, d.Origin.DotProduct(eixo), d.Value.Value, alt, hor, linha, R, U);
                    if (t != null) r.Add(t);
                }
                else
                    foreach (DimensionSegment s in d.Segments)
                    {
                        if (s.Value == null) continue;
                        var t = Novo(d, s, s.TextPosition, s.ValueString, s.Origin.DotProduct(eixo), s.Value.Value, alt, hor, linha, R, U);
                        if (t != null) r.Add(t);
                    }
            }
            catch { }
            return r;
        }

        static Texto Novo(Dimension d, DimensionSegment s, XYZ pos, string txt, double centro, double valor, double alt,
                          bool hor, double linha, XYZ R, XYZ U)
        {
            if (pos == null) return null;
            var t = new Texto
            {
                Dim = d, Seg = s, Pos = pos, Alt = alt * 1.15, Hor = hor, Linha = linha,
                Larg = Math.Max(1, (txt ?? "").Length) * alt * 0.62 + alt * 0.3,
                A = centro - valor / 2, B = centro + valor / 2,
            };
            t.Caixa = CaixaEm(t, pos, R, U);
            t.Caixa.Dono = d;
            return t;
        }

        /// <summary>Retângulo do texto centrado em p (texto ao longo da linha da cota).</summary>
        static Caixa CaixaEm(Texto t, XYZ p, XYZ R, XYZ U)
        {
            double x = p.DotProduct(R), y = p.DotProduct(U);
            double hx = (t.Hor ? t.Larg : t.Alt) / 2, hy = (t.Hor ? t.Alt : t.Larg) / 2;
            return new Caixa { X0 = x - hx, X1 = x + hx, Y0 = y - hy, Y1 = y + hy };
        }

        /// <summary>Altura do texto no modelo: tamanho do texto do tipo de cota (papel) × escala da vista.</summary>
        static double AlturaTexto(Dimension d, View v)
        {
            double papel = 2.0 / 304.8;
            try
            {
                var p = d.DimensionType?.get_Parameter(BuiltInParameter.TEXT_SIZE);
                if (p != null && p.AsDouble() > 0) papel = p.AsDouble();
            }
            catch { }
            return papel * Math.Max(1, v.Scale);
        }

        // ------------------------------------------------------------ posições

        static IEnumerable<XYZ> Candidatos(Texto t)
        {
            XYZ p0 = t.Pos;
            XYZ ao = t.Dim.Curve is Line ln ? ln.Direction : XYZ.BasisX;     // ao longo da linha da cota
            XYZ nrm = XYZ.BasisZ.CrossProduct(ao).Normalize();              // perpendicular, no plano da vista
            double lado = (p0 - t.Dim.Origin).DotProduct(nrm);              // de que lado da linha o texto está
            double off = Math.Max(Math.Abs(lado), t.Alt * 0.6);
            double s = lado >= 0 ? 1 : -1;
            XYZ outroLado = p0 - nrm * (s * 2 * off);
            yield return outroLado;

            // ao longo do trecho, sem sair dele
            double meio = (t.A + t.B) / 2, folga = (t.B - t.A - t.Larg) / 2;
            double passo = Math.Max(t.Larg * 0.5, t.Alt);
            if (folga > 0)
                for (double dd = passo; dd <= folga + 1e-9; dd += passo)
                    foreach (double sg in new[] { 1.0, -1.0 })
                    {
                        yield return p0 + ao * (sg * dd);
                        yield return outroLado + ao * (sg * dd);
                    }
            // trecho curto: além das pontas (o Revit liga com chamada, conforme o tipo de cota)
            double fora = (t.B - t.A) / 2 + t.Larg / 2 + t.Alt * 0.4;
            foreach (double sg in new[] { 1.0, -1.0 })
            {
                yield return p0 + ao * (sg * fora);
                yield return outroLado + ao * (sg * fora);
            }
        }

        static bool Mover(Texto t, XYZ p)
        {
            try
            {
                if (t.Seg != null)
                {
                    if (!t.Seg.IsTextPositionAdjustable()) return false;
                    t.Seg.TextPosition = p;
                }
                else
                {
                    if (!t.Dim.IsTextPositionAdjustable()) return false;
                    t.Dim.TextPosition = p;
                }
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ obstáculos

        static readonly BuiltInCategory[] Categorias =
        {
            BuiltInCategory.OST_RoomTags, BuiltInCategory.OST_AreaTags, BuiltInCategory.OST_DoorTags, BuiltInCategory.OST_WindowTags,
            BuiltInCategory.OST_GenericAnnotation, BuiltInCategory.OST_TextNotes, BuiltInCategory.OST_KeynoteTags,
            BuiltInCategory.OST_MultiCategoryTags, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_SpotElevations,
        };

        static List<Caixa> Obstaculos(Document doc, View v, XYZ R, XYZ U)
        {
            var r = new List<Caixa>();
            var filtro = new ElementMulticategoryFilter(Categorias.ToList());
            foreach (var e in new FilteredElementCollector(doc, v.Id).WherePasses(filtro).WhereElementIsNotElementType())
            {
                try
                {
                    var bb = e.get_BoundingBox(v);
                    if (bb == null) continue;
                    double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                    foreach (var p in new[] { bb.Min, bb.Max, new XYZ(bb.Min.X, bb.Max.Y, 0), new XYZ(bb.Max.X, bb.Min.Y, 0) })
                    {
                        double x = p.DotProduct(R), y = p.DotProduct(U);
                        x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    }
                    r.Add(new Caixa { X0 = x0, X1 = x1, Y0 = y0, Y1 = y1, Dono = e });
                }
                catch { }
            }
            return r;
        }
    }
}
