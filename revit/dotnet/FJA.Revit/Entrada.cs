using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using FJA.Revit.Nucleo;

namespace FJA.Revit
{
    /// <summary>Resultado de um comando, no formato que o pyRevit lê com facilidade.</summary>
    public sealed class Resultado
    {
        /// <summary>"ok", "cancelado" ou "erro".</summary>
        public string Status { get; set; }
        public string Mensagem { get; set; }
        /// <summary>Detalhe técnico (pilha de chamadas) quando Status = "erro".</summary>
        public string Detalhe { get; set; }
    }

    /// <summary>
    /// Porta de entrada única chamada pelo pyRevit (lib/fja_dotnet.py):
    ///   Entrada.Executar("Sobre", __revit__)
    /// O nome é o da classe em FJA.Revit.Comandos (sem o namespace).
    /// </summary>
    public static class Entrada
    {
        public static Resultado Executar(string comando, UIApplication app)
        {
            Type tipo = typeof(Entrada).Assembly.GetType("FJA.Revit.Comandos." + comando, false, true);
            if (tipo == null || !typeof(ComandoFJA).IsAssignableFrom(tipo) || tipo.IsAbstract)
                return new Resultado { Status = "erro", Mensagem = "Comando não encontrado na DLL: " + comando };

            string msg = "";
            try
            {
                var cmd = (ComandoFJA)Activator.CreateInstance(tipo);
                Result r = cmd.Executar(app, ref msg);
                if (r == Result.Succeeded) return new Resultado { Status = "ok", Mensagem = msg };
                if (r == Result.Cancelled) return new Resultado { Status = "cancelado", Mensagem = msg };
                return new Resultado { Status = "erro", Mensagem = string.IsNullOrEmpty(msg) ? "O comando falhou." : msg };
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return new Resultado { Status = "cancelado", Mensagem = msg };
            }
            catch (Exception e)
            {
                var ex = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                return new Resultado { Status = "erro", Mensagem = ex.Message, Detalhe = ex.ToString() };
            }
        }

        /// <summary>Lista dos comandos disponíveis nesta DLL (para diagnóstico).</summary>
        public static string[] Comandos()
        {
            return typeof(Entrada).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(ComandoFJA).IsAssignableFrom(t) && t.Namespace == "FJA.Revit.Comandos")
                .Select(t => t.Name).OrderBy(n => n).ToArray();
        }

        /// <summary>Versão da DLL (a mesma da extensão) e geração do Revit para a qual foi compilada.</summary>
        public static string Versao
        {
            get
            {
                var a = typeof(Entrada).Assembly;
                var info = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return info != null ? info.InformationalVersion : a.GetName().Version.ToString();
            }
        }

        public static string Geracao
        {
            get
            {
#if REVIT2024
                return "R2024";
#else
                return "R2027";
#endif
            }
        }
    }
}
