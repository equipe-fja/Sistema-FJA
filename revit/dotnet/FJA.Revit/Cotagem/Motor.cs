using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Cotagem
{
    /// <summary>Resultado da cotagem de uma vista (vai para o relatório).</summary>
    public sealed class ResultadoVista
    {
        public string Vista;
        public int Criadas, Segmentos, JaExistiam, Removidas;
        public List<string> Avisos = new List<string>();
    }

    /// <summary>
    /// Motor da cotagem (1ª versão): cadeias de eixos (grids) e de níveis, com total, no lado em que ficam as
    /// bolhas. Antes de criar, lê as cotas lineares da vista e só cria os trechos que ainda não estão medidos
    /// (mesmos pontos de início e fim, na mesma direção, tolerância de 1 mm), em qualquer posição da vista.
    /// </summary>
    /// <summary>Trecho já medido por uma cota (coordenadas da vista).</summary>
    sealed class Intervalo { public bool Horizontal; public double A, B; }

    public static class Motor
    {
        internal const double TOL = 1.0 / 304.8;   // 1 mm, em pés
        const double PARALELO = 0.999;

        /// <summary>Eixo ou nível como aparece na vista (coordenadas da vista: X = RightDirection, Y = UpDirection).</summary>
        sealed class Datum
        {
            public Element E;
            public bool Vertical;          // linha vertical na tela (medida em X); senão horizontal (medida em Y)
            public double Pos;             // X da linha vertical / Y da linha horizontal
            public double Ini, Fim;        // extensão ao longo da linha (Ini < Fim)
            public bool BolhaIni, BolhaFim;
        }

        public static ResultadoVista Cotar(Document doc, View v, TipoVista tipo, DimensionType dt, double espacamentoMm, bool refazer)
        {
            var res = new ResultadoVista { Vista = v.Name };
            var regra = Regras.Para(tipo, v.Scale);
            if (regra.Vazia) { res.Avisos.Add(regra.Descricao); return res; }

            XYZ R = v.RightDirection, U = v.UpDirection, O = v.Origin;
            double esp = espacamentoMm / 304.8 * v.Scale;

            // modo "Refazer as minhas": apaga só as cotas marcadas pela ferramenta
            if (refazer)
            {
                var minhas = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).Where(Marca.DaFerramenta).Select(e => e.Id).ToList();
                if (minhas.Count > 0) { doc.Delete(minhas); res.Removidas = minhas.Count; }
            }

            var existentes = Existentes(doc, v, R, U);
            var datums = Datums(doc, v, R, U, regra);

            // eixos e níveis medidos em X (linhas verticais na tela) → cadeia horizontal (em cima/embaixo)
            var emX = datums.Where(d => d.Vertical).ToList();
            var emY = datums.Where(d => !d.Vertical).ToList();
            CotarGrupo(doc, v, dt, emX, true, R, U, O, esp, existentes, res, NomeGrupo(emX));
            CotarGrupo(doc, v, dt, emY, false, R, U, O, esp, existentes, res, NomeGrupo(emY));

            // planta baixa 1:75: panos e vãos de fachada + total (2.20.0)
            if (regra.Fachada && v is ViewPlan vp)
                Fachada.Cotar(doc, vp, dt, R, U, O, esp, existentes, res);

            if ((regra.Eixos || regra.Niveis) && datums.Count == 0)
                res.Avisos.Add(regra.Eixos && regra.Niveis ? "nenhum eixo ou nível visível na vista" : regra.Eixos ? "nenhum eixo visível na vista" : "nenhum nível visível na vista");
            return res;
        }

        static string NomeGrupo(List<Datum> g) => g.Count > 0 && g[0].E is Level ? "niveis" : "eixos";

        // ------------------------------------------------------------ leitura

        static List<Datum> Datums(Document doc, View v, XYZ R, XYZ U, Regra regra)
        {
            var r = new List<Datum>();
            var col = new List<Element>();
            if (regra.Eixos) col.AddRange(new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).ToElements());
            if (regra.Niveis) col.AddRange(new FilteredElementCollector(doc, v.Id).OfClass(typeof(Level)).ToElements());
            foreach (var e in col)
            {
                var dp = e as DatumPlane;
                if (dp == null) continue;
                if (e is Grid g && g.IsCurved) continue;
                IList<Curve> cs;
                try { cs = dp.GetCurvesInView(DatumExtentType.ViewSpecific, v); }
                catch { continue; }
                var ln = cs?.OfType<Line>().FirstOrDefault();
                if (ln == null) continue;
                XYZ p0 = ln.GetEndPoint(0), p1 = ln.GetEndPoint(1), dir = ln.Direction;
                bool b0 = false, b1 = false;
                try { b0 = dp.IsBubbleVisibleInView(DatumEnds.End0, v); b1 = dp.IsBubbleVisibleInView(DatumEnds.End1, v); } catch { }

                var d = new Datum { E = e };
                if (Math.Abs(dir.DotProduct(U)) > PARALELO)
                {
                    d.Vertical = true;
                    d.Pos = p0.DotProduct(R);
                    double a = p0.DotProduct(U), b = p1.DotProduct(U);
                    d.Ini = Math.Min(a, b); d.Fim = Math.Max(a, b);
                    d.BolhaIni = a < b ? b0 : b1; d.BolhaFim = a < b ? b1 : b0;
                }
                else if (Math.Abs(dir.DotProduct(R)) > PARALELO)
                {
                    d.Vertical = false;
                    d.Pos = p0.DotProduct(U);
                    double a = p0.DotProduct(R), b = p1.DotProduct(R);
                    d.Ini = Math.Min(a, b); d.Fim = Math.Max(a, b);
                    d.BolhaIni = a < b ? b0 : b1; d.BolhaFim = a < b ? b1 : b0;
                }
                else continue;   // inclinado na vista: fica para outra versão
                r.Add(d);
            }
            // eixos e níveis no mesmo grupo não se misturam: níveis só aparecem horizontais nos cortes e eixos verticais
            return r;
        }

        /// <summary>Trechos já medidos por cotas lineares da vista (cada segmento de uma cadeia conta sozinho).</summary>
        static List<Intervalo> Existentes(Document doc, View v, XYZ R, XYZ U)
        {
            var r = new List<Intervalo>();
            foreach (Dimension dim in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)))
            {
                try
                {
                    if (dim is SpotDimension) continue;
                    if (dim.DimensionShape != DimensionShape.Linear) continue;
                    var ln = dim.Curve as Line;
                    if (ln == null) continue;
                    XYZ d = ln.Direction;
                    bool hor;
                    if (Math.Abs(d.DotProduct(R)) > PARALELO) hor = true;
                    else if (Math.Abs(d.DotProduct(U)) > PARALELO) hor = false;
                    else continue;
                    XYZ eixo = hor ? R : U;
                    if (dim.NumberOfSegments == 0)
                        Add(r, hor, dim.Origin.DotProduct(eixo), dim.Value);
                    else
                        foreach (DimensionSegment s in dim.Segments) Add(r, hor, s.Origin.DotProduct(eixo), s.Value);
                }
                catch { }
            }
            return r;
        }

        static void Add(List<Intervalo> r, bool hor, double centro, double? valor)
        {
            if (valor == null) return;
            double m = valor.Value / 2;
            r.Add(new Intervalo { Horizontal = hor, A = centro - m, B = centro + m });
        }

        internal static bool Medido(List<Intervalo> ex, bool hor, double a, double b)
        {
            return ex.Any(i => i.Horizontal == hor && Math.Abs(i.A - a) < TOL && Math.Abs(i.B - b) < TOL);
        }

        // ------------------------------------------------------------ criação

        /// <param name="emX">true: datums verticais na tela, cadeia horizontal; false: datums horizontais, cadeia vertical.</param>
        static void CotarGrupo(Document doc, View v, DimensionType dt, List<Datum> grupo, bool emX, XYZ R, XYZ U, XYZ O,
                               double esp, List<Intervalo> ex, ResultadoVista res, string regra)
        {
            // ordena e tira os coincidentes (dois eixos na mesma posição não geram trecho)
            var ds = new List<Datum>();
            foreach (var d in grupo.OrderBy(x => x.Pos))
                if (ds.Count == 0 || d.Pos - ds[ds.Count - 1].Pos > TOL) ds.Add(d);
            if (ds.Count < 2) return;

            // um lado só, o das bolhas (bolha do eixo / cabeça do nível). Preferência: cadeia horizontal em cima,
            // cadeia vertical à esquerda (como nas pranchas da FJA); o outro lado só se as bolhas estiverem só lá.
            bool temFim = ds.Any(d => d.BolhaFim), temIni = ds.Any(d => d.BolhaIni);
            bool preferido = emX;                      // emX: Fim = em cima; senão Ini = à esquerda
            bool lado = preferido ? (temFim || !temIni) : !(temIni || !temFim);

            foreach (bool fim in new[] { lado })
            {
                // linha da cadeia logo para dentro das bolhas: a ponta mais recuada entre os datums, menos o espaçamento
                double borda = fim ? ds.Min(d => d.Fim) : ds.Max(d => d.Ini);
                double sinal = fim ? -1 : 1;
                double linha1 = borda + sinal * esp;
                double linha2 = borda + sinal * 2 * esp;

                // trechos que faltam, em sequências contínuas
                int i = 0;
                while (i < ds.Count - 1)
                {
                    if (Medido(ex, emX, ds[i].Pos, ds[i + 1].Pos)) { res.JaExistiam++; i++; continue; }
                    int j = i;
                    while (j < ds.Count - 1 && !Medido(ex, emX, ds[j].Pos, ds[j + 1].Pos)) j++;
                    var trecho = ds.GetRange(i, j - i + 1);
                    if (Criar(doc, v, dt, trecho, emX, linha1, R, U, O, regra, res))
                    {
                        res.Segmentos += trecho.Count - 1;
                        foreach (var k in Enumerable.Range(0, trecho.Count - 1))
                            ex.Add(new Intervalo { Horizontal = emX, A = trecho[k].Pos, B = trecho[k + 1].Pos });
                    }
                    i = j;
                }

                // total entre os extremos (só faz sentido com 3 ou mais)
                if (ds.Count >= 3)
                {
                    if (Medido(ex, emX, ds[0].Pos, ds[ds.Count - 1].Pos)) res.JaExistiam++;
                    else if (Criar(doc, v, dt, new List<Datum> { ds[0], ds[ds.Count - 1] }, emX, linha2, R, U, O, regra + "-total", res))
                    {
                        res.Segmentos++;
                        ex.Add(new Intervalo { Horizontal = emX, A = ds[0].Pos, B = ds[ds.Count - 1].Pos });
                    }
                }
            }
        }

        internal static XYZ Ponto(XYZ R, XYZ U, XYZ O, double x, double y)
        {
            return O + R * (x - O.DotProduct(R)) + U * (y - O.DotProduct(U));
        }

        static bool Criar(Document doc, View v, DimensionType dt, List<Datum> ds, bool emX, double linha,
                          XYZ R, XYZ U, XYZ O, string regra, ResultadoVista res)
        {
            var ra = new ReferenceArray();
            foreach (var d in ds) ra.Append(new Reference(d.E));
            double a = ds[0].Pos, b = ds[ds.Count - 1].Pos;
            Line ln = emX
                ? Line.CreateBound(Ponto(R, U, O, a, linha), Ponto(R, U, O, b, linha))
                : Line.CreateBound(Ponto(R, U, O, linha, a), Ponto(R, U, O, linha, b));
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
