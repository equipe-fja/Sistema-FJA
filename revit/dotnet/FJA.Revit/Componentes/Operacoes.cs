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
        public static List<string> Ligar(Document doc, DadosSistema dados, List<TipoInfo> tipos, Dictionary<string, object> item, bool renomear)
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
            return avisos;
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
