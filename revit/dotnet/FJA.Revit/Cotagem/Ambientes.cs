using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace FJA.Revit.Cotagem
{
    /// <summary>
    /// Planta baixa 1:200: dimensões gerais de cada ambiente — duas cotas perpendiculares (largura e profundidade),
    /// entre as faces acabadas das paredes que limitam o ambiente (o contorno do ambiente pelo acabamento).
    /// </summary>
    static class Ambientes
    {
        const double TOL = Motor.TOL;
        const double PERTO = 20 / 304.8;   // face de parede até 2 cm do limite do ambiente

        public static void Cotar(Document doc, ViewPlan v, Leitura lt, DimensionType dt, XYZ R, XYZ U, XYZ O,
                                 List<Intervalo> ex, ResultadoVista res)
        {
            var op = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
            int semParede = 0, n = 0;
            foreach (var room in new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>())
            {
                if (room.Area <= 0) continue;
                IList<IList<BoundarySegment>> bs;
                try { bs = room.GetBoundarySegments(op); } catch { continue; }
                if (bs == null || bs.Count == 0) continue;
                double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                foreach (var p in bs[0].SelectMany(s => s.GetCurve().Tessellate()))   // contorno externo
                {
                    double x = p.DotProduct(R), y = p.DotProduct(U);
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
                if (x1 - x0 < 10 * TOL || y1 - y0 < 10 * TOL) continue;
                n++;
                string nome = "ambiente";
                // largura (cadeia horizontal) um pouco abaixo do centro, profundidade um pouco à esquerda: o rótulo fica livre
                if (!Uma(doc, v, dt, lt, 0, x0, x1, y0, y1, y0 + 0.35 * (y1 - y0), R, U, O, ex, res, nome)) semParede++;
                if (!Uma(doc, v, dt, lt, 1, y0, y1, x0, x1, x0 + 0.35 * (x1 - x0), R, U, O, ex, res, nome)) semParede++;
            }
            if (n == 0) res.Avisos.Add("nenhum ambiente colocado na vista");
            if (semParede > 0) res.Avisos.Add(semParede + " dimensão(ões) de ambiente sem parede num dos lados (linha de separação?)");
        }

        /// <param name="k">eixo da medida (0: largura em X; 1: profundidade em Y).</param>
        /// <param name="a0">limites do ambiente no eixo da medida; c0/c1 no outro eixo; linha = posição da cota no outro eixo.</param>
        static bool Uma(Document doc, View v, DimensionType dt, Leitura lt, int k, double a0, double a1, double c0, double c1,
                        double linha, XYZ R, XYZ U, XYZ O, List<Intervalo> ex, ResultadoVista res, string nome)
        {
            Plano FaceEm(double pos) => lt.Planos
                .Where(p => p.Eixo == k && Math.Abs(p.Pos - pos) < PERTO && p.B > c0 + TOL && p.A < c1 - TOL)
                .OrderBy(p => Math.Abs(p.Pos - pos)).FirstOrDefault();
            var fa = FaceEm(a0); var fb = FaceEm(a1);
            if (fa == null || fb == null) return false;
            // pelas quinas (arestas verticais) das faces, a mais próxima da linha da cota: paredes um pouco fora de
            // esquadro não são paralelas, e a cota entre faces não paralelas fica sem valor
            fa = fa.PelaAresta(k, linha, double.MaxValue, fa.Pos);
            fb = fb.PelaAresta(k, linha, double.MaxValue, fb.Pos);
            bool hor = k == 0;
            // já cotado por uma cota dentro deste ambiente (ambientes iguais lado a lado têm cada um a sua)
            if (Motor.MedidoEm(ex, hor, fa.Pos, fb.Pos, c0, c1)) { res.JaExistiam++; return true; }
            if (Fachada.Criar(doc, v, dt, new List<Plano> { fa, fb }, hor, linha, R, U, O, nome, res))
            {
                res.Segmentos++;
                ex.Add(new Intervalo { Horizontal = hor, A = fa.Pos, B = fb.Pos, Linha = linha });
            }
            return true;
        }
    }
}
