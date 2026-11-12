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
            return !n.Contains("(LEG)") && !n.Contains("(FIX)") && !n.EndsWith("__ORIG");
        }

        static readonly Regex RxSigla = new Regex(@"^\(\s*[A-Z]+\s*\)");

        /// <summary>O tipo é da disciplina: tem a sigla dela no nome da família ou, nas disciplinas por
        /// categoria (ELE, LUM), é de uma das categorias e não tem a sigla de outra disciplina.</summary>
        public bool EhDaDisciplina(FamilySymbol s)
        {
            string n = Texto.Norm(s.FamilyName);
            if (n.Contains("(LEG)") || n.Contains("(FIX)") || n.EndsWith("__ORIG")) return false;
            if (D.FamiliasN.Any(p => n.StartsWith(p))) return true;
            if (D.Categorias == null || s.Category == null) return false;
            var cid = s.Category.Id;
            if (!D.Categorias.Any(c => cid == new ElementId(c))) return false;
            return !RxSigla.IsMatch(n);
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

        // ------------------------------------------------------------ variações (baixa / média / alta…)
        static readonly System.Text.RegularExpressions.Regex RxAltura =
            new System.Text.RegularExpressions.Regex(@"\b(BAIX[AO]S?|MEDI[AO]S?|ALT[AO]S?)\b");

        static bool SimNao(Parameter p)
        {
            try { return p.StorageType == StorageType.Integer && p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo; }
            catch { return false; }
        }

        /// <summary>Parâmetros Sim/Não de instância que mudam entre as instâncias do tipo e que, na família, ligam ou
        /// desligam a visibilidade de algo (anotação aninhada, linha simbólica…): cada combinação é um desenho diferente
        /// na vista, e vira uma linha da legenda. 'vis' = ParametrosDeVisibilidade da família; null (família que não
        /// abre) usa a regra antiga: só leitura (fórmula) ou nome Baixa/Média/Alta, com pelo menos duas opções.</summary>
        static List<Definition> ParametrosDeVariacao(List<FamilyInstance> insts, HashSet<string> vis)
        {
            var r = new List<Definition>();
            if (insts.Count < 2) return r;
            if (vis != null)
            {
                foreach (Parameter p in insts[0].Parameters)
                {
                    try
                    {
                        if (!SimNao(p) || !vis.Contains(p.Definition.Name)) continue;
                        if (p.Definition is InternalDefinition idf && idf.BuiltInParameter != BuiltInParameter.INVALID) continue;
                        var d = p.Definition;
                        int vals = insts.Select(i => { try { var q = i.get_Parameter(d); return q != null ? q.AsInteger() : -1; } catch { return -1; } }).Distinct().Count();
                        if (vals > 1) r.Add(d);
                    }
                    catch { }
                }
                return r.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            var cand = new List<Parameter>();
            foreach (Parameter p in insts[0].Parameters)
            {
                try
                {
                    if (!SimNao(p)) continue;
                    if (p.Definition is InternalDefinition idf && idf.BuiltInParameter != BuiltInParameter.INVALID) continue;
                    bool nomeAltura = RxAltura.IsMatch(Texto.Norm(p.Definition.Name));
                    if (!p.IsReadOnly && !nomeAltura) continue;
                    cand.Add(p);
                }
                catch { }
            }
            // a família só tem uma opção (ex.: só "Média"): não há variação a mostrar
            if (cand.Count < 2) return r;
            foreach (Parameter p in cand)
            {
                try
                {
                    var d = p.Definition;
                    int vals = insts.Select(i => { try { var q = i.get_Parameter(d); return q != null ? q.AsInteger() : -1; } catch { return -1; } }).Distinct().Count();
                    if (vals > 1) r.Add(d);
                }
                catch { }
            }
            return r.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Há algum parâmetro Sim/Não de instância (da família) com valores diferentes entre as instâncias?</summary>
        static bool AlgumSimNaoMuda(List<FamilyInstance> insts)
        {
            if (insts.Count < 2) return false;
            foreach (Parameter p in insts[0].Parameters)
            {
                try
                {
                    if (!SimNao(p)) continue;
                    if (p.Definition is InternalDefinition idf && idf.BuiltInParameter != BuiltInParameter.INVALID) continue;
                    var d = p.Definition;
                    if (insts.Select(i => { try { var q = i.get_Parameter(d); return q != null ? q.AsInteger() : -1; } catch { return -1; } }).Distinct().Count() > 1) return true;
                }
                catch { }
            }
            return false;
        }

        static string Assinatura(FamilyInstance i, List<Definition> defs)
        {
            var on = new List<string>();
            foreach (var d in defs)
            {
                try { var q = i.get_Parameter(d); if (q != null && q.AsInteger() == 1) on.Add(d.Name.Trim().ToUpperInvariant()); } catch { }
            }
            return on.Count == 0 ? "-" : string.Join(" + ", on);
        }

        /// <summary>Desenho do símbolo na planta (sem a rotação e a posição do elemento), para comparar variações.
        /// null quando não dá para ler.</summary>
        static string AssinaturaDesenho(Acum a)
        {
            if (a.GeoInst == null || a.Geo == null) return null;
            try
            {
                var curvas = Simbolos.CurvasNaPlanta(a.GeoInst, a.Geo);
                if (curvas.Count == 0) return null;
                XYZ o = a.GeoInst.GetTransform().Origin;
                double tol = 1.0 / 304.8;   // 1 mm
                Func<XYZ, string> pk = p => Math.Round((p.X - o.X) / tol) + ":" + Math.Round((p.Y - o.Y) / tol);
                var ks = new List<string>();
                foreach (var c in curvas)
                {
                    string k1 = pk(c.GetEndPoint(0)), k2 = pk(c.GetEndPoint(1));
                    string mid = c is Line ? "" : "~" + pk(c.Evaluate(0.5, true));
                    ks.Add((string.CompareOrdinal(k1, k2) < 0 ? k1 + "|" + k2 : k2 + "|" + k1) + mid);
                }
                return string.Join(";", ks.Distinct().OrderBy(x => x, StringComparer.Ordinal));
            }
            catch { return null; }
        }

        static void Juntar(Acum destino, Acum origem)
        {
            foreach (var i in origem.Inst) destino.Inst.Add(i);
            destino.Vistos += origem.Vistos;
            foreach (var kv in origem.Cores)
            {
                if (!destino.Cores.TryGetValue(kv.Key, out var n)) destino.Cores[kv.Key] = n = new int[2];
                n[0] += kv.Value[0]; n[1] += kv.Value[1];
            }
            foreach (var kv in origem.Origem) if (!destino.Origem.ContainsKey(kv.Key)) destino.Origem[kv.Key] = kv.Value;
            if (destino.RepVis == null && origem.RepVis != null) { destino.Rep = origem.Rep; destino.RepVis = origem.RepVis; }
            if (destino.GeoVis == null && origem.GeoVis != null) { destino.Geo = origem.Geo; destino.GeoInst = origem.GeoInst; destino.GeoVis = origem.GeoVis; }
        }

        sealed class Acum
        {
            public string Sufixo;   // texto da variação (", A 0,30M DO PISO" ou " – MÉDIA")
            public string Var = "";
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
                .Where(EhDaDisciplina).ToList();
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
            // ELE: só pontos de parede (com "Elevação do ponto"), de piso, caixas e quadros; altura de cada um
            var bloco = new Dictionary<string, int>();
            var alturas = new Dictionary<string, int?>();
            if (D.PorAltura)
            {
                var ok = new List<FamilyInstance>();
                foreach (var i in insts)
                {
                    if (!Eletrica.Inclui(i, out int bl, out int? h)) continue;
                    string k = i.Id.ToString();
                    bloco[k] = bl; alturas[k] = h;
                    ok.Add(i);
                }
                insts = ok;
            }
            bool porAltura = D.PorAltura;
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

            // variações: por tipo, parâmetros Sim/Não que mudam entre as instâncias (ex.: Baixa, Média, Alta)
            double tv = Tempos.Agora;
            var assin = new Dictionary<string, string>();
            var defsTipo = new Dictionary<string, List<Definition>>();
            var visFam = new Dictionary<string, HashSet<string>>();
            int comVariacao = 0;
            if (porAltura)
            {
                // ELE: o item é tipo + altura; a variação do desenho (Baixa/Média/Alta) acompanha a altura
                foreach (var i in insts)
                {
                    var h = alturas[i.Id.ToString()];
                    if (h != null) assin[i.Id.ToString()] = "h" + h.Value;
                }
                foreach (var g in insts.GroupBy(x => x.GetTypeId().ToString()))
                    defsTipo[g.Key] = ParametrosDeVariacao(g.ToList(), null);
            }
            else foreach (var g in insts.GroupBy(x => x.GetTypeId().ToString()))
            {
                // o que muda o símbolo na vista: parâmetros que ligam a visibilidade de algo dentro da família
                var lista = g.ToList();
                if (!AlgumSimNaoMuda(lista)) continue;
                var fam = lista[0].Symbol?.Family;
                string fk = fam?.Id.ToString() ?? "";
                if (!visFam.TryGetValue(fk, out var vis))
                {
                    try { vis = Simbolos.ParametrosDeVisibilidade(doc, fam); } catch { vis = null; }
                    visFam[fk] = vis;
                }
                var defs = ParametrosDeVariacao(lista, vis);
                if (defs.Count == 0) continue;
                var sigs = lista.ToDictionary(x => x.Id.ToString(), x => Assinatura(x, defs));
                if (sigs.Values.Distinct().Count() < 2) continue;
                comVariacao++;
                foreach (var kv in sigs) assin[kv.Key] = kv.Value;
            }
            Tempos.Marca($"  variações do símbolo: {comVariacao} tipo(s), {visFam.Count} família(s) aberta(s)", tv);

            double t4 = Tempos.Agora;
            var tipos = new Dictionary<string, Acum>();
            var cache = new Cores.Cache();
            foreach (var inst in insts)
            {
                assin.TryGetValue(inst.Id.ToString(), out string sig);
                string key = inst.GetTypeId().ToString() + (sig != null ? "|" + sig : "");
                if (!tipos.TryGetValue(key, out var a)) tipos[key] = a = new Acum { Sym = inst.Symbol, Var = sig ?? "" };
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

            // texto de cada variação: as alturas, quando separam as variações do tipo; senão os parâmetros ligados
            if (comVariacao > 0)
            {
                var porId = insts.ToDictionary(x => x.Id.ToString());
                foreach (var gr in tipos.Values.Where(x => x.Var != "").GroupBy(x => x.Sym.Id.ToString()))
                {
                    var grupos = gr.ToList();
                    var hs = grupos.Select(a => a.Inst.Select(id => porId.TryGetValue(id, out var fi) ? Eletrica.AlturaGeral(fi) : null).ToList()).ToList();
                    bool porAlt = hs.All(l => l.All(x => x != null));
                    if (porAlt)
                    {
                        var sets = hs.Select(l => new HashSet<int>(l.Select(x => x.Value))).ToList();
                        for (int a1 = 0; a1 < sets.Count && porAlt; a1++)
                            for (int b1 = a1 + 1; b1 < sets.Count && porAlt; b1++)
                                if (sets[a1].Overlaps(sets[b1])) porAlt = false;
                        if (porAlt)
                            for (int k = 0; k < grupos.Count; k++) grupos[k].Sufixo = ", " + Eletrica.TextoAlturas(sets[k]);
                    }
                    if (!porAlt)
                        foreach (var a in grupos) a.Sufixo = " – " + (a.Var == "-" ? "PADRÃO" : a.Var);
                }
            }

            var famTipos = new Dictionary<string, int>();
            foreach (var a in tipos.Values.GroupBy(x => x.Sym.Id.ToString()).Select(gr => gr.First()))
            {
                string fk = a.Sym.Family.Id.ToString();
                famTipos[fk] = famTipos.TryGetValue(fk, out int c) ? c + 1 : 1;
            }
            var itens = new List<Item>();
            var ordemEle = new Dictionary<string, string>();
            foreach (var kv in tipos)
            {
                var a = kv.Value;
                var melhor = a.Cores.OrderByDescending(x => x.Value[0]).ThenByDescending(x => x.Value[1]).First().Key;
                // texto = Description (que a Codificação mantém igual ao nome do tipo); sem ela, o nome da família
                string desc = "";
                try
                {
                    // Description calculada por fórmula na família ("-", "4x2"…) não é texto de legenda
                    var pd = a.Sym.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    if (pd != null && !pd.IsReadOnly) desc = (pd.AsString() ?? "").Trim();
                    if (!Regex.IsMatch(desc, "[A-Za-zÀ-ÿ]{3}")) desc = "";
                }
                catch { }
                string rot = desc != "" ? desc.ToUpperInvariant() : RotuloItem(a.Sym, famTipos[a.Sym.Family.Id.ToString()] > 1);
                string varTxt = a.Sufixo != null ? a.Sufixo.TrimStart(',', ' ', '–') : a.Var == "-" ? "PADRÃO" : a.Var;
                string varLeg = null, ordem = null;
                int? alt = null;
                if (porAltura)
                {
                    string rk = a.Rep.Id.ToString();
                    int bl = bloco.TryGetValue(rk, out int b0) ? b0 : 0;
                    alt = alturas.TryGetValue(rk, out var h0) ? h0 : null;
                    if (D.PorAltura) { rot = Eletrica.Rotulo(a.Sym, bl, alt); ordem = Eletrica.Ordem(a.Sym, bl, alt); }
                    else if (alt != null) rot = rot + ", " + Eletrica.TextoAltura(alt.Value);
                    var defs = defsTipo.TryGetValue(a.Sym.Id.ToString(), out var dl) ? dl : new List<Definition>();
                    varLeg = defs.Count > 0 ? Assinatura(a.Rep, defs) : "";
                    if (varLeg == "-") varLeg = "PADRÃO";
                    varTxt = "";
                }
                else if (a.Var != "")
                {
                    rot = rot + (a.Sufixo ?? " – " + varTxt);
                    varLeg = a.Var == "-" ? "PADRÃO" : a.Var;   // o desenho (família (LEG)) segue a variação, não o texto
                }
                if (ordem != null) ordemEle[kv.Key] = ordem;
                itens.Add(new Item
                {
                    Sym = a.Sym, Rep = a.Rep, Quantidade = a.Inst.Count,
                    Vistas = nviews.TryGetValue(a.Sym.Id.ToString(), out var hs) ? hs.Count : 0,
                    Variante = a.Var == "" ? "" : varTxt, VarKey = a.Var, VarLeg = varLeg, Altura = alt,
                    EmPrancha = verificado ? a.Vistos > 0 : (a.Vistos > 0 ? true : (bool?)null),
                    Cor = Cores.DeChave(melhor), CorOrigem = a.Origem[melhor],
                    CoresCont = a.Cores,
                    Rotulo = rot, Padrao = rot, Codigo = Codificacao.Marca(a.Sym),
                    Geo = a.Geo, GeoInst = a.GeoInst,
                });
            }
            // desenho de cada item = os parâmetros de visibilidade da família ligados no item (no tipo ou na instância).
            // Itens da mesma família com desenhos diferentes (tipos "baixa"/"média", ou a altura mudando o símbolo)
            // ganham famílias (LEG) diferentes; com o mesmo desenho, dividem a mesma.
            double td = Tempos.Agora;
            foreach (var gf in itens.GroupBy(x => x.Sym.Family.Id.ToString()).Where(g => g.Count() > 1).ToList())
            {
                var fam = gf.First().Sym.Family;
                if (!visFam.TryGetValue(gf.Key, out var vis))
                {
                    try { vis = Simbolos.ParametrosDeVisibilidade(doc, fam); } catch { vis = null; }
                    visFam[gf.Key] = vis;
                }
                if (vis == null || vis.Count == 0) continue;
                var sigs = gf.Select(it => SigDesenho(it, vis)).ToList();
                bool varia = sigs.Distinct().Count() > 1;
                int n = 0;
                foreach (var it in gf) { it.VarLeg = varia ? sigs[n] : ""; n++; }
            }
            Tempos.Marca("  desenho por família", td);
            if (D.PorAltura)
                return itens.OrderBy(x => OrdemEle(ordemEle, x), StringComparer.Ordinal).ToList();
            return itens
                .OrderBy(x => Texto.Norm(x.Sym.Category != null ? x.Sym.Category.Name : ""), StringComparer.Ordinal)
                .ThenBy(x => Texto.Norm(x.Rotulo.Split('–')[0]), StringComparer.Ordinal)
                .ThenBy(x => x.Variante.Contains("BAIX") ? 0 : x.Variante.Contains("MEDI") || x.Variante.Contains("MÉDI") ? 1 : x.Variante.Contains("ALT") ? 2 : 3)
                .ThenBy(x => x.Rotulo, StringComparer.Ordinal)
                .ToList();
        }

        static string SigDesenho(Item it, HashSet<string> vis)
        {
            var on = new List<string>();
            foreach (var n in vis.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var p = it.Rep?.LookupParameter(n) ?? it.Sym.LookupParameter(n);
                    if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() == 1) on.Add(n.Trim().ToUpperInvariant());
                }
                catch { }
            }
            return on.Count == 0 ? "PADRÃO" : string.Join(" + ", on);
        }

        static string OrdemEle(Dictionary<string, string> ordem, Item x)
        {
            string k = x.Sym.Id.ToString() + (x.VarKey != "" ? "|" + x.VarKey : "");
            return ordem.TryGetValue(k, out string o) ? o : "9";
        }

        /// <summary>Item acrescentado à mão: tipo de símbolo (anotação genérica) ou detail item.</summary>
        public static Item ItemExtra(FamilySymbol sym, string rotulo = null)
        {
            string nome = rotulo ?? Regex.Replace(sym.FamilyName.Trim(), @"^(\([A-Z]+\)\s*)+", "").ToUpperInvariant();
            // símbolos fixos criados pela ferramenta: o preenchimento tem cor própria (ex.: eletrocalha)
            bool fixo = Texto.Norm(sym.FamilyName).Contains("(FIX)");
            return new Item { Sym = sym, Extra = true, Rotulo = nome, Padrao = nome, SoLinhas = fixo };
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
