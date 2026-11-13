using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Autodesk.Revit.DB;
using WColor = System.Windows.Media.Color;
using WGrid = System.Windows.Controls.Grid;

namespace FJA.Revit.Cotagem
{
    /// <summary>Uma linha da janela: a vista, a escala e o tipo (sugerido e editável).</summary>
    public sealed class LinhaVista
    {
        public View Vista;
        public TipoVista Tipo;
        public string Origem;
        public bool Incluir = true;
    }

    /// <summary>
    /// Janela da Cotagem: lista das vistas com o tipo sugerido (template, nome ou tipo do Revit) e o que vai ser
    /// cotado em cada uma; tipo de cota, espaçamento entre linhas e modo. Montada em código (a DLL é carregada da
    /// memória), estilo de lib/fja_estilo.xaml.
    /// </summary>
    public sealed class JanelaCotagem : Window
    {
        readonly List<LinhaVista> linhas;
        readonly List<DimensionType> tipos;
        readonly OpcoesCotagem o;
        public bool Confirmado { get; private set; }
        public DimensionType TipoCota { get; private set; }

        readonly List<TextBlock> txRegra = new List<TextBlock>();
        ComboBox cbCota, cbTodas;
        TextBox txEsp;
        RadioButton rbComplementar, rbRefazer;
        TextBlock txErro;
        Button btCotar;
        readonly List<ComboBox> cbTipos = new List<ComboBox>();
        readonly List<CheckBox> cks = new List<CheckBox>();

        public JanelaCotagem(List<LinhaVista> linhas, List<DimensionType> tipos, DimensionType tipoPadrao, OpcoesCotagem o)
        {
            this.linhas = linhas; this.tipos = tipos; this.o = o;
            Title = "Cotagem";
            Width = 860; Height = 560; MinWidth = 700; MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            AplicarEstilo();
            Montar(tipoPadrao);
            Validar();
            PreviewKeyDown += (s, a) => { if (a.Key == Key.Escape) Close(); };
        }

        void AplicarEstilo()
        {
            try
            {
                string ext = AppDomain.CurrentDomain.GetData("FJA.Extensao") as string;
                string arq = ext != null ? System.IO.Path.Combine(ext, "lib", "fja_estilo.xaml") : null;
                if (arq != null && File.Exists(arq))
                    Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(arq, Encoding.UTF8)));
            }
            catch { }
            if (!Resources.Contains("Fundo"))
            {
                Background = new SolidColorBrush(WColor.FromRgb(246, 246, 245));
                FontFamily = new FontFamily("Segoe UI");
                FontSize = 13;
            }
        }

        object Rec(string k) => TryFindResource(k);
        Brush Pincel(string k, byte r, byte g, byte b) => Rec(k) as Brush ?? new SolidColorBrush(WColor.FromRgb(r, g, b));

        Border Cartao()
        {
            var b = new Border();
            if (Rec("Cartao") is Style st) b.Style = st;
            else { b.Background = Brushes.White; b.BorderBrush = Pincel("Linha", 226, 226, 223); b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(6); }
            return b;
        }

        Button Botao(string texto, string estilo = null)
        {
            var b = new Button { Content = texto, Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
            if (estilo != null && Rec(estilo) is Style st) b.Style = st;
            return b;
        }

        TextBlock Rotulo(string t)
        {
            var tb = new TextBlock { Text = t, Margin = new Thickness(0, 12, 0, 4) };
            if (Rec("Rotulo") is Style st) { tb.Style = st; tb.Margin = new Thickness(0, 12, 0, 4); }
            return tb;
        }

        TextBlock Nota(string t) => new TextBlock
        {
            Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0),
            Foreground = Pincel("Fraco", 143, 143, 140),
        };

        TextBlock Celula(string t, bool cab = false) => new TextBlock
        {
            Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 4, 6, 4),
            TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = cab ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = cab ? Pincel("Suave", 94, 94, 92) : Pincel("Tinta", 30, 30, 28), FontSize = cab ? 12 : 13,
        };

        void Montar(DimensionType tipoPadrao)
        {
            var raiz = new WGrid { Margin = new Thickness(16) };
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = raiz;

            // ---- lista das vistas
            var cLista = Cartao();
            var dp = new DockPanel { Margin = new Thickness(12) };
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            cbTodas = new ComboBox { Width = 190, VerticalAlignment = VerticalAlignment.Center };
            cbTodas.Items.Add("Aplicar a todas…");
            foreach (var t in Regras.Lista) cbTodas.Items.Add(Regras.Nome(t));
            cbTodas.SelectedIndex = 0;
            cbTodas.SelectionChanged += (s, a) =>
            {
                if (cbTodas.SelectedIndex <= 0) return;
                for (int i = 0; i < cbTipos.Count; i++) if (cks[i].IsChecked == true) cbTipos[i].SelectedIndex = cbTodas.SelectedIndex;
                cbTodas.SelectedIndex = 0;
            };
            DockPanel.SetDock(cbTodas, Dock.Right);
            topo.Children.Add(cbTodas);
            topo.Children.Add(new TextBlock { Text = linhas.Count == 1 ? "Vista" : linhas.Count + " vistas", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(topo, Dock.Top);
            dp.Children.Add(topo);

            var g = new WGrid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] cab = { "", "Vista", "Escala", "Tipo da vista", "O que será cotado" };
            for (int c = 0; c < cab.Length; c++) { var tb = Celula(cab[c], true); WGrid.SetColumn(tb, c); g.Children.Add(tb); }

            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int row = i + 1;
                var ck = new CheckBox { IsChecked = l.Incluir, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                var nome = Celula(l.Vista.Name); nome.ToolTip = l.Vista.Name;
                var esc = Celula("1:" + l.Vista.Scale);
                var cb = new ComboBox { Margin = new Thickness(4, 3, 4, 3), VerticalAlignment = VerticalAlignment.Center };
                cb.Items.Add(Regras.Nome(TipoVista.Indefinido));
                foreach (var t in Regras.Lista) cb.Items.Add(Regras.Nome(t));
                cb.SelectedIndex = l.Tipo == TipoVista.Indefinido ? 0 : Array.IndexOf(Regras.Lista, l.Tipo) + 1;
                cb.ToolTip = "Sugerido por: " + l.Origem;
                var rg = Celula(""); rg.Foreground = Pincel("Suave", 94, 94, 92); rg.FontSize = 12;
                int idx = i;
                cb.SelectionChanged += (s, a) =>
                {
                    linhas[idx].Tipo = cb.SelectedIndex <= 0 ? TipoVista.Indefinido : Regras.Lista[cb.SelectedIndex - 1];
                    linhas[idx].Origem = "escolhido agora";
                    cb.ToolTip = "Sugerido por: " + linhas[idx].Origem;
                    AtualizarRegra(idx); Validar();
                };
                ck.Checked += (s, a) => { linhas[idx].Incluir = true; Validar(); };
                ck.Unchecked += (s, a) => { linhas[idx].Incluir = false; Validar(); };
                cbTipos.Add(cb); cks.Add(ck); txRegra.Add(rg);
                var els = new UIElement[] { ck, nome, esc, cb, rg };
                for (int c = 0; c < els.Length; c++) { WGrid.SetRow(els[c], row); WGrid.SetColumn(els[c], c); g.Children.Add(els[c]); }
                AtualizarRegra(i);
            }
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = g };
            dp.Children.Add(sv);
            cLista.Child = dp;
            WGrid.SetColumn(cLista, 0);
            raiz.Children.Add(cLista);

            // ---- opções
            var cOp = Cartao();
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Opções", FontWeight = FontWeights.SemiBold });

            sp.Children.Add(Rotulo("Tipo de cota"));
            cbCota = new ComboBox();
            foreach (var t in tipos) cbCota.Items.Add(t.Name);
            int sel = tipos.FindIndex(t => t.Name == o.TipoCota);
            if (sel < 0 && tipoPadrao != null) sel = tipos.FindIndex(t => t.Id == tipoPadrao.Id);
            cbCota.SelectedIndex = sel >= 0 ? sel : (tipos.Count > 0 ? 0 : -1);
            sp.Children.Add(cbCota);

            sp.Children.Add(Rotulo("Espaçamento entre linhas de cota (mm no papel)"));
            txEsp = new TextBox { Text = o.EspacamentoMm.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")), Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
            txEsp.TextChanged += (s, a) => Validar();
            sp.Children.Add(txEsp);
            sp.Children.Add(Nota("Também é a distância da primeira linha até as bolhas dos eixos e as cabeças dos níveis."));

            sp.Children.Add(Rotulo("Modo"));
            rbComplementar = new RadioButton { Content = "Complementar", IsChecked = !o.Refazer, Margin = new Thickness(0, 2, 0, 0) };
            rbRefazer = new RadioButton { Content = "Refazer as minhas", IsChecked = o.Refazer, Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(rbComplementar);
            sp.Children.Add(Nota("Só acrescenta o que falta; não mexe em nenhuma cota existente."));
            sp.Children.Add(rbRefazer);
            sp.Children.Add(Nota("Apaga e refaz só as cotas criadas por esta ferramenta. Cotas feitas à mão nunca são apagadas."));

            sp.Children.Add(Nota("\nVersão inicial: eixos e níveis (cadeia + total). Paredes, vãos, ambientes e as plantas de teto, pontos e paginação vêm nas próximas versões."));
            cOp.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            WGrid.SetColumn(cOp, 2);
            raiz.Children.Add(cOp);

            // ---- rodapé
            var rod = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            var btCancelar = Botao("Cancelar", "Discreto");
            btCancelar.Click += (s, a) => Close();
            btCotar = Botao("Cotar", "Primario");
            btCotar.Margin = new Thickness(8, 0, 0, 0);
            btCotar.Click += (s, a) => Confirmar();
            bts.Children.Add(btCancelar); bts.Children.Add(btCotar);
            DockPanel.SetDock(bts, Dock.Right);
            rod.Children.Add(bts);
            txErro = new TextBlock { Foreground = new SolidColorBrush(WColor.FromRgb(176, 58, 46)), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            rod.Children.Add(txErro);
            WGrid.SetRow(rod, 1); WGrid.SetColumnSpan(rod, 3);
            raiz.Children.Add(rod);
        }

        void AtualizarRegra(int i)
        {
            var l = linhas[i];
            var r = Regras.Para(l.Tipo, l.Vista.Scale);
            txRegra[i].Text = (l.Tipo == TipoVista.Indefinido ? "" : Regras.NomeFaixa(Regras.FaixaDe(l.Vista.Scale)) + ": ") + r.Descricao;
            txRegra[i].ToolTip = txRegra[i].Text;
        }

        bool LerEspacamento(out double mm)
        {
            string s = (txEsp.Text ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out mm) && mm >= 2 && mm <= 30;
        }

        void Validar()
        {
            string erro = "";
            var inc = linhas.Where(l => l.Incluir).ToList();
            if (inc.Count == 0) erro = "Marque ao menos uma vista.";
            else if (inc.Any(l => l.Tipo == TipoVista.Indefinido)) erro = "Escolha o tipo de todas as vistas marcadas.";
            else if (tipos.Count == 0) erro = "O projeto não tem tipo de cota linear.";
            else if (!LerEspacamento(out _)) erro = "Espaçamento entre 2 e 30 mm.";
            if (txErro != null) txErro.Text = erro;
            if (btCotar != null) btCotar.IsEnabled = erro == "";
        }

        void Confirmar()
        {
            if (!LerEspacamento(out double mm)) return;
            o.EspacamentoMm = mm;
            o.Refazer = rbRefazer.IsChecked == true;
            TipoCota = cbCota.SelectedIndex >= 0 ? tipos[cbCota.SelectedIndex] : null;
            o.TipoCota = TipoCota?.Name ?? "";
            Confirmado = true;
            Close();
        }
    }
}
