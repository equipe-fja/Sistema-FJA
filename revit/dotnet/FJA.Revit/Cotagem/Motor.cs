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
        public int Criadas, Segmentos, JaExistiam, Removidas, EixosEstendidos;
        public List<string> Avisos = new List<string>();
    }

    /// <summary>Trecho já medido por uma cota (coordenadas da vista). Linha: posição da cota no outro eixo.</summary>
    sealed class Intervalo { public bool Horizontal; public double A, B; public double? Linha; }

    /// <summary>
    /// Motor da cotagem. Plantas: fachadas (Fachada.cs) e ambientes (Ambientes.cs); depois a cadeia de eixos e o
    /// total, sempre em cima (eixos verticais) e à esquerda (horizontais), por fora das cotas de fachada — de dentro
    /// para fora: fachada, total da fachada, eixos, total dos eixos, bolhas (os eixos são estendidos na vista, em 2D,
    /// quando as bolhas não cabem). Cortes e fachadas: eixos e níveis. Antes de criar, lê as cotas lineares da vista
    /// e só cria os trechos que ainda não estão medidos (mesmos pontos de início e fim, na mesma direção, tolerância
    /// de 1 mm), em qualquer posição da vista.
    /// </summary>
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
            public XYZ P0, P1;             // pontas da linha na vista (End0, End1)
            public bool FimEhP1;           // a ponta "Fim" é a End1
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

            // plantas: fachada (1:75: panos e vãos + total; 1:200: contorno) e ambientes (1:200)
            var fora = new double?[4];
            if ((regra.Fachada || regra.Contorno || regra.Ambientes) && v is ViewPlan vp)
            {
                var lt = Fachada.Ler(doc, vp, R, U);
                if (regra.Fachada || regra.Contorno)
                    fora = Fachada.Cotar(doc, vp, lt, dt, regra.Contorno, R, U, O, esp, existentes, res);
                if (regra.Ambientes)
                    Ambientes.Cotar(doc, vp, lt, dt, R, U, O, existentes, res);
            }

            var datums = Datums(doc, v, R, U, regra);
            // eixos e níveis medidos em X (linhas verticais na tela) → cadeia horizontal, em cima; em Y → vertical, à esquerda
            var emX = datums.Where(d => d.Vertical).ToList();
            var emY = datums.Where(d => !d.Vertical).ToList();
            bool planta = v is ViewPlan;
            CotarGrupo(doc, v, dt, emX, true, fora[0], planta, R, U, O, esp, existentes, res, NomeGrupo(emX));
            CotarGrupo(doc, v, dt, emY, false, fora[2], planta, R, U, O, esp, existentes, res, NomeGrupo(emY));

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
                var ln = Extensao2D.Visivel(dp, v);   // como aparece (a curva 2D guardada pode ser outra)
                if (ln == null) continue;
                XYZ p0 = ln.GetEndPoint(0), p1 = ln.GetEndPoint(1), dir = ln.Direction;
                bool b0 = false, b1 = false;
                try { b0 = dp.IsBubbleVisibleInView(DatumEnds.End0, v); b1 = dp.IsBubbleVisibleInView(DatumEnds.End1, v); } catch { }

                var d = new Datum { E = e, P0 = p0, P1 = p1 };
                XYZ ao;   // eixo ao longo da linha
                if (Math.Abs(dir.DotProduct(U)) > PARALELO) { d.Vertical = true; d.Pos = p0.DotProduct(R); ao = U; }
                else if (Math.Abs(dir.DotProduct(R)) > PARALELO) { d.Vertical = false; d.Pos = p0.DotProduct(U); ao = R; }
                else continue;   // inclinado na vista: fica para outra versão
                double a = p0.DotProduct(ao), b = p1.DotProduct(ao);
                d.Ini = Math.Min(a, b); d.Fim = Math.Max(a, b);
                d.FimEhP1 = b >= a;
                d.BolhaIni = d.FimEhP1 ? b0 : b1; d.BolhaFim = d.FimEhP1 ? b1 : b0;
                r.Add(d);
            }
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
                    XYZ eixo = hor ? R : U, outro = hor ? U : R;
                    if (dim.NumberOfSegments == 0)
                        Add(r, hor, dim.Origin.DotProduct(eixo), dim.Value, dim.Origin.DotProduct(outro));
                    else
                        foreach (DimensionSegment s in dim.Segments) Add(r, hor, s.Origin.DotProduct(eixo), s.Value, s.Origin.DotProduct(outro));
                }
                catch { }
            }
            return r;
        }

        static void Add(List<Intervalo> r, bool hor, double centro, double? valor, double linha)
        {
            if (valor == null) return;
            double m = valor.Value / 2;
            r.Add(new Intervalo { Horizontal = hor, A = centro - m, B = centro + m, Linha = linha });
        }

        internal static bool Medido(List<Intervalo> ex, bool hor, double a, double b)
        {
            return ex.Any(i => i.Horizontal == hor && Math.Abs(i.A - a) < TOL && Math.Abs(i.B - b) < TOL);
        }

        /// <summary>Medido por uma cota cuja linha está entre c0 e c1 (ex.: dentro do mesmo ambiente).</summary>
        internal static bool MedidoEm(List<Intervalo> ex, bool hor, double a, double b, double c0, double c1)
        {
            return ex.Any(i => i.Horizontal == hor && Math.Abs(i.A - a) < TOL && Math.Abs(i.B - b) < TOL
                               && i.Linha != null && i.Linha >= c0 - TOL && i.Linha <= c1 + TOL);
        }

        // ------------------------------------------------------------ criação

        /// <param name="emX">true: datums verticais na tela, cadeia horizontal (em cima); false: horizontais, cadeia vertical (à esquerda).</param>
        /// <param name="base_">linha de cota mais externa da fachada deste lado (planta), ou null.</param>
        static void CotarGrupo(Document doc, View v, DimensionType dt, List<Datum> grupo, bool emX, double? base_, bool planta,
                               XYZ R, XYZ U, XYZ O, double esp, List<Intervalo> ex, ResultadoVista res, string regra)
        {
            // ordena e tira os coincidentes (dois eixos na mesma posição não geram trecho)
            var ds = new List<Datum>();
            foreach (var d in grupo.OrderBy(x => x.Pos))
                if (ds.Count == 0 || d.Pos - ds[ds.Count - 1].Pos > TOL) ds.Add(d);
            if (ds.Count < 2) return;

            // decisão da FJA (13/11/2026): cadeia de eixos em cima e à esquerda, todos os eixos na mesma cadeia
            bool fim = emX;                 // emX: Fim = em cima; senão Ini = à esquerda
            double fora = fim ? 1 : -1;     // sentido para fora do desenho
            double linha1, linha2;
            if (base_ != null)
            {
                // por fora das cotas de fachada; as bolhas vão para fora do total (eixos estendidos na vista, se preciso)
                linha1 = base_.Value + fora * esp;
                linha2 = linha1 + fora * esp;
                if (planta)
                {
                    double alvo = linha2 + fora * esp;
                    foreach (var d in ds)
                        if (d.E is Grid && fora * (alvo - (fim ? d.Fim : d.Ini)) > TOL && Estender(v, d, fim, alvo, R, U, res))
                            res.EixosEstendidos++;
                }
            }
            else
            {
                // sem fachada: logo para dentro das pontas (bolhas) — total junto delas, cadeia depois
                double borda = fim ? ds.Min(d => d.Fim) : ds.Max(d => d.Ini);
                linha2 = borda - fora * esp;
                linha1 = borda - fora * 2 * esp;
            }

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

        /// <summary>Leva a ponta do eixo (do lado da cadeia) até "alvo", só nesta vista (pontas em 2D).</summary>
        static bool Estender(View v, Datum d, bool fim, double alvo, XYZ R, XYZ U, ResultadoVista res)
        {
            try
            {
                var dp = (DatumPlane)d.E;
                XYZ ao = d.Vertical ? U : R;
                bool moveP1 = fim == d.FimEhP1;
                XYZ p = moveP1 ? d.P1 : d.P0;
                // anda pela direção do próprio eixo (vista girada: o eixo nunca é exatamente paralelo a R/U, e o
                // Revit recusa uma linha que não coincide com a do eixo)
                XYZ dirEixo = (d.P1 - d.P0).Normalize();
                double cos = dirEixo.DotProduct(ao);
                XYZ np = p + dirEixo * ((alvo - p.DotProduct(ao)) / cos);
                XYZ a = moveP1 ? d.P0 : np, b = moveP1 ? np : d.P1;
                Extensao2D.Para2D(dp, v);
                dp.SetCurveInView(DatumExtentType.ViewSpecific, v, Line.CreateBound(a, b));
                if (moveP1) d.P1 = np; else d.P0 = np;
                if (fim) d.Fim = alvo; else d.Ini = alvo;
                return true;
            }
            catch (Exception e)
            {
                res.Avisos.Add("eixo " + d.E.Name + " não estendido: " + e.Message);
                return false;
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
