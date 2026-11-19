using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FJA.Revit.Legendas;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Componentes
{
    /// <summary>
    /// Biblioteca central + especificação de um projeto, como publicados pelo Sistema FJA (porte de
    /// lib/fja_sistema.py, classe Dados). Oficial: banco do sistema com o login do computador, senão a cópia do G:
    /// (REVIT); Desenvolvimento: REVIT\DEV no G:.
    /// </summary>
    public sealed class DadosSistema
    {
        public const string PASTA_REVIT = @"G:\Drives compartilhados\BI-BIBLIOTECA\SISTEMA FJA\REVIT";

        public bool Dev, Nuvem;
        public string Pasta, Rotulo = "", Aviso = "", GeradoEm = "";
        public Dictionary<string, object> Bib;
        public Dictionary<string, Dictionary<string, object>> Itens = new Dictionary<string, Dictionary<string, object>>();
        public List<Dictionary<string, object>> Projetos = new List<Dictionary<string, object>>();
        public Dictionary<string, object> Projeto;
        public List<Dictionary<string, object>> Espec = new List<Dictionary<string, object>>();
        public Dictionary<string, Dictionary<string, object>> PorItem = new Dictionary<string, Dictionary<string, object>>();
        public List<Dictionary<string, object>> Pendentes = new List<Dictionary<string, object>>();
        public string PastaPedidos => Path.Combine(Pasta, "pedidos");

        public static DadosSistema Carregar(bool dev)
        {
            var d = new DadosSistema { Dev = dev, Pasta = dev ? Path.Combine(PASTA_REVIT, "DEV") : PASTA_REVIT };
            if (!dev)
            {
                try
                {
                    if (Nucleo.Nuvem.TemLogin)
                    {
                        d.Bib = Nucleo.Nuvem.Publicado("biblioteca");
                        d.Nuvem = d.Bib != null;
                    }
                    else d.Aviso = "sem login no banco neste computador";
                }
                catch (Exception e) { d.Aviso = ("banco indisponível: " + e.Message); if (d.Aviso.Length > 120) d.Aviso = d.Aviso.Substring(0, 120); }
            }
            if (d.Bib == null)
            {
                string arq = MaisRecente(d.Pasta, "biblioteca");
                if (arq == null) throw new IOException("Não encontrei biblioteca.json em " + d.Pasta);
                d.Bib = Json.LerObjeto(File.ReadAllText(arq, Encoding.UTF8));
            }
            d.GeradoEm = d.Bib.Txt("geradoEm") ?? "";
            foreach (var x in d.Bib.Lista("itens").OfType<Dictionary<string, object>>())
                if (x.Txt("id") != null) d.Itens[x.Txt("id")] = x;
            d.Projetos = d.Bib.Lista("projetos").OfType<Dictionary<string, object>>().ToList();
            d.Rotulo = dev ? "Desenvolvimento (REVIT\\DEV)"
                : d.Nuvem ? "Oficial (banco, " + Nucleo.Nuvem.Email + ")"
                : "Oficial (cópia do G:" + (d.Aviso != "" ? " — " + d.Aviso : "") + ")";
            return d;
        }

        /// <summary>'biblioteca.json', 'biblioteca (3).json'… → o mais recente (o Drive para Desktop renomeia).</summary>
        static string MaisRecente(string pasta, string prefixo)
        {
            if (!Directory.Exists(pasta)) return null;
            var rx = new Regex("^" + Regex.Escape(prefixo) + @"( \(\d+\))?\.json$", RegexOptions.IgnoreCase);
            return Directory.GetFiles(pasta).Where(f => rx.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        /// <summary>Itens de elemento/família (siglas do tipo elemento), mais os códigos novos pedidos pelo Revit que o
        /// sistema ainda não processou (provisórios, id "pedido:&lt;id&gt;").</summary>
        public IEnumerable<Dictionary<string, object>> Elementos() =>
            Bib.Lista("itens").OfType<Dictionary<string, object>>().Where(x => x.Txt("tipo") == "elemento")
                .Concat(ItensPendentes().Where(x => x.Txt("tipo") == "elemento"));

        /// <summary>Itens provisórios dos pedidos "novo-item" ainda não processados (como item_do_pedido do Python).</summary>
        public List<Dictionary<string, object>> ItensPendentes()
        {
            var feitos = new HashSet<string>(Bib.Lista("itens").OfType<Dictionary<string, object>>().Select(x => x.Txt("pedido")).Where(x => !string.IsNullOrEmpty(x)));
            var r = new List<Dictionary<string, object>>();
            foreach (var p in Pendentes)
            {
                if (p.Txt("acao") != "novo-item" || feitos.Contains(p.Txt("id"))) continue;
                var i = p.Obj("item");
                r.Add(new Dictionary<string, object>
                {
                    ["id"] = "pedido:" + p.Txt("id"), ["pedido"] = p.Txt("id"), ["pendente"] = true,
                    ["fja"] = p.Obj("reservas").Txt("fja") ?? "", ["sigla"] = i.Txt("sigla") ?? "", ["tipo"] = i.Txt("tipo") ?? "elemento",
                    ["desc"] = i.Txt("desc") ?? "", ["texto"] = i.Txt("texto") ?? "", ["rvCat"] = i.Txt("rvCat") ?? "",
                });
            }
            return r;
        }

        /// <summary>Siglas de elemento/família da biblioteca (com as novas criadas nesta sessão), em ordem.</summary>
        public List<Dictionary<string, object>> SiglasElemento() =>
            Bib.Lista("siglas").OfType<Dictionary<string, object>>().Where(s => s.Txt("tipo") == "elemento")
                .OrderBy(s => s.Txt("sigla") ?? "").ToList();

        public bool SiglaExiste(string sg) =>
            Bib.Lista("siglas").OfType<Dictionary<string, object>>().Any(s => string.Equals(s.Txt("sigla"), sg, StringComparison.OrdinalIgnoreCase));

        /// <summary>Próximo código FJ.&lt;SIGLA&gt;.NNNN (numeração de cada sigla), contando a biblioteca, os pedidos pendentes
        /// e as reservas deste lote.</summary>
        public string ProxFja(string sigla, ICollection<string> tambem)
        {
            string sg = (sigla ?? "").ToUpperInvariant();
            var rx = new Regex(@"^FJ\." + Regex.Escape(sg) + @"\.(\d+)$");
            int n = 0;
            IEnumerable<string> todos = Bib.Lista("itens").OfType<Dictionary<string, object>>().Select(x => x.Txt("fja"))
                .Concat(Pendentes.Select(p => p.Obj("reservas").Txt("fja"))).Concat(tambem ?? new List<string>());
            foreach (var f in todos)
            {
                var m = rx.Match(f ?? "");
                if (m.Success) n = Math.Max(n, int.Parse(m.Groups[1].Value));
            }
            return "FJ." + sg + "." + (n + 1).ToString("0000");
        }

        /// <summary>Pedido "novo-item": item de elemento/família novo na biblioteca, já incluído no projeto com o keynote
        /// reservado. siglaNova: {sigla, nome, tipo} quando a sigla também é nova (o sistema cadastra antes).</summary>
        public Dictionary<string, object> PedidoNovoItem(string id, Dictionary<string, object> item, string kn, string modelo)
        {
            var it = new Dictionary<string, object>
            {
                ["sigla"] = item.Txt("sigla"), ["tipo"] = "elemento", ["rvCat"] = item.Txt("rvCat") ?? "", ["papel"] = "",
                ["desc"] = item.Txt("desc") ?? "", ["texto"] = item.Txt("texto") ?? "", ["fabricante"] = "", ["nbr"] = "",
                ["camadas"] = new List<object>(),
            };
            if (item.TryGetValue("siglaNova", out var sn) && sn != null) it["siglaNova"] = sn;
            return new Dictionary<string, object>
            {
                ["fja"] = "pedido",
                ["formato"] = 1,
                ["id"] = id,
                ["acao"] = "novo-item",
                ["criadoEm"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["autor"] = Autor(),
                ["modelo"] = modelo ?? "",
                ["projeto"] = Projeto == null ? null : new Dictionary<string, object> { ["id"] = Projeto.Txt("id"), ["codigo"] = Projeto.Txt("codigo") },
                ["reservas"] = new Dictionary<string, object> { ["kn"] = kn, ["fja"] = item.Txt("fja") },
                ["item"] = it,
            };
        }

        /// <summary>'131-MO-0001-R00' → projeto 131; senão, projeto cujo nome aparece no título do arquivo.</summary>
        public Dictionary<string, object> ProjetoDoDocumento(string titulo)
        {
            var m = Regex.Match(titulo ?? "", @"^\s*([A-Za-z0-9]+)[-_ ]");
            string cod = m.Success ? m.Groups[1].Value : "";
            var p = Projetos.FirstOrDefault(x => cod != "" && x.Txt("codigo") == cod);
            if (p != null) return p;
            string tit = Texto.NormSistema(titulo);
            return Projetos.FirstOrDefault(x => { string n = Texto.NormSistema(x.Txt("nome")); return n.Length >= 5 && tit.Contains(n); });
        }

        public void CarregarProjeto(Dictionary<string, object> proj)
        {
            Projeto = proj;
            Espec = new List<Dictionary<string, object>>();
            PorItem = new Dictionary<string, Dictionary<string, object>>();
            string arquivo = proj?.Txt("arquivo");
            string nome = proj == null ? "" : Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(arquivo) ? "projeto-" + proj.Txt("codigo") + ".json" : arquivo);
            Dictionary<string, object> dados = null;
            if (proj != null && proj.Txt("semEspec") == "True")
            {
                // projeto ainda sem especificação técnica: o sistema não publica o arquivo do projeto
                try { Pendentes = Nuvem ? Nucleo.Nuvem.PedidosPendentes() : LerPendentes(PastaPedidos); } catch { Pendentes = new List<Dictionary<string, object>>(); }
                return;
            }
            if (Nuvem)
            {
                try { Pendentes = Nucleo.Nuvem.PedidosPendentes(); } catch { Pendentes = new List<Dictionary<string, object>>(); }
                if (proj == null) return;
                dados = Nucleo.Nuvem.Publicado(nome) ?? throw new IOException("O sistema ainda não publicou " + nome + " no banco.");
            }
            else
            {
                Pendentes = LerPendentes(PastaPedidos);
                if (proj == null) return;
                string arq = MaisRecente(Pasta, nome) ?? throw new IOException("Não encontrei " + nome + ".json em " + Pasta);
                dados = Json.LerObjeto(File.ReadAllText(arq, Encoding.UTF8));
            }
            Espec = dados.Lista("itens").OfType<Dictionary<string, object>>().ToList();
            foreach (var e in Espec)
                if (!string.IsNullOrEmpty(e.Txt("item")) && !PorItem.ContainsKey(e.Txt("item"))) PorItem[e.Txt("item")] = e;
        }

        static List<Dictionary<string, object>> LerPendentes(string pasta)
        {
            var r = new List<Dictionary<string, object>>();
            if (!Directory.Exists(pasta)) return r;
            foreach (var f in Directory.GetFiles(pasta, "*.json").OrderBy(x => x))
                try { r.Add(Json.LerObjeto(File.ReadAllText(f, Encoding.UTF8))); } catch { }
            return r;
        }

        /// <summary>Linha da especificação do projeto; sem ela, mas com pedido pendente que reservou keynote para o item,
        /// uma linha provisória {id:"", kn, pendente}.</summary>
        public Dictionary<string, object> LinhaProjeto(string itemId)
        {
            if (itemId != null && PorItem.TryGetValue(itemId, out var ln)) return ln;
            if (Projeto == null || itemId == null) return null;
            foreach (var p in Pendentes)
            {
                if (p.Obj("projeto").Txt("id") != Projeto.Txt("id")) continue;
                var r = p.Obj("reservas");
                if (!string.IsNullOrEmpty(r.Txt("kn")) && (p.Obj("item").Txt("id") == itemId || "pedido:" + p.Txt("id") == itemId))
                    return new Dictionary<string, object> { ["id"] = "", ["kn"] = r.Txt("kn"), ["pendente"] = p.Txt("id") };
            }
            return null;
        }

        /// <summary>Próximo keynote livre da sigla no projeto (especificação, pedidos pendentes e reservas deste lote).</summary>
        public string ProxKn(string sigla, ICollection<string> tambem)
        {
            sigla = (sigla ?? "").ToUpperInvariant();
            var usados = new HashSet<string>(Espec.Select(e => (e.Txt("kn") ?? "").ToUpperInvariant()));
            foreach (var p in Pendentes)
            {
                var r = p.Obj("reservas");
                usados.Add((r.Txt("kn") ?? "").ToUpperInvariant());
                foreach (var kv in r.Obj("materiais")) usados.Add(Convert.ToString(kv.Value).ToUpperInvariant());
            }
            foreach (var k in tambem ?? new List<string>()) usados.Add((k ?? "").ToUpperInvariant());
            int n = 0;
            var rx = new Regex(@"^\.?" + Regex.Escape(sigla) + @"\.(\d+)$");
            foreach (var k in usados)
            {
                var m = rx.Match(k);
                if (m.Success) n = Math.Max(n, int.Parse(m.Groups[1].Value));
            }
            while (true)
            {
                n++;
                string k = sigla + "." + n.ToString("00");
                if (!usados.Contains(k)) return k;
            }
        }

        // ------------------------------------------------------------ pedidos

        public static string Autor() => Environment.UserName ?? "?";

        static readonly Random rnd = new Random();

        public static string NovoIdPedido()
        {
            string a = Regex.Replace(Autor(), "[^A-Za-z0-9]", "");
            if (a.Length > 12) a = a.Substring(0, 12);
            return DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + (a == "" ? "rv" : a) + "-" + rnd.Next(0, 10000).ToString("0000");
        }

        /// <summary>Pedido "incluir-no-projeto": o item entra na especificação do projeto com o keynote reservado.</summary>
        public Dictionary<string, object> PedidoIncluir(Dictionary<string, object> item, string kn, string modelo)
        {
            return new Dictionary<string, object>
            {
                ["fja"] = "pedido",
                ["formato"] = 1,
                ["id"] = NovoIdPedido(),
                ["acao"] = "incluir-no-projeto",
                ["criadoEm"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["autor"] = Autor(),
                ["modelo"] = modelo ?? "",
                ["projeto"] = Projeto == null ? null : new Dictionary<string, object> { ["id"] = Projeto.Txt("id"), ["codigo"] = Projeto.Txt("codigo") },
                ["reservas"] = new Dictionary<string, object> { ["kn"] = kn },
                ["item"] = new Dictionary<string, object> { ["id"] = item.Txt("id") },
            };
        }

        /// <summary>Envia ao banco (oficial com login) ou grava em REVIT\pedidos no G:. Devolve onde foi.</summary>
        public string GravarPedido(Dictionary<string, object> pedido)
        {
            string onde;
            if (Nuvem)
            {
                Nucleo.Nuvem.EnviarPedido(pedido);
                onde = "banco";
            }
            else
            {
                Directory.CreateDirectory(PastaPedidos);
                onde = Path.Combine(PastaPedidos, "pedido-" + pedido.Txt("id") + ".json");
                File.WriteAllText(onde, Json.Gravar(pedido), new UTF8Encoding(false));
            }
            Pendentes.Add(pedido);
            return onde;
        }
    }
}
