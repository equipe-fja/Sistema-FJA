using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Pranchas
{
    /// <summary>Uma prancha na janela: a folha, o nome do arquivo montado e os parâmetros da regra que estão em branco.</summary>
    public sealed class LinhaPrancha
    {
        public ViewSheet Folha;
        public string Numero, NomeFolha, Disciplina, Arquivo;
        /// <summary>Parâmetros da regra em branco nesta folha (saem do nome; aviso antes de exportar).</summary>
        public List<string> Faltam = new List<string>();
        public bool Incluir;
        public bool Valida => !string.IsNullOrEmpty(Arquivo);
    }

    /// <summary>Uma parte do nome: um parâmetro (em branco na folha = sai do nome) ou um texto fixo (2.39.0).</summary>
    public sealed class ParteNome
    {
        public string Param;
        /// <summary>Texto fixo digitado na regra; null = a parte é um parâmetro.</summary>
        public string Texto;
        public bool Fixo => Texto != null;
        /// <summary>Separador que vem depois desta parte (2.40.0); null = o separador geral da regra.</summary>
        public string Sep;
        public ParteNome Copia() => new ParteNome { Param = Param, Texto = Texto, Sep = Sep };
    }

    /// <summary>
    /// Regra do nome do arquivo = a composição do campo "Prancha" do carimbo. Padrão FJA: Project Number - Etapa (sigla)
    /// - Disciplina (abreviação) - Tipo de documento - Trecho - Sheet Number - Current Revision (parâmetro em branco sai:
    /// 131-LE-ARQ-DT-HUB-H002-R00; sem Trecho, 128-AP-ARQ-DT-A002-R00). Cada projeto pode ter a sua, gravada no
    /// modelo (DataStorage FJA_ExportarPranchas).
    /// </summary>
    public sealed class RegraNome
    {
        public List<ParteNome> Partes = new List<ParteNome>();
        public string Sep = "-";

        static readonly Guid GUID = new Guid("6f1d2b7e-3c84-4a51-9e0b-5d7c2a1f8e43");
        const string CAMPO = "regra";

        public static RegraNome Padrao()
        {
            var r = new RegraNome();
            foreach (var p in new[] { NomeArquivo.ProjectNumber, NomeArquivo.Etapa, NomeArquivo.Disciplina, NomeArquivo.Tipo })
                r.Partes.Add(new ParteNome { Param = p });
            r.Partes.Add(new ParteNome { Param = NomeArquivo.Trecho });
            r.Partes.Add(new ParteNome { Param = NomeArquivo.SheetNumber });
            r.Partes.Add(new ParteNome { Param = NomeArquivo.CurrentRevision });
            return r;
        }

        public RegraNome Copia() => new RegraNome { Sep = Sep, Partes = Partes.Select(p => p.Copia()).ToList() };

        public string ParaJson()
        {
            return Json.Gravar(new Dictionary<string, object>
            {
                ["sep"] = Sep,
                // texto fixo vai em "p" com o prefixo: a 2.38 (que só lê "p") mantém a parte ao gravar a regra,
                // em vez de apagá-la; lá ela só aparece como parâmetro inexistente (fica fora do nome, com aviso)
                // "s": separador depois da parte (2.40.0; a 2.39 ignora e usa o geral)
                ["partes"] = Partes.Select(p =>
                {
                    var d = new Dictionary<string, object> { ["p"] = p.Fixo ? PREFIXO_TEXTO + p.Texto : p.Param };
                    if (p.Sep != null) d["s"] = p.Sep;
                    return (object)d;
                }).ToList(),
            });
        }

        /// <summary>Marca, em "p", de uma parte de texto fixo (2.39.0).</summary>
        public const string PREFIXO_TEXTO = "#texto:";

        public static RegraNome DeJson(string s)
        {
            var d = Json.LerObjeto(s);
            var r = new RegraNome { Sep = d.Txt("sep") ?? "-" };
            foreach (var x in d.Lista("partes") ?? new List<object>())
            {
                if (!(x is Dictionary<string, object> pd)) continue;
                string pp = pd.Txt("p");
                ParteNome parte = null;
                if (pd.ContainsKey("t")) parte = new ParteNome { Texto = pd.Txt("t") ?? "" };   // formato provisório da 2.39.0
                else if (pp != null && pp.StartsWith(PREFIXO_TEXTO, StringComparison.Ordinal)) parte = new ParteNome { Texto = pp.Substring(PREFIXO_TEXTO.Length) };
                else if (!string.IsNullOrWhiteSpace(pp)) parte = new ParteNome { Param = pp };
                if (parte == null) continue;
                if (pd.ContainsKey("s")) parte.Sep = pd.Txt("s") ?? "";
                r.Partes.Add(parte);
            }
            return r.Partes.Count > 0 ? r : null;
        }

        /// <summary>Separador depois de uma parte (o dela ou, sem um, o geral).</summary>
        public string SepDe(ParteNome p) => p.Sep ?? Sep ?? "";

        public bool Igual(RegraNome o) => o != null && o.ParaJson() == ParaJson();

        static DataStorage Armazem(Document doc)
        {
            var sc = Schema.Lookup(GUID);
            if (sc == null) return null;
            foreach (DataStorage ds in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)))
                try { var e = ds.GetEntity(sc); if (e != null && e.IsValid()) return ds; } catch { }
            return null;
        }

        /// <summary>Regra gravada no modelo; null se o projeto ainda não tem uma (usa o padrão).</summary>
        public static RegraNome DoModelo(Document doc)
        {
            try
            {
                var ds = Armazem(doc);
                return ds == null ? null : DeJson(ds.GetEntity(Schema.Lookup(GUID)).Get<string>(CAMPO) ?? "");
            }
            catch { return null; }
        }

        /// <summary>Grava a regra no modelo (numa transação própria).</summary>
        public void Gravar(Document doc)
        {
            using (var t = new Transaction(doc, "Regra do nome das pranchas (FJA)"))
            {
                t.Start();
                var sc = Schema.Lookup(GUID);
                if (sc == null)
                {
                    var sb = new SchemaBuilder(GUID);
                    sb.SetSchemaName("FJA_ExportarPranchas");
                    sb.SetReadAccessLevel(AccessLevel.Public);
                    sb.SetWriteAccessLevel(AccessLevel.Public);
                    sb.SetDocumentation("Regra do nome dos arquivos (Ferramentas FJA > Pranchas > Exportar)");
                    sb.AddSimpleField(CAMPO, typeof(string));
                    sc = sb.Finish();
                }
                var ds = Armazem(doc) ?? DataStorage.Create(doc);
                var e = new Entity(sc);
                e.Set(CAMPO, ParaJson());
                ds.SetEntity(e);
                t.Commit();
            }
        }
    }

    /// <summary>
    /// Monta o nome do arquivo de uma folha pela regra. Project Number, Sheet Number e Current Revision vêm dos
    /// parâmetros internos do Revit; os demais, pelo nome, na folha e, se não estiverem nela, nas informações do projeto.
    /// </summary>
    public static class NomeArquivo
    {
        public const string ProjectNumber = "Project Number";
        public const string SheetNumber = "Sheet Number";
        public const string CurrentRevision = "Current Revision";
        public const string Etapa = "Etapa (sigla)";
        public const string Disciplina = "Disciplina (abreviação)";
        public const string Tipo = "Tipo de documento";
        public const string Trecho = "Trecho";

        public static LinhaPrancha Montar(Document doc, ViewSheet f, RegraNome regra)
        {
            var l = new LinhaPrancha { Folha = f, Numero = f.SheetNumber, NomeFolha = f.Name };
            l.Disciplina = Valor(doc, f, Disciplina) ?? "";
            Aplicar(doc, l, regra);
            return l;
        }

        /// <summary>(Re)calcula o nome e o que falta de uma linha pela regra.</summary>
        public static void Aplicar(Document doc, LinhaPrancha l, RegraNome regra)
        {
            l.Faltam.Clear();
            // cada parte que entra traz o separador que vem depois dela; parte que fica de fora leva junto o seu
            var sb = new StringBuilder();
            string sepPendente = null;
            foreach (var p in regra.Partes)
            {
                string v;
                // texto fixo: entra como digitado (vazio = ignorado, sem aviso)
                if (p.Fixo) v = p.Texto;
                else
                {
                    v = Valor(doc, l.Folha, p.Param);
                    // todos os parâmetros são opcionais (decisão da FJA, 23/11/2026): em branco sai do nome e é avisado ao exportar
                    if (string.IsNullOrWhiteSpace(v)) l.Faltam.Add(p.Param);
                }
                if (string.IsNullOrWhiteSpace(v)) continue;
                if (sepPendente != null) sb.Append(sepPendente);
                sb.Append(v.Trim());
                sepPendente = regra.SepDe(p);
            }
            l.Arquivo = Limpar(sb.ToString());
        }

        public static string Valor(Document doc, ViewSheet f, string nome)
        {
            var pi = doc.ProjectInformation;
            switch (Chave(nome))
            {
                case "PROJECT NUMBER": case "NUMERO DO PROJETO":
                    return Txt(pi?.get_Parameter(BuiltInParameter.PROJECT_NUMBER));
                case "SHEET NUMBER": case "NUMERO DA FOLHA":
                    return f.SheetNumber;
                case "CURRENT REVISION": case "REVISAO ATUAL":
                    return Txt(f.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION));
            }
            return Txt(f.LookupParameter(nome)) ?? Txt(pi?.LookupParameter(nome));
        }

        static string Chave(string s) => Cotagem.Regras.SemAcento(s ?? "").Trim().ToUpperInvariant();

        /// <summary>Parâmetros que podem entrar no nome: os três internos e os de texto/número criados no projeto
        /// (compartilhados ou de projeto) nas folhas e nas informações do projeto.</summary>
        public static List<string> Disponiveis(Document doc, IEnumerable<ViewSheet> folhas)
        {
            var r = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            void De(Element e)
            {
                if (e == null) return;
                foreach (Parameter p in e.Parameters)
                {
                    try
                    {
                        if (p.IsShared || (p.Definition is InternalDefinition idf && idf.BuiltInParameter == BuiltInParameter.INVALID))
                            if (p.StorageType == StorageType.String || p.StorageType == StorageType.Integer || p.StorageType == StorageType.Double)
                                r.Add(p.Definition.Name);
                    }
                    catch { }
                }
            }
            foreach (var f in folhas.Take(30)) De(f);
            De(doc.ProjectInformation);
            var l = new List<string> { ProjectNumber, SheetNumber, CurrentRevision };
            l.AddRange(r.Where(n => !l.Contains(n)));
            return l;
        }

        static string Txt(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            string s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        /// <summary>Nome da pasta de uma emissão: data de hoje (AA-MM-DD) + espaço + descrição, ex.:
        /// "26-02-02 EMISSÃO FINAL EXECUTIVO - ARQUITETURA - 1ª PARTE".</summary>
        public static string PastaEmissao(string descricao)
        {
            // padrão do sistema (pastaEm): "AA-MM-DD EMISSÃO - Descrição"; se a descrição já começa com "EMISSÃO", não repete
            string d = (descricao ?? "").Trim();
            bool jaTem = Cotagem.Regras.SemAcento(d).ToUpperInvariant().StartsWith("EMISSAO");
            return Limpar(DateTime.Now.ToString("yy-MM-dd", CultureInfo.InvariantCulture) + (jaTem ? " " : " EMISSÃO - ") + d);
        }

        /// <summary>Tira os caracteres que o Windows não aceita em nome de arquivo.</summary>
        public static string Limpar(string s)
        {
            var inv = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(inv.Contains(c) ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.');
        }
    }

    public enum CorPdf { Colorido, Cinza, Preto }

    /// <summary>Preferências da pessoa (%APPDATA%\FJA\exportar-pranchas.json); a pasta é lembrada por projeto.</summary>
    public sealed class OpcoesExportar
    {
        public bool Pdf = true, Dwg = true, Subpastas, Substituir = true, AbrirPasta = true, NovaPasta;
        public CorPdf Cor = CorPdf.Colorido;
        public string ConfigDwg = Exportacao.ConfigFJA;
        public Dictionary<string, object> Pastas = new Dictionary<string, object>();
        public Dictionary<string, object> Descricoes = new Dictionary<string, object>();   // última descrição da pasta nova, por projeto

        static string Arquivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "exportar-pranchas.json");

        public static OpcoesExportar Carregar()
        {
            var o = new OpcoesExportar();
            try
            {
                if (!File.Exists(Arquivo)) return o;
                var d = Json.LerObjeto(File.ReadAllText(Arquivo, Encoding.UTF8));
                bool B(string k, bool padrao) { var t = d.Txt(k); return t == null ? padrao : t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1"; }
                o.Pdf = B("pdf", true); o.Dwg = B("dwg", true); o.Subpastas = B("subpastas", false);
                o.Substituir = B("substituir", true); o.AbrirPasta = B("abrirPasta", true); o.NovaPasta = B("novaPasta", false);
                o.Descricoes = d.Obj("descricoes") ?? new Dictionary<string, object>();
                if (Enum.TryParse(d.Txt("cor") ?? "", true, out CorPdf c)) o.Cor = c;
                o.ConfigDwg = d.Txt("configDwg") ?? Exportacao.ConfigFJA;
                o.Pastas = d.Obj("pastas") ?? new Dictionary<string, object>();
            }
            catch { }
            return o;
        }

        public void Gravar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                File.WriteAllText(Arquivo, Json.Gravar(new Dictionary<string, object>
                {
                    ["pdf"] = Pdf, ["dwg"] = Dwg, ["subpastas"] = Subpastas, ["substituir"] = Substituir, ["abrirPasta"] = AbrirPasta,
                    ["cor"] = Cor.ToString(), ["configDwg"] = ConfigDwg, ["pastas"] = Pastas,
                    ["novaPasta"] = NovaPasta, ["descricoes"] = Descricoes,
                }), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>Chave do projeto para lembrar a pasta: o caminho do modelo (central, se houver), senão o título.</summary>
        public static string Chave(Document doc)
        {
            try
            {
                if (doc.IsWorkshared)
                {
                    var mp = doc.GetWorksharingCentralModelPath();
                    if (mp != null) return ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                }
            }
            catch { }
            return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
        }
    }

    /// <summary>Resultado da exportação (relatório).</summary>
    public sealed class ResultadoExportar
    {
        public int Pdfs, Dwgs, Pulados;
        public List<string> Erros = new List<string>();
        public string AvisoConfig, AvisoRegra, Emissao;
        /// <summary>Arquivos gerados (caminho completo), para a emissão no sistema.</summary>
        public List<string> Arquivos = new List<string>();
        public bool Cancelado;
    }

    /// <summary>Exportação folha a folha: um PDF e/ou um DWG por prancha, com o nome do campo "Prancha".</summary>
    public static class Exportacao
    {
        /// <summary>Configuração de exportação DWG da FJA: as opções padrão do Revit, com True Color e sem referências externas.</summary>
        public const string ConfigFJA = "FJA TRUE COLOR";

        public static List<string> ConfiguracoesDwg(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(ExportDWGSettings)).Select(e => e.Name).OrderBy(n => n).ToList();
        }

        /// <summary>Opções DWG pelo nome da configuração; a "FJA TRUE COLOR" é criada no projeto se não existir.</summary>
        public static DWGExportOptions OpcoesDwg(Document doc, string nome, ResultadoExportar res)
        {
            var cfg = new FilteredElementCollector(doc).OfClass(typeof(ExportDWGSettings)).Cast<ExportDWGSettings>()
                .FirstOrDefault(s => s.Name == nome);
            if (cfg != null) return cfg.GetDWGExportOptions();
            if (nome != ConfigFJA)
            {
                res.AvisoConfig = "Configuração DWG \"" + nome + "\" não encontrada; usada a " + ConfigFJA + ".";
                return OpcoesDwg(doc, ConfigFJA, res);
            }
            var o = OpcoesFJA();
            try
            {
                using (var t = new Transaction(doc, "Criar configuração DWG " + ConfigFJA))
                {
                    t.Start();
                    ExportDWGSettings.Create(doc, ConfigFJA, o);
                    t.Commit();
                }
                res.AvisoConfig = "Configuração DWG \"" + ConfigFJA + "\" criada no projeto.";
            }
            catch (Exception e)
            {
                res.AvisoConfig = "Não foi possível gravar a configuração \"" + ConfigFJA + "\" no projeto (" + e.Message + "); exportado com as mesmas opções.";
            }
            return o;
        }

        static DWGExportOptions OpcoesFJA()
        {
            var o = new DWGExportOptions();
            o.Colors = ExportColorMode.TrueColorPerView;   // "True Color (valores RGB)"
            o.MergedViews = true;                          // sem referências externas: tudo num DWG por prancha
            return o;
        }

        public static PDFExportOptions OpcoesPdf(CorPdf cor)
        {
            var o = new PDFExportOptions
            {
                Combine = true,                            // uma folha por chamada: o FileName vale
                PaperFormat = ExportPaperFormat.Default,   // tamanho do papel = o da folha
                ZoomType = ZoomType.Zoom,
                ZoomPercentage = 100,
                HideCropBoundaries = true,
                HideScopeBoxes = true,
                HideReferencePlane = true,
                HideUnreferencedViewTags = true,
            };
            o.ColorDepth = cor == CorPdf.Preto ? ColorDepthType.BlackLine : cor == CorPdf.Cinza ? ColorDepthType.GrayScale : ColorDepthType.Color;
            return o;
        }

        /// <summary>Exporta uma prancha (PDF e/ou DWG); contagens e erros vão para o resultado.</summary>
        public static void Uma(Document doc, LinhaPrancha l, string pasta, OpcoesExportar op, PDFExportOptions pdf, DWGExportOptions dwg, ResultadoExportar res)
        {
            var ids = new List<ElementId> { l.Folha.Id };
            if (op.Pdf && pdf != null)
            {
                string dir = op.Subpastas ? Path.Combine(pasta, "PDF") : pasta;
                string arq = Path.Combine(dir, l.Arquivo + ".pdf");
                if (Preparar(dir, arq, op, l, "PDF", res))
                {
                    try
                    {
                        pdf.FileName = l.Arquivo;
                        if (doc.Export(dir, ids, pdf) && File.Exists(arq)) { res.Pdfs++; res.Arquivos.Add(arq); }
                        else res.Erros.Add(l.Arquivo + ".pdf: o Revit não gerou o arquivo");
                    }
                    catch (Exception e) { res.Erros.Add(l.Arquivo + ".pdf: " + e.Message); }
                }
            }
            if (op.Dwg && dwg != null)
            {
                string dir = op.Subpastas ? Path.Combine(pasta, "DWG") : pasta;
                string arq = Path.Combine(dir, l.Arquivo + ".dwg");
                if (Preparar(dir, arq, op, l, "DWG", res))
                {
                    try
                    {
                        if (doc.Export(dir, l.Arquivo, ids, dwg) && File.Exists(arq)) { res.Dwgs++; res.Arquivos.Add(arq); }
                        else res.Erros.Add(l.Arquivo + ".dwg: o Revit não gerou o arquivo");
                    }
                    catch (Exception e) { res.Erros.Add(l.Arquivo + ".dwg: " + e.Message); }
                }
            }
        }

        /// <summary>Cria a pasta e trata o arquivo que já existe (substituir ou pular). Falso = não exportar.</summary>
        static bool Preparar(string dir, string arq, OpcoesExportar op, LinhaPrancha l, string tipo, ResultadoExportar res)
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception e) { res.Erros.Add(tipo + " " + l.Arquivo + ": pasta não criada (" + e.Message + ")"); return false; }
            if (!File.Exists(arq)) return true;
            if (!op.Substituir) { res.Pulados++; return false; }
            try { File.Delete(arq); return true; }
            catch (Exception e) { res.Erros.Add(Path.GetFileName(arq) + ": não foi possível substituir (aberto em outro programa?) — " + e.Message); return false; }
        }
    }
}
