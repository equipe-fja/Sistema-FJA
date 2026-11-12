using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Links
{
    /// <summary>
    /// Cores de diagnóstico do Verificar alterações: sobreposição gráfica por elemento na vista ativa, na cor da
    /// situação de cada cópia. Temporário: "Limpar cores" devolve a sobreposição que o elemento tinha antes
    /// (guardada na sessão do Revit) ou, depois de reabrir o Revit, tira só as sobreposições nas cores daqui.
    /// </summary>
    public static class CoresDiagnostico
    {
        public static readonly Dictionary<string, Color> COR = new Dictionary<string, Color>
        {
            ["link"] = new Color(255, 160, 0),
            ["projeto"] = new Color(0, 120, 255),
            ["ambos"] = new Color(220, 0, 0),
            ["apagado"] = new Color(170, 0, 170),
            ["semlink"] = new Color(128, 128, 128),
            ["ok"] = new Color(0, 160, 60),
        };

        // sobreposições anteriores, por vista: "documento|vista" → (elemento → sobreposição de antes)
        static readonly Dictionary<string, Dictionary<long, OverrideGraphicSettings>> antes = new Dictionary<string, Dictionary<long, OverrideGraphicSettings>>();

        static string Chave(Document doc, View v) => (doc.PathName ?? doc.Title) + "|" + Motor.Id(v.Id);

        static ElementId Solido(Document doc)
        {
            var fp = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
            return fp != null ? fp.Id : ElementId.InvalidElementId;
        }

        public static bool VistaAceita(View v) =>
            v != null && !v.IsTemplate && v.ViewType != ViewType.Schedule && v.ViewType != ViewType.DrawingSheet && v.AreGraphicsOverridesAllowed();

        static OverrideGraphicSettings Sobrepor(Color c, ElementId solido)
        {
            var o = new OverrideGraphicSettings();
            o.SetProjectionLineColor(c);
            o.SetCutLineColor(c);
            o.SetProjectionLineWeight(5);
            if (solido != ElementId.InvalidElementId)
            {
                o.SetSurfaceForegroundPatternId(solido);
                o.SetSurfaceForegroundPatternColor(c);
                o.SetCutForegroundPatternId(solido);
                o.SetCutForegroundPatternColor(c);
            }
            return o;
        }

        /// <summary>Colore as cópias dos itens na vista (dentro de uma Transaction aberta). Devolve quantas.</summary>
        public static int Colorir(Document doc, View v, IEnumerable<Motor.Item> itens, bool iguais)
        {
            if (!VistaAceita(v)) return 0;
            var solido = Solido(doc);
            string k = Chave(doc, v);
            if (!antes.TryGetValue(k, out var mem)) antes[k] = mem = new Dictionary<long, OverrideGraphicSettings>();
            var visiveis = new HashSet<long>(new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().ToElementIds().Select(Motor.Id));
            int n = 0;
            foreach (var it in itens)
            {
                if (it.Copia == null || !it.Copia.IsValidObject) continue;
                if (it.Estado == "ok" && !iguais) continue;
                if (!COR.TryGetValue(it.Estado, out var c)) continue;
                long id = Motor.Id(it.Copia.Id);
                if (!visiveis.Contains(id)) continue;
                if (!mem.ContainsKey(id)) mem[id] = v.GetElementOverrides(it.Copia.Id);
                v.SetElementOverrides(it.Copia.Id, Sobrepor(c, solido));
                n++;
            }
            return n;
        }

        static bool CorNossa(Color c) => c != null && c.IsValid && COR.Values.Any(x => x.Red == c.Red && x.Green == c.Green && x.Blue == c.Blue);

        /// <summary>Tira as cores desta ferramenta na vista (dentro de uma Transaction aberta). Devolve quantas.</summary>
        public static int Limpar(Document doc, View v)
        {
            if (!VistaAceita(v)) return 0;
            int n = 0;
            antes.TryGetValue(Chave(doc, v), out var mem);
            foreach (var e in Registro.Elementos(doc))
            {
                long id = Motor.Id(e.Id);
                OverrideGraphicSettings atual;
                try { atual = v.GetElementOverrides(e.Id); } catch { continue; }
                if (mem != null && mem.TryGetValue(id, out var ant))
                {
                    v.SetElementOverrides(e.Id, ant);
                    n++;
                }
                else if (CorNossa(atual.ProjectionLineColor) && (CorNossa(atual.SurfaceForegroundPatternColor) || !atual.SurfaceForegroundPatternColor.IsValid))
                {
                    v.SetElementOverrides(e.Id, new OverrideGraphicSettings());
                    n++;
                }
            }
            if (mem != null) antes.Remove(Chave(doc, v));
            return n;
        }

        /// <summary>Há cópias coloridas por esta ferramenta na vista?</summary>
        public static bool TemCores(Document doc, View v)
        {
            if (!VistaAceita(v)) return false;
            if (antes.TryGetValue(Chave(doc, v), out var mem) && mem.Count > 0) return true;
            foreach (var e in Registro.Elementos(doc))
            {
                try { if (CorNossa(v.GetElementOverrides(e.Id).ProjectionLineColor)) return true; } catch { }
            }
            return false;
        }
    }
}
