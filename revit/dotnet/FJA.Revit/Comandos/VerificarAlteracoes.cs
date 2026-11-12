using Autodesk.Revit.UI;
using FJA.Revit.Links;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>Links › Verificar alterações: compara as cópias com a origem no link e sincroniza (peças e redes).</summary>
    public class VerificarAlteracoes : ComandoFJA
    {
        public override Result Executar(UIApplication app, ref string mensagem)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument) { mensagem = "Abra um modelo de projeto antes."; return Result.Failed; }
            var j = new JanelaVerificar(uidoc);
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            j.ShowDialog();
            return Result.Succeeded;
        }
    }
}
