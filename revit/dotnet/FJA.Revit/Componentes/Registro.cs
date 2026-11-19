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
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(246, 246, 245)),   // sem fundo, o Revit mostra preto
                Child = Aviso("Clique em Ferramentas FJA › Tipos › Componentes para abrir o painel."),
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
            try { if (AppDomain.CurrentDomain.GetData(ATUAL) == null) Montar(app); }
            catch (Exception ex)
            {
                if (AppDomain.CurrentDomain.GetData(MOLDURA) is Border m)
                    m.Child = Aviso("Não foi possível montar o painel na abertura do Revit (" + ex.Message + "). Clique em Ferramentas FJA › Tipos › Componentes para tentar de novo.");
            }
        }

        static TextBlock Aviso(string texto) => new TextBlock
        {
            Text = texto, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 94, 92)),
        };

        static void Montar(UIApplication app)
        {
            var moldura = AppDomain.CurrentDomain.GetData(MOLDURA) as Border
                ?? throw new InvalidOperationException("O painel Componentes é registrado na abertura do Revit. Feche e abra o Revit uma vez depois de atualizar a suíte.");
            moldura.Child = new PainelComponentes(app);
        }

        const string JANELA = "FJA.Componentes.Janela";

        /// <summary>Botão Componentes: mostra o painel encaixável (montando o conteúdo com esta DLL, se ainda não for dela).
        /// Se o Revit ainda não criou o painel — o registro só vale na abertura do Revit; num Atualizar/Reload do pyRevit
        /// ele é aceito mas o painel não é criado —, abre o mesmo conteúdo numa janela.</summary>
        public static void Mostrar(UIApplication app)
        {
            DockablePane dp = null;
            try { if (AppDomain.CurrentDomain.GetData(MOLDURA) is Border) dp = app.GetDockablePane(Id); }
            catch { dp = null; }
            if (dp != null)
            {
                var moldura = (Border)AppDomain.CurrentDomain.GetData(MOLDURA);
                // monta de novo se a moldura não tem o painel desta DLL (DLL nova, falha na abertura ou painel em janela)
                if (!(moldura.Child is PainelComponentes p) || p.EstaVisivel != null || !ReferenceEquals(AppDomain.CurrentDomain.GetData(ATUAL), p))
                {
                    (AppDomain.CurrentDomain.GetData(JANELA) as Window)?.Close();
                    Montar(app);
                }
                dp.Show();
                return;
            }
            AbrirJanela(app);
        }

        static void AbrirJanela(UIApplication app)
        {
            if (AppDomain.CurrentDomain.GetData(JANELA) is Window w && w.IsVisible && w.Content is PainelComponentes)
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Activate();
                return;
            }
            var painel = new PainelComponentes(app);
            var j = new Window
            {
                Title = "Componentes FJA", Width = 440, Height = 820, MinWidth = 340, MinHeight = 420,
                Content = painel, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.WorkArea.Right - 460, Top = SystemParameters.WorkArea.Top + 40,
            };
            painel.EstaVisivel = () => j.IsVisible && j.WindowState != WindowState.Minimized;
            new System.Windows.Interop.WindowInteropHelper(j) { Owner = app.MainWindowHandle };
            j.Closed += (s, e) =>
            {
                if (ReferenceEquals(AppDomain.CurrentDomain.GetData(ATUAL), painel)) AppDomain.CurrentDomain.SetData(ATUAL, null);
                AppDomain.CurrentDomain.SetData(JANELA, null);
            };
            AppDomain.CurrentDomain.SetData(JANELA, j);
            j.Show();
            if (AppDomain.CurrentDomain.GetData("FJA.Componentes.AvisoJanela") == null)
            {
                AppDomain.CurrentDomain.SetData("FJA.Componentes.AvisoJanela", true);
                TaskDialog.Show("Componentes", "O painel encaixável só fica disponível depois de fechar e abrir o Revit (o Revit cria painéis apenas na abertura).\n\nAté lá, Componentes abre nesta janela, com as mesmas funções.");
            }
        }
    }
}
