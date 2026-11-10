using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace FJA.Revit.Legendas
{
    /// <summary>Itens da legenda (tipos da disciplina posicionados no modelo) e a cor de cada um.</summary>
    public sealed class Coleta
    {
        readonly Document doc;
        readonly Disciplina D;

        public Coleta(Document doc, Disciplina d) { this.doc = doc; D = d; }

        public static readonly ViewType[] TiposPulados =
        {
            ViewType.Legend, ViewType.Schedule, ViewType.DraftingView, ViewType.DrawingSheet,
            ViewType.PanelSchedule, ViewType.ColumnSchedule
        };

        public bool EhFamiliaDaDisciplina(string nome)
        {
            string n = Texto.Norm(nome);
            if (!D.FamiliasN.Any(p => n.StartsWith(p))) return false;
            return !n.Contains("(LEG)") && !n.EndsWith("__ORIG");
        }

        public string RotuloItem(FamilySymbol sym, bool multi)
        {
            string nome = Regex.Replace(sym.FamilyName.Trim(), @"\.\d{4}$", "");
            foreach (string pf in D.FamiliasN)
            {
                if (Texto.Norm(nome).StartsWith(pf))
                {
                    nome = nome.Substring(Math.Min(pf.Length, nome.Length)).Trim(' ', '-', '_');
                    break;
                }
            }
            if (multi) nome = nome + " - " + Texto.Nome(sym).Trim();
            return nome.ToUpperInvariant();
        }

        public List<View> VistasEmPranchas()
        {
            var vistas = new Dictionary<string, View>();
            foreach (ViewSheet sh in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)))
            {
                try
                {
                    if (sh.IsTemplate || sh.IsPlaceholder) continue;
                    foreach (ElementId vid in sh.GetAllPlacedViews())
                    {
                        var v = doc.GetElement(vid) as View;
                        if (v == null || v.IsTemplate || TiposPulados.Contains(v.ViewType)) continue;
                        vistas[vid.ToString()] = v;
                    }
                }
                catch { }
            }
            return vistas.Values.ToList();
        }

        readonly Dictionary<string, bool> tipoVista = new Dictionary<string, bool>();

        bool EmVistaDeLegenda(Element e)
        {
            ElementId vid = e.OwnerViewId;
            if (vid == null || vid == ElementId.InvalidElementId) return false;
            string k = vid.ToString();
            if (!tipoVista.TryGetValue(k, out bool r))
            {
                var v = doc.GetElement(vid) as View;
                r = v == null || TiposPulados.Contains(v.ViewType);
                tipoVista[k] = r;
            }
            return r;
        }

        sealed class Acum
        {
            public FamilySymbol Sym;
            public FamilyInstance Rep; public View RepVis;
            public View Geo; public FamilyInstance GeoInst; public View GeoVis;
            public HashSet<string> Inst = new HashSet<string>();
            public Dictionary<string, int[]> Cores = new Dictionary<string, int[]>();
            public Dictionary<string, string> Origem = new Dictionary<string, string>();
            public int Vistos;
        }

        /// <summary>Um item por tipo da disciplina posicionado no modelo (aparecendo ou não em prancha).
        /// Não percorre as vistas das pranchas: a cor é calculada numa vista de referência e a verificação
        /// "aparece em prancha" só olha as plantas da disciplina, com prazo.</summary>
        public List<Item> Itens(double prazoVerificacao = 4.0)
        {
            double t0 = Tempos.Agora;
            var syms = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => EhFamiliaDaDisciplina(s.FamilyName)).ToList();
            var symOk = new HashSet<string>(syms.Select(s => s.Id.ToString()));
            var cats = new Dictionary<string, ElementId>();
            foreach (var s in syms)
                if (s.Category != null) cats[s.Category.Id.ToString()] = s.Category.Id;
            Tempos.Marca($"  tipos da disciplina: {syms.Count} em {cats.Count} categoria(s)", t0);
            if (symOk.Count == 0 || cats.Count == 0) return new List<Item>();
            var catf = new ElementMulticategoryFilter(cats.Values.ToList());

            double t1 = Tempos.Agora;
            var insts = new FilteredElementCollector(doc).WherePasses(catf).OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(e => e.SuperComponent == null && symOk.Contains(e.GetTypeId().ToString()) && !EmVistaDeLegenda(e))
                .ToList();
            Tempos.Marca($"  instâncias no modelo: {insts.Count}", t1);
            if (insts.Count == 0) return new List<Item>();

            double t2 = Tempos.Agora;
            var vistas = VistasEmPranchas();
            var vn = D.VistasN;
            var disc = vistas.Where(v => vn.Length > 0 && vn.Any(p => Texto.Norm(v.Name).StartsWith(p))).ToList();
            var plantasDisc = disc.Where(v => v is ViewPlan).ToList();
            var plantas = vistas.Where(v => v is ViewPlan).ToList();
            var porNivel = new Dictionary<string, List<View>>();
            foreach (var v in plantasDisc.Concat(plantas.Where(p => !plantasDisc.Contains(p))))
            {
                string lid = null;
                try { lid = ((ViewPlan)v).GenLevel?.Id.ToString(); } catch { }
                if (lid == null) continue;
                if (!porNivel.TryGetValue(lid, out var l)) porNivel[lid] = l = new List<View>();
                l.Add(v);
            }
            View refGeral = plantasDisc.FirstOrDefault() ?? disc.FirstOrDefault() ?? plantas.FirstOrDefault() ?? vistas.FirstOrDefault();
            Tempos.Marca($"  vistas em pranchas: {vistas.Count} ({plantasDisc.Count} plantas {string.Join("/", D.Vistas ?? new string[0])})", t2);

            double t3 = Tempos.Agora;
            var vistos = new Dictionary<string, View>();
            var nviews = new Dictionary<string, HashSet<string>>();
            bool verificado = true;
            foreach (var v in plantasDisc)
            {
                if (Tempos.Agora - t3 > prazoVerificacao) { verificado = false; break; }
                try
                {
                    foreach (Element e in new FilteredElementCollector(doc, v.Id).WherePasses(catf).OfClass(typeof(FamilyInstance)))
                    {
                        string k = e.GetTypeId().ToString();
                        if (!symOk.Contains(k)) continue;
                        vistos[e.Id.ToString()] = v;
                        if (!nviews.TryGetValue(k, out var hs)) nviews[k] = hs = new HashSet<string>();
                        hs.Add(v.Id.ToString());
                    }
                }
                catch { }
            }
            Tempos.Marca($"  conferir nas {plantasDisc.Count} plantas em prancha" + (verificado ? "" : " (interrompido)"), t3);

            double t4 = Tempos.Agora;
            var tipos = new Dictionary<string, Acum>();
            var cache = new Cores.Cache();
            foreach (var inst in insts)
            {
                string key = inst.GetTypeId().ToString();
                if (!tipos.TryGetValue(key, out var a)) tipos[key] = a = new Acum { Sym = inst.Symbol };
                a.Inst.Add(inst.Id.ToString());
                vistos.TryGetValue(inst.Id.ToString(), out View vVis);
                if (vVis != null) a.Vistos++;
                View v = vVis;
                if (v == null)
                {
                    List<View> cand = null;
                    try { if (inst.LevelId != null) porNivel.TryGetValue(inst.LevelId.ToString(), out cand); } catch { }
                    v = cand != null && cand.Count > 0 ? cand[0] : refGeral;
                }
                if (a.Rep == null || (vVis != null && a.RepVis == null)) { a.Rep = inst; a.RepVis = vVis; }
                if (v is ViewPlan && (a.Geo == null || (vVis != null && a.GeoVis == null))) { a.Geo = v; a.GeoInst = inst; a.GeoVis = vVis; }
                byte[] cor; string origem;
                if (v != null)
                {
                    try { Cores.Efetiva(doc, v, inst, cache, D, out cor, out origem); }
                    catch { cor = D.Cor; origem = "padrão"; }
                }
                else { cor = D.Cor; origem = "padrão"; }
                string ck = Cores.Chave(cor);
                if (!a.Cores.TryGetValue(ck, out var n)) a.Cores[ck] = n = new int[2];
                n[vVis != null ? 0 : 1]++;
                if (!a.Origem.ContainsKey(ck)) a.Origem[ck] = origem;
            }
            Tempos.Marca("  cores e representantes", t4);

            var famTipos = new Dictionary<string, int>();
            foreach (var a in tipos.Values)
            {
                string fk = a.Sym.Family.Id.ToString();
                famTipos[fk] = famTipos.TryGetValue(fk, out int c) ? c + 1 : 1;
            }
            var itens = new List<Item>();
            foreach (var kv in tipos)
            {
                var a = kv.Value;
                var melhor = a.Cores.OrderByDescending(x => x.Value[0]).ThenByDescending(x => x.Value[1]).First().Key;
                // texto = Description (que a Codificação mantém igual ao nome do tipo); sem ela, o nome da família
                string desc = "";
                try { desc = (a.Sym.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() ?? "").Trim(); } catch { }
                string rot = desc != "" ? desc.ToUpperInvariant() : RotuloItem(a.Sym, famTipos[a.Sym.Family.Id.ToString()] > 1);
                itens.Add(new Item
                {
                    Sym = a.Sym, Rep = a.Rep, Quantidade = a.Inst.Count,
                    Vistas = nviews.TryGetValue(kv.Key, out var hs) ? hs.Count : 0,
                    EmPrancha = verificado ? a.Vistos > 0 : (a.Vistos > 0 ? true : (bool?)null),
                    Cor = Cores.DeChave(melhor), CorOrigem = a.Origem[melhor],
                    CoresCont = a.Cores,
                    Rotulo = rot, Padrao = rot, Codigo = Codificacao.Marca(a.Sym),
                    Geo = a.Geo, GeoInst = a.GeoInst,
                });
            }
            return itens
                .OrderBy(x => Texto.Norm(x.Sym.Category != null ? x.Sym.Category.Name : ""), StringComparer.Ordinal)
                .ThenBy(x => Texto.Norm(x.Rotulo), StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Item acrescentado à mão: tipo de símbolo (anotação genérica) ou detail item.</summary>
        public static Item ItemExtra(FamilySymbol sym, string rotulo = null)
        {
            string nome = rotulo ?? Regex.Replace(sym.FamilyName.Trim(), @"^\([A-Z]+\)\s*", "").ToUpperInvariant();
            return new Item { Sym = sym, Extra = true, Rotulo = nome, Padrao = nome };
        }

        /// <summary>Tipos de anotação genérica e de detail item do projeto (para acrescentar), sem os (LEG).</summary>
        public List<FamilySymbol> SimbolosParaExtra()
        {
            var res = new List<FamilySymbol>();
            foreach (var bic in new[] { BuiltInCategory.OST_GenericAnnotation, BuiltInCategory.OST_DetailComponents })
            {
                foreach (FamilySymbol s in new FilteredElementCollector(doc).OfCategory(bic).OfClass(typeof(FamilySymbol)))
                {
                    string n = Texto.Norm(s.FamilyName);
                    if (n.Contains("(LEG)") || n.EndsWith("__ORIG")) continue;
                    res.Add(s);
                }
            }
            return res.OrderBy(s => Texto.Norm(s.Category?.Name), StringComparer.Ordinal)
                .ThenBy(s => Texto.Norm(s.FamilyName), StringComparer.Ordinal)
                .ThenBy(s => Texto.Norm(s.Name), StringComparer.Ordinal).ToList();
        }
    }
}
