using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Pranchas
{
    /// <summary>Uma prancha na janela: a folha, o nome do arquivo montado e o que faltou para montá-lo.</summary>
    public sealed class LinhaPrancha
    {
        public ViewSheet Folha;
        public string Numero, NomeFolha, Disciplina, Arquivo;
        public List<string> Faltam = new List<string>();
        public bool Incluir;
        public bool Valida => Faltam.Count == 0 && !string.IsNullOrEmpty(Arquivo);
    }

    /// <summary>
    /// Nome do arquivo = o campo "Prancha" do carimbo da FJA: Project Number - Etapa (sigla) - Disciplina (abreviação)
    /// - Tipo de documento - Sheet Number - Current Revision (ex.: 128-AP-ARQ-DT-A002-R00). Cada parâmetro é lido na
    /// folha e, se não estiver nela, nas informações do projeto.
    /// </summary>
    public static class NomeArquivo
    {
        public const string Etapa = "Etapa (sigla)";
        public const string Disciplina = "Disciplina (abreviação)";
        public const string Tipo = "Tipo de documento";

        public static LinhaPrancha Montar(Document doc, ViewSheet f)
        {
            var l = new LinhaPrancha { Folha = f, Numero = f.SheetNumber, NomeFolha = f.Name };
            var pi = doc.ProjectInformation;
            string proj = Txt(f.get_Parameter(BuiltInParameter.PROJECT_NUMBER)) ?? Txt(pi?.get_Parameter(BuiltInParameter.PROJECT_NUMBER));
            string etapa = Param(f, pi, Etapa), disc = Param(f, pi, Disciplina), tipo = Param(f, pi, Tipo);
            string rev = Txt(f.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION));
            l.Disciplina = disc ?? "";
            var partes = new List<string>();
            void Add(string v, string nome) { if (string.IsNullOrWhiteSpace(v)) l.Faltam.Add(nome); else partes.Add(v.Trim()); }
            Add(proj, "Project Number");
            Add(etapa, Etapa);
            Add(disc, Disciplina);
            Add(tipo, Tipo);
            Add(f.SheetNumber, "Sheet Number");
            Add(rev, "Current Revision");
            l.Arquivo = Limpar(string.Join("-", partes));
            return l;
        }

        static string Param(Element folha, Element pi, string nome)
        {
            return Txt(folha.LookupParameter(nome)) ?? Txt(pi?.LookupParameter(nome));
        }

        static string Txt(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            string s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
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
        public bool Pdf = true, Dwg = true, Subpastas, Substituir = true, AbrirPasta = true;
        public CorPdf Cor = CorPdf.Colorido;
        public string ConfigDwg = Exportacao.ConfigFJA;
        public Dictionary<string, object> Pastas = new Dictionary<string, object>();

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
                o.Substituir = B("substituir", true); o.AbrirPasta = B("abrirPasta", true);
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
        public string AvisoConfig;
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
                        if (doc.Export(dir, ids, pdf) && File.Exists(arq)) res.Pdfs++;
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
                        if (doc.Export(dir, l.Arquivo, ids, dwg) && File.Exists(arq)) res.Dwgs++;
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
