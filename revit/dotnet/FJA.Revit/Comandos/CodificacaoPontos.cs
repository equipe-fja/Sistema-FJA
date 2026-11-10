using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Legendas;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Legendas › Codificação: códigos dos pontos das disciplinas complementares (LOG.01, AUT.01, SON.01…)
    /// gravados no Type Mark dos tipos. Ver docs/codificacao-pontos.md.
    /// </summary>
    public class CodificacaoPontos : ComandoFJA
    {
        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) { mensagem = "Abra um modelo antes."; return Result.Failed; }
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument) { mensagem = "Abra um projeto (não uma família)."; return Result.Failed; }
            var j = new JanelaCodificacao(doc, Disciplina.LOG);
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            j.ShowDialog();
            return j.Aplicados > 0 ? Result.Succeeded : Result.Cancelled;
        }
    }
}
