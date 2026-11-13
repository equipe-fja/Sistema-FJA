using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Cotagem
{
    /// <summary>Face plana vertical de parede (ou referência de porta/janela), perpendicular a um dos eixos da vista.</summary>
    sealed class Plano
    {
        public Reference Ref;
        public int Eixo;          // 0: normal paralela a R (posição medida em X); 1: paralela a U (medida em Y)
        public double Pos;        // posição ao longo do eixo
        public double A, B;       // extensão no outro eixo
        public double Z0, Z1;
    }

    /// <summary>Porta ou janela numa parede: vão livre (referências Esquerda/Direita da família).</summary>
    sealed class Vao
    {
        public double Esq, Dir;   // posições ao longo da parede (Esq < Dir)
        public Reference RefEsq, RefDir;
    }

    /// <summary>Parede como retângulo na vista (Lo/Hi por eixo: 0 = X, 1 = Y).</summary>
    sealed class Parede
    {
        public int Dir;           // 0: paralela a R (horizontal na tela); 1: paralela a U
        public double[] Lo = new double[2], Hi = new double[2];
        public List<Vao> Vaos = new List<Vao>();
    }

    /// <summary>Paredes e faces lidas de uma planta (cortadas pelo plano de corte).</summary>
    sealed class Leitura
    {
        public List<Parede> Paredes = new List<Parede>();
        public List<Plano> Planos = new List<Plano>();
        public int VaosSemReferencia;
    }

    /// <summary>
    /// Planta baixa: fachadas. Na 1:75, em cada um dos quatro lados, a cadeia de panos e vãos (vão livre das portas e
    /// janelas, pontas de parede e cantos) e o total; na 1:200, só o total do contorno. Ver docs/cotagem.md.
    /// </summary>
    static class Fachada
    {
        const double TOL = Motor.TOL;
        const double PAR = 0.999;
        const double FOLGA_BATENTE = 120 / 304.8;   // ombreira da parede até 12 cm do vão livre = mesmo vão

        /// <summary>Lados: (eixo ao longo da cadeia, sentido para fora). 0: em cima, 1: embaixo, 2: esquerda, 3: direita.</summary>
        internal static readonly (int k, int s, string nome)[] Lados =
        {
            (0, 1, "fachada-cima"), (0, -1, "fachada-baixo"), (1, -1, "fachada-esquerda"), (1, 1, "fachada-direita"),
        };

        /// <summary>Cota as fachadas. Devolve, por lado, a linha de cota mais externa (null se o lado não tem fachada).</summary>
        public static double?[] Cotar(Document doc, ViewPlan v, Leitura lt, DimensionType dt, bool soTotal, XYZ R, XYZ U, XYZ O,
                                      double esp, List<Intervalo> ex, ResultadoVista res)
        {
            var fora = new double?[4];
            if (lt.Paredes.Count == 0) { res.Avisos.Add("nenhuma parede reta cortada na vista (fachada)"); return fora; }
            for (int i = 0; i < 4; i++)
                fora[i] = Lado(doc, v, dt, lt, Lados[i].k, Lados[i].s, Lados[i].nome, soTotal, R, U, O, esp, ex, res);
            if (!soTotal && lt.VaosSemReferencia > 0)
                res.Avisos.Add(lt.VaosSemReferencia + " porta(s)/janela(s) sem referências de largura: cotadas pelas ombreiras da parede");
            return fora;
        }

        static List<Parede> Externas(List<Parede> paredes, int k, int s)
        {
            int c = 1 - k;
            return paredes.Where(w => w.Dir == k && Livre(w, paredes, k, c, s, s > 0 ? w.Hi[c] : w.Lo[c])).ToList();
        }

        static double? Lado(Document doc, View v, DimensionType dt, Leitura lt, int k, int s, string nome, bool soTotal,
                            XYZ R, XYZ U, XYZ O, double esp, List<Intervalo> ex, ResultadoVista res)
        {
            int c = 1 - k;
            Func<Parede, double> fora = p => s > 0 ? p.Hi[c] : p.Lo[c];
            var externas = Externas(lt.Paredes, k, s);
            if (externas.Count == 0) return null;

            // faces perpendiculares à cadeia que chegam à face externa de alguma parede de fachada; nos vãos com
            // porta/janela, as ombreiras da parede dão lugar ao vão livre (referências da família)
            var refs = new List<Plano>();
            foreach (var w in externas)
            {
                double f = fora(w);
                var daParede = lt.Planos.Where(p => p.Eixo == k && p.Pos >= w.Lo[k] - TOL && p.Pos <= w.Hi[k] + TOL
                                                    && p.A <= f + TOL && p.B >= f - TOL).ToList();
                foreach (var vao in w.Vaos)
                {
                    daParede.RemoveAll(p => p.Pos > vao.Esq - FOLGA_BATENTE && p.Pos < vao.Dir + FOLGA_BATENTE);
                    refs.Add(new Plano { Ref = vao.RefEsq, Eixo = k, Pos = vao.Esq });
                    refs.Add(new Plano { Ref = vao.RefDir, Eixo = k, Pos = vao.Dir });
                }
                refs.AddRange(daParede);
            }
            var ds = new List<Plano>();
            foreach (var p in refs.OrderBy(x => x.Pos))
                if (ds.Count == 0 || p.Pos - ds[ds.Count - 1].Pos > TOL) ds.Add(p);
            if (ds.Count < 2) return null;

            double borda = s > 0 ? externas.Max(fora) : externas.Min(fora);
            double linha1 = borda + s * esp, linha2 = borda + s * 2 * esp;
            bool hor = k == 0;

            if (soTotal)
            {
                // 1:200: só o contorno (das pontas), na 1ª linha
                var a0 = ds[0]; var b0 = ds[ds.Count - 1];
                if (Motor.Medido(ex, hor, a0.Pos, b0.Pos)) { res.JaExistiam++; return borda; }
                if (Criar(doc, v, dt, new List<Plano> { a0, b0 }, hor, linha1, R, U, O, nome.Replace("fachada", "contorno"), res))
                {
                    res.Segmentos++;
                    ex.Add(new Intervalo { Horizontal = hor, A = a0.Pos, B = b0.Pos });
                }
                return linha1;
            }

            int i = 0;
            while (i < ds.Count - 1)
            {
                if (Motor.Medido(ex, hor, ds[i].Pos, ds[i + 1].Pos)) { res.JaExistiam++; i++; continue; }
                int j = i;
                while (j < ds.Count - 1 && !Motor.Medido(ex, hor, ds[j].Pos, ds[j + 1].Pos)) j++;
                var trecho = ds.GetRange(i, j - i + 1);
                if (Criar(doc, v, dt, trecho, hor, linha1, R, U, O, nome, res))
                {
                    res.Segmentos += trecho.Count - 1;
                    for (int m = 0; m < trecho.Count - 1; m++)
                        ex.Add(new Intervalo { Horizontal = hor, A = trecho[m].Pos, B = trecho[m + 1].Pos });
                }
                i = j;
            }
            if (ds.Count < 3) return linha1;

            var a = ds[0]; var b = ds[ds.Count - 1];
            if (Motor.Medido(ex, hor, a.Pos, b.Pos)) res.JaExistiam++;
            else if (Criar(doc, v, dt, new List<Plano> { a, b }, hor, linha2, R, U, O, nome + "-total", res))
            {
                res.Segmentos++;
                ex.Add(new Intervalo { Horizontal = hor, A = a.Pos, B = b.Pos });
            }
            return linha2;
        }

        /// <summary>A parede está na fachada se, em pelo menos um ponto ao longo dela, não há outra parede mais para fora.</summary>
        static bool Livre(Parede w, List<Parede> todas, int k, int c, int s, double fora)
        {
            double len = w.Hi[k] - w.Lo[k];
            if (len < 10 * TOL) return false;
            foreach (double t in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
            {
                double x = w.Lo[k] + t * len;
                bool bloqueado = todas.Any(p => !ReferenceEquals(p, w) && p.Lo[k] < x && p.Hi[k] > x &&
                                               (s > 0 ? p.Lo[c] > fora - TOL : p.Hi[c] < fora + TOL));
                if (!bloqueado) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ leitura

        internal static double CorteZ(Document doc, ViewPlan v)
        {
            try
            {
                var vr = v.GetViewRange();
                var lid = vr.GetLevelId(PlanViewPlane.CutPlane);
                var lv = (lid != null && lid != ElementId.InvalidElementId ? doc.GetElement(lid) : null) as Level ?? v.GenLevel;
                return (lv?.ProjectElevation ?? 0) + vr.GetOffset(PlanViewPlane.CutPlane);
            }
            catch { return (v.GenLevel?.ProjectElevation ?? 0) + 1200 / 304.8; }
        }

        internal static Leitura Ler(Document doc, ViewPlan v, XYZ R, XYZ U)
        {
            var lt = new Leitura();
            double cz = CorteZ(doc, v);
            var opt = new Options { ComputeReferences = true, View = v };
            foreach (Wall w in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Wall)))
            {
                try
                {
                    if (w.WallType != null && w.WallType.Kind == WallKind.Curtain) continue;
                    if (!(w.Location is LocationCurve lc) || !(lc.Curve is Line ln)) continue;
                    XYZ d = ln.Direction;
                    int dir;
                    if (Math.Abs(d.DotProduct(R)) > PAR) dir = 0;
                    else if (Math.Abs(d.DotProduct(U)) > PAR) dir = 1;
                    else continue;   // inclinada na vista: fica para outra versão

                    var ge = w.get_Geometry(opt);
                    if (ge == null) continue;
                    var par = new Parede { Dir = dir };
                    par.Lo[0] = par.Lo[1] = double.MaxValue; par.Hi[0] = par.Hi[1] = double.MinValue;
                    double z0 = double.MaxValue, z1 = double.MinValue;
                    var meus = new List<Plano>();

                    foreach (var sol in Solidos(ge))
                    {
                        foreach (Edge e in sol.Edges)
                            foreach (var p in e.Tessellate())
                            {
                                Estender(par, p.DotProduct(R), p.DotProduct(U));
                                z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
                            }
                        foreach (var f in sol.Faces)
                        {
                            if (!(f is PlanarFace pf) || pf.Reference == null) continue;
                            XYZ n = pf.FaceNormal;
                            if (Math.Abs(n.Z) > 0.001) continue;
                            int eixo;
                            if (Math.Abs(n.DotProduct(R)) > PAR) eixo = 0;
                            else if (Math.Abs(n.DotProduct(U)) > PAR) eixo = 1;
                            else continue;
                            var pl = new Plano { Ref = pf.Reference, Eixo = eixo, A = double.MaxValue, B = double.MinValue, Z0 = double.MaxValue, Z1 = double.MinValue };
                            pl.Pos = pf.Origin.DotProduct(eixo == 0 ? R : U);
                            XYZ outro = eixo == 0 ? U : R;
                            foreach (EdgeArray loop in pf.EdgeLoops)
                                foreach (Edge e in loop)
                                    foreach (var p in e.Tessellate())
                                    {
                                        double q = p.DotProduct(outro);
                                        pl.A = Math.Min(pl.A, q); pl.B = Math.Max(pl.B, q);
                                        pl.Z0 = Math.Min(pl.Z0, p.Z); pl.Z1 = Math.Max(pl.Z1, p.Z);
                                    }
                            // só as faces cortadas pelo plano de corte (vão acima do corte não aparece na planta)
                            if (pl.Z0 <= cz + TOL && pl.Z1 >= cz - TOL) meus.Add(pl);
                        }
                    }
                    if (z0 > cz || z1 < cz || par.Lo[0] > par.Hi[0]) continue;   // parede não cortada
                    LerVaos(doc, w, dir == 0 ? R : U, cz, par, lt);
                    lt.Paredes.Add(par);
                    lt.Planos.AddRange(meus);
                }
                catch { }
            }
            return lt;
        }

        /// <summary>Portas e janelas da parede cortadas pelo plano de corte: vão livre = referências Esquerda/Direita
        /// da família, nas posições centro ± largura/2 (lado da direita = sentido da HandOrientation).</summary>
        static void LerVaos(Document doc, Wall w, XYZ eixo, double cz, Parede par, Leitura lt)
        {
            IList<ElementId> ins;
            try { ins = w.FindInserts(true, false, false, false); } catch { return; }
            foreach (var id in ins)
            {
                if (!(doc.GetElement(id) is FamilyInstance fi)) continue;
                long cat = fi.Category?.Id != null ? Id(fi.Category.Id) : 0;
                if (cat != (long)BuiltInCategory.OST_Doors && cat != (long)BuiltInCategory.OST_Windows) continue;
                var bb = fi.get_BoundingBox(null);
                if (bb == null || bb.Min.Z > cz || bb.Max.Z < cz) continue;
                if (!(fi.Location is LocationPoint lp)) continue;
                double larg = Largura(fi);
                var re = fi.GetReferences(FamilyInstanceReferenceType.Left).FirstOrDefault();
                var rd = fi.GetReferences(FamilyInstanceReferenceType.Right).FirstOrDefault();
                if (larg <= TOL || re == null || rd == null) { lt.VaosSemReferencia++; continue; }
                double c = lp.Point.DotProduct(eixo);
                double sinal = fi.HandOrientation.DotProduct(eixo) >= 0 ? 1 : -1;
                double pd = c + sinal * larg / 2, pe = c - sinal * larg / 2;
                par.Vaos.Add(pe < pd
                    ? new Vao { Esq = pe, Dir = pd, RefEsq = re, RefDir = rd }
                    : new Vao { Esq = pd, Dir = pe, RefEsq = rd, RefDir = re });
            }
        }

        static double Largura(FamilyInstance fi)
        {
            var bips = new[] { BuiltInParameter.DOOR_WIDTH, BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM };
            foreach (Element e in new Element[] { fi, fi.Symbol })
                foreach (var b in bips)
                {
                    try
                    {
                        var p = e?.get_Parameter(b);
                        if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0) return p.AsDouble();
                    }
                    catch { }
                }
            return 0;
        }

        static long Id(ElementId id)
        {
#if REVIT2024
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        static void Estender(Parede p, double x, double y)
        {
            p.Lo[0] = Math.Min(p.Lo[0], x); p.Hi[0] = Math.Max(p.Hi[0], x);
            p.Lo[1] = Math.Min(p.Lo[1], y); p.Hi[1] = Math.Max(p.Hi[1], y);
        }

        static IEnumerable<Solid> Solidos(GeometryElement ge)
        {
            foreach (var g in ge)
            {
                if (g is Solid s && s.Volume > 1e-9) yield return s;
                else if (g is GeometryElement sub) foreach (var x in Solidos(sub)) yield return x;
            }
        }

        // ------------------------------------------------------------ criação

        internal static bool Criar(Document doc, View v, DimensionType dt, List<Plano> ps, bool hor, double linha,
                                   XYZ R, XYZ U, XYZ O, string regra, ResultadoVista res)
        {
            var ra = new ReferenceArray();
            foreach (var p in ps) ra.Append(p.Ref);
            double a = ps[0].Pos, b = ps[ps.Count - 1].Pos;
            Line ln = hor
                ? Line.CreateBound(Motor.Ponto(R, U, O, a, linha), Motor.Ponto(R, U, O, b, linha))
                : Line.CreateBound(Motor.Ponto(R, U, O, linha, a), Motor.Ponto(R, U, O, linha, b));
            try
            {
                Dimension dim = dt != null ? doc.Create.NewDimension(v, ln, ra, dt) : doc.Create.NewDimension(v, ln, ra);
                if (dim == null) { res.Avisos.Add("o Revit não criou uma cota de " + regra); return false; }
                Marca.MarcarCota(dim, regra);
                res.Criadas++;
                return true;
            }
            catch (Exception e)
            {
                res.Avisos.Add("cota de " + regra + " não criada: " + e.Message);
                return false;
            }
        }
    }
}
