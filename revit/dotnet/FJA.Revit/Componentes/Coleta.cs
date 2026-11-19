using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Componentes
{
    /// <summary>Situação de um tipo em relação ao Sistema FJA (cor na árvore).</summary>
    public enum Situacao
    {
        Sem,          // sem vínculo (cinza)
        Ok,           // ligado a um keynote do projeto e igual ao sistema (verde)
        Desatualizado,// ligado, o sistema mudou (âmbar)
        Revit,        // ligado, alguém alterou no Revit (âmbar)
        Fora,         // ligado a um item que não está na especificação do projeto (âmbar)
        Aguardando,   // pedido ainda não processado pelo sistema (azul)
        Orfao,        // ligado a um item que não existe mais no sistema (vermelho)
        SemDados,     // ligado, mas os dados do sistema não foram lidos (cinza-azulado)
    }

    /// <summary>Um tipo de família carregável do modelo, com o diagnóstico.</summary>
    public sealed class TipoInfo
    {
        public ElementId Id;
        public string Nome, Familia, Categoria;
        public List<ElementId> Instancias = new List<ElementId>();
        public string Kn, Tm, Desc;
        public Dictionary<string, string> Vinc;
        public Situacao Sit;
        public Dictionary<string, object> Item, Linha;
        public string KnSis = "", DescSis = "";
        public List<string[]> Difs = new List<string[]>();   // (campo, no Revit, no sistema)
        public bool Ligado => Vinc != null;
        public string Fja => Item?.Txt("fja") ?? Vinc?.Campo("fja") ?? "";
    }

    static class Ext
    {
        public static string Campo(this Dictionary<string, string> d, string k) =>
            d != null && d.TryGetValue(k, out var v) ? v : null;
    }

    public static class Coleta
    {
        /// <summary>Categorias de famílias carregáveis que não entram (anotação e auxiliares).</summary>
        static readonly HashSet<BuiltInCategory> Fora = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_DetailComponents, BuiltInCategory.OST_ProfileFamilies, BuiltInCategory.OST_Mass,
            BuiltInCategory.OST_GenericAnnotation, BuiltInCategory.OST_TitleBlocks,
        };

        public static long Num(ElementId id)
        {
#if REVIT2024
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        /// <summary>Todos os tipos de famílias carregáveis de modelo (sem in-place, anotações, detalhes e perfis).</summary>
        public static List<TipoInfo> Tipos(Document doc, DadosSistema dados)
        {
            var insts = new Dictionary<long, List<ElementId>>();
            foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType())
            {
                try
                {
                    long k = Num(fi.GetTypeId());
                    if (!insts.TryGetValue(k, out var l)) insts[k] = l = new List<ElementId>();
                    l.Add(fi.Id);
                }
                catch { }
            }

            var r = new List<TipoInfo>();
            foreach (FamilySymbol s in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
            {
                try
                {
                    var fam = s.Family;
                    if (fam == null || fam.IsInPlace) continue;
                    var cat = fam.FamilyCategory ?? s.Category;
                    if (cat == null || cat.CategoryType != CategoryType.Model) continue;
                    if (Fora.Contains((BuiltInCategory)(int)Num(cat.Id))) continue;
                    var t = new TipoInfo
                    {
                        Id = s.Id, Nome = s.Name, Familia = fam.Name, Categoria = cat.Name,
                        Kn = Vinculo.Param(s, BuiltInParameter.KEYNOTE_PARAM),
                        Tm = Vinculo.Param(s, BuiltInParameter.ALL_MODEL_TYPE_MARK),
                        Desc = Vinculo.Param(s, BuiltInParameter.ALL_MODEL_DESCRIPTION),
                        Vinc = Vinculo.Ler(s),
                    };
                    if (t.Vinc != null && string.IsNullOrEmpty(t.Vinc.Campo("itemCentral")) &&
                        string.IsNullOrEmpty(t.Vinc.Campo("pedido")) && string.IsNullOrEmpty(t.Vinc.Campo("fja")))
                        t.Vinc = null;
                    if (insts.TryGetValue(Num(s.Id), out var li)) t.Instancias = li;
                    Diagnosticar(t, dados);
                    r.Add(t);
                }
                catch { }
            }
            return r;
        }

        /// <summary>Compara o tipo ligado com o sistema (como diagnosticar do Python, para componentes).</summary>
        public static void Diagnosticar(TipoInfo t, DadosSistema dados)
        {
            t.Difs.Clear(); t.Item = null; t.Linha = null; t.KnSis = ""; t.DescSis = "";
            var v = t.Vinc;
            if (v == null) { t.Sit = Situacao.Sem; return; }
            if (dados == null) { t.Sit = Situacao.SemDados; return; }
            string ic = v.Campo("itemCentral"), ped = v.Campo("pedido");
            Dictionary<string, object> item = null;
            if (!string.IsNullOrEmpty(ic)) dados.Itens.TryGetValue(ic, out item);
            if (item == null && !string.IsNullOrEmpty(ped))
            {
                item = dados.Itens.Values.FirstOrDefault(x => x.Txt("pedido") == ped);
                if (item == null && dados.Pendentes.Any(p => p.Txt("id") == ped)) { t.Sit = Situacao.Aguardando; return; }
            }
            if (item == null) { t.Sit = Situacao.Orfao; return; }
            t.Item = item;
            var linha = dados.LinhaProjeto(item.Txt("id"));
            t.Linha = linha;
            if (dados.Projeto != null && linha == null) { t.Sit = Situacao.Fora; return; }
            if (!string.IsNullOrEmpty(linha?.Txt("pendente"))) { t.Sit = Situacao.Aguardando; return; }
            string kn = linha?.Txt("kn") ?? "";
            string desc = (linha?.Txt("desc") ?? item.Txt("desc") ?? "").ToUpperInvariant();
            t.KnSis = kn; t.DescSis = desc;
            if (kn != "" && t.Kn != kn) t.Difs.Add(new[] { "Keynote", t.Kn, kn });
            if (kn != "" && t.Tm != kn) t.Difs.Add(new[] { "Type Mark", t.Tm, kn });
            if (desc != "" && (t.Desc ?? "").ToUpperInvariant() != desc) t.Difs.Add(new[] { "Descrição", t.Desc, desc });
            if (t.Difs.Count == 0) { t.Sit = Situacao.Ok; return; }
            if (v.Campo("_versao") == "2")
            {
                bool mexeu = t.Kn != (v.Campo("kn") ?? "") || t.Desc != (v.Campo("desc") ?? "");
                t.Sit = mexeu ? Situacao.Revit : Situacao.Desatualizado;
            }
            else t.Sit = Situacao.Desatualizado;
        }

        public static string Rotulo(Situacao s)
        {
            switch (s)
            {
                case Situacao.Ok: return "sincronizado";
                case Situacao.Desatualizado: return "o sistema mudou";
                case Situacao.Revit: return "alterado no Revit";
                case Situacao.Fora: return "fora da especificação do projeto";
                case Situacao.Aguardando: return "aguardando o sistema";
                case Situacao.Orfao: return "item não existe mais no sistema";
                case Situacao.SemDados: return "ligado (sistema não lido)";
                default: return "sem ligação";
            }
        }

        /// <summary>Precisa de Sincronizar (as diferenças e o fora do projeto).</summary>
        public static bool Sincronizavel(Situacao s) => s == Situacao.Desatualizado || s == Situacao.Revit || s == Situacao.Fora;
    }
}
