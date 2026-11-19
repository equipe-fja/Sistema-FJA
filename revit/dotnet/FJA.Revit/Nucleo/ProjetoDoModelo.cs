using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using FJA.Revit.Legendas;

namespace FJA.Revit.Nucleo
{
    /// <summary>
    /// Projeto do Sistema FJA a que o modelo pertence, para todas as ferramentas (C# e Python: lib/fja_sistema.py,
    /// projeto_do_modelo). A ligação fica gravada no próprio modelo (DataStorage com o esquema FJA_ProjetoModelo):
    /// vale para a equipe toda depois de sincronizar, e continua valendo com o arquivo renomeado e nas cópias locais.
    /// Sem ligação gravada, o projeto é reconhecido por pistas, nesta ordem: Número do projeto (Informações do
    /// projeto), código numa pasta do caminho do arquivo (do central, nas cópias locais), código no nome do arquivo,
    /// código do cliente e nome do projeto. A primeira pista que aponta para um único projeto vale.
    /// Ver docs/projeto-do-modelo.md.
    /// </summary>
    public static class ProjetoDoModelo
    {
        public static readonly Guid GUID = new Guid("5e1b7c3a-2d84-4f69-9a1e-7c40b2d8f613");
        static readonly string[] CAMPOS = { "id", "codigo", "nome", "gravadoEm", "gravadoPor" };

        public sealed class Ligacao { public string Id, Codigo, Nome, GravadoEm, GravadoPor; }

        static Schema Esquema()
        {
            var sc = Schema.Lookup(GUID);
            if (sc != null) return sc;
            var sb = new SchemaBuilder(GUID);
            sb.SetSchemaName("FJA_ProjetoModelo");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("Projeto do Sistema FJA a que o modelo pertence");
            foreach (var c in CAMPOS) sb.AddSimpleField(c, typeof(string));
            return sb.Finish();
        }

        static DataStorage Armazem(Document doc, Schema sc)
        {
            if (sc == null) return null;
            foreach (DataStorage ds in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)))
            {
                try { var e = ds.GetEntity(sc); if (e != null && e.IsValid()) return ds; } catch { }
            }
            return null;
        }

        /// <summary>A ligação gravada no modelo, ou null.</summary>
        public static Ligacao Ler(Document doc)
        {
            var sc = Schema.Lookup(GUID);
            var ds = Armazem(doc, sc);
            if (ds == null) return null;
            var e = ds.GetEntity(sc);
            var l = new Ligacao
            {
                Id = e.Get<string>("id"), Codigo = e.Get<string>("codigo"), Nome = e.Get<string>("nome"),
                GravadoEm = e.Get<string>("gravadoEm"), GravadoPor = e.Get<string>("gravadoPor"),
            };
            return string.IsNullOrEmpty(l.Id) && string.IsNullOrEmpty(l.Codigo) ? null : l;
        }

        /// <summary>Grava (ou, com proj = null, apaga) a ligação. Precisa de transação aberta.</summary>
        public static void Gravar(Document doc, Dictionary<string, object> proj)
        {
            var ds = Armazem(doc, Schema.Lookup(GUID));
            if (proj == null)
            {
                if (ds != null) doc.Delete(ds.Id);
                return;
            }
            if (ds == null) ds = DataStorage.Create(doc);
            var ent = new Entity(Esquema());
            ent.Set<string>("id", proj.Txt("id") ?? "");
            ent.Set<string>("codigo", proj.Txt("codigo") ?? "");
            ent.Set<string>("nome", proj.Txt("nome") ?? "");
            ent.Set<string>("gravadoEm", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
            ent.Set<string>("gravadoPor", Environment.UserName ?? "");
            ds.SetEntity(ent);
        }

        /// <summary>Grava numa transação própria, quando o modelo pode ser alterado. Devolve "" ou o motivo de não ter gravado.</summary>
        public static string GravarAgora(Document doc, Dictionary<string, object> proj)
        {
            try
            {
                if (doc.IsReadOnly) return "modelo somente leitura";
                if (doc.IsModifiable) { Gravar(doc, proj); return ""; }   // já dentro de uma transação
                using (var t = new Transaction(doc, "FJA: projeto do modelo"))
                {
                    t.Start();
                    Gravar(doc, proj);
                    return t.Commit() == TransactionStatus.Committed ? "" : "transação não concluída";
                }
            }
            catch (Exception e) { return e.Message; }
        }

        // ------------------------------------------------------------ reconhecimento

        /// <summary>Caminho do arquivo: o do central nas cópias locais de modelos compartilhados.</summary>
        public static string Caminho(Document doc)
        {
            try
            {
                if (doc.IsWorkshared)
                {
                    var mp = doc.GetWorksharingCentralModelPath();
                    if (mp != null)
                    {
                        string c = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                        if (!string.IsNullOrEmpty(c)) return c;
                    }
                }
            }
            catch { }
            return doc.PathName ?? "";
        }

        static string CodigoNoInicio(string s)
        {
            var m = Regex.Match(s ?? "", @"^\s*([A-Za-z0-9]+)\s*[-_ .]");
            return m.Success ? m.Groups[1].Value : "";
        }

        static bool MesmoCodigo(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Pistas em ordem: cada uma com os projetos para os quais aponta.</summary>
        public static List<Tuple<string, List<Dictionary<string, object>>>> Pistas(Document doc, IList<Dictionary<string, object>> projetos)
        {
            var r = new List<Tuple<string, List<Dictionary<string, object>>>>();
            void Add(string origem, IEnumerable<Dictionary<string, object>> ps)
            {
                var l = ps.Distinct().ToList();
                if (l.Count > 0) r.Add(Tuple.Create(origem, l));
            }

            // 1. Número do projeto (Informações do projeto)
            string num = "", nomeInfo = "", cliente = "";
            try
            {
                var pi = doc.ProjectInformation;
                if (pi != null) { num = pi.Number ?? ""; nomeInfo = pi.Name ?? ""; cliente = pi.ClientName ?? ""; }
            }
            catch { }
            if (num.Trim() != "")
            {
                string c0 = CodigoNoInicio(num + " ");
                Add("Número do projeto (" + num.Trim() + ")", projetos.Where(p => MesmoCodigo(p.Txt("codigo"), num) || MesmoCodigo(p.Txt("codigo"), c0) || MesmoCodigo(p.Txt("codCliente"), num)));
            }

            // 2. código numa pasta do caminho; 3. no nome do arquivo
            string caminho = Caminho(doc);
            var partes = caminho.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string arquivo = partes.Count > 0 ? Path.GetFileNameWithoutExtension(partes[partes.Count - 1]) : (doc.Title ?? "");
            if (partes.Count > 0) partes.RemoveAt(partes.Count - 1);
            for (int i = partes.Count - 1; i >= 0; i--)
            {
                string c = CodigoNoInicio(partes[i] + " ");
                if (c == "") continue;
                var ps = projetos.Where(p => MesmoCodigo(p.Txt("codigo"), c)).ToList();
                if (ps.Count > 0) { Add("pasta \"" + partes[i] + "\"", ps); break; }
            }
            foreach (var nome in new[] { arquivo, doc.Title ?? "" }.Distinct())
            {
                string c = CodigoNoInicio(nome);
                if (c != "") Add("nome do arquivo", projetos.Where(p => MesmoCodigo(p.Txt("codigo"), c)));
            }

            // 4. código do cliente e nome do projeto no caminho, no título ou nas Informações do projeto
            string texto = Texto.NormSistema(string.Join(" ", caminho, doc.Title, nomeInfo, cliente));
            Add("código do cliente", projetos.Where(p =>
            {
                string cc = Texto.NormSistema(p.Txt("codCliente"));
                return cc.Length >= 3 && (" " + texto + " ").Contains(" " + cc + " ");
            }));
            Add("nome do projeto", projetos.Where(p =>
            {
                string n = Texto.NormSistema(p.Txt("nome"));
                return n.Length >= 5 && texto.Contains(n);
            }));
            return r;
        }

        /// <summary>
        /// Projeto do modelo: o gravado; senão, o da primeira pista que aponta para um único projeto.
        /// gravado = veio da ligação gravada. candidatos = projetos sugeridos quando as pistas não decidem.
        /// </summary>
        public static Dictionary<string, object> Resolver(Document doc, IList<Dictionary<string, object>> projetos, out string origem,
                                                          out bool gravado, out List<Dictionary<string, object>> candidatos)
        {
            gravado = false;
            candidatos = new List<Dictionary<string, object>>();
            var lig = Ler(doc);
            if (lig != null)
            {
                gravado = true;
                var p = projetos.FirstOrDefault(x => !string.IsNullOrEmpty(lig.Id) && x.Txt("id") == lig.Id)
                        ?? projetos.FirstOrDefault(x => MesmoCodigo(x.Txt("codigo"), lig.Codigo));
                origem = "gravado no modelo" + (string.IsNullOrEmpty(lig.GravadoPor) ? "" : " por " + lig.GravadoPor);
                if (p == null) origem = "gravado no modelo: " + ((lig.Codigo ?? "") + " " + (lig.Nome ?? "")).Trim() + " — não está entre os projetos publicados nesta fonte";
                return p;
            }
            foreach (var pista in Pistas(doc, projetos))
            {
                if (pista.Item2.Count == 1) { origem = pista.Item1; return pista.Item2[0]; }
                foreach (var p in pista.Item2) if (!candidatos.Contains(p)) candidatos.Add(p);
            }
            origem = candidatos.Count > 0 ? "várias possibilidades: escolha o projeto" : "não reconhecido: escolha o projeto";
            return null;
        }
    }
}
