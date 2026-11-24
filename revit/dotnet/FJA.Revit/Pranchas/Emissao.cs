using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Pranchas
{
    /// <summary>Disciplina do projeto (código do catálogo, sigla no projeto e nome completo).</summary>
    public sealed class DiscEm { public string Cod, Sigla, Nome; public bool DoProjeto; public override string ToString() => (Sigla ?? Cod) + " · " + Nome; }

    /// <summary>Quem pode receber a emissão (mesma lista de envolvidosEm(pid) no sistema).</summary>
    public sealed class DestEm { public string Nome, Empresa, Email; }

    /// <summary>Emissão em Rascunho do projeto, para acrescentar a exportação a ela.</summary>
    public sealed class RascunhoEm { public string Id, Descricao, Data; public long Num; public override string ToString() => "EM-" + Num.ToString("000") + " · " + Descricao; }

    /// <summary>Opções da emissão escolhidas na janela.</summary>
    public sealed class PedidoEm
    {
        public bool Registrar;
        public RascunhoEm Rascunho;              // null = nova
        public string Descricao = "";
        public List<string> Disciplinas = new List<string>();
        public List<DestEm> Destinatarios = new List<DestEm>();
    }

    /// <summary>
    /// Integração do Exportar pranchas com Projeto › Emissões do Sistema FJA (docs/emissoes.md): lê do banco os dados do
    /// projeto do modelo (disciplinas, envolvidos, emissões em Rascunho) e grava a emissão em Rascunho com a pasta da
    /// exportação e os arquivos. O Emitir (pasta compartilhada, SUBSTITUÍDOS, RD) continua no sistema.
    /// </summary>
    public sealed class DadosEmissao
    {
        public string Pid, NomeProjeto, Erro;
        public Dictionary<string, object> Projeto;
        public List<DiscEm> Disciplinas = new List<DiscEm>();
        public List<DestEm> Envolvidos = new List<DestEm>();
        public List<RascunhoEm> Rascunhos = new List<RascunhoEm>();
        public bool Ok => Erro == null;

        public static DadosEmissao Carregar(Document doc)
        {
            var d = new DadosEmissao();
            try
            {
                if (!Nuvem.TemLogin) { d.Erro = "sem login no sistema neste computador"; return d; }
                // projeto do modelo, como nas demais ferramentas (ligação gravada ou pistas)
                var bib = Nuvem.Publicado("biblioteca");
                var lista = (bib?.Lista("projetos") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                if (lista.Count == 0) lista = (bib?.Lista("coresProjetos") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                var lig = ProjetoDoModelo.Ler(doc);
                var proj = ProjetoDoModelo.Resolver(doc, lista, out string origem, out _, out _);
                d.Pid = proj?.Txt("id") ?? lig?.Id;
                if (string.IsNullOrEmpty(d.Pid)) { d.Erro = "projeto do modelo não reconhecido (" + origem + ")"; return d; }

                d.Projeto = Nuvem.Documento("itens", d.Pid);
                if (d.Projeto == null) { d.Erro = "projeto não encontrado no sistema"; return d; }
                string cod = d.Projeto.Obj("campos").Txt("codigo");
                d.NomeProjeto = ((cod ?? "") + " " + (d.Projeto.Txt("nome") ?? "")).Trim();

                // catálogo de disciplinas (itens com secao = disciplinas) e disciplinas do projeto
                var cat = new Dictionary<string, Dictionary<string, object>>();
                foreach (var i in Nuvem.Consulta("itens", "secao", "disciplinas"))
                {
                    string c = (i.Obj("campos").Txt("codigo") ?? "").Trim().ToUpperInvariant();
                    if (c != "") cat[c] = i;
                }
                var linhas = d.Projeto.Lista("disciplinas").OfType<Dictionary<string, object>>().ToList();
                foreach (var l in linhas)
                {
                    string c = (l.Txt("cod") ?? "").ToUpperInvariant();
                    if (c == "") continue;
                    d.Disciplinas.Add(new DiscEm { Cod = c, Sigla = string.IsNullOrEmpty(l.Txt("sigla")) ? c : l.Txt("sigla"), Nome = cat.TryGetValue(c, out var ci) ? ci.Txt("nome") ?? c : c, DoProjeto = true });
                }
                foreach (var kv in cat.Where(kv => !d.Disciplinas.Any(x => x.Cod == kv.Key) && kv.Value.Txt("status") != "Inativa").OrderBy(kv => kv.Key))
                    d.Disciplinas.Add(new DiscEm { Cod = kv.Key, Sigla = kv.Key, Nome = kv.Value.Txt("nome") ?? kv.Key });
                d.catalogo = cat;

                // envolvidos: parceiros das disciplinas do projeto, equipe interna, responsáveis de pendências e
                // cadastrados só no projeto — a mesma composição de envolvidos(pid)/envolvidosEm(pid) do sistema
                var parcs = Nuvem.Consulta("itens", "secao", "parceiros");
                var porId = parcs.ToDictionary(p => p.Txt("id"), p => p);
                var nomes = new Dictionary<string, DestEm>(StringComparer.OrdinalIgnoreCase);
                void Add(string n, string emp = null, string em = null)
                {
                    n = (n ?? "").Trim();
                    if (n == "") return;
                    nomes[n] = new DestEm { Nome = n, Empresa = emp ?? "", Email = em ?? "" };
                }
                foreach (var l in linhas)
                    if (!string.IsNullOrEmpty(l.Txt("parceiro")) && porId.TryGetValue(l.Txt("parceiro"), out var pc)) Add(pc.Txt("nome"));
                foreach (var p in parcs.Where(p => p.Obj("campos").Txt("papel") == "Interno (FJA)"))
                {
                    Add(p.Txt("nome"));
                    foreach (var x in (p.Obj("campos").Txt("equipe") ?? "").Split('\n').Select(s => s.Trim()).Where(s => s != ""))
                        Add(p.Txt("nome") + " (" + x + ")");
                }
                foreach (var e in Nuvem.Consulta("pend", "projeto", d.Pid)) Add(e.Txt("responsavel"));
                foreach (var e in d.Projeto.Lista("envolvidosProj").OfType<Dictionary<string, object>>()) Add(e.Txt("nome"), e.Txt("empresa"), e.Txt("email"));
                d.Envolvidos = nomes.Values.OrderBy(x => x.Nome, StringComparer.Create(new CultureInfo("pt-BR"), true)).ToList();

                // emissões do projeto: as em Rascunho para escolher; todas para o próximo número
                d.emissoes = Nuvem.Consulta("emis", "projeto", d.Pid);
                foreach (var e in d.emissoes.Where(e => (e.Txt("status") ?? "Rascunho") == "Rascunho"))
                    d.Rascunhos.Add(new RascunhoEm { Id = e.Txt("id"), Descricao = e.Txt("descricao") ?? "", Data = e.Txt("data") ?? "", Num = Num(e) });
                d.Rascunhos = d.Rascunhos.OrderByDescending(r => r.Num).ToList();
            }
            catch (Exception e) { d.Erro = "não consegui ler o sistema (" + e.Message + ")"; }
            return d;
        }

        Dictionary<string, Dictionary<string, object>> catalogo = new Dictionary<string, Dictionary<string, object>>();
        List<Dictionary<string, object>> emissoes = new List<Dictionary<string, object>>();

        static long Num(Dictionary<string, object> e) => long.TryParse(e.Txt("num"), NumberStyles.Any, CultureInfo.InvariantCulture, out long n) ? n
            : (double.TryParse(e.Txt("num"), NumberStyles.Any, CultureInfo.InvariantCulture, out double x) ? (long)x : 0);

        static string Nrm(string s) => System.Text.RegularExpressions.Regex.Replace(Cotagem.Regras.SemAcento(s ?? "").ToUpperInvariant(), "[^A-Z0-9]+", " ").Trim();

        /// <summary>Código da disciplina a partir do valor da folha (ex.: "ARQ"): sigla do projeto, código ou sinônimo do catálogo.</summary>
        public string Resolver(string valor)
        {
            string t = Nrm(valor);
            if (t == "") return null;
            var l = Disciplinas.FirstOrDefault(x => Nrm(x.Sigla) == t) ?? Disciplinas.FirstOrDefault(x => Nrm(x.Cod) == t);
            if (l != null) return l.Cod;
            foreach (var kv in catalogo)
                foreach (var s in (kv.Value.Obj("campos").Txt("sinonimos") ?? "").Split(',', ';', '\n'))
                    if (Nrm(s) == t || Nrm(kv.Value.Txt("nome")) == t) return kv.Key;
            return null;
        }

        /// <summary>
        /// Grava a emissão: nova (EM-NNN seguinte, com trava contra número repetido) ou acrescentando ao Rascunho escolhido.
        /// Devolve o texto para o relatório.
        /// </summary>
        public string Gravar(PedidoEm p, string pasta, List<string> arquivos)
        {
            long agora = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            string uid = Nuvem.Uid();
            var arqs = arquivos.Where(File.Exists).Select(Arquivo).ToList();
            var dest = p.Destinatarios.Select(x => (object)new Dictionary<string, object> { ["nome"] = x.Nome, ["empresa"] = x.Empresa ?? "", ["email"] = x.Email ?? "" }).ToList();
            var pastaObj = new Dictionary<string, object> { ["caminho"] = pasta, ["nome"] = Path.GetFileName(pasta.TrimEnd('\\')) };

            if (p.Rascunho != null)
            {
                var e = Nuvem.Documento("emis", p.Rascunho.Id) ?? throw new InvalidOperationException("a emissão " + p.Rascunho + " não existe mais");
                // arquivos: os exportados substituem os de mesmo nome
                var lista = e.Lista("arquivos").OfType<Dictionary<string, object>>().ToList();
                foreach (var a in arqs)
                {
                    lista.RemoveAll(x => string.Equals(x.Txt("nome"), a.Txt("nome"), StringComparison.OrdinalIgnoreCase));
                    lista.Add(a);
                }
                var ds = e.Lista("destinatarios").OfType<Dictionary<string, object>>().ToList();
                foreach (Dictionary<string, object> x in dest)
                    if (!ds.Any(y => string.Equals(y.Txt("nome"), x.Txt("nome"), StringComparison.OrdinalIgnoreCase))) ds.Add(x);
                var discs = e.Lista("disciplinas").Select(x => Convert.ToString(x)).Union(p.Disciplinas).Distinct().ToList();
                var upd = new Dictionary<string, object>
                {
                    ["arquivos"] = lista.Cast<object>().ToList(), ["destinatarios"] = ds.Cast<object>().ToList(),
                    ["disciplinas"] = discs, ["discAuto"] = false,
                    ["origem"] = "revit", ["atualizadoEm"] = agora,
                };
                if (!string.IsNullOrEmpty(p.Descricao)) upd["descricao"] = p.Descricao;
                if (uid != "") upd["atualizadoPor"] = uid;
                string aviso = "";
                var pastaAtual = e.Obj("pasta");
                if (pastaAtual.Count == 0 || string.IsNullOrEmpty(pastaAtual.Txt("caminho")) && string.IsNullOrEmpty(pastaAtual.Txt("id"))) upd["pasta"] = pastaObj;
                else if (!string.Equals(pastaAtual.Txt("caminho"), pasta, StringComparison.OrdinalIgnoreCase))
                    aviso = "\nAtenção: a emissão já tinha a pasta " + (pastaAtual.Txt("caminho") ?? pastaAtual.Txt("nome")) + "; os arquivos desta exportação estão em " + pasta + ".";
                Nuvem.Atualizar("emis", p.Rascunho.Id, upd);
                return "Acrescentado à emissão " + p.Rascunho + " (Rascunho) no sistema: " + arqs.Count + " arquivo(s)." + aviso;
            }

            long n = emissoes.Select(Num).DefaultIfEmpty(0).Max() + 1;
            var body = new Dictionary<string, object>
            {
                ["projeto"] = Pid, ["num"] = n, ["data"] = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                ["descricao"] = p.Descricao, ["status"] = "Rascunho", ["destinatarios"] = dest, ["obs"] = "",
                ["arquivos"] = arqs.Cast<object>().ToList(), ["disciplinas"] = p.Disciplinas, ["discAuto"] = false,
                ["pasta"] = pastaObj, ["origem"] = "revit",
                ["criadoEm"] = agora, ["atualizadoEm"] = agora,
            };
            var comp = Projeto.Obj("emisCompart");
            if (comp.Count > 0) { var c = new Dictionary<string, object>(comp) { ["usar"] = true }; body["compart"] = c; }
            if (uid != "") { body["criadoPor"] = uid; body["atualizadoPor"] = uid; }
            for (int t = 0; t < 20; t++, n++)
            {
                body["num"] = n;
                string id = Pid + "__em" + n.ToString("000");
                if (Nuvem.Criar("emis", id, body)) return "Emissão EM-" + n.ToString("000") + " criada no sistema (Rascunho): " + arqs.Count + " arquivo(s). Complete e emita em Projeto › Emissões.";
            }
            throw new InvalidOperationException("não achei número livre para a emissão");
        }

        /// <summary>Arquivo como o sistema guarda (partesArqEm): nome, base, rev (R00), ext (PDF), tamanho, mime; driveId vazio
        /// (o sistema completa pelo nome, na pasta da emissão).</summary>
        static Dictionary<string, object> Arquivo(string caminho)
        {
            string nome = Path.GetFileName(caminho);
            string ext = Path.GetExtension(nome).TrimStart('.').ToUpperInvariant();
            string sem = Path.GetFileNameWithoutExtension(nome);
            string bas = sem, rev = "";
            var m = System.Text.RegularExpressions.Regex.Match(sem, @"^(.*?)[_\-\s]R(\d{1,3})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) { bas = m.Groups[1].Value; rev = "R" + m.Groups[2].Value.PadLeft(2, '0'); }
            return new Dictionary<string, object>
            {
                ["nome"] = nome, ["base"] = bas, ["rev"] = rev, ["ext"] = ext,
                ["tamanho"] = new FileInfo(caminho).Length,
                ["mime"] = ext == "PDF" ? "application/pdf" : ext == "DWG" ? "image/vnd.dwg" : "application/octet-stream",
                ["driveId"] = "",
            };
        }
    }
}
