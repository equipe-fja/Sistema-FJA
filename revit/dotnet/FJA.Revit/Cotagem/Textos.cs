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
    /// textos, símbolos de anotação, portas, janelas, pilares, paredes, escadas, mobiliário e louças, e as linhas das
    /// outras cotas e dos eixos — cada um com um peso (2.26.4). Posições tentadas: a do Revit; o outro
    /// lado da linha; deslocado ao longo do trecho (sem sair dele), dos dois lados; e, em trecho curto, além das
    /// pontas. Fica a de menor nota (área sobreposta × peso); sem nenhuma livre, a que atrapalha menos (contada no relatório).
    /// </summary>
    static class Textos
    {
        sealed class Caixa
        {
            public double X0, X1, Y0, Y1;
            public object Dono;
            public double Peso = P_FORTE;   // quanto incomoda sobrepor (ver os pesos abaixo)
            public bool Cruza(Caixa o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
            /// <summary>Fração da área desta caixa coberta por o.</summary>
            public double Fracao(Caixa o)
            {
                double w = Math.Min(X1, o.X1) - Math.Max(X0, o.X0), h = Math.Min(Y1, o.Y1) - Math.Max(Y0, o.Y0);
                if (w <= 0 || h <= 0) return 0;
                return w * h / Math.Max((X1 - X0) * (Y1 - Y0), 1e-12);
            }
        }

        // pesos (pedido da FJA, 13/11/2026: evitar ao máximo números sobre outros desenhos)
        const double P_FORTE = 10;   // textos, etiquetas, paredes, escadas, pilares, portas
        const double P_MEDIO = 4;    // mobiliário, louças, equipamentos
        const double P_LINHA = 2;    // linha de outra cota
        const double P_EIXO = 1;     // linha de eixo

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
            foreach (var t in todos)
            {
                obst.Add(t.Caixa);
                obst.Add(LinhaDa(t, R, U));   // a linha da cota (a do próprio texto é ignorada abaixo)
            }

            foreach (var t in todos.Where(x => novas.Contains(x.Dim.Id)))
            {
                double atual = Nota(t, t.Caixa, obst);
                if (atual <= 0) continue;
                // o menor movimento que resolve (pedido da FJA, 13/11/2026): nota = sobreposição × peso + distância
                // percorrida (em larguras do texto); só move se a sobreposição cair pelo menos 20 %
                XYZ melhor = null; Caixa cm = null; double nm = atual, total = double.MaxValue;
                foreach (var p in Candidatos(t))
                {
                    var c = CaixaEm(t, p, R, U);
                    double n = Nota(t, c, obst);
                    if (n > atual * 0.8) continue;
                    double dist = p.DistanceTo(t.Pos) / Math.Max(t.Larg, 1e-6);
                    double tot = n + PESO_DISTANCIA * dist;
                    if (tot < total - 1e-9) { total = tot; nm = n; melhor = p; cm = c; }
                }
                if (melhor != null && Mover(t, melhor))
                {
                    t.Caixa.X0 = cm.X0; t.Caixa.X1 = cm.X1; t.Caixa.Y0 = cm.Y0; t.Caixa.Y1 = cm.Y1;
                    t.Pos = melhor;
                    SemLeader(t.Dim);
                    res.TextosMovidos++;
                    if (nm > 0) res.TextosSemLugar++;
                }
                else res.TextosSemLugar++;
            }
        }

        const double PESO_DISTANCIA = 3;   // cada largura de texto percorrida vale ~ 30 % de sobreposição com algo forte

        /// <summary>Sem o traço de chamada (leader) entre o número deslocado e a cota: parâmetro "Leader" da cota.</summary>
        static void SemLeader(Dimension d)
        {
            try
            {
                var p = d.get_Parameter(BuiltInParameter.DIM_LEADER);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer && p.AsInteger() != 0) p.Set(0);
            }
            catch { }
        }

        /// <summary>Soma de (fração sobreposta × peso) com tudo, menos o próprio texto e a linha da própria cota.</summary>
        static double Nota(Texto t, Caixa c, List<Caixa> obst)
        {
            double n = 0;
            foreach (var o in obst)
            {
                if (ReferenceEquals(o, t.Caixa) || (o.Peso == P_LINHA && ReferenceEquals(o.Dono, t.Dim))) continue;
                if (!c.Cruza(o)) continue;
                n += c.Fracao(o) * o.Peso + 0.05 * o.Peso;   // qualquer toque conta um pouco
            }
            return n;
        }

        /// <summary>A linha de cota de um trecho, como faixa fina (espessura de 30 % da altura do texto).</summary>
        static Caixa LinhaDa(Texto t, XYZ R, XYZ U)
        {
            double e = t.Alt * 0.15;
            return t.Hor
                ? new Caixa { X0 = t.A, X1 = t.B, Y0 = t.Linha - e, Y1 = t.Linha + e, Dono = t.Dim, Peso = P_LINHA }
                : new Caixa { X0 = t.Linha - e, X1 = t.Linha + e, Y0 = t.A, Y1 = t.B, Dono = t.Dim, Peso = P_LINHA };
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
            double passo = Math.Max(t.Larg * 0.25, t.Alt * 0.5);
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

        static readonly BuiltInCategory[] Fortes =
        {
            BuiltInCategory.OST_RoomTags, BuiltInCategory.OST_AreaTags, BuiltInCategory.OST_DoorTags, BuiltInCategory.OST_WindowTags,
            BuiltInCategory.OST_GenericAnnotation, BuiltInCategory.OST_TextNotes, BuiltInCategory.OST_KeynoteTags,
            BuiltInCategory.OST_MultiCategoryTags, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_SpotElevations,
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsRailing,
        };
        static readonly BuiltInCategory[] Medios =
        {
            BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureSystems, BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_GenericModel,
        };

        static List<Caixa> Obstaculos(Document doc, View v, XYZ R, XYZ U)
        {
            var r = new List<Caixa>();
            Coletar(doc, v, R, U, Fortes, P_FORTE, r);
            Coletar(doc, v, R, U, Medios, P_MEDIO, r);
            // linhas de eixo (faixas finas): passar sobre elas incomoda pouco, mas conta
            double e = 0.6 / 304.8 * Math.Max(1, v.Scale);
            foreach (Grid g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)))
            {
                try
                {
                    var c = g.GetCurvesInView(DatumExtentType.ViewSpecific, v)?.OfType<Line>().FirstOrDefault();
                    if (c == null) continue;
                    XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1);
                    double x0 = Math.Min(a.DotProduct(R), b.DotProduct(R)), x1 = Math.Max(a.DotProduct(R), b.DotProduct(R));
                    double y0 = Math.Min(a.DotProduct(U), b.DotProduct(U)), y1 = Math.Max(a.DotProduct(U), b.DotProduct(U));
                    r.Add(new Caixa { X0 = x0 - e, X1 = x1 + e, Y0 = y0 - e, Y1 = y1 + e, Dono = g, Peso = P_EIXO });
                }
                catch { }
            }
            return r;
        }

        static void Coletar(Document doc, View v, XYZ R, XYZ U, BuiltInCategory[] cats, double peso, List<Caixa> r)
        {
            var filtro = new ElementMulticategoryFilter(cats.ToList());
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
                    r.Add(new Caixa { X0 = x0, X1 = x1, Y0 = y0, Y1 = y1, Dono = e, Peso = peso });
                }
                catch { }
            }
        }
    }
}
