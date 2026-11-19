using Autodesk.Revit.UI;
using FJA.Revit.Nucleo;

namespace FJA.Revit.Comandos
{
    /// <summary>Tipos › Componentes: abre o painel encaixável (Componentes/PainelComponentes.cs).</summary>
    public class ComponentesPainel : ComandoFJA
    {
        public override Result Executar(UIApplication app, ref string mensagem)
        {
            Componentes.Registro.Mostrar(app);
            return Result.Succeeded;
        }
    }
}
