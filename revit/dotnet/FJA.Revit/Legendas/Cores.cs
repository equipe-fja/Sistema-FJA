using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace FJA.Revit.Legendas
{
    /// <summary>Cor dos elementos no modelo e pintura dos símbolos na legenda.</summary>
    public static class Cores
    {
        public sealed class Cache
        {
            public Dictionary<string, Tuple<List<Tuple<FilterElement, byte[]>>, View>> Vistas =
                new Dictionary<string, Tuple<List<Tuple<FilterElement, byte[]>>, View>>();
            public Dictionary<string, Tuple<byte[], string>> Tipos = new Dictionary<string, Tuple<byte[], string>>();
        }

        public static string Chave(byte[] c) => c == null ? "" : c[0] + "," + c[1] + "," + c[2];

        public static byte[] DeChave(string k)
        {
            if (string.IsNullOrEmpty(k)) return null;
            var p = k.Split(',');
            return new[] { byte.Parse(p[0]), byte.Parse(p[1]), byte.Parse(p[2]) };
        }

        public static byte[] Rgb(Color c)
        {
            try { if (c != null && c.IsValid) return new[] { c.Red, c.Green, c.Blue }; } catch { }
            return null;
        }

        public static byte[] Hex(string h)
        {
            h = (h ?? "").Trim().TrimStart('#');
            if (h.Length != 6) return null;
            try
            {
                return new[]
                {
                    byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber),
                    byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber),
                    byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber),
                };
            }
            catch { return null; }
        }

        /// <summary>Vista que controla o parâmetro (o modelo de vista, se ele controlar).</summary>
        static View Controladora(Document doc, View view, BuiltInParameter bip)
        {
            try
            {
                ElementId tid = view.ViewTemplateId;
                if (tid != null && tid != ElementId.InvalidElementId && doc.GetElement(tid) is View tpl)
                {
                    var nc = tpl.GetNonControlledTemplateParameterIds();
                    if (!nc.Any(i => i == new ElementId(bip))) return tpl;
                }
            }
            catch { }
            return view;
        }

        static bool FiltroPega(FilterElement f, Element e)
        {
            if (f is SelectionFilterElement sel)
                return sel.GetElementIds().Any(i => i == e.Id);
            if (f is ParameterFilterElement pf)
            {
                try
                {
                    if (e.Category == null || !pf.GetCategories().Any(c => c == e.Category.Id)) return false;
                }
                catch { }
                ElementFilter ef = null;
                try { ef = pf.GetElementFilter(); } catch { }
                if (ef == null) return true;
                try { return ef.PassesFilter(e); } catch { return false; }
            }
            return false;
        }

        /// <summary>Cor das linhas do elemento na vista: sobreposição do elemento &gt; filtros (na ordem
        /// da vista) &gt; sobreposição da categoria &gt; estilo de objeto.</summary>
        public static void Efetiva(Document doc, View view, Element e, Cache cache, Disciplina d, out byte[] cor, out string origem)
        {
            byte[] c = Rgb(view.GetElementOverrides(e.Id).ProjectionLineColor);
            if (c != null) { cor = c; origem = "sobreposição do elemento"; return; }
            string vk = view.Id.ToString();
            if (!cache.Vistas.TryGetValue(vk, out var info))
            {
                View fv = Controladora(doc, view, BuiltInParameter.VIS_GRAPHICS_FILTERS);
                View cv = Controladora(doc, view, BuiltInParameter.VIS_GRAPHICS_MODEL);
                var lista = new List<Tuple<FilterElement, byte[]>>();
                IList<ElementId> ids;
                try { ids = fv.GetOrderedFilters(); } catch { ids = fv.GetFilters().ToList(); }
                foreach (ElementId fid in ids)
                {
                    try
                    {
                        if (!fv.GetIsFilterEnabled(fid) || !fv.GetFilterVisibility(fid)) continue;
                        byte[] col = Rgb(fv.GetFilterOverrides(fid).ProjectionLineColor);
                        if (col != null) lista.Add(Tuple.Create(doc.GetElement(fid) as FilterElement, col));
                    }
                    catch { }
                }
                cache.Vistas[vk] = info = Tuple.Create(lista, cv);
            }
            // filtros e categoria: mesmo resultado para os elementos do mesmo tipo na vista
            string tk = vk + "|" + e.GetTypeId();
            if (!cache.Tipos.TryGetValue(tk, out var r))
            {
                r = null;
                foreach (var fc in info.Item1)
                {
                    if (fc.Item1 != null && FiltroPega(fc.Item1, e)) { r = Tuple.Create(fc.Item2, "filtro '" + fc.Item1.Name + "'"); break; }
                }
                if (r == null && e.Category != null)
                {
                    try
                    {
                        byte[] cc = Rgb(info.Item2.GetCategoryOverrides(e.Category.Id).ProjectionLineColor);
                        if (cc != null) r = Tuple.Create(cc, "sobreposição da categoria");
                    }
                    catch { }
                    if (r == null)
                    {
                        byte[] cc = Rgb(e.Category.LineColor);
                        if (cc != null) r = Tuple.Create(cc, "estilo de objeto");
                    }
                }
                r = r ?? Tuple.Create(d.Cor, "padrão");
                cache.Tipos[tk] = r;
            }
            cor = r.Item1;
            origem = r.Item2;
        }

        /// <summary>Uma cor só para a legenda: a mais frequente entre os elementos da disciplina,
        /// contando primeiro os que aparecem nas plantas da disciplina em prancha.</summary>
        public static byte[] Dominante(IEnumerable<Item> itens, Disciplina d)
        {
            var soma = new Dictionary<string, int[]>();
            foreach (var it in itens)
            {
                foreach (var kv in it.CoresCont)
                {
                    if (!soma.TryGetValue(kv.Key, out var a)) soma[kv.Key] = a = new int[2];
                    a[0] += kv.Value[0];
                    a[1] += kv.Value[1];
                }
            }
            if (soma.Count == 0) return d.Cor;
            return DeChave(soma.OrderByDescending(x => x.Value[0]).ThenByDescending(x => x.Value[1]).First().Key);
        }

        public static void Pintar(View view, ElementId id, byte[] rgb) => Pintar(view, id, rgb, false);

        /// <param name="soLinhas">true: só as linhas (o preenchimento mantém a cor da família).</param>
        public static void Pintar(View view, ElementId id, byte[] rgb, bool soLinhas)
        {
            if (rgb == null)
            {
                view.SetElementOverrides(id, new OverrideGraphicSettings());
                return;
            }
            var c = new Color(rgb[0], rgb[1], rgb[2]);
            var o = view.GetElementOverrides(id);
            o.SetProjectionLineColor(c);
            o.SetCutLineColor(c);
            if (soLinhas) { view.SetElementOverrides(id, o); return; }
            try { o.SetSurfaceForegroundPatternColor(c); } catch { }
            try { o.SetSurfaceBackgroundPatternColor(c); } catch { }
            try { o.SetCutForegroundPatternColor(c); } catch { }
            try { o.SetCutBackgroundPatternColor(c); } catch { }
            view.SetElementOverrides(id, o);
        }
    }

    /// <summary>Escolha de cor da legenda.</summary>
    public enum ModoCor { Modelo = 0, Sistema = 1, Escolhida = 2, Preto = 3 }
}
