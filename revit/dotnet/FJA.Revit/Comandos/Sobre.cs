using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>
    /// Suíte › Sobre: versões da extensão e da DLL C#, e como a DLL foi carregada.
    /// Também serve de teste da base C#: se esta janela abre, a DLL certa foi encontrada,
    /// carregada sem travar o arquivo e conversou com o Revit.
    /// </summary>
    public class Sobre : ComandoFJA
    {
        public override Result Executar(UIApplication app, ref string mensagem)
        {
            var asm = typeof(Sobre).Assembly;
            int copias = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == asm.GetName().Name);
            bool memoria = string.IsNullOrEmpty(asm.Location);
            Document doc = app.ActiveUIDocument != null ? app.ActiveUIDocument.Document : null;

            var sb = new StringBuilder();
            sb.AppendLine("Biblioteca C#: FJA.Revit " + Entrada.Versao + " (" + Entrada.Geracao + ")");
            sb.AppendLine("Revit: " + app.Application.VersionName + " (" + app.Application.VersionBuild + ")");
            sb.AppendLine("Plataforma: " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine("Carregamento: " + (memoria
                ? "a partir da memória (o arquivo da DLL fica livre para atualizar)"
                : "direto do arquivo " + asm.Location));
            if (copias > 1)
                sb.AppendLine("Versões da DLL carregadas nesta sessão do Revit: " + copias + " (reiniciar o Revit libera a memória)");
            sb.AppendLine("Comandos C#: " + string.Join(", ", Entrada.Comandos()));
            if (doc != null)
                sb.AppendLine("Modelo ativo: " + doc.Title);

            var td = new TaskDialog("Ferramentas FJA")
            {
                MainInstruction = "Ferramentas FJA",
                MainContent = sb.ToString().TrimEnd(),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.Show();
            return Result.Succeeded;
        }
    }
}
