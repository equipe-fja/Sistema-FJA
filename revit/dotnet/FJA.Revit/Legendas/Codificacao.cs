using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Legendas
{
    /// <summary>Um tipo de ponto da disciplina, na tabela da codificação.</summary>
    public sealed class TipoPonto
    {
        public FamilySymbol Sym;
        public int Quantidade;
        public string Origem = "";        // "modelo", "link X", "modelo + link X", "sem instância"
        public string MarcaAtual = "";    // Type Mark atual
        public string Codigo = "";        // código proposto/editado
        public string DescAtual = "";     // Description atual
        public string Desc = "";          // Description proposta/editada; ao aplicar, também vira o nome do tipo
        public string TipoAtual = "";     // nome do tipo atual
        public string Prefixo = "";       // sigla da família, mantida ao renomear: "(LOG) "
        public string FamAtual = "";      // nome da família sem a sigla
        public string Fam = "";           // nome da família sem a sigla (editado)
        public string Erro;               // última falha ao aplicar
        public string Nome => Sym.FamilyName + " : " + Sym.Name;

        public bool Pendente => Codigo != MarcaAtual || Desc != DescAtual || (Desc != "" && Desc != TipoAtual) || Fam != FamAtual;
    }

    /// <summary>
    /// Codificação de pontos das disciplinas complementares (docs/codificacao-pontos.md): código por disciplina
    /// no padrão SIGLA.NN (ex.: LOG.01), gravado no Type Mark dos tipos das famílias da disciplina.
    /// Os códigos já usados ficam reservados num DataStorage do modelo, para não serem reaproveitados por engano.
    /// Tipos ligados ao sistema (FJA_SistemaTipo) não entram. Cópias feitas pelo Copiar do link (FJA_CopiaLink)
    /// mostram o link de origem.
    /// </summary>
    public sealed class Codificacao
    {
        static readonly Guid GUID_RESERVAS = new Guid("b47e2c91-6d3f-4a58-8e1b-2f9c7d4a6e30");
        static readonly Guid GUID_COPIA_LINK = new Guid("5b1e9c47-2d8a-4f63-9e05-c7a4d1b83f26");
        static readonly Guid[] GUIDS_SISTEMA = { new Guid("3c6f1d2a-9b84-4e57-a1f0-6d2e8b4c7a15"), new Guid("8d2e4b61-5f3a-4c09-b7e2-1a9c6f0d3e58") };
        const string CAMPO = "Json";

        readonly Document doc;
        readonly Disciplina D;
        readonly Coleta col;
        public int LigadosAoSistema;

        public Codificacao(Document doc, Disciplina d) { this.doc = doc; D = d; col = new Coleta(doc, d); }

        public string Sigla => D.Codigo;
        public Regex Padrao => new Regex("^" + Regex.Escape(Sigla) + @"\.(\d{2,3})$");

        static string Param(Element e, BuiltInParameter bip)
        {
            try { return (e.get_Parameter(bip)?.AsString() ?? "").Trim(); } catch { return ""; }
        }

        /// <summary>Description esperada: o nome do tipo; se a família tiver um tipo só, o nome da família
        /// (sem o prefixo da disciplina e sem o sufixo de cópia ".0001").</summary>
        public static string DescricaoPadrao(FamilySymbol s)
        {
            int n = 1;
            try { n = s.Family.GetFamilySymbolIds().Count; } catch { }
            if (n > 1) return s.Name.Trim();
            string f = Regex.Replace(s.FamilyName.Trim(), @"\.\d{4}$", "");
            f = Regex.Replace(f, @"^\([A-Za-z]+\)\s*", "");
            return f.Trim(' ', '-', '_');
        }

        public static string Marca(Element tipo)
        {
            try { return (tipo.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString() ?? "").Trim(); } catch { return ""; }
        }

        static bool TemEntidade(Element e, Guid g)
        {
            var sc = Schema.Lookup(g);
            if (sc == null) return false;
            try { var ent = e.GetEntity(sc); return ent != null && ent.IsValid(); } catch { return false; }
        }

        string LinkDaCopia(Element e)
        {
            var sc = Schema.Lookup(GUID_COPIA_LINK);
            if (sc == null) return null;
            try
            {
                var ent = e.GetEntity(sc);
                if (ent == null || !ent.IsValid()) return null;
                string l = ent.Get<string>("link");
                return string.IsNullOrEmpty(l) ? "link" : l;
            }
            catch { return null; }
        }

        /// <summary>Tipos da disciplina no modelo (com ou sem instância), na ordem do código atual.</summary>
        public List<TipoPonto> Tipos()
        {
            LigadosAoSistema = 0;
            var syms = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => col.EhFamiliaDaDisciplina(s.FamilyName)).ToList();
            var porTipo = new Dictionary<string, List<FamilyInstance>>();
            var cats = syms.Where(s => s.Category != null).Select(s => s.Category.Id).GroupBy(i => i.ToString()).Select(g => g.First()).ToList();
            if (cats.Count > 0)
            {
                foreach (FamilyInstance fi in new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(cats)).OfClass(typeof(FamilyInstance)))
                {
                    if (fi.SuperComponent != null) continue;
                    var ov = fi.OwnerViewId;
                    if (ov != null && ov != ElementId.InvalidElementId && doc.GetElement(ov) is View vv && Coleta.TiposPulados.Contains(vv.ViewType)) continue;
                    string k = fi.GetTypeId().ToString();
                    if (!porTipo.TryGetValue(k, out var l)) porTipo[k] = l = new List<FamilyInstance>();
                    l.Add(fi);
                }
            }
            var r = new List<TipoPonto>();
            foreach (var s in syms)
            {
                if (GUIDS_SISTEMA.Any(g => TemEntidade(s, g))) { LigadosAoSistema++; continue; }
                porTipo.TryGetValue(s.Id.ToString(), out var insts);
                insts = insts ?? new List<FamilyInstance>();
                var links = insts.Select(LinkDaCopia).ToList();
                var nomesLink = links.Where(x => x != null).Distinct().ToList();
                bool temModelo = links.Any(x => x == null);
                string origem = insts.Count == 0 ? "sem instância"
                    : nomesLink.Count == 0 ? "modelo"
                    : (temModelo ? "modelo + " : "") + string.Join(", ", nomesLink);
                string m = Marca(s);
                string da = Param(s, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                var mp = Regex.Match(s.FamilyName, @"^\s*(\([A-Za-z]+\)\s*)(.*)$");
                string pre = mp.Success ? mp.Groups[1].Value : "", fam = mp.Success ? mp.Groups[2].Value : s.FamilyName;
                r.Add(new TipoPonto
                {
                    Sym = s, Quantidade = insts.Count, Origem = origem, MarcaAtual = m, Codigo = m,
                    DescAtual = da, Desc = da != "" ? da : DescricaoPadrao(s), TipoAtual = s.Name,
                    Prefixo = pre, FamAtual = fam, Fam = fam,
                });
            }
            var rx = Padrao;
            return r.OrderBy(t => rx.IsMatch(t.Codigo) ? 0 : 1)
                .ThenBy(t => rx.IsMatch(t.Codigo) ? int.Parse(rx.Match(t.Codigo).Groups[1].Value) : 0)
                .ThenBy(t => Texto.Norm(t.Sym.Category?.Name), StringComparer.Ordinal)
                .ThenBy(t => Texto.Norm(t.Nome), StringComparer.Ordinal).ToList();
        }

        // ------------------------------------------------------------ códigos reservados (DataStorage)
        DataStorage Armazem(bool criar)
        {
            var sc = Schema.Lookup(GUID_RESERVAS);
            if (sc != null)
            {
                foreach (DataStorage ds in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)))
                {
                    try { var e = ds.GetEntity(sc); if (e != null && e.IsValid()) return ds; } catch { }
                }
            }
            if (!criar) return null;
            if (sc == null)
            {
                var sb = new SchemaBuilder(GUID_RESERVAS);
                sb.SetSchemaName("FJA_CodigosPontos");
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Public);
                sb.SetDocumentation("Codigos de pontos ja usados por disciplina (Ferramentas FJA > Codificacao)");
                sb.AddSimpleField(CAMPO, typeof(string));
                sc = sb.Finish();
            }
            var nds = DataStorage.Create(doc);
            nds.SetEntity(new Entity(sc));
            return nds;
        }

        Dictionary<string, object> LerTudo()
        {
            var ds = Armazem(false);
            if (ds == null) return new Dictionary<string, object>();
            try { return Json.LerObjeto(ds.GetEntity(Schema.Lookup(GUID_RESERVAS)).Get<string>(CAMPO) ?? "{}"); }
            catch { return new Dictionary<string, object>(); }
        }

        /// <summary>Códigos já usados nesta disciplina: código → UniqueId do tipo que o recebeu.</summary>
        public Dictionary<string, string> Reservas()
        {
            var d = LerTudo().Obj(Sigla);
            return d.ToDictionary(kv => kv.Key, kv => Convert.ToString(kv.Value));
        }

        /// <summary>Próximo código livre (não usado na tabela nem reservado por um tipo que não está nela).</summary>
        public string ProximoLivre(HashSet<string> usados)
        {
            for (int n = 1; n < 1000; n++)
            {
                string c = Sigla + "." + n.ToString(n < 100 ? "00" : "000");
                if (!usados.Contains(c)) return c;
            }
            return Sigla + ".999";
        }

        /// <summary>UniqueId dos tipos alterados no último Aplicar (para atualizar as legendas).</summary>
        public readonly HashSet<string> MudouUid = new HashSet<string>();

        /// <summary>Família renomeada: as famílias de símbolo da legenda dela, "(SIGLA) (LEG) NOME" e as variações
        /// "(SIGLA) (LEG) NOME - BAIXA", acompanham (assim a legenda mantém os símbolos e as escalas ajustadas).</summary>
        void RenomearLeg(string antes, string depois)
        {
            if (string.IsNullOrEmpty(antes) || antes == depois) return;
            string pre = D.Prefixo + " (LEG) ";
            string velho = Texto.Norm(pre + antes);
            foreach (Family f in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList())
            {
                string n = Texto.Norm(f.Name);
                if (n != velho && !n.StartsWith(velho + " - ")) continue;
                string resto = f.Name.Length > (pre + antes).Length ? f.Name.Substring((pre + antes).Length) : "";
                try { f.Name = pre + depois + resto; } catch { }
            }
        }

        /// <summary>Apaga o tipo (e os elementos dele). Se for o único tipo da família, apaga a família.
        /// O código dele continua reservado. Devolve quantos elementos saíram do modelo.</summary>
        public int Apagar(FamilySymbol s)
        {
            Element alvo = s;
            try { if (s.Family.GetFamilySymbolIds().Count <= 1) alvo = s.Family; } catch { }
            using (var t = ResolverFalhas.Iniciar(doc, "Codificação: apagar " + s.FamilyName + " : " + s.Name))
            {
                // o código do tipo apagado fica reservado, mesmo que nunca tenha passado pelo Aplicar
                string m = Marca(s);
                if (m != "" && Padrao.IsMatch(m))
                {
                    var tudo = LerTudo();
                    var res = tudo.Obj(Sigla);
                    if (!res.ContainsKey(m)) res[m] = s.UniqueId;
                    tudo[Sigla] = res;
                    var ds = Armazem(true);
                    var ent = new Entity(Schema.Lookup(GUID_RESERVAS));
                    ent.Set(CAMPO, Json.Gravar(tudo));
                    ds.SetEntity(ent);
                }
                int n = doc.Delete(alvo.Id).Count;
                t.Commit();
                return n;
            }
        }

        /// <summary>Grava, para cada tipo: o código no Type Mark, a Description (que também renomeia o tipo) e o
        /// nome da família (mantendo a sigla). Uma transação; um tipo com erro (ex.: nome repetido) não impede os
        /// outros: o erro fica em TipoPonto.Erro. Devolve quantos tipos mudaram.</summary>
        public int Aplicar(IEnumerable<TipoPonto> tipos) => Aplicar(tipos, false);

        /// <param name="liberarApagados">true depois de "Renumerar tudo": tira das reservas os códigos de tipos que
        /// não existem mais, para a sequência nova não ter buracos.</param>
        public int Aplicar(IEnumerable<TipoPonto> tipos, bool liberarApagados)
        {
            int n = 0;
            MudouUid.Clear();
            using (var t = ResolverFalhas.Iniciar(doc, "Codificação " + Sigla))
            {
                var tudo = LerTudo();
                var res = tudo.Obj(Sigla);
                if (liberarApagados)
                {
                    foreach (var k in res.Keys.ToList())
                    {
                        Element e = null;
                        try { e = doc.GetElement(Convert.ToString(res[k])); } catch { }
                        if (e == null) res.Remove(k);
                    }
                }
                foreach (var tp in tipos)
                {
                    tp.Erro = null;
                    if (!tp.Pendente) continue;
                    var sub = new SubTransaction(doc);
                    sub.Start();
                    try
                    {
                        string c = (tp.Codigo ?? "").Trim();
                        if (c != tp.MarcaAtual)
                        {
                            var p = tp.Sym.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
                            if (p != null && !p.IsReadOnly) p.Set(c);
                        }
                        string dd = (tp.Desc ?? "").Trim();
                        if (dd != tp.DescAtual)
                        {
                            var pd = tp.Sym.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                            if (pd != null && !pd.IsReadOnly) pd.Set(dd);
                        }
                        if (dd != "" && dd != tp.Sym.Name)
                        {
                            try { tp.Sym.Name = dd; }
                            catch (Exception e) { throw new Exception("não deu para renomear o tipo para \"" + dd + "\" (" + e.Message + ")"); }
                        }
                        string fam = (tp.Fam ?? "").Trim();
                        string novoF = tp.Prefixo + fam;
                        if (fam != "" && tp.Sym.Family.Name != novoF)
                        {
                            string legAntes = col.RotuloItem(tp.Sym, false);
                            try { tp.Sym.Family.Name = novoF; }
                            catch (Exception e) { throw new Exception("não deu para renomear a família para \"" + novoF + "\" (" + e.Message + ")"); }
                            RenomearLeg(legAntes, col.RotuloItem(tp.Sym, false));
                        }
                        // confere o que ficou gravado
                        if (dd != "" && tp.Sym.Name != dd) throw new Exception("o Revit não aceitou o nome do tipo \"" + dd + "\"");
                        if (Param(tp.Sym, BuiltInParameter.ALL_MODEL_DESCRIPTION) != dd) throw new Exception("a Description não foi gravada");
                        sub.Commit();
                        MudouUid.Add(tp.Sym.UniqueId);
                        tp.MarcaAtual = c;
                        tp.DescAtual = dd;
                        tp.TipoAtual = tp.Sym.Name;
                        if (fam != "") tp.FamAtual = fam;
                        if (c != "" && Padrao.IsMatch(c)) res[c] = tp.Sym.UniqueId;
                        n++;
                    }
                    catch (Exception e)
                    {
                        if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack();
                        tp.Erro = e.Message;
                    }
                }
                tudo[Sigla] = res;
                var ds = Armazem(true);
                var ent = new Entity(Schema.Lookup(GUID_RESERVAS));
                ent.Set(CAMPO, Json.Gravar(tudo));
                ds.SetEntity(ent);
                t.Commit();
            }
            return n;
        }
    }
}
