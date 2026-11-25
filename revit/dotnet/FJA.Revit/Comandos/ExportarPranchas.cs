using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Nucleo;
using FJA.Revit.Pranchas;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Pranchas › Exportar: PDF e DWG das folhas, um arquivo por prancha, com o nome do campo "Prancha" do carimbo
    /// (ex.: 128-AP-ARQ-DT-A002-R00), na pasta escolhida. DWG pela configuração "FJA TRUE COLOR" (criada no projeto
    /// se não existir). Ver docs/exportar-pranchas.md.
    /// </summary>
    public class ExportarPranchas : ComandoFJA
    {
        const string Titulo = "Exportar pranchas";

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument) { mensagem = "Abra um projeto (não uma família)."; return Result.Failed; }

            var folhas = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(f => !f.IsPlaceholder && !f.IsTemplate).ToList();
            if (folhas.Count == 0) { TaskDialog.Show(Titulo, "O projeto não tem folhas."); return Result.Cancelled; }

            // marcadas de partida: as folhas selecionadas no navegador de projeto; sem seleção, a folha ativa
            var marcadas = new HashSet<ElementId>(uidoc.Selection.GetElementIds().Where(id => doc.GetElement(id) is ViewSheet));
            if (marcadas.Count == 0 && doc.ActiveView is ViewSheet ativa) marcadas.Add(ativa.Id);

            // regra do nome: a gravada no modelo, senão o padrão FJA (com Trecho opcional)
            var regraModelo = RegraNome.DoModelo(doc);
            var regra = (regraModelo ?? RegraNome.Padrao()).Copia();
            var linhas = folhas.Select(f => NomeArquivo.Montar(doc, f, regra))
                .OrderBy(l => l.Numero, Comparer<string>.Create(CompararNumero)).ToList();
            foreach (var l in linhas) l.Incluir = marcadas.Contains(l.Folha.Id);

            // conjuntos de folhas (sheet sets) do projeto: nome → índices das linhas
            var conjuntos = new List<KeyValuePair<string, HashSet<int>>>();
            var porId = new Dictionary<ElementId, int>();
            for (int i = 0; i < linhas.Count; i++) porId[linhas[i].Folha.Id] = i;
            foreach (var vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().OrderBy(s => s.Name))
            {
                var idx = new HashSet<int>();
                try { foreach (View v in vs.Views) if (v is ViewSheet && porId.TryGetValue(v.Id, out int k)) idx.Add(k); } catch { }
                if (idx.Count > 0) conjuntos.Add(new KeyValuePair<string, HashSet<int>>(vs.Name, idx));
            }

            var o = OpcoesExportar.Carregar();
            string chave = OpcoesExportar.Chave(doc);
            var j = new JanelaExportar(linhas, o, Exportacao.ConfiguracoesDwg(doc), chave, conjuntos,
                regra, NomeArquivo.Disponiveis(doc, folhas), r => { foreach (var l in linhas) NomeArquivo.Aplicar(doc, l, r); },
                () => DadosEmissao.Carregar(doc));
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            j.ShowDialog();

            // a regra mudou: grava no modelo (também ao fechar sem exportar), para o projeto seguir sempre a última
            // configuração e a equipe toda exportar com os mesmos nomes
            string avisoRegra = null;
            if (!regra.Igual(regraModelo ?? RegraNome.Padrao()))
            {
                try { regra.Gravar(doc); avisoRegra = "Regra do nome gravada no modelo."; }
                catch (Exception e) { avisoRegra = "A regra do nome não foi gravada no modelo (" + e.Message + "); vale só para esta exportação."; }
            }
            if (!j.Confirmado)
            {
                bool gravou = avisoRegra != null && avisoRegra.StartsWith("Regra");
                if (avisoRegra != null && !gravou) TaskDialog.Show(Titulo, avisoRegra.Replace("; vale só para esta exportação", ""));
                // Cancelled faria o Revit desfazer a regra recém-gravada
                return gravou ? Result.Succeeded : Result.Cancelled;
            }
            o.Gravar();

            var sel = linhas.Where(l => l.Incluir && l.Valida).ToList();
            var res = new ResultadoExportar { AvisoRegra = avisoRegra };
            var dwg = o.Dwg ? Exportacao.OpcoesDwg(doc, o.ConfigDwg, res) : null;
            var pdf = o.Pdf ? Exportacao.OpcoesPdf(o.Cor) : null;

            if (j.PastaNova != null)
            {
                try { System.IO.Directory.CreateDirectory(j.Pasta); }
                catch (Exception e) { TaskDialog.Show(Titulo, "Não foi possível criar a pasta:\n" + j.Pasta + "\n\n" + e.Message); return Result.Failed; }
            }

            var prog = new JanelaProgresso(sel.Count);
            new System.Windows.Interop.WindowInteropHelper(prog) { Owner = app.MainWindowHandle };
            prog.Show();
            try
            {
                for (int i = 0; i < sel.Count; i++)
                {
                    prog.Passo(i, (i + 1) + " de " + sel.Count + " · " + sel[i].Arquivo);
                    if (prog.Cancelado) { res.Cancelado = true; break; }
                    Exportacao.Uma(doc, sel[i], j.Pasta, o, pdf, dwg, res);
                }
                prog.Passo(sel.Count, "Concluído");
            }
            finally { prog.Close(); }

            // emissão no sistema (só com a opção marcada): Rascunho com a pasta desta exportação e os arquivos gerados
            if (j.Emissao != null && j.Emissao.Registrar && j.DadosEm != null && j.DadosEm.Ok)
            {
                if (res.Arquivos.Count == 0) res.Emissao = "Nenhum arquivo gerado: a emissão não foi registrada no sistema.";
                else
                {
                    try { res.Emissao = j.DadosEm.Gravar(j.Emissao, j.Pasta, res.Arquivos); }
                    catch (Exception e) { res.Emissao = "A emissão NÃO foi registrada no sistema: " + e.Message; }
                }
            }

            Relatorio(res, sel.Count, j.Pasta, o);
            return Result.Succeeded;
        }

        /// <summary>Ordena números de folha com partes numéricas (A2 antes de A10).</summary>
        static int CompararNumero(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            int i = 0, k = 0;
            while (i < a.Length && k < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[k]))
                {
                    int i0 = i, k0 = k;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (k < b.Length && char.IsDigit(b[k])) k++;
                    long na = long.Parse(a.Substring(i0, Math.Min(18, i - i0))), nb = long.Parse(b.Substring(k0, Math.Min(18, k - k0)));
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[k]));
                    if (c != 0) return c;
                    i++; k++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - k);
        }

        static void Relatorio(ResultadoExportar r, int total, string pasta, OpcoesExportar o)
        {
            var partes = new List<string>();
            if (o.Pdf) partes.Add(r.Pdfs + " PDF");
            if (o.Dwg) partes.Add(r.Dwgs + " DWG");
            var td = new TaskDialog(Titulo)
            {
                MainInstruction = (r.Cancelado ? "Exportação interrompida: " : "Exportação concluída: ") + string.Join(" e ", partes) + ".",
            };
            var txt = new List<string> { "Pasta: " + pasta };
            if (r.Pulados > 0) txt.Add(r.Pulados + " arquivo(s) já existiam e foram mantidos (opção Pular).");
            if (r.AvisoConfig != null) txt.Add(r.AvisoConfig);
            if (r.AvisoRegra != null) txt.Add(r.AvisoRegra);
            if (r.Emissao != null) txt.Add("\n" + r.Emissao);
            if (r.Erros.Count > 0) txt.Add("\nErros:\n• " + string.Join("\n• ", r.Erros.Take(20)) + (r.Erros.Count > 20 ? "\n… e mais " + (r.Erros.Count - 20) : ""));
            td.MainContent = string.Join("\n", txt);
            td.Show();
            if (o.AbrirPasta)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + pasta + "\"") { UseShellExecute = true }); } catch { }
        }
    }
}
