using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Legendas;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Legendas (ferramenta única): escolhe a disciplina (LOG, AUT, SON), vê as legendas já criadas e cria novas;
    /// para a legenda escolhida, a lista de itens (texto, ordem arrastando, escala, símbolos acrescentados),
    /// opções da tabela e cor, e a prévia desenhada pelo Revit, criada ou atualizada ali mesmo.
    /// Os ajustes ficam gravados em cada vista de legenda (os antigos botões Legenda LOG/AUT/SON/ELE/LUM, em Python, foram retirados em 2.13.0).
    /// </summary>
    public class FerramentaLegendas : ComandoFJA
    {
        protected virtual Disciplina Inicial => Disciplina.LOG;

        public override Result Executar(UIApplication app, ref string mensagem)
        {
            double inicio = Tempos.Agora;
            Tempos.Limpar();
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument) { mensagem = "Abra um projeto (não uma família)."; return Result.Failed; }

            double t = Tempos.Agora;
            var j = new JanelaPrevia(doc, Inicial, inicio);
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            Tempos.Marca("montar a janela", t);
            j.ShowDialog();
            if (j.Gravado && j.VistaGerada != null && j.VistaGerada.IsValidObject)
            {
                try { uidoc.ActiveView = j.VistaGerada; } catch { }
            }
            return j.Gravado ? Result.Succeeded : Result.Cancelled;
        }
    }

    /// <summary>Nome antigo do botão (Prévia LOG, 2.6.0 a 2.6.5): abre a ferramenta Legendas em LOG.</summary>
    public class PreviaLegendaLOG : FerramentaLegendas { }
}
