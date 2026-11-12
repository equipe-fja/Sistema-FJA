using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Legendas
{
    /// <summary>Opções da montagem (vêm da janela).</summary>
    public sealed class Opcoes
    {
        public Dictionary<string, double> Tamanhos = new Dictionary<string, double>(); // id da família -> mm (DWG)
        public double PassoMm = Montagem.MM_ROW_STEP;
        public double LarguraMm = Montagem.MM_LARGURA;
        public bool LinhasH, LinhaV, Margens;
        public int Texto;                // (antigo) 1 = "código – nome"
        public int TypeMark;             // 0 = não mostra; 1 = em coluna própria; 2 = antes do texto ("LOG.01 – nome")
        public ModoCor Modo = ModoCor.Modelo;
        public byte[] Cor;               // para Sistema / Escolhida
        public string Config;            // JSON gravado na vista de legenda (ordem, textos, extras)
    }

    public sealed class ResultadoLegenda
    {
        public bool Ok;
        public string Msg;
        public View Vista;
        public bool Criada;
        public List<Item> Itens = new List<Item>();
        public int[] Colunas;
        public string TemplateRemovido;
        public List<string> LegCriadas = new List<string>(), LegExistentes = new List<string>(), LegErros = new List<string>(), ErrosAninhadas = new List<string>();
    }

    /// <summary>Monta a vista de legenda (equivale a atualizar_legenda do Python).</summary>
    public sealed class Montagem
    {
        public const double MM_ROW_STEP = 6.7, MM_ROW_PAD = 1.4, MM_SYM_X = 8.0, MM_TEXT_X = 16.0, MM_UNDERLINE = 41.4,
            MM_TITLE_LINE = 4.7, MM_FIRST_ROW = 1.0, MM_LARGURA = 175.0, MM_PAD_TXT = 3.0, MM_SEP_TXT = 2.0, MM_MARGEM = 2.0;
        public const int MAX_ROWS_PER_COL = 14;
        const int LEGEND_VIEW_PLAN = -8;
        const string CAMPO = "Origem", CAMPO_CFG = "Json";

        readonly Document doc;
        readonly Disciplina D;
        readonly Coleta col;

        public Montagem(Document doc, Disciplina d) { this.doc = doc; D = d; col = new Coleta(doc, d); }

        // ------------------------------------------------------------ marca e ajustes (Extensible Storage)
        Schema Esquema()
        {
            var g = new Guid(D.Guid);
            return Schema.Lookup(g) ?? Construir(g, D.Schema, CAMPO);
        }

        static Schema Construir(Guid g, string nome, string campo)
        {
            var sb = new SchemaBuilder(g);
            sb.SetSchemaName(nome);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.AddSimpleField(campo, typeof(string));
            return sb.Finish();
        }

        /// <summary>Mesmo GUID que o Python (md5 de "cfg:" + guid, versão 3).</summary>
        Guid GuidCfg()
        {
            byte[] h;
            using (var md5 = MD5.Create()) h = md5.ComputeHash(Encoding.ASCII.GetBytes("cfg:" + D.Guid));
            h[7] = (byte)((h[7] & 0x0F) | 0x30);
            return new Guid(h);
        }

        void Marcar(Element e, Schema s)
        {
            var ent = new Entity(s);
            ent.Set(CAMPO, D.Marca);
            e.SetEntity(ent);
        }

        bool Marcado(Element e, Schema s)
        {
            try
            {
                var ent = e.GetEntity(s);
                return ent != null && ent.IsValid() && ent.Get<string>(CAMPO) == D.Marca;
            }
            catch { return false; }
        }

        public Dictionary<string, object> LerConfig() => LerConfig(Legendas().Item2);

        /// <summary>Ajustes gravados numa vista de legenda (vazio se não houver).</summary>
        public Dictionary<string, object> LerConfig(View alvo)
        {
            try
            {
                if (alvo == null) return new Dictionary<string, object>();
                var sc = Schema.Lookup(GuidCfg());
                if (sc == null) return new Dictionary<string, object>();
                var ent = alvo.GetEntity(sc);
                if (ent == null || !ent.IsValid()) return new Dictionary<string, object>();
                return Json.LerObjeto(ent.Get<string>(CAMPO_CFG) ?? "{}");
            }
            catch { return new Dictionary<string, object>(); }
        }

        /// <summary>Grava os ajustes (JSON) na vista, numa transação própria.</summary>
        public void GravarAjustes(View v, string json)
        {
            using (var t = new Transaction(doc, "Legenda: ajustes"))
            {
                t.Start();
                GravarConfig(v, json);
                t.Commit();
            }
        }

        void GravarConfig(View v, string json)
        {
            var g = GuidCfg();
            var sc = Schema.Lookup(g) ?? Construir(g, D.Schema + "Cfg", CAMPO_CFG);
            var ent = new Entity(sc);
            ent.Set(CAMPO_CFG, json);
            v.SetEntity(ent);
        }

        /// <summary>Aplica os ajustes gravados: extras, textos e ordem. Devolve as chaves desmarcadas.</summary>
        public HashSet<string> AplicarConfig(List<Item> itens, Dictionary<string, object> cfg = null)
        {
            cfg = cfg ?? LerConfig();
            foreach (var x in cfg.Lista("extras").OfType<Dictionary<string, object>>())
            {
                if (doc.GetElement(x.Txt("uid") ?? "") is FamilySymbol s)
                    itens.Add(Coleta.ItemExtra(s, x.Txt("rotulo")));
            }
            // símbolos fixos da disciplina: entram em toda legenda que ainda não gravou a lista de acrescentados
            if (!cfg.ContainsKey("extras") && D.Fixos != null)
            {
                foreach (var fx in D.Fixos)
                {
                    var s = Simbolos.SimboloFixo(doc, D, fx);
                    if (s != null && !itens.Any(i => i.Extra && i.Sym.Id == s.Id)) itens.Add(Coleta.ItemExtra(s, fx.Rotulo));
                }
            }
            var rot = cfg.Obj("rotulos");
            foreach (var it in itens)
            {
                string r = rot.Txt(it.Chave);
                if (!string.IsNullOrEmpty(r)) it.Rotulo = r;
            }
            var ordem = new Dictionary<string, int>();
            int i = 0;
            foreach (var k in cfg.Lista("ordem")) ordem[Convert.ToString(k)] = i++;
            var pos = itens.Select((it, j) => new { it.Chave, j }).GroupBy(x => x.Chave).ToDictionary(g => g.Key, g => g.First().j);
            var ord = itens.OrderBy(it => ordem.TryGetValue(it.Chave, out int o) ? o : ordem.Count + pos[it.Chave]).ToList();
            itens.Clear();
            itens.AddRange(ord);
            return new HashSet<string>(cfg.Lista("desmarcados").Select(Convert.ToString));
        }

        /// <summary>Legendas desta disciplina feitas pela ferramenta: com os ajustes gravados, com o nome padrão
        /// ou com textos marcados por ela.</summary>
        public List<View> ListarLegendas()
        {
            Schema sc = Schema.Lookup(GuidCfg()), mk = Schema.Lookup(new Guid(D.Guid));
            string alvoN = Texto.Norm(D.Alvo);
            var r = new List<View>();
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)))
            {
                try { if (v.ViewType != ViewType.Legend || v.IsTemplate) continue; } catch { continue; }
                bool ok = Texto.Norm(v.Name) == alvoN;
                if (!ok && sc != null) { try { var e = v.GetEntity(sc); ok = e != null && e.IsValid(); } catch { } }
                if (!ok && mk != null)
                    ok = new FilteredElementCollector(doc, v.Id).OfClass(typeof(TextNote)).Any(t => Marcado(t, mk));
                if (ok) r.Add(v);
            }
            return r.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Nome livre para uma vista nova (acrescenta " (2)", " (3)"... se já existir).</summary>
        public string NomeLivre(string nome)
        {
            var nomes = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend).Select(v => Texto.Norm(v.Name)));
            if (!nomes.Contains(Texto.Norm(nome))) return nome;
            for (int i = 2; ; i++)
            {
                string n = nome + " (" + i + ")";
                if (!nomes.Contains(Texto.Norm(n))) return n;
            }
        }

        /// <summary>Opções gravadas numa legenda (as mesmas que a janela Legendas grava).</summary>
        public static Opcoes OpcoesGravadas(Dictionary<string, object> cfg)
        {
            var o = cfg.Obj("opcoes");
            double Num(string k, double pad) { try { return o.ContainsKey(k) ? Convert.ToDouble(o[k], System.Globalization.CultureInfo.InvariantCulture) : pad; } catch { return pad; } }
            bool Sim(string k) => o.ContainsKey(k) && o[k] is bool bb && bb;
            var op = new Opcoes
            {
                PassoMm = Num("passo", MM_ROW_STEP), LarguraMm = Num("largura", MM_LARGURA),
                LinhasH = Sim("linhasH"), LinhaV = Sim("linhaV"), Margens = Sim("margens"),
                TypeMark = o.ContainsKey("typeMark") ? (int)Num("typeMark", 0) : ((int)Num("texto", 0) == 1 ? 2 : 0),
            };
            int m = (int)Num("modoCor", 0);
            op.Modo = m >= 0 && m <= 3 ? (ModoCor)m : ModoCor.Modelo;
            var oc = o.Lista("cor");
            if (oc.Count == 3) op.Cor = oc.Select(x => (byte)Convert.ToInt32(x)).ToArray();
            return op;
        }

        /// <summary>Refaz uma legenda gravada com os ajustes dela (ordem, textos, desmarcados, acrescentados,
        /// opções), lendo de novo os nomes, Descriptions e códigos dos tipos. Os textos editados à mão dos tipos em
        /// 'esquecerTextos' (UniqueId) voltam a seguir a Description.</summary>
        public ResultadoLegenda AtualizarGravada(View v, List<Item> baseItens, ISet<string> esquecerTextos)
        {
            var cfg = LerConfig(v);
            var rot = cfg.Obj("rotulos");
            foreach (var k in rot.Keys.ToList())
            {
                string uid = k.StartsWith("x:") ? k.Substring(2) : k;
                int bar = uid.IndexOf('|');
                if (bar >= 0) uid = uid.Substring(0, bar);
                if (esquecerTextos != null && esquecerTextos.Contains(uid)) rot.Remove(k);
            }
            cfg["rotulos"] = rot;
            var itens = baseItens.Select(i => i.Novo()).ToList();
            var desm = AplicarConfig(itens, cfg);
            itens = itens.Where(i => !desm.Contains(i.Chave)).ToList();
            var op = OpcoesGravadas(cfg);
            if (op.Modo == ModoCor.Sistema)
            {
                try { op.Cor = CorSistema.Ler(doc, D.Codigo).Item1; } catch { op.Cor = null; }
                if (op.Cor == null) op.Modo = ModoCor.Modelo;
            }
            if (op.Modo == ModoCor.Escolhida && op.Cor == null) op.Modo = ModoCor.Modelo;
            op.Config = Json.Gravar(cfg);
            return Atualizar(itens, op, v, null);
        }

        // ------------------------------------------------------------ vistas
        /// <summary>(base, alvo, outras). Base = a primeira legenda que bate com Bases.</summary>
        public Tuple<View, View, List<View>> Legendas()
        {
            View alvo = null;
            var cands = new SortedDictionary<int, View>();
            var outras = new List<View>();
            string alvoN = Texto.Norm(D.Alvo);
            var bases = D.BasesN;
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)))
            {
                try { if (v.ViewType != ViewType.Legend || v.IsTemplate) continue; } catch { continue; }
                string n = Texto.Norm(v.Name);
                if (n == alvoN) { alvo = v; continue; }
                outras.Add(v);
                for (int i = 0; i < bases.Length; i++)
                    if (n.StartsWith(bases[i]) && !cands.ContainsKey(i)) cands[i] = v;
            }
            View src = cands.Count > 0 ? cands.First().Value : null;
            if (src != null) outras.Remove(src);
            return Tuple.Create(src, alvo, outras);
        }

        Tuple<ElementId, ElementId, GraphicsStyle> Estilos(View refv)
        {
            ElementId titulo = null;
            var cont = new Dictionary<string, Tuple<int, ElementId>>();
            foreach (TextNote tn in new FilteredElementCollector(doc, refv.Id).OfClass(typeof(TextNote)))
            {
                string txt = Texto.Norm(tn.Text);
                if (txt.StartsWith("LEGENDA") && titulo == null) titulo = tn.GetTypeId();
                else if (!txt.StartsWith("NOTAS"))
                {
                    string k = tn.GetTypeId().ToString();
                    cont[k] = Tuple.Create((cont.TryGetValue(k, out var c) ? c.Item1 : 0) + 1, tn.GetTypeId());
                }
            }
            ElementId corpo = cont.Count > 0 ? cont.Values.OrderByDescending(x => x.Item1).First().Item2 : null;
            corpo = corpo ?? doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            titulo = titulo ?? corpo;
            GraphicsStyle estilo = null;
            foreach (CurveElement ce in new FilteredElementCollector(doc, refv.Id).OfClass(typeof(CurveElement)))
            {
                try
                {
                    var c = ce.GeometryCurve;
                    XYZ p0 = c.GetEndPoint(0), p1 = c.GetEndPoint(1);
                    if (Math.Abs(p0.Y - p1.Y) < 1e-4 && Math.Abs(p1.X - p0.X) > 8.0 && Math.Abs(p1.X - p0.X) < 22.0)
                    {
                        estilo = ce.LineStyle as GraphicsStyle;
                        break;
                    }
                }
                catch { }
            }
            return Tuple.Create(titulo, corpo, estilo);
        }

        static bool ConteudoDaVista(Element e)
        {
            if (e is TextNote || e is CurveElement || e is FamilyInstance || e is FilledRegion) return true;
            try { return e.Category != null && e.Category.Id == new ElementId(BuiltInCategory.OST_LegendComponents); } catch { return false; }
        }

        Tuple<View, Element> ComponenteDeLegenda(IEnumerable<View> vistas)
        {
            foreach (var v in vistas)
            {
                if (v == null) continue;
                var c = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_LegendComponents)
                    .WhereElementIsNotElementType().FirstElement();
                if (c != null) return Tuple.Create(v, c);
            }
            return Tuple.Create<View, Element>(null, null);
        }

        public static byte[] CorDoItem(Item it, Opcoes op)
        {
            switch (op.Modo)
            {
                case ModoCor.Preto: return null;
                case ModoCor.Modelo: return it.CorLegenda ?? it.Cor;
                default: return op.Cor;
            }
        }

        // ------------------------------------------------------------ principal
        public ResultadoLegenda Atualizar(List<Item> itens, Opcoes op) => Atualizar(itens, op, Legendas().Item2, null);

        /// <summary>Monta a legenda na vista 'alvo'; sem vista, cria uma nova (cópia da legenda base) com o nome
        /// 'nomeNova' (ou o nome padrão da disciplina).</summary>
        public ResultadoLegenda Atualizar(List<Item> itens, Opcoes op, View alvo, string nomeNova)
        {
            var res = new ResultadoLegenda();
            double tIni = Tempos.Agora;
            if (itens == null || itens.Count == 0)
            {
                res.Msg = "Nenhuma família '" + string.Join("' / '", D.Familias) + "' posicionada no modelo.";
                return res;
            }
            if (op.Modo == ModoCor.Modelo)
            {
                var cu = Cores.Dominante(itens, D);
                foreach (var it in itens) it.CorLegenda = cu;
            }
            var leg = Legendas();
            View src = leg.Item1;
            var outras = leg.Item3.ToList();
            if (leg.Item2 != null && (alvo == null || leg.Item2.Id != alvo.Id)) outras.Add(leg.Item2);
            Tempos.Marca("legendas do projeto", tIni);
            View baseV = src ?? alvo ?? outras.FirstOrDefault();
            if (baseV == null) { res.Msg = "O projeto não tem nenhuma vista de legenda para servir de base."; return res; }
            var comp = ComponenteDeLegenda(new[] { src, alvo }.Concat(outras));
            Tempos.Marca("componente de legenda");
            var est = Estilos(src ?? baseV);
            Tempos.Marca("estilos de texto e linha");

            // famílias (LEG): usa as que existem; cria as que faltam (fora de transação)
            int bit = Simbolos.BitsDetalhe.TryGetValue((alvo ?? baseV).DetailLevel, out int b) ? b : 16384;
            var carregadas = Simbolos.NomesFamilias(doc);
            var aninhadas = new Dictionary<string, List<Aninhada>>();
            var legs = new Dictionary<string, Family>();
            string pasta = Path.Combine(Path.GetTempPath(), "FJA_LEG");
            double tFams = Tempos.Agora;
            foreach (var it in itens)
            {
                if (it.Extra)
                {
                    // símbolo acrescentado: com escala ajustada, vira uma família (LEG) própria (DWG);
                    // se ela já existe, é a versão ajustada dele (100% = como está)
                    string kx = "x:" + it.Sym.Id;
                    if (legs.ContainsKey(kx)) continue;
                    double tF0 = Tempos.Agora;
                    string nomeX = Simbolos.NomeLegExtra(D, it.Sym);
                    Family lx = Simbolos.FamiliaPorNome(doc, nomeX);
                    double tx = op.Tamanhos.TryGetValue(kx, out double tv) ? tv : 0;
                    if (tx > 0)
                    {
                        bool feito = false;
                        if (lx != null)
                        {
                            try { feito = Simbolos.Reescalar(doc, lx, tx, it); }
                            catch (Exception ex) { res.LegErros.Add(nomeX + ": " + ex.Message); }
                            Family nova = Simbolos.FamiliaPorNome(doc, nomeX);
                            if (!feito && nova != null)
                                using (var td = new Transaction(doc, "Refazer símbolo de legenda")) { td.Start(); doc.Delete(nova.Id); td.Commit(); }
                            lx = feito ? nova : null;
                        }
                        if (lx == null)
                        {
                            try { lx = Simbolos.ViaDwg(doc, it.Sym, nomeX, pasta, tx, it); }
                            catch (Exception ex) { res.LegErros.Add(nomeX + ": " + ex.Message); lx = null; }
                        }
                        Tempos.Marca("  " + nomeX + (feito ? " (escala do DWG)" : " (DWG)"), tF0);
                    }
                    legs[kx] = lx;
                    continue;
                }
                Family fam = it.Sym.Family;
                string key = it.ChaveLeg, famKey = fam.Id.ToString();
                if (legs.ContainsKey(key)) continue;
                double tF = Tempos.Agora;
                // variação (baixa/média/alta): família (LEG) própria, montada com a instância representante dela
                string nomeLeg = Simbolos.NomeLeg(D, col, it.Sym) + (it.VarLegNome != "" ? " - " + it.VarLegNome : "");
                Family lf = Simbolos.FamiliaPorNome(doc, nomeLeg);
                double tam = op.Tamanhos.TryGetValue(key, out double tt) ? tt : 0;
                if (lf != null && tam > 0)
                {
                    // já é DWG: só muda o fator de escala do DWG na família (rápido, sem exportar de novo)
                    bool feito = false;
                    try { feito = Simbolos.Reescalar(doc, lf, tam, it); }
                    catch (Exception ex) { res.LegErros.Add(nomeLeg + ": " + ex.Message); }
                    // depois de recarregar a família, o objeto 'lf' antigo deixa de valer: procura de novo pelo nome
                    Family nova = Simbolos.FamiliaPorNome(doc, nomeLeg);
                    if (feito && nova != null)
                    {
                        legs[key] = nova;
                        res.LegExistentes.Add(nomeLeg);
                        Tempos.Marca("  " + nomeLeg + " (escala do DWG)", tF);
                        continue;
                    }
                    // ainda não é DWG: refaz pelo caminho completo (exporta e importa uma vez)
                    if (nova != null)
                        using (var td = new Transaction(doc, "Refazer símbolo de legenda")) { td.Start(); doc.Delete(nova.Id); td.Commit(); }
                    lf = null;
                }
                if (lf != null) { legs[key] = lf; res.LegExistentes.Add(lf.Name); continue; }
                if (!aninhadas.ContainsKey(famKey))
                {
                    try { aninhadas[famKey] = Simbolos.LerAninhadas(doc, fam, bit, carregadas); }
                    catch (Exception ex) { aninhadas[famKey] = new List<Aninhada>(); res.ErrosAninhadas.Add(fam.Name + ": " + ex.Message); }
                }
                var specs = Simbolos.Visiveis(aninhadas[famKey], it.Rep, it.Sym);
                try
                {
                    if (tam > 0)
                    {
                        Family orig = Simbolos.FamiliaPorNome(doc, nomeLeg + " __ORIG") ?? Simbolos.CriarFamiliaLeg(doc, it, specs, nomeLeg + " __ORIG", pasta);
                        lf = null;
                        if (orig != null)
                        {
                            try { lf = Simbolos.ViaDwg(doc, orig, nomeLeg, pasta, tam, it); }
                            finally
                            {
                                using (var td = new Transaction(doc, "Símbolo de legenda (DWG)"))
                                {
                                    td.Start();
                                    try { doc.Delete(orig.Id); } catch { }
                                    td.Commit();
                                }
                            }
                        }
                    }
                    else lf = Simbolos.CriarFamiliaLeg(doc, it, specs, nomeLeg, pasta);
                }
                catch (Exception ex) { lf = null; res.LegErros.Add(nomeLeg + ": " + ex.Message); }
                legs[key] = lf;
                if (lf != null) res.LegCriadas.Add(lf.Name);
                if (tam > 0 && lf != null && !it.Redimensionado) res.LegErros.Add(nomeLeg + ": não foi possível redimensionar");
                Tempos.Marca("  " + nomeLeg + (tam > 0 ? " (refeita pelo DWG)" : " (criada)"), tF);
            }
            Tempos.Marca($"famílias (LEG): {res.LegExistentes.Count} existentes, {res.LegCriadas.Count} criadas", tFams);

            double tMont = Tempos.Agora;
            using (var t = ResolverFalhas.Iniciar(doc, "Atualizar " + D.Botao))
            {
                Schema schema = Esquema();
                if (alvo == null)
                {
                    alvo = doc.GetElement(baseV.Duplicate(ViewDuplicateOption.Duplicate)) as View;
                    alvo.Name = NomeLivre(string.IsNullOrWhiteSpace(nomeNova) ? D.Alvo : nomeNova.Trim());
                    res.Criada = true;
                    var apagar = new FilteredElementCollector(doc, alvo.Id).WhereElementIsNotElementType()
                        .Where(e => e.OwnerViewId == alvo.Id && ConteudoDaVista(e)).Select(e => e.Id).ToList();
                    if (apagar.Count > 0) doc.Delete(apagar);
                }
                // DWG importados nas famílias precisam aparecer na legenda
                try
                {
                    var cid = new ElementId(BuiltInCategory.OST_ImportObjectStyles);
                    if (alvo.GetCategoryHidden(cid))
                    {
                        ElementId tid = alvo.ViewTemplateId;
                        if (tid != null && tid != ElementId.InvalidElementId)
                        {
                            res.TemplateRemovido = doc.GetElement(tid)?.Name;
                            alvo.ViewTemplateId = ElementId.InvalidElementId;
                        }
                        alvo.SetCategoryHidden(cid, false);
                    }
                }
                catch { }

                ElementId tplId = null;
                if (comp.Item2 != null)
                {
                    tplId = ElementTransformUtils.CopyElements(comp.Item1, new List<ElementId> { comp.Item2.Id }, alvo, Transform.Identity, new CopyPasteOptions()).First();
                    try { doc.GetElement(tplId).DeleteEntity(schema); } catch { }
                }
                var velhos = new FilteredElementCollector(doc, alvo.Id).WhereElementIsNotElementType()
                    .Where(e => e.Id != tplId && e.OwnerViewId == alvo.Id && Marcado(e, schema)).Select(e => e.Id).ToList();
                if (velhos.Count > 0) doc.Delete(velhos);
                if (D.LegadoLum && !res.Criada && LerConfig(alvo).Count == 0) LimparLegadoLum(alvo, schema);

                double k = alvo.Scale / 304.8;
                double x0 = 0, y0 = 0;
                var titulo = TextNote.Create(doc, alvo.Id, new XYZ(x0 + 1.0 * k, y0, 0), D.Titulo, est.Item1);
                Marcar(titulo, schema);
                double lineY = y0 - MM_TITLE_LINE * k;
                var ul = doc.Create.NewDetailCurve(alvo, Line.CreateBound(new XYZ(x0, lineY, 0), new XYZ(x0 + MM_UNDERLINE * k, lineY, 0)));
                if (est.Item3 != null) ul.LineStyle = est.Item3;
                Marcar(ul, schema);

                int ncols = itens.Count > MAX_ROWS_PER_COL ? 2 : 1;
                double colWmm = op.LarguraMm / ncols;
                double txtWmm = Math.Max(20.0, colWmm - MM_TEXT_X - MM_PAD_TXT);
                var linhas = new List<Linha>();
                double textDx = 0, provTop = lineY - MM_FIRST_ROW * k;
                double dirSimb = double.MinValue;   // borda direita do símbolo mais largo
                foreach (var it in itens)
                {
                    var ids = new List<ElementId>();
                    Family lf = legs.TryGetValue(it.ChaveLeg, out var f) ? f : null;
                    FamilySymbol lsym = it.Extra ? (lf != null ? Simbolos.PrimeiroTipo(doc, lf) ?? it.Sym : it.Sym) : (lf != null ? Simbolos.PrimeiroTipo(doc, lf) : null);
                    if (lsym != null)
                    {
                        if (!lsym.IsActive) { lsym.Activate(); doc.Regenerate(); }
                        try { ids.Add(doc.Create.NewFamilyInstance(XYZ.Zero, lsym, alvo).Id); }
                        catch { if (!it.Extra) throw; }
                        if (it.Extra) it.Modo = "extra";
                        else { it.Modo = "leg"; it.Leg = lf.Name; }
                    }
                    if (ids.Count == 0 && !it.Extra)
                    {
                        aninhadas.TryGetValue(it.Sym.Family.Id.ToString(), out var an);
                        var vs = Simbolos.Visiveis(an, it.Rep, it.Sym);
                        if (vs.Count > 0)
                        {
                            ids = Simbolos.ColocarAninhadas(doc, alvo, vs, it.Rep, it.Sym);
                            it.Modo = "simbolos";
                            doc.Regenerate();
                            if (ids.Count > 0 && Simbolos.Caixa(doc, ids, alvo) == null) { doc.Delete(ids); ids.Clear(); }
                        }
                    }
                    if (ids.Count == 0 && !it.Extra) { ids = Simbolos.CopiarDaPlanta(doc, alvo, it); it.Modo = "planta"; }
                    if (ids.Count == 0 && tplId != null)
                    {
                        var c = doc.GetElement(ElementTransformUtils.CopyElement(doc, tplId, XYZ.Zero).First());
                        c.get_Parameter(BuiltInParameter.LEGEND_COMPONENT).Set(it.Sym.Id);
                        try { c.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW).Set(LEGEND_VIEW_PLAN); } catch { }
                        ids.Add(c.Id);
                        it.Modo = "componente";
                    }
                    if (ids.Count == 0) it.Modo = "sem simbolo";
                    byte[] cor = CorDoItem(it, op);
                    foreach (var i in ids)
                    {
                        Cores.Pintar(alvo, i, cor, it.SoLinhas);
                        Marcar(doc.GetElement(i), schema);
                    }
                    var to = new TextNoteOptions(est.Item2) { HorizontalAlignment = HorizontalTextAlignment.Left };
                    bool antes = (op.TypeMark == 2 || (op.TypeMark == 0 && op.Texto == 1)) && !string.IsNullOrEmpty(it.Codigo);
                    string texto = antes ? it.Codigo + " – " + it.Rotulo : it.Rotulo;
                    var tn = TextNote.Create(doc, alvo.Id, new XYZ(x0 + MM_TEXT_X * k, lineY, 0), texto, to);
                    try { tn.Width = txtWmm / 304.8; } catch { }
                    Marcar(tn, schema);
                    TextNote tc = null;     // Type Mark em coluna própria
                    if (op.TypeMark == 1 && !string.IsNullOrEmpty(it.Codigo))
                    {
                        tc = TextNote.Create(doc, alvo.Id, new XYZ(x0 + MM_TEXT_X * k, lineY, 0), it.Codigo, to);
                        Marcar(tc, schema);
                    }
                    doc.Regenerate();

                    var bb = Simbolos.Caixa(doc, ids, alvo);
                    var tbb = tn.get_BoundingBox(alvo);
                    double symH = bb != null ? bb[3] - bb[1] : 0, txtH = tbb != null ? tbb.Max.Y - tbb.Min.Y : 0;
                    double rowH = Math.Max(op.PassoMm * k, Math.Max(symH + MM_ROW_PAD * k, txtH + MM_ROW_PAD * k));
                    double cy = provTop - rowH / 2;
                    provTop -= rowH;
                    double dxTxt = 0;
                    if (bb != null)
                    {
                        // símbolo largo: encosta no início da coluna e empurra o texto
                        double half = (bb[2] - bb[0]) / 2;
                        double sx = x0 + Math.Max(MM_SYM_X * k, half);
                        dxTxt = Math.Max(0, sx + half + 2.0 * k - (x0 + MM_TEXT_X * k));
                        dirSimb = Math.Max(dirSimb, sx + half);
                        var cur = new XYZ((bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2, 0);
                        ElementTransformUtils.MoveElements(doc, ids, new XYZ(sx, cy, 0) - cur);
                    }
                    if (tbb != null) ElementTransformUtils.MoveElement(doc, tn.Id, new XYZ(0, cy - (tbb.Min.Y + tbb.Max.Y) / 2, 0));
                    if (tc != null)
                    {
                        var cbb = tc.get_BoundingBox(alvo);
                        if (cbb != null) ElementTransformUtils.MoveElement(doc, tc.Id, new XYZ(0, cy - (cbb.Min.Y + cbb.Max.Y) / 2, 0));
                    }
                    linhas.Add(new Linha { Ids = ids, Tn = tn, Tc = tc, Cy = cy, H = rowH, SymH = symH });
                    it.SymIds = new List<ElementId>(ids);
                    textDx = Math.Max(textDx, dxTxt);
                }

                // com a linha entre símbolo e texto: folga dos dois lados da linha (símbolo | linha | texto)
                if (op.LinhaV && dirSimb > double.MinValue)
                    textDx = Math.Max(textDx, dirSimb + 2 * MM_SEP_TXT * k - (x0 + MM_TEXT_X * k));
                // Type Mark em coluna própria: a coluna tem a largura do código mais largo; o texto vem depois
                double codW = 0;
                if (linhas.Any(r => r.Tc != null))
                {
                    doc.Regenerate();
                    foreach (var r in linhas.Where(r => r.Tc != null))
                    {
                        var cbb = r.Tc.get_BoundingBox(alvo);
                        if (cbb != null) codW = Math.Max(codW, cbb.Max.X - cbb.Min.X);
                    }
                }
                double shiftTxt = codW > 0 ? codW + MM_PAD_TXT * k : 0;
                // textos alinhados pela esquerda, logo depois do símbolo mais largo
                if (textDx > 1e-9 || shiftTxt > 0)
                {
                    if (textDx > 1e-9)
                    {
                        ElementTransformUtils.MoveElements(doc, linhas.Select(r => r.Tn.Id).ToList(), new XYZ(textDx, 0, 0));
                        var cods = linhas.Where(r => r.Tc != null).Select(r => r.Tc.Id).ToList();
                        if (cods.Count > 0) ElementTransformUtils.MoveElements(doc, cods, new XYZ(textDx, 0, 0));
                    }
                    if (shiftTxt > 0) ElementTransformUtils.MoveElements(doc, linhas.Select(r => r.Tn.Id).ToList(), new XYZ(shiftTxt, 0, 0));
                    double novoW = Math.Max(15.0, txtWmm - (textDx + shiftTxt) / k);
                    foreach (var r in linhas) { try { r.Tn.Width = novoW / 304.8; } catch { } }
                    doc.Regenerate();
                    foreach (var r in linhas)
                    {
                        var tbb = r.Tn.get_BoundingBox(alvo);
                        if (tbb == null) continue;
                        ElementTransformUtils.MoveElement(doc, r.Tn.Id, new XYZ(0, r.Cy - (tbb.Min.Y + tbb.Max.Y) / 2, 0));
                        r.H = Math.Max(op.PassoMm * k, Math.Max(r.SymH + MM_ROW_PAD * k, (tbb.Max.Y - tbb.Min.Y) + MM_ROW_PAD * k));
                        var cbb = r.Tc?.get_BoundingBox(alvo);
                        if (cbb != null) ElementTransformUtils.MoveElement(doc, r.Tc.Id, new XYZ(0, r.Cy - (cbb.Min.Y + cbb.Max.Y) / 2, 0));
                    }
                }
                int split = linhas.Count;
                if (ncols == 2)
                {
                    double total = linhas.Sum(r => r.H), acc = 0;
                    double? melhor = null;
                    for (int s = 1; s < linhas.Count; s++)
                    {
                        acc += linhas[s - 1].H;
                        double pior = Math.Max(acc, total - acc);
                        if (melhor == null || pior <= melhor.Value + 1e-9) { melhor = pior; split = s; }
                    }
                }
                res.Colunas = new[] { split, linhas.Count - split };

                double topo = lineY - MM_FIRST_ROW * k;
                var colTops = new[] { topo, topo };
                double colW = colWmm * k;
                var faixas = new[] { new List<double[]>(), new List<double[]>() };
                for (int idx = 0; idx < linhas.Count; idx++)
                {
                    var r = linhas[idx];
                    int c = idx < split ? 0 : 1;
                    double cy = colTops[c] - r.H / 2;
                    faixas[c].Add(new[] { colTops[c], colTops[c] - r.H });
                    colTops[c] -= r.H;
                    var d = new XYZ(c * colW, cy - r.Cy, 0);
                    if (d.GetLength() > 1e-9) ElementTransformUtils.MoveElements(doc, r.Ids.Concat(new[] { r.Tn.Id }).Concat(r.Tc != null ? new[] { r.Tc.Id } : new ElementId[0]).ToList(), d);
                }

                // linhas opcionais: entre os itens, entre símbolo e texto, contorno
                Action<XYZ, XYZ> linha = (a, bq) =>
                {
                    if (a.DistanceTo(bq) < 1e-6) return;
                    var cv = doc.Create.NewDetailCurve(alvo, Line.CreateBound(a, bq));
                    if (est.Item3 != null) { try { cv.LineStyle = est.Item3; } catch { } }
                    Marcar(cv, schema);
                };
                double baseY = ncols == 2 ? Math.Min(colTops[0], colTops[1]) : colTops[0];
                if (op.LinhasH || op.LinhaV || op.Margens)
                    ul.GeometryCurve = Line.CreateBound(new XYZ(x0, lineY, 0), new XYZ(x0 + op.LarguraMm * k, lineY, 0));
                for (int c = 0; c < ncols; c++)
                {
                    double xa = x0 + c * colW, xb = x0 + (c + 1) * colW;
                    var fx = faixas[c];
                    if (fx.Count == 0) continue;
                    if (op.LinhasH)
                    {
                        for (int i = 0; i < fx.Count - 1; i++) linha(new XYZ(xa, fx[i][1], 0), new XYZ(xb, fx[i][1], 0));
                        if (op.Margens && fx[fx.Count - 1][1] > baseY + 1e-9)
                            linha(new XYZ(xa, fx[fx.Count - 1][1], 0), new XYZ(xb, fx[fx.Count - 1][1], 0));
                    }
                    if (op.LinhaV)
                    {
                        double xv = xa + MM_TEXT_X * k + textDx - MM_SEP_TXT * k;
                        linha(new XYZ(xv, lineY, 0), new XYZ(xv, op.Margens ? baseY : fx[fx.Count - 1][1], 0));
                    }
                }
                if (op.Margens)
                {
                    var tbb = titulo.get_BoundingBox(alvo);
                    double yTop = (tbb != null ? tbb.Max.Y : y0) + MM_MARGEM * k, xr = x0 + op.LarguraMm * k;
                    linha(new XYZ(x0, yTop, 0), new XYZ(xr, yTop, 0));
                    linha(new XYZ(xr, yTop, 0), new XYZ(xr, baseY, 0));
                    linha(new XYZ(xr, baseY, 0), new XYZ(x0, baseY, 0));
                    linha(new XYZ(x0, baseY, 0), new XYZ(x0, yTop, 0));
                    if (ncols == 2) linha(new XYZ(x0 + colW, lineY, 0), new XYZ(x0 + colW, baseY, 0));
                }
                if (tplId != null) doc.Delete(tplId);
                if (op.Config != null) GravarConfig(alvo, op.Config);
                double tC = Tempos.Agora;
                t.Commit();
                Tempos.Marca("gravar a transação", tC);
            }
            Tempos.Marca($"montar a legenda ({itens.Count} itens)", tMont);

            foreach (var it in itens)
            {
                var bb = it.SymIds.Count > 0 ? Simbolos.Caixa(doc, it.SymIds, alvo) : null;
                it.Mm = bb != null ? new[] { (bb[2] - bb[0]) / alvo.Scale * 304.8, (bb[3] - bb[1]) / alvo.Scale * 304.8 } : null;
            }
            res.Ok = true;
            res.Vista = alvo;
            res.Itens = itens;
            return res;
        }

        /// <summary>Legenda LUM feita pelo botão antigo (Python): apaga os textos "SIGLA.NN - ...", as linhas
        /// magenta que ele desenhou e o título com o sublinhado (a ferramenta cria os seus).</summary>
        void LimparLegadoLum(View alvo, Schema schema)
        {
            var rx = new System.Text.RegularExpressions.Regex(@"^[A-Z]{2,4}[.-]\d{2,3}\s*-");
            var apagar = new List<ElementId>();
            TextNote tituloVelho = null;
            var els = new FilteredElementCollector(doc, alvo.Id).WhereElementIsNotElementType()
                .Where(e => e.OwnerViewId == alvo.Id && !Marcado(e, schema)).ToList();
            foreach (var e in els)
            {
                if (e is TextNote tn)
                {
                    string t = Texto.Norm(tn.Text);
                    if (rx.IsMatch(t)) apagar.Add(e.Id);
                    else if (t == Texto.Norm(D.Titulo)) { tituloVelho = tn; apagar.Add(e.Id); }
                }
                else if (e is CurveElement)
                {
                    try
                    {
                        var c = alvo.GetElementOverrides(e.Id).ProjectionLineColor;
                        if (c != null && c.IsValid && c.Red == 255 && c.Green == 0 && c.Blue == 128) apagar.Add(e.Id);
                    }
                    catch { }
                }
            }
            var bb = tituloVelho?.get_BoundingBox(alvo);
            if (bb != null)
            {
                foreach (var ce in els.OfType<CurveElement>())
                {
                    try
                    {
                        var g = ce.GeometryCurve;
                        XYZ p0 = g.GetEndPoint(0), p1 = g.GetEndPoint(1);
                        if (Math.Abs(p0.Y - p1.Y) > 1e-4) continue;
                        if (p0.Y < bb.Min.Y - 1.0 || p0.Y > bb.Min.Y + 0.1) continue;
                        if (Math.Min(p0.X, p1.X) < bb.Min.X - 3.0 || Math.Min(p0.X, p1.X) > bb.Max.X) continue;
                        apagar.Add(ce.Id);
                    }
                    catch { }
                }
            }
            apagar = apagar.Distinct().ToList();
            if (apagar.Count > 0) doc.Delete(apagar);
        }

        sealed class Linha
        {
            public List<ElementId> Ids;
            public TextNote Tn, Tc;
            public double Cy, H, SymH;
        }
    }
}
