using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Links
{
    /// <summary>
    /// Elemento de origem no link. Peças: FamilyInstance (Equipamentos; conexões e acessórios das redes também são
    /// FamilyInstance). Trechos: MEPCurve (tubos, dutos, eletrocalhas, eletrodutos, flexíveis).
    /// </summary>
    public sealed class Origem
    {
        public Element E;
        public string Uid, Cat, Fam, Tipo, Nivel, Sistema, Dim;
        /// <summary>"ok", "hospedeiro" (precisa do hospedeiro) ou null (não entra).</summary>
        public string Sit;
        public readonly List<ElementId> Copias = new List<ElementId>();
        public bool Trecho => E is MEPCurve;

        public string Estado => Sit == "hospedeiro" ? "hospedeiro" : Copias.Count > 0 ? "copiado" : "novo";

        public Origem(Element e, Document ldoc)
        {
            E = e;
            Uid = e.UniqueId;
            Cat = e.Category != null ? e.Category.Name : "?";
            var tipo = ldoc.GetElement(e.GetTypeId()) as ElementType;
            Fam = tipo != null ? tipo.FamilyName : "?";
            Tipo = tipo != null ? tipo.Name : "?";
            Nivel = Motor.NomeNivel(ldoc, e);
            Sit = Motor.SituacaoOrigem(e);
            Sistema = Motor.NomeSistema(ldoc, e);
            Dim = Motor.Dimensao(e);
        }
    }

    /// <summary>Copiar do link e Verificar alterações: coleta, cópia, registro, comparação e atualização.</summary>
    public static class Motor
    {
        public const int LOTE = 1000;

        // ------------------------------------------------------------ dados do elemento
        static readonly Dictionary<int, List<Level>> niveis = new Dictionary<int, List<Level>>();

        public static string NomeNivel(Document ldoc, Element e)
        {
            ElementId lid = ElementId.InvalidElementId;
            if (e is MEPCurve mc && mc.ReferenceLevel != null) lid = mc.ReferenceLevel.Id;
            if (lid == ElementId.InvalidElementId) lid = e.LevelId;
            if (lid == null || lid == ElementId.InvalidElementId)
                foreach (var bip in new[] { BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.RBS_START_LEVEL_PARAM })
                {
                    try { var p = e.get_Parameter(bip); if (p != null && p.AsElementId() != ElementId.InvalidElementId) { lid = p.AsElementId(); break; } } catch { }
                }
            Level lv = lid != null && lid != ElementId.InvalidElementId ? ldoc.GetElement(lid) as Level : null;
            if (lv == null)
            {
                try
                {
                    XYZ o = Pontas(e).FirstOrDefault() ?? (e as FamilyInstance)?.GetTotalTransform().Origin;
                    if (o != null)
                    {
                        int k = ldoc.GetHashCode();
                        if (!niveis.TryGetValue(k, out var l)) niveis[k] = l = new FilteredElementCollector(ldoc).OfClass(typeof(Level)).Cast<Level>().ToList();
                        lv = l.Where(x => x.ProjectElevation <= o.Z + 0.01).OrderByDescending(x => x.ProjectElevation).FirstOrDefault();
                    }
                }
                catch { }
            }
            return lv != null ? lv.Name : "";
        }

        public static string SituacaoOrigem(Element e)
        {
            if (e is MEPCurve) return "ok";
            if (!(e is FamilyInstance fi)) return null;
            try { if (fi.SuperComponent != null) return null; } catch { }
            try { var op = fi.DesignOption; if (op != null && !op.IsPrimary) return null; } catch { }
            var host = fi.Host;
            if (host == null || host is Level) return "ok";
            if (fi.HostFace != null) return "ok";
            try { if (fi.Symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted) return "hospedeiro"; } catch { }
            return "ok";
        }

        public static string NomeSistema(Document d, Element e)
        {
            try
            {
                var p = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                string s = p?.AsString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
                foreach (var bip in new[] { BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM })
                {
                    var q = e.get_Parameter(bip);
                    if (q != null && q.AsElementId() != ElementId.InvalidElementId) { var t = d.GetElement(q.AsElementId()); if (t != null) return t.Name; }
                }
                var sv = e.get_Parameter(BuiltInParameter.RBS_CTC_SERVICE_TYPE)?.AsString();
                if (!string.IsNullOrWhiteSpace(sv)) return sv;
            }
            catch { }
            return "";
        }

        public static string Dimensao(Element e)
        {
            try
            {
                var p = e.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE);
                string s = p?.AsString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
                return p?.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        public static ConnectorManager Conectores(Element e)
        {
            if (e is MEPCurve mc) return mc.ConnectorManager;
            if (e is FamilyInstance fi && fi.MEPModel != null) return fi.MEPModel.ConnectorManager;
            return null;
        }

        static bool Fisico(Connector c)
        {
            try { return c.ConnectorType == ConnectorType.End || c.ConnectorType == ConnectorType.Curve || c.ConnectorType == ConnectorType.Physical; }
            catch { return false; }
        }

        /// <summary>Pontas de um trecho (conectores de ponta, na ordem do Id).</summary>
        public static List<XYZ> Pontas(Element e)
        {
            var r = new List<XYZ>();
            if (!(e is MEPCurve)) return r;
            var cm = Conectores(e);
            if (cm != null)
                foreach (Connector c in cm.Connectors)
                    if (c.ConnectorType == ConnectorType.End) r.Add(c.Origin);
            if (r.Count == 0 && e.Location is LocationCurve lc) { r.Add(lc.Curve.GetEndPoint(0)); r.Add(lc.Curve.GetEndPoint(1)); }
            return r;
        }

        // ------------------------------------------------------------ parâmetros acompanhados
        static Parameter Param(Element e, string nome)
        {
            if (nome.ToUpperInvariant() == nome && Enum.TryParse(nome, out BuiltInParameter bip)) return e.get_Parameter(bip);
            return e.LookupParameter(nome);
        }

        public static string ValorParam(Element e, string nome)
        {
            try
            {
                var p = Param(e, nome);
                if (p == null || !p.HasValue) return "";
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString() ?? "";
                    case StorageType.Double: return p.AsDouble().ToString("0.000000", CultureInfo.InvariantCulture);
                    case StorageType.Integer: return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                    default: return p.AsValueString() ?? "";
                }
            }
            catch { return ""; }
        }

        public static string RotuloParam(Element e, string nome)
        {
            try { var p = Param(e, nome); if (p != null) return p.Definition.Name; } catch { }
            return nome;
        }

        static bool CopiarParam(Element src, Element dst, string nome)
        {
            var ps = Param(src, nome); var pd = Param(dst, nome);
            if (ps == null || pd == null || pd.IsReadOnly || ps.StorageType != pd.StorageType) return false;
            switch (ps.StorageType)
            {
                case StorageType.String: return pd.Set(ps.AsString() ?? "");
                case StorageType.Double: return pd.Set(ps.AsDouble());
                case StorageType.Integer: return pd.Set(ps.AsInteger());
            }
            return false;
        }

        // ------------------------------------------------------------ foto
        /// <summary>Estado da origem (coordenadas do link, em pés). Peças: o mesmo formato do Python.</summary>
        public static Dictionary<string, object> Foto(Element e, Origem o, Modulo mod)
        {
            var f = new Dictionary<string, object>
            {
                ["cat"] = o.Cat, ["fam"] = o.Fam, ["tipo"] = o.Tipo, ["niv"] = o.Nivel,
            };
            if (e is FamilyInstance fi)
            {
                var t = fi.GetTotalTransform();
                f["o"] = Geo.V(t.Origin); f["x"] = Geo.V(t.BasisX); f["z"] = Geo.V(t.BasisZ);
                f["esp"] = fi.Mirrored;
                f["face"] = fi.HostFace != null && !(fi.Host is Level);
            }
            if (e is MEPCurve) f["e"] = Pontas(e).Select(p => (object)Geo.V(p)).ToList();
            if (mod.Redes) { f["sis"] = o.Sistema; f["dim"] = o.Dim; f["k"] = "rede"; }
            var p = new Dictionary<string, object>();
            foreach (var n in mod.Parametros) p[n] = ValorParam(e, n);
            f["p"] = p;
            return f;
        }

        static Dictionary<string, object> PoseCopia(Element n)
        {
            if (n is FamilyInstance fi) return Geo.Pose(fi.GetTotalTransform());
            return new Dictionary<string, object> { ["e"] = Pontas(n).Select(p => (object)Geo.V(p)).ToList() };
        }

        /// <summary>Grava/regrava o registro da cópia: foto da origem, posição do link (lt) e da cópia (c).</summary>
        public static void Registrar(Element n, Origem o, LinkInfo link, Modulo mod, string por)
        {
            var f = Foto(o.E, o, mod);
            f["lt"] = Geo.Pose(link.Transf);
            f["c"] = PoseCopia(n);
            Registro.Gravar(n, new Dictionary<string, string>
            {
                ["modulo"] = mod.Id, ["link"] = link.Tipo, ["linkInst"] = link.Uid, ["origem"] = o.Uid,
                ["foto"] = Json.Gravar(f), ["por"] = por ?? "",
            });
        }

        // ------------------------------------------------------------ coleta
        /// <summary>{(instância do link, origem): cópias no modelo}</summary>
        public static Dictionary<string, List<ElementId>> Copias(Document doc)
        {
            var r = new Dictionary<string, List<ElementId>>();
            foreach (var e in Registro.Elementos(doc))
            {
                var d = Registro.Ler(e);
                if (d == null) continue;
                string k = d.LinkInst + "|" + d.Origem;
                if (!r.TryGetValue(k, out var l)) r[k] = l = new List<ElementId>();
                l.Add(e.Id);
            }
            return r;
        }

        public static List<Origem> Elementos(Document doc, LinkInfo link, Modulo mod)
        {
            var r = new List<Origem>();
            if (link == null || !link.Carregado) return r;
            var cats = mod.IdsCategorias();
            if (cats.Count == 0) return r;
            niveis.Clear();
            var ja = Copias(doc);
            var col = new FilteredElementCollector(link.Doc).WherePasses(new ElementMulticategoryFilter(cats)).WhereElementIsNotElementType();
            foreach (Element e in col)
            {
                if (!(e is FamilyInstance) && !(e is MEPCurve)) continue;
                var o = new Origem(e, link.Doc);
                if (o.Sit == null) continue;
                if (ja.TryGetValue(link.Uid + "|" + o.Uid, out var l)) o.Copias.AddRange(l.Where(i => doc.GetElement(i) != null));
                r.Add(o);
            }
            return r;
        }

        /// <summary>A rede conectada no link a partir dos elementos dados (só elementos do módulo).</summary>
        public static HashSet<ElementId> RedeConectada(Document ldoc, IEnumerable<ElementId> inicio, Modulo mod, int limite = 50000)
        {
            var cats = new HashSet<long>(mod.IdsCategorias().Select(Id));
            var vistos = new HashSet<ElementId>();
            var fila = new Queue<ElementId>(inicio);
            while (fila.Count > 0 && vistos.Count < limite)
            {
                var id = fila.Dequeue();
                if (vistos.Contains(id)) continue;
                var e = ldoc.GetElement(id);
                if (e == null || e.Category == null || !cats.Contains(Id(e.Category.Id))) continue;
                vistos.Add(id);
                var cm = Conectores(e);
                if (cm == null) continue;
                foreach (Connector c in cm.Connectors)
                {
                    if (!Fisico(c) || !c.IsConnected) continue;
                    foreach (Connector r in c.AllRefs)
                    {
                        try { if (r.Owner != null && r.Owner.Id != id && Fisico(r) && !vistos.Contains(r.Owner.Id)) fila.Enqueue(r.Owner.Id); } catch { }
                    }
                }
            }
            return vistos;
        }

        public static long Id(ElementId id)
        {
#if REVIT2024
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        // ------------------------------------------------------------ cópia
        sealed class UsarTiposDoModelo : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes;
        }

        /// <summary>Apaga avisos (não erros) durante a cópia, para o Revit não abrir caixas a cada lote.</summary>
        public sealed class Silenciar : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (var f in fa.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }

        public static void OpcoesTransacao(Transaction t)
        {
            var fo = t.GetFailureHandlingOptions();
            fo.SetFailuresPreprocessor(new Silenciar());
            t.SetFailureHandlingOptions(fo);
        }

        public sealed class ResultadoCopia
        {
            public readonly List<KeyValuePair<Origem, Element>> Copiados = new List<KeyValuePair<Origem, Element>>();
            public readonly List<KeyValuePair<Origem, string>> Falhas = new List<KeyValuePair<Origem, string>>();
            public readonly List<string> Avisos = new List<string>();
        }

        static List<ElementId> Isolamentos(Document ldoc, Element e)
        {
            var r = new List<ElementId>();
            try { r.AddRange(InsulationLiningBase.GetInsulationIds(ldoc, e.Id)); } catch { }
            try { r.AddRange(InsulationLiningBase.GetLiningIds(ldoc, e.Id)); } catch { }
            return r;
        }

        static List<Element> CopiarIds(Document doc, LinkInfo link, List<Origem> lote, CopyPasteOptions opc, bool comIsolamento)
        {
            var ids = new List<ElementId>(lote.Select(o => o.E.Id));
            if (comIsolamento) foreach (var o in lote) ids.AddRange(Isolamentos(link.Doc, o.E));
            var novos = ElementTransformUtils.CopyElements(link.Doc, ids, doc, link.Transf, opc);
            doc.Regenerate();
            var r = new List<Element>();
            foreach (var i in novos)
            {
                var n = doc.GetElement(i);
                if (n is MEPCurve) r.Add(n);
                else if (n is FamilyInstance fi) { try { if (fi.SuperComponent == null) r.Add(n); } catch { r.Add(n); } }
            }
            return r;
        }

        /// <summary>Liga cada origem à sua cópia: mesma família e tipo, posição transformada mais próxima.</summary>
        static void Casar(LinkInfo link, List<Origem> lote, List<Element> novos, List<KeyValuePair<Origem, Element>> pares, List<Origem> sem)
        {
            var livres = novos.GroupBy(n => n.GetType().Name + "|" + Chave(n.Document, n)).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var o in lote)
            {
                string k = o.E.GetType().Name + "|" + o.Fam + "|" + o.Tipo;
                if (!livres.TryGetValue(k, out var cand) || cand.Count == 0) { sem.Add(o); continue; }
                Element melhor = null; double dm = double.MaxValue;
                if (o.E is FamilyInstance fo)
                {
                    XYZ alvo = link.Transf.OfPoint(fo.GetTotalTransform().Origin);
                    foreach (var n in cand) { double d = ((FamilyInstance)n).GetTotalTransform().Origin.DistanceTo(alvo); if (d < dm) { dm = d; melhor = n; } }
                }
                else
                {
                    var pa = Pontas(o.E).Select(p => link.Transf.OfPoint(p)).ToList();
                    foreach (var n in cand)
                    {
                        var pb = Pontas(n);
                        double d = pa.Count == pb.Count ? pa.Sum(p => pb.Min(q => q.DistanceTo(p))) : double.MaxValue / 2;
                        if (d < dm) { dm = d; melhor = n; }
                    }
                }
                if (melhor == null) { sem.Add(o); continue; }
                cand.Remove(melhor);
                pares.Add(new KeyValuePair<Origem, Element>(o, melhor));
            }
        }

        static string Chave(Document d, Element n)
        {
            var t = d.GetElement(n.GetTypeId()) as ElementType;
            return (t?.FamilyName ?? "?") + "|" + (t?.Name ?? "?");
        }

        /// <summary>
        /// Copia as origens para o modelo (dentro de uma Transaction aberta pelo chamador), em lotes grandes: elementos
        /// copiados juntos mantêm as ligações entre si. Lote recusado é dividido ao meio até isolar o que falha.
        /// </summary>
        public static ResultadoCopia Copiar(Document doc, LinkInfo link, List<Origem> origens, Modulo mod, WorksetId workset, string por, Action<int, int> progresso = null)
        {
            var opc = new CopyPasteOptions();
            opc.SetDuplicateTypeNamesHandler(new UsarTiposDoModelo());
            var res = new ResultadoCopia();
            var alvo = origens.Where(o => o.Sit == "ok").ToList();
            foreach (var o in origens.Where(o => o.Sit == "hospedeiro"))
                res.Falhas.Add(new KeyValuePair<Origem, string>(o, "precisa do hospedeiro (família baseada em parede/forro/piso)"));
            int feitos = 0;
            for (int i = 0; i < alvo.Count; i += LOTE)
            {
                var lote = alvo.Skip(i).Take(LOTE).ToList();
                var pares = new List<KeyValuePair<Origem, Element>>();
                var sem = new List<Origem>();
                CopiarLote(doc, link, lote, opc, false, pares, sem, res); // o isolamento vem sozinho com o tubo/duto (testado no Revit 2027)
                foreach (var o in sem) res.Falhas.Add(new KeyValuePair<Origem, string>(o, "a cópia não foi encontrada depois de copiar"));
                foreach (var kv in pares)
                {
                    var n = kv.Value;
                    if (workset != null && doc.IsWorkshared)
                    {
                        try { var p = n.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM); if (p != null && !p.IsReadOnly) p.Set(workset.IntegerValue); } catch { }
                    }
                    Registrar(n, kv.Key, link, mod, por);
                    res.Copiados.Add(kv);
                }
                feitos += lote.Count;
                progresso?.Invoke(feitos, alvo.Count);
            }
            return res;
        }

        static void CopiarLote(Document doc, LinkInfo link, List<Origem> lote, CopyPasteOptions opc, bool isol,
            List<KeyValuePair<Origem, Element>> pares, List<Origem> sem, ResultadoCopia res)
        {
            var st = new SubTransaction(doc);
            st.Start();
            try
            {
                var novos = CopiarIds(doc, link, lote, opc, isol);
                Casar(link, lote, novos, pares, sem);
                st.Commit();
                return;
            }
            catch (Exception ex)
            {
                if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                if (isol)
                {
                    // de novo sem o isolamento (o Revit às vezes recusa o conjunto)
                    var st2 = new SubTransaction(doc);
                    st2.Start();
                    try
                    {
                        var novos = CopiarIds(doc, link, lote, opc, false);
                        Casar(link, lote, novos, pares, sem);
                        st2.Commit();
                        res.Avisos.Add("um lote foi copiado sem isolamento/revestimento");
                        return;
                    }
                    catch { if (st2.HasStarted() && !st2.HasEnded()) st2.RollBack(); }
                }
                if (lote.Count == 1) { res.Falhas.Add(new KeyValuePair<Origem, string>(lote[0], ex.Message)); return; }
            }
            int m = lote.Count / 2;
            CopiarLote(doc, link, lote.Take(m).ToList(), opc, isol, pares, sem, res);
            CopiarLote(doc, link, lote.Skip(m).ToList(), opc, isol, pares, sem, res);
        }

        // ------------------------------------------------------------ worksets
        public static List<Workset> Worksets(Document doc)
        {
            if (!doc.IsWorkshared) return new List<Workset>();
            return new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets().OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ============================================================ verificar alterações
        public static readonly string[] ESTADOS = { "ok", "link", "projeto", "ambos", "apagado", "novo", "semlink" };

        public sealed class Item
        {
            public Element Copia;
            public Registro.Dados Reg;
            public LinkInfo Link;
            public Element Src;
            public Origem Origem;
            public Modulo Mod;
            public string Estado = "ok";
            public readonly List<string> DifLink = new List<string>(), DifProj = new List<string>();
            public int LigacoesFaltando;
            public string Cat = "", Fam = "", Tipo = "", Nivel = "", Sistema = "";
            public bool Rede => Mod != null && Mod.Redes;
            public string Difs => string.Join(" · ", DifLink.Select(d => "link: " + d).Concat(DifProj.Select(d => "projeto: " + d)));
        }

        public static List<Item> Verificar(Document doc, bool novos = true)
        {
            var mods = Modulo.Todos().ToDictionary(m => m.Id);
            var lks = LinkInfo.Todos(doc).ToDictionary(l => l.Uid);
            var itens = new List<Item>();
            var usados = new Dictionary<string, HashSet<string>>();
            var copiaPorOrigem = new Dictionary<string, Element>();
            niveis.Clear();
            foreach (var e in Registro.Elementos(doc))
            {
                var r = Registro.Ler(e);
                if (r == null || !(e is FamilyInstance || e is MEPCurve)) continue;
                var f = r.Foto;
                var it = new Item
                {
                    Copia = e, Reg = r, Cat = f.Txt("cat") ?? "", Fam = f.Txt("fam") ?? "", Tipo = f.Txt("tipo") ?? "", Nivel = f.Txt("niv") ?? "",
                    Sistema = f.Txt("sis") ?? "",
                    Mod = mods.TryGetValue(r.Modulo ?? "", out var md) ? md : new Modulo { Id = r.Modulo, Nome = r.Modulo, Tipo = f.Txt("k") == "rede" ? "redes" : "pecas" },
                };
                lks.TryGetValue(r.LinkInst ?? "", out var lk);
                it.Link = lk;
                itens.Add(it);
                if (lk == null || !lk.Carregado) { it.Estado = "semlink"; continue; }
                if (!usados.TryGetValue(lk.Uid, out var u)) usados[lk.Uid] = u = new HashSet<string>();
                u.Add(r.Origem);
                copiaPorOrigem[lk.Uid + "|" + r.Origem] = e;
                var src = lk.Doc.GetElement(r.Origem);
                if (src == null || !(src is FamilyInstance || src is MEPCurve)) { it.Estado = "apagado"; continue; }
                it.Src = src;
                it.Origem = new Origem(src, lk.Doc);
                it.Cat = it.Origem.Cat; it.Fam = it.Origem.Fam; it.Tipo = it.Origem.Tipo; it.Nivel = it.Origem.Nivel; it.Sistema = it.Origem.Sistema;
                Comparar(doc, it, f);
            }
            // ligações do link que faltam nas cópias (redes)
            foreach (var it in itens.Where(i => i.Src != null))
            {
                it.LigacoesFaltando = Reconexao.Faltando(it.Src, it.Copia, it.Link, copiaPorOrigem);
                if (it.LigacoesFaltando > 0)
                {
                    it.DifProj.Add(it.LigacoesFaltando == 1 ? "1 ligação do link desfeita" : it.LigacoesFaltando + " ligações do link desfeitas");
                    it.Estado = it.DifLink.Count > 0 ? "ambos" : "projeto";
                }
            }
            if (novos)
                foreach (var kv in usados)
                {
                    var lk = lks[kv.Key];
                    foreach (var mid in itens.Where(i => i.Link == lk).Select(i => i.Reg.Modulo).Distinct())
                    {
                        if (!mods.TryGetValue(mid ?? "", out var mod)) continue;
                        foreach (var o in Elementos(doc, lk, mod))
                        {
                            if (o.Estado != "novo" || kv.Value.Contains(o.Uid)) continue;
                            itens.Add(new Item { Link = lk, Src = o.E, Origem = o, Mod = mod, Cat = o.Cat, Fam = o.Fam, Tipo = o.Tipo, Nivel = o.Nivel, Sistema = o.Sistema, Estado = "novo" });
                        }
                    }
                }
            return itens;
        }

        static void Comparar(Document doc, Item it, Dictionary<string, object> f)
        {
            var src = it.Src; var o = it.Origem; var lk = it.Link;
            var agora = Foto(src, o, it.Mod);
            var dl = it.DifLink; var dp = it.DifProj;
            if (agora.Txt("fam") != f.Txt("fam") || agora.Txt("tipo") != f.Txt("tipo"))
                dl.Add("tipo (" + f.Txt("fam") + " : " + f.Txt("tipo") + " → " + agora.Txt("fam") + " : " + agora.Txt("tipo") + ")");
            if (src is MEPCurve) dl.AddRange(Geo.DifPontas(Geo.Pontos(f.ContainsKey("e") ? f["e"] : null), Pontas(src)));
            else
            {
                dl.AddRange(Geo.DifPose(f, agora));
                bool fe = f.ContainsKey("esp") && f["esp"] is bool bb && bb, ae = agora["esp"] is bool ba && ba;
                if (f.ContainsKey("esp") && fe != ae) dl.Add(ae ? "espelhado" : "deixou de ser espelhado");
            }
            if (f.ContainsKey("niv") && f.Txt("niv") != agora.Txt("niv"))
                dl.Add("nível (" + Vazio(f.Txt("niv")) + " → " + Vazio(agora.Txt("niv")) + ")");
            if (f.ContainsKey("dim") && f.Txt("dim") != agora.Txt("dim")) dl.Add("dimensão (" + Vazio(f.Txt("dim")) + " → " + Vazio(agora.Txt("dim")) + ")");
            if (f.ContainsKey("sis") && f.Txt("sis") != agora.Txt("sis")) dl.Add("sistema (" + Vazio(f.Txt("sis")) + " → " + Vazio(agora.Txt("sis")) + ")");
            var pf = f.Obj("p"); var pa = agora.Obj("p");
            foreach (var kv in pa.OrderBy(k => k.Key))
                if (pf.ContainsKey(kv.Key) && pf.Txt(kv.Key) != (kv.Value as string))
                    dl.Add(RotuloParam(src, kv.Key) + " (" + Vazio(pf.Txt(kv.Key)) + " → " + Vazio(kv.Value as string) + ")");
            var lt = f.Obj("lt");
            if (lt.Count > 0 && Geo.DifPose(lt, Geo.Pose(lk.Transf)).Count > 0) dl.Add("o link foi movido no modelo");
            // projeto: cópia agora × última sincronização
            var c = f.Obj("c");
            var e = it.Copia;
            if (e is MEPCurve)
            {
                var esperadas = c.ContainsKey("e") ? Geo.Pontos(c["e"])
                    : Geo.Pontos(f.ContainsKey("e") ? f["e"] : null).Select(p => (lt.Count > 0 ? Geo.Transf(lt) : lk.Transf).OfPoint(p)).ToList();
                dp.AddRange(Geo.DifPontas(esperadas, Pontas(e)));
            }
            else if (e is FamilyInstance fi)
            {
                if (c.Count == 0)
                {
                    var t0 = lt.Count > 0 ? Geo.Transf(lt) : lk.Transf;
                    c = Geo.Pose(t0.Multiply(Geo.Transf(new Dictionary<string, object> { ["o"] = f.ContainsKey("o") ? f["o"] : null, ["x"] = f.ContainsKey("x") ? f["x"] : null, ["z"] = f.ContainsKey("z") ? f["z"] : null })));
                }
                dp.AddRange(Geo.DifPose(c, Geo.Pose(fi.GetTotalTransform())));
            }
            string tc = Chave(doc, e);
            if (tc != f.Txt("fam") + "|" + f.Txt("tipo")) dp.Add("tipo trocado para " + tc.Replace("|", " : "));
            it.Estado = dl.Count > 0 && dp.Count > 0 ? "ambos" : dl.Count > 0 ? "link" : dp.Count > 0 ? "projeto" : "ok";
        }

        static string Vazio(string s) => string.IsNullOrEmpty(s) ? "—" : s;

        // ------------------------------------------------------------ ações
        static ElementType Simbolo(Document doc, string fam, string tipo)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<ElementType>()
                .FirstOrDefault(s => s.FamilyName == fam && s.Name == tipo);
        }

        /// <summary>
        /// Deixa as cópias iguais às origens atuais (dentro de uma Transaction aberta). Peças soltas: move/gira/troca o
        /// tipo no lugar quando possível. Trechos e conexões das redes (e peças que não dá para ajustar): copia de novo,
        /// tudo junto, apaga as cópias antigas e reconecta como no link. Devolve (ajustados, recriados, falhas).
        /// </summary>
        public static (int ajustados, int recriados, List<string> falhas) Atualizar(Document doc, List<Item> itens, string por)
        {
            int aj = 0; var falhas = new List<string>();
            var recriar = new List<Item>();
            foreach (var it in itens.Where(i => i.Src != null && i.Copia != null))
            {
                if (it.Rede || it.Copia is MEPCurve || Conectores(it.Copia) != null && TemLigacao(it.Copia)) { recriar.Add(it); continue; }
                string r;
                try { r = AjustarNoLugar(doc, it, por); }
                catch (Exception ex) { r = "recriar"; falhas.Add(it.Fam + " : " + it.Tipo + " — " + ex.Message); }
                if (r == "ajustado") aj++; else recriar.Add(it);
            }
            int rec = 0;
            foreach (var g in recriar.GroupBy(i => i.Link.Uid + "|" + i.Mod.Id))
            {
                var lst = g.ToList();
                var lk = lst[0].Link; var mod = lst[0].Mod;
                WorksetId ws = null;
                try { var p = lst[0].Copia.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM); if (p != null && doc.IsWorkshared) ws = new WorksetId(p.AsInteger()); } catch { }
                var res = Copiar(doc, lk, lst.Select(i => i.Origem).ToList(), mod, ws, por);
                var copiadas = new HashSet<string>(res.Copiados.Select(kv => kv.Key.Uid));
                foreach (var it in lst)
                {
                    if (!copiadas.Contains(it.Origem.Uid)) { falhas.Add(it.Fam + " : " + it.Tipo + " — não foi possível copiar de novo"); continue; }
                    try { doc.Delete(it.Copia.Id); } catch { }
                    rec++;
                }
                foreach (var kv in res.Falhas) falhas.Add(kv.Key.Fam + " : " + kv.Key.Tipo + " — " + kv.Value);
                Reconexao.ComoNoLink(doc, res.Copiados.Select(kv => kv.Value).ToList());
            }
            return (aj, rec, falhas);
        }

        static bool TemLigacao(Element e)
        {
            var cm = Conectores(e);
            if (cm == null) return false;
            foreach (Connector c in cm.Connectors) if (Fisico(c) && c.IsConnected) return true;
            return false;
        }

        static string AjustarNoLugar(Document doc, Item it, string por)
        {
            var e = (FamilyInstance)it.Copia; var src = (FamilyInstance)it.Src; var o = it.Origem;
            Transform alvo = it.Link.Transf.Multiply(src.GetTotalTransform());
            var f = it.Reg.Foto;
            if (e.Mirrored != src.Mirrored || (f.ContainsKey("niv") && f.Txt("niv") != o.Nivel)) return "recriar";
            var st = new SubTransaction(doc);
            st.Start();
            try
            {
                if (Chave(doc, e) != o.Fam + "|" + o.Tipo)
                {
                    var s = Simbolo(doc, o.Fam, o.Tipo);
                    if (s == null) { st.RollBack(); return "recriar"; }
                    e.ChangeTypeId(s.Id);
                    doc.Regenerate();
                }
                Transform tc = e.GetTotalTransform();
                if (Geo.Ang(tc.BasisZ, alvo.BasisZ) > Geo.TOL_ANG) { st.RollBack(); return "recriar"; }
                double a = Geo.Ang(tc.BasisX, alvo.BasisX);
                if (a > Geo.TOL_ANG)
                {
                    double sinal = tc.BasisX.CrossProduct(alvo.BasisX).DotProduct(tc.BasisZ) >= 0 ? 1 : -1;
                    ElementTransformUtils.RotateElement(doc, e.Id, Line.CreateUnbound(tc.Origin, tc.BasisZ), sinal * a);
                    doc.Regenerate();
                }
                XYZ d = alvo.Origin - e.GetTotalTransform().Origin;
                if (d.GetLength() > Geo.TOL_POS) { ElementTransformUtils.MoveElement(doc, e.Id, d); doc.Regenerate(); }
                if (Geo.DifPose(Geo.Pose(alvo), Geo.Pose(e.GetTotalTransform())).Count > 0) { st.RollBack(); return "recriar"; }
                foreach (var n in it.Mod.Parametros)
                    if (ValorParam(src, n) != ValorParam(e, n)) CopiarParam(src, e, n);
                Registrar(e, o, it.Link, it.Mod, por);
                st.Commit();
                return "ajustado";
            }
            catch
            {
                if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                return "recriar";
            }
        }

        /// <summary>Aceita o estado atual: a cópia fica como está e a referência passa a ser a origem atual.</summary>
        public static void Aceitar(Item it, string por) => Registrar(it.Copia, it.Origem, it.Link, it.Mod, por);
    }
}
