using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Cotagem
{
    /// <summary>
    /// Planta baixa: panos e vãos de fachada (2.20.0). Em cada um dos quatro lados do edifício, uma cadeia com as
    /// faces que chegam à face externa das paredes de fachada (ombreiras dos vãos, pontas de parede e cantos), e o
    /// total da fachada por fora dela. Só paredes retas, alinhadas à vista e cortadas pelo plano de corte.
    /// </summary>
    static class Fachada
    {
        const double TOL = Motor.TOL;
        const double PAR = 0.999;

        /// <summary>Face plana vertical de parede, perpendicular a um dos eixos da vista.</summary>
        sealed class Plano
        {
            public Reference Ref;
            public int Eixo;          // 0: normal paralela a R (posição medida em X); 1: paralela a U (medida em Y)
            public double Pos;        // posição ao longo do eixo
            public double A, B;       // extensão no outro eixo
            public double Z0, Z1;
        }

        /// <summary>Parede como retângulo na vista (Lo/Hi por eixo: 0 = X, 1 = Y).</summary>
        sealed class Parede
        {
            public int Dir;           // 0: paralela a R (horizontal na tela); 1: paralela a U
            public double[] Lo = new double[2], Hi = new double[2];
        }

        public static void Cotar(Document doc, ViewPlan v, DimensionType dt, XYZ R, XYZ U, XYZ O, double esp,
                                 List<Intervalo> ex, ResultadoVista res)
        {
            double cz = CorteZ(doc, v);
            var paredes = new List<Parede>();
            var planos = new List<Plano>();
            Ler(doc, v, R, U, cz, paredes, planos);
            if (paredes.Count == 0) { res.Avisos.Add("nenhuma parede reta cortada na vista (fachada)"); return; }

            var lados = new[] { (k: 0, s: 1, nome: "fachada-cima"), (k: 0, s: -1, nome: "fachada-baixo"),
                                (k: 1, s: -1, nome: "fachada-esquerda"), (k: 1, s: 1, nome: "fachada-direita") };
            foreach (var l in lados) Lado(doc, v, dt, paredes, planos, l.k, l.s, l.nome, R, U, O, esp, ex, res);
        }

        /// <param name="k">eixo ao longo da cadeia (0: cadeia horizontal, em cima/embaixo; 1: vertical, à esquerda/direita).</param>
        /// <param name="s">+1: lado de cima/direita; -1: de baixo/esquerda.</param>
        static void Lado(Document doc, View v, DimensionType dt, List<Parede> paredes, List<Plano> planos, int k, int s,
                         string nome, XYZ R, XYZ U, XYZ O, double esp, List<Intervalo> ex, ResultadoVista res)
        {
            int c = 1 - k;
            Func<Parede, double> fora = p => s > 0 ? p.Hi[c] : p.Lo[c];

            // paredes de fachada deste lado: paralelas à cadeia e sem outra parede à frente em algum ponto
            var externas = paredes.Where(w => w.Dir == k && Livre(w, paredes, k, c, s, fora(w))).ToList();
            if (externas.Count == 0) return;

            // faces perpendiculares à cadeia que chegam à face externa de alguma parede de fachada
            var refs = new List<Plano>();
            foreach (var w in externas)
            {
                double f = fora(w);
                foreach (var p in planos)
                    if (p.Eixo == k && p.Pos >= w.Lo[k] - TOL && p.Pos <= w.Hi[k] + TOL && p.A <= f + TOL && p.B >= f - TOL)
                        refs.Add(p);
            }
            var ds = new List<Plano>();
            foreach (var p in refs.OrderBy(x => x.Pos))
                if (ds.Count == 0 || p.Pos - ds[ds.Count - 1].Pos > TOL) ds.Add(p);
            if (ds.Count < 2) return;

            double borda = s > 0 ? externas.Max(fora) : externas.Min(fora);
            double linha1 = borda + s * esp, linha2 = borda + s * 2 * esp;
            bool hor = k == 0;

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

            if (ds.Count >= 3)
            {
                var a = ds[0]; var b = ds[ds.Count - 1];
                if (Motor.Medido(ex, hor, a.Pos, b.Pos)) res.JaExistiam++;
                else if (Criar(doc, v, dt, new List<Plano> { a, b }, hor, linha2, R, U, O, nome + "-total", res))
                {
                    res.Segmentos++;
                    ex.Add(new Intervalo { Horizontal = hor, A = a.Pos, B = b.Pos });
                }
            }
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

        static double CorteZ(Document doc, ViewPlan v)
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

        static void Ler(Document doc, View v, XYZ R, XYZ U, double cz, List<Parede> paredes, List<Plano> planos)
        {
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
                    paredes.Add(par);
                    planos.AddRange(meus);
                }
                catch { }
            }
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

        static bool Criar(Document doc, View v, DimensionType dt, List<Plano> ps, bool hor, double linha,
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
