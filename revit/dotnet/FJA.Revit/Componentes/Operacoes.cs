using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Componentes
{
    /// <summary>Gravações no modelo (precisam de transação aberta): ligar a um código, sincronizar, desligar.</summary>
    public static class Operacoes
    {
        /// <summary>Grava Keynote, Type Mark, Descrição, Comentários de tipo (= FJ) e o vínculo; renomeia se pedido.</summary>
        static void Gravar(Document doc, FamilySymbol s, TipoInfo t, Dictionary<string, object> item, string itemProjeto,
                           string projeto, string pedido, string kn, string desc, bool renomear, bool repetido, List<string> avisos)
        {
            Vinculo.Definir(s, BuiltInParameter.ALL_MODEL_TYPE_MARK, kn);
            Vinculo.Definir(s, BuiltInParameter.KEYNOTE_PARAM, kn);
            Vinculo.Definir(s, BuiltInParameter.ALL_MODEL_DESCRIPTION, desc);
            Vinculo.Definir(s, BuiltInParameter.ALL_MODEL_TYPE_COMMENTS, item.Txt("fja") ?? "");
            Vinculo.Gravar(s, item.Txt("fja"), item.Txt("pendente") == "True" ? "" : item.Txt("id"), itemProjeto, projeto, pedido,
                           Vinculo.Param(s, BuiltInParameter.KEYNOTE_PARAM), Vinculo.Param(s, BuiltInParameter.ALL_MODEL_DESCRIPTION));
            if (renomear && kn != "") Renomear(doc, s, t, kn, desc, repetido, avisos);
        }

        /// <summary>Nome "KEYNOTE - DESCRIÇÃO"; se outro tipo da família já tem esse nome (ou vários tipos usam o mesmo
        /// código), acrescenta " - nome original".</summary>
        static void Renomear(Document doc, FamilySymbol s, TipoInfo t, string kn, string desc, bool repetido, List<string> avisos)
        {
            string orig = t.Nome;
            string prefixo = kn + " - ";
            if (orig.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            {
                // já renomeado antes: o original é o que vem depois da descrição
                string resto = orig.Substring(prefixo.Length);
                int i = resto.LastIndexOf(" - ", StringComparison.Ordinal);
                orig = i >= 0 ? resto.Substring(i + 3) : resto;
            }
            string novo = Vinculo.LimparNome(kn + " - " + desc);
            var irmaos = new HashSet<string>(s.Family.GetFamilySymbolIds().Where(i => i != s.Id).Select(i => doc.GetElement(i)?.Name ?? ""));
            if (repetido || irmaos.Contains(novo)) novo = Vinculo.LimparNome(novo + " - " + orig);
            if (novo == s.Name) return;
            try { s.Name = novo; t.Nome = novo; }
            catch (Exception e) { avisos.Add("Não foi possível renomear '" + s.Name + "': " + e.Message); }
        }

        /// <summary>Liga os tipos ao item da biblioteca. Item fora da especificação do projeto: reserva o próximo keynote
        /// da sigla e envia o pedido "incluir-no-projeto".</summary>
        public static List<string> Ligar(Document doc, DadosSistema dados, List<TipoInfo> tipos, Dictionary<string, object> item, bool renomear,
                                         bool renomearFamilia)
        {
            var avisos = new List<string>();
            if (dados.Projeto == null)
                throw new InvalidOperationException("O modelo não está ligado a um projeto do sistema: escolha o projeto no alto do painel.");
            var linha = dados.LinhaProjeto(item.Txt("id"));
            string kn = linha?.Txt("kn") ?? "";
            string desc = (linha?.Txt("desc") ?? item.Txt("desc") ?? "").ToUpperInvariant();
            string pedido = linha?.Txt("pendente") ?? "", itemProjeto = linha?.Txt("id") ?? "";
            if (desc.Trim() == "") throw new InvalidOperationException(item.Txt("fja") + " está sem descrição no sistema.");
            if (linha == null)
            {
                string sg = item.Txt("sigla");
                if (string.IsNullOrEmpty(sg)) throw new InvalidOperationException(item.Txt("fja") + " não está na especificação do projeto e não tem sigla.");
                kn = dados.ProxKn(sg, null);
                var ped = dados.PedidoIncluir(item, kn, doc.Title);
                dados.GravarPedido(ped);
                pedido = ped.Txt("id");
                avisos.Add(item.Txt("fja") + " entra na especificação do projeto como " + kn + " (pedido enviado ao sistema).");
            }
            bool rep = tipos.Count > 1;
            foreach (var t in tipos)
            {
                if (!(doc.GetElement(t.Id) is FamilySymbol s)) continue;
                Gravar(doc, s, t, item, itemProjeto, dados.Projeto.Txt("id"), pedido, kn, desc, renomear, rep, avisos);
            }
            if (renomearFamilia) RenomearFamilia(doc, tipos, item.Txt("sigla"), desc, avisos);
            return avisos;
        }

        /// <summary>Código novo na biblioteca (pedido "novo-item", com FJ e keynote reservados; o sistema confirma) e
        /// ligação dos tipos a ele. siglaNova: {sigla, nome, tipo} quando a sigla também é nova.</summary>
        public static List<string> LigarNovo(Document doc, DadosSistema dados, List<TipoInfo> tipos, string sigla, string desc, string texto,
                                             Dictionary<string, object> siglaNova, bool renomear, bool renomearFamilia)
        {
            if (dados.Projeto == null)
                throw new InvalidOperationException("O modelo não está ligado a um projeto do sistema: escolha o projeto no alto do painel.");
            sigla = (sigla ?? "").Trim().ToUpperInvariant();
            desc = (desc ?? "").Trim().ToUpperInvariant();
            if (sigla == "") throw new InvalidOperationException("Escolha a sigla do código novo.");
            if (desc == "") throw new InvalidOperationException("Informe a descrição do código novo.");
            string pid = DadosSistema.NovoIdPedido();
            var item = new Dictionary<string, object>
            {
                ["id"] = "pedido:" + pid, ["pedido"] = pid, ["pendente"] = true, ["fja"] = dados.ProxFja(sigla, null), ["sigla"] = sigla,
                ["tipo"] = "elemento", ["desc"] = desc, ["texto"] = (texto ?? "").Trim(), ["rvCat"] = tipos.FirstOrDefault()?.Categoria ?? "",
                ["siglaNova"] = siglaNova,
            };
            string kn = dados.ProxKn(sigla, null);
            dados.GravarPedido(dados.PedidoNovoItem(pid, item, kn, doc.Title));
            var avisos = new List<string> { "Código novo " + item.Txt("fja") + " (" + desc + "), no projeto como " + kn + ": pedido enviado ao sistema, que confirma o código e o keynote." };
            if (siglaNova != null) dados.Bib.Lista("siglas").Add(siglaNova);   // disponível para os próximos códigos novos
            if (siglaNova != null) avisos.Add("Sigla nova " + sigla + " (" + siglaNova.Txt("nome") + "): o sistema cadastra antes de criar o item.");
            bool rep = tipos.Count > 1;
            foreach (var t in tipos)
            {
                if (!(doc.GetElement(t.Id) is FamilySymbol s)) continue;
                Gravar(doc, s, t, item, "", dados.Projeto.Txt("id"), pid, kn, desc, renomear, rep, avisos);
            }
            if (renomearFamilia) RenomearFamilia(doc, tipos, sigla, desc, avisos);
            return avisos;
        }

        /// <summary>Família como "(SIGLA) DESCRIÇÃO" (todas as famílias dos tipos; normalmente uma só).</summary>
        static void RenomearFamilia(Document doc, List<TipoInfo> tipos, string sigla, string desc, List<string> avisos)
        {
            string nome = Vinculo.LimparNome("(" + (sigla ?? "").ToUpperInvariant() + ") " + (desc ?? "").ToUpperInvariant());
            if (string.IsNullOrEmpty(sigla) || nome.Length < 5) return;
            var fams = tipos.Select(t => (doc.GetElement(t.Id) as FamilySymbol)?.Family).Where(f => f != null)
                .GroupBy(f => Coleta.Num(f.Id)).Select(g => g.First()).ToList();
            if (fams.Count != 1) { if (fams.Count > 1) avisos.Add("Tipos de famílias diferentes: os nomes das famílias não foram trocados."); return; }
            var fam = fams[0];
            if (fam.Name == nome) return;
            bool existe = new FilteredElementCollector(doc).OfClass(typeof(Family)).Any(f => f.Name == nome && f.Id != fam.Id);
            if (existe) { avisos.Add("Já existe uma família chamada '" + nome + "': o nome não foi trocado."); return; }
            try { fam.Name = nome; }
            catch (Exception e) { avisos.Add("Não foi possível renomear a família: " + e.Message); }
        }

        sealed class OpcoesCarga : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = false; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family; overwriteParameterValues = false; return true;
            }
        }

        /// <summary>Carrega o .rfa (se já existir, recarrega sem sobrescrever os valores dos parâmetros). Devolve a família.</summary>
        public static Family CarregarFamilia(Document doc, string caminho, out bool jaExistia)
        {
            string nome = System.IO.Path.GetFileNameWithoutExtension(caminho);
            jaExistia = new FilteredElementCollector(doc).OfClass(typeof(Family)).Any(f => f.Name == nome);
            doc.LoadFamily(caminho, new OpcoesCarga(), out Family fam);
            return fam ?? new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == nome)
                ?? throw new InvalidOperationException("Não foi possível carregar a família " + nome + ".");
        }

        /// <summary>Traz para o Revit o que está no sistema (o sistema é a referência). Tipos ligados a itens fora da
        /// especificação do projeto entram nela (keynote reservado + pedido "incluir-no-projeto", um por item).</summary>
        public static List<string> Sincronizar(Document doc, DadosSistema dados, List<TipoInfo> tipos, bool renomear)
        {
            var avisos = new List<string>();
            var reservados = new List<string>();
            var porItem = new Dictionary<string, Tuple<string, string>>();   // item → (kn, pedido) dos que entram no projeto agora
            foreach (var t in tipos.Where(x => Coleta.Sincronizavel(x.Sit) && x.Item != null))
            {
                if (!(doc.GetElement(t.Id) is FamilySymbol s)) continue;
                var item = t.Item;
                string kn = t.KnSis, desc = t.DescSis, pedido = t.Vinc?.Campo("pedido") ?? "", itemProjeto = t.Linha?.Txt("id") ?? "";
                if (t.Sit == Situacao.Fora)
                {
                    if (dados.Projeto == null) continue;
                    string iid = item.Txt("id");
                    if (!porItem.TryGetValue(iid, out var res))
                    {
                        string sg = item.Txt("sigla");
                        if (string.IsNullOrEmpty(sg)) { avisos.Add(t.Familia + " : " + t.Nome + " — " + item.Txt("fja") + " sem sigla: não entrou no projeto."); continue; }
                        string k = dados.ProxKn(sg, reservados);
                        reservados.Add(k);
                        var ped = dados.PedidoIncluir(item, k, doc.Title);
                        dados.GravarPedido(ped);
                        porItem[iid] = res = Tuple.Create(k, ped.Txt("id"));
                    }
                    kn = res.Item1; pedido = res.Item2; itemProjeto = "";
                    desc = (item.Txt("desc") ?? "").ToUpperInvariant();
                }
                Gravar(doc, s, t, item, itemProjeto, dados.Projeto?.Txt("id") ?? t.Vinc?.Campo("projeto") ?? "", pedido, kn, desc, renomear, false, avisos);
            }
            if (porItem.Count > 0)
                avisos.Insert(0, porItem.Count + " item(ns) entraram na especificação do projeto (pedido enviado ao sistema; ele confirma o keynote).");
            return avisos;
        }

        /// <summary>Seta →: envia ao sistema a Descrição do Revit (pedido "editar-item", um por item) e marca o pedido no vínculo
        /// dos tipos (ficam "aguardando" até o sistema processar). Itens com descrições diferentes entre os tipos ficam de fora.
        /// Precisa de transação aberta.</summary>
        public static List<string> LevarDescricao(Document doc, DadosSistema dados, List<TipoInfo> tipos)
        {
            var avisos = new List<string>();
            foreach (var g in tipos.Where(Coleta.PodeLevar).GroupBy(t => t.Item.Txt("id")))
            {
                var descs = g.Select(t => (t.Desc ?? "").Trim().ToUpperInvariant()).Distinct().ToList();
                var item = g.First().Item;
                if (descs.Count > 1)
                {
                    avisos.Add(item.Txt("fja") + ": os tipos têm descrições diferentes no Revit (" + string.Join(" / ", descs) + "); nada enviado.");
                    continue;
                }
                var ped = dados.PedidoDescricao(item, descs[0], doc.Title);
                dados.GravarPedido(ped);
                foreach (var t in g)
                {
                    if (!(doc.GetElement(t.Id) is FamilySymbol s)) continue;
                    // keynote e Type Mark são numeração do sistema: voltam aos do sistema, se alterados no Revit
                    if (!string.IsNullOrEmpty(t.KnSis)) { Vinculo.Definir(s, BuiltInParameter.ALL_MODEL_TYPE_MARK, t.KnSis); Vinculo.Definir(s, BuiltInParameter.KEYNOTE_PARAM, t.KnSis); }
                    Vinculo.Gravar(s, item.Txt("fja"), item.Txt("id"), t.Linha?.Txt("id") ?? "", dados.Projeto?.Txt("id") ?? t.Vinc?.Campo("projeto") ?? "",
                                   ped.Txt("id"), Vinculo.Param(s, BuiltInParameter.KEYNOTE_PARAM), Vinculo.Param(s, BuiltInParameter.ALL_MODEL_DESCRIPTION));
                }
                avisos.Add(item.Txt("fja") + ": descrição \"" + descs[0] + "\" enviada ao sistema (" + g.Count() + " tipo(s)).");
            }
            return avisos;
        }

        /// <summary>Apaga o vínculo (o tipo fica como estava, só deixa de ser acompanhado).</summary>
        public static void Desligar(Document doc, IEnumerable<TipoInfo> tipos)
        {
            foreach (var t in tipos)
            {
                var e = doc.GetElement(t.Id);
                if (e == null) continue;
                foreach (var g in new[] { new Guid("8d2e4b61-5f3a-4c09-b7e2-1a9c6f0d3e58"), new Guid("3c6f1d2a-9b84-4e57-a1f0-6d2e8b4c7a15") })
                {
                    var sc = Schema.Lookup(g);
                    if (sc != null) try { e.DeleteEntity(sc); } catch { }
                }
            }
        }
    }
}
