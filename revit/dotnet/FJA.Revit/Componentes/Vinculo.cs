using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace FJA.Revit.Componentes
{
    /// <summary>
    /// Vínculo tipo ↔ item do Sistema FJA (Extensible Storage), o mesmo das ferramentas em Python
    /// (lib/fja_sistema.py): FJA_SistemaTipo (v1) e FJA_SistemaTipo2 (v2, com o keynote e a descrição gravados na
    /// última sincronização, para distinguir "o sistema mudou" de "alguém alterou no Revit").
    /// </summary>
    public static class Vinculo
    {
        static readonly Guid G1 = new Guid("3c6f1d2a-9b84-4e57-a1f0-6d2e8b4c7a15");
        static readonly Guid G2 = new Guid("8d2e4b61-5f3a-4c09-b7e2-1a9c6f0d3e58");
        static readonly string[] C1 = { "fja", "itemCentral", "itemProjeto", "projeto", "variacao", "pedido", "sincronizadoEm" };
        static readonly string[] C2 = { "fja", "itemCentral", "itemProjeto", "projeto", "variacao", "pedido", "sincronizadoEm", "kn", "desc" };

        static Schema Esquema2()
        {
            var sc = Schema.Lookup(G2);
            if (sc != null) return sc;
            var sb = new SchemaBuilder(G2);
            sb.SetSchemaName("FJA_SistemaTipo2");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("Vinculo do tipo/material com o Sistema FJA (com valores sincronizados)");
            foreach (var c in C2) sb.AddSimpleField(c, typeof(string));
            return sb.Finish();
        }

        /// <summary>Campos do vínculo (com "_versao" = "1" ou "2"), ou null se o elemento não tem vínculo.</summary>
        public static Dictionary<string, string> Ler(Element e)
        {
            foreach (var (g, campos, ver) in new[] { (G2, C2, "2"), (G1, C1, "1") })
            {
                var sc = Schema.Lookup(g);
                if (sc == null) continue;
                try
                {
                    var ent = e.GetEntity(sc);
                    if (ent == null || !ent.IsValid()) continue;
                    var v = new Dictionary<string, string> { ["_versao"] = ver };
                    foreach (var c in campos) v[c] = ent.Get<string>(c) ?? "";
                    return v;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Grava o vínculo v2 (precisa de transação). kn e desc: os valores gravados agora no tipo.</summary>
        public static void Gravar(Element e, string fja, string itemCentral, string itemProjeto, string projeto, string pedido,
                                  string kn, string desc)
        {
            var ent = new Entity(Esquema2());
            var v = new Dictionary<string, string>
            {
                ["fja"] = fja, ["itemCentral"] = itemCentral, ["itemProjeto"] = itemProjeto, ["projeto"] = projeto,
                ["variacao"] = "", ["pedido"] = pedido, ["sincronizadoEm"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["kn"] = kn, ["desc"] = desc,
            };
            foreach (var c in C2) ent.Set<string>(c, v[c] ?? "");
            e.SetEntity(ent);
        }

        // ------------------------------------------------------------ parâmetros

        public static string Param(Element e, BuiltInParameter bip)
        {
            try { return e.get_Parameter(bip)?.AsString() ?? ""; } catch { return ""; }
        }

        public static bool Definir(Element e, BuiltInParameter bip, string valor)
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && !p.IsReadOnly) return p.Set(valor ?? "");
            }
            catch { }
            return false;
        }

        /// <summary>Caracteres proibidos no Revit trocados por "-" (como limpar_nome do Python).</summary>
        public static string LimparNome(string s)
        {
            s = (s ?? "").Replace(">=", "≥").Replace("<=", "≤");
            s = Regex.Replace(s, @"[\\:{}\[\]|;<>?`~]", "-");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}
