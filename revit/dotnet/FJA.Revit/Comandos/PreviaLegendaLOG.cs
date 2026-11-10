using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Legendas;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Legendas › Prévia LOG: a legenda de pontos de lógica numa janela com prévia do próprio Revit
    /// (escala por item, ordem por arrastar, símbolos acrescentados, opções da tabela e cor).
    /// Os ajustes ficam gravados na vista de legenda e valem também para o botão Legenda LOG (Python).
    /// </summary>
    public class PreviaLegendaLOG : ComandoFJA
    {
        protected virtual Disciplina Disc => Disciplina.LOG;

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            double inicio = Tempos.Agora;
            Tempos.Limpar();
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument) { mensagem = "Abra um projeto (não uma família)."; return Result.Failed; }

            var D = Disc;
            double t = Tempos.Agora;
            List<Item> itens;
            HashSet<string> desmarcados;
            try
            {
                itens = new Coleta(doc, D).Itens();
                desmarcados = new Montagem(doc, D).AplicarConfig(itens);
            }
            catch (Exception e)
            {
                mensagem = "Erro ao ler os itens: " + e.Message;
                return Result.Failed;
            }
            Tempos.Marca($"LER OS ITENS DO MODELO ({itens.Count})", t);
            if (itens.Count == 0)
            {
                TaskDialog.Show(D.Botao, "Nenhuma família '" + string.Join("' / '", D.Familias) + "' posicionada no modelo.");
                return Result.Cancelled;
            }
            t = Tempos.Agora;
            var j = new JanelaPrevia(doc, D, itens, desmarcados, inicio);
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            Tempos.Marca("montar a janela", t);
            j.ShowDialog();
            if (j.Gravado && j.VistaGerada != null)
            {
                try { uidoc.ActiveView = j.VistaGerada; } catch { }
            }
            return j.Gravado ? Result.Succeeded : Result.Cancelled;
        }
    }
}
