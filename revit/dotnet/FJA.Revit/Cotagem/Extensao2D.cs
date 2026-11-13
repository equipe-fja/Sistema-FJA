using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Cotagem
{
    /// <summary>
    /// Extensão de eixos e níveis na vista (2.23.2). Com a ponta em 3D (Model), o que aparece é a curva do modelo;
    /// a curva 2D (ViewSpecific) guardada na vista pode ser outra, antiga e bem mais longa. Ler a curva 2D nesse
    /// caso dá as pontas erradas, e só trocar a ponta para 2D faz o Revit passar a mostrar essa curva antiga
    /// (linhas esticadas, com as cabeças longe). Aqui: a curva como aparece hoje e a passagem para 2D sem mudar
    /// a extensão visível.
    /// </summary>
    public static class Extensao2D
    {
        /// <summary>A linha do eixo/nível como aparece na vista: cada ponta pela curva do tipo de extensão dela.</summary>
        public static Line Visivel(DatumPlane dp, View v)
        {
            Line lv = null, lm = null;
            try { lv = dp.GetCurvesInView(DatumExtentType.ViewSpecific, v)?.OfType<Line>().FirstOrDefault(); } catch { }
            try { lm = dp.GetCurvesInView(DatumExtentType.Model, v)?.OfType<Line>().FirstOrDefault(); } catch { }
            bool m0 = false, m1 = false;
            try
            {
                m0 = dp.GetDatumExtentTypeInView(DatumEnds.End0, v) == DatumExtentType.Model;
                m1 = dp.GetDatumExtentTypeInView(DatumEnds.End1, v) == DatumExtentType.Model;
            }
            catch { }
            if (lv == null) return lm;
            if (lm == null || (!m0 && !m1)) return lv;

            // pontas do modelo projetadas na reta da curva 2D (o Revit só aceita curva 2D sobre a mesma reta)
            XYZ c0 = lv.GetEndPoint(0), dir = lv.Direction;
            double a0 = (lm.GetEndPoint(0) - c0).DotProduct(dir), a1 = (lm.GetEndPoint(1) - c0).DotProduct(dir);
            if (a0 > a1) { var x = a0; a0 = a1; a1 = x; }
            double t0 = m0 ? a0 : 0, t1 = m1 ? a1 : lv.Length;
            if (t1 - t0 < 1e-6) return lv;
            return Line.CreateBound(c0 + dir * t0, c0 + dir * t1);
        }

        /// <summary>Passa as duas pontas para 2D mantendo a extensão que aparece hoje. Devolve a linha 2D final.</summary>
        public static Line Para2D(DatumPlane dp, View v)
        {
            bool m0 = dp.GetDatumExtentTypeInView(DatumEnds.End0, v) != DatumExtentType.ViewSpecific;
            bool m1 = dp.GetDatumExtentTypeInView(DatumEnds.End1, v) != DatumExtentType.ViewSpecific;
            var vis = Visivel(dp, v);
            if (!m0 && !m1) return vis;
            if (m0) dp.SetDatumExtentType(DatumEnds.End0, v, DatumExtentType.ViewSpecific);
            if (m1) dp.SetDatumExtentType(DatumEnds.End1, v, DatumExtentType.ViewSpecific);
            if (vis != null) dp.SetCurveInView(DatumExtentType.ViewSpecific, v, vis);   // se falhar, quem chama desfaz
            return vis;
        }
    }
}
