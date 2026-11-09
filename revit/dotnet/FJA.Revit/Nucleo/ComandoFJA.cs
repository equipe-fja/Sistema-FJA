using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FJA.Revit.Nucleo
{
    /// <summary>
    /// Base de todo comando C# da suíte.
    ///
    /// Hoje os comandos são chamados pelos botões do pyRevit (lib/fja_dotnet.py → Entrada.Executar),
    /// que carregam a DLL a partir da memória: o arquivo nunca fica travado e dá para recompilar e testar
    /// sem reiniciar o Revit.
    ///
    /// Cada comando também é um IExternalCommand normal. Se um dia a suíte sair do pyRevit, o .addin próprio
    /// aponta direto para estas classes, sem reescrever nada.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public abstract class ComandoFJA : IExternalCommand
    {
        /// <summary>Corpo do comando. Devolve Succeeded, Cancelled ou Failed; em Failed, preencher a mensagem.</summary>
        public abstract Result Executar(UIApplication app, ref string mensagem);

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                return Executar(data.Application, ref message);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception e)
            {
                message = e.Message;
                return Result.Failed;
            }
        }
    }
}
