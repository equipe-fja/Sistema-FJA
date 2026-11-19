using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace FJA.Revit.Componentes
{
    /// <summary>
    /// Registro do painel encaixável Componentes. O Revit só aceita registrar painéis na abertura: o startup.py da
    /// extensão chama Entrada.RegistrarPaineis, que registra uma "moldura" fixa (Border) guardada no AppDomain. O
    /// conteúdo (PainelComponentes) vem da DLL carregada no momento: quando a DLL muda (Suíte › Atualizar), o botão
    /// Componentes monta o conteúdo novo dentro da mesma moldura, sem reiniciar o Revit.
    /// </summary>
    public static class Registro
    {
        public static readonly Guid PaneGuid = new Guid("b8f0c2e4-6a1d-4f3b-9c57-2e8d1a4f6b90");
        public static DockablePaneId Id => new DockablePaneId(PaneGuid);
        const string MOLDURA = "FJA.Componentes.Moldura";
        public const string ATUAL = "FJA.Componentes.Atual";

        sealed class Provedor : IDockablePaneProvider
        {
            public FrameworkElement Elemento;
            public void SetupDockablePane(DockablePaneProviderData data)
            {
                data.FrameworkElement = Elemento;
                data.VisibleByDefault = false;
                data.InitialState = new DockablePaneState
                {
                    DockPosition = DockPosition.Tabbed,
                    TabBehind = DockablePanes.BuiltInDockablePanes.PropertiesPalette,   // em aba com Propriedades
                };
            }
        }

        /// <summary>Chamado na abertura do Revit (startup.py). Num Reload do pyRevit o painel já existe e nada muda.</summary>
        public static string Registrar(UIApplication app)
        {
            if (DockablePane.PaneIsRegistered(Id)) return "já registrado";
            var moldura = new Border
            {
                Child = new TextBlock { Text = "Clique em Ferramentas FJA › Tipos › Componentes para abrir o painel.", Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap },
            };
            AppDomain.CurrentDomain.SetData(MOLDURA, moldura);
            app.RegisterDockablePane(Id, "Componentes FJA", new Provedor { Elemento = moldura });
            app.Idling += PrimeiroOcioso;
            return "ok";
        }

        /// <summary>Monta o conteúdo quando o Revit fica pronto (o painel pode abrir sozinho, se estava aberto ao fechar).</summary>
        static void PrimeiroOcioso(object s, IdlingEventArgs e)
        {
            var app = s as UIApplication;
            if (app == null) return;
            app.Idling -= PrimeiroOcioso;
            try { if (AppDomain.CurrentDomain.GetData(ATUAL) == null) Montar(app); } catch { }
        }

        static void Montar(UIApplication app)
        {
            var moldura = AppDomain.CurrentDomain.GetData(MOLDURA) as Border
                ?? throw new InvalidOperationException("O painel Componentes é registrado na abertura do Revit. Feche e abra o Revit uma vez depois de atualizar a suíte.");
            moldura.Child = new PainelComponentes(app);
        }

        /// <summary>Botão Componentes: mostra o painel (montando o conteúdo com esta DLL, se ainda não for dela).</summary>
        public static void Mostrar(UIApplication app)
        {
            if (!(AppDomain.CurrentDomain.GetData(ATUAL) is PainelComponentes)) Montar(app);
            app.GetDockablePane(Id).Show();
        }
    }
}
