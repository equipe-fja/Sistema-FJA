using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using WColor = System.Windows.Media.Color;
using WGrid = System.Windows.Controls.Grid;

namespace FJA.Revit.Pranchas
{
    /// <summary>Estilo comum das janelas do painel Pranchas (lib/fja_estilo.xaml, com reserva).</summary>
    static class Estilo
    {
        public static void Aplicar(Window w)
        {
            try
            {
                string ext = AppDomain.CurrentDomain.GetData("FJA.Extensao") as string;
                string arq = ext != null ? Path.Combine(ext, "lib", "fja_estilo.xaml") : null;
                if (arq != null && File.Exists(arq))
                    w.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(arq, Encoding.UTF8)));
            }
            catch { }
            if (!w.Resources.Contains("Fundo"))
            {
                w.Background = new SolidColorBrush(WColor.FromRgb(246, 246, 245));
                w.FontFamily = new FontFamily("Segoe UI");
                w.FontSize = 13;
            }
        }

        public static Brush Pincel(FrameworkElement e, string k, byte r, byte g, byte b) =>
            e.TryFindResource(k) as Brush ?? new SolidColorBrush(WColor.FromRgb(r, g, b));

        public static Border Cartao(FrameworkElement e)
        {
            var b = new Border();
            if (e.TryFindResource("Cartao") is Style st) b.Style = st;
            else { b.Background = Brushes.White; b.BorderBrush = Pincel(e, "Linha", 226, 226, 223); b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(6); }
            return b;
        }

        public static Button Botao(FrameworkElement e, string texto, string estilo = null)
        {
            var b = new Button { Content = texto, Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
            if (estilo != null && e.TryFindResource(estilo) is Style st) b.Style = st;
            return b;
        }
    }

    /// <summary>
    /// Janela do Exportar pranchas: lista das folhas com o nome do arquivo (campo "Prancha" do carimbo), filtro por
    /// texto e por disciplina, PDF e DWG ao mesmo tempo, pasta de destino (lembrada por projeto) e o que fazer com
    /// arquivos que já existem.
    /// </summary>
    public sealed class JanelaExportar : Window
    {
        readonly List<LinhaPrancha> linhas;
        readonly OpcoesExportar o;
        readonly List<string> configs;
        readonly string chave;
        public bool Confirmado { get; private set; }
        public string Pasta { get; private set; }

        readonly List<CheckBox> cks = new List<CheckBox>();
        readonly List<UIElement[]> celulas = new List<UIElement[]>();
        TextBox txFiltro, txPasta;
        ComboBox cbDisc, cbCor, cbConfig, cbExiste;
        CheckBox ckPdf, ckDwg, ckSub, ckAbrir;
        TextBlock txErro, txConta;
        Button btExportar;

        public JanelaExportar(List<LinhaPrancha> linhas, OpcoesExportar o, List<string> configs, string chave)
        {
            this.linhas = linhas; this.o = o; this.configs = configs; this.chave = chave;
            Title = "Exportar pranchas";
            Width = 1000; Height = 620; MinWidth = 820; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Estilo.Aplicar(this);
            Montar();
            Filtrar();
            Validar();
            PreviewKeyDown += (s, a) => { if (a.Key == Key.Escape) Close(); };
        }

        Brush P(string k, byte r, byte g, byte b) => Estilo.Pincel(this, k, r, g, b);

        TextBlock Rotulo(string t)
        {
            var tb = new TextBlock { Text = t, Margin = new Thickness(0, 12, 0, 4) };
            if (TryFindResource("Rotulo") is Style st) { tb.Style = st; tb.Margin = new Thickness(0, 12, 0, 4); }
            return tb;
        }

        TextBlock Nota(string t) => new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), Foreground = P("Fraco", 143, 143, 140) };

        TextBlock Celula(string t, bool cab = false) => new TextBlock
        {
            Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3, 6, 3),
            TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = cab ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = cab ? P("Suave", 94, 94, 92) : P("Tinta", 30, 30, 28), FontSize = cab ? 12 : 13, ToolTip = cab ? null : t,
        };

        void Montar()
        {
            var raiz = new WGrid { Margin = new Thickness(16) };
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = raiz;

            // ---- lista das pranchas
            var cLista = Estilo.Cartao(this);
            var dp = new DockPanel { Margin = new Thickness(12) };
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var btTodas = Estilo.Botao(this, "Marcar as visíveis", "Discreto");
            var btNenhuma = Estilo.Botao(this, "Desmarcar todas", "Discreto");
            btNenhuma.Margin = new Thickness(6, 0, 0, 0);
            btTodas.Click += (s, a) => { for (int i = 0; i < linhas.Count; i++) if (celulas[i][0].Visibility == Visibility.Visible && linhas[i].Valida) cks[i].IsChecked = true; };
            btNenhuma.Click += (s, a) => { foreach (var c in cks) c.IsChecked = false; };
            var pb = new StackPanel { Orientation = Orientation.Horizontal };
            pb.Children.Add(btTodas); pb.Children.Add(btNenhuma);
            DockPanel.SetDock(pb, Dock.Right);
            topo.Children.Add(pb);
            txFiltro = new TextBox { Width = 180, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Filtrar pelo número, nome da folha ou nome do arquivo" };
            txFiltro.TextChanged += (s, a) => Filtrar();
            cbDisc = new ComboBox { Width = 110, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Disciplina (abreviação)" };
            cbDisc.Items.Add("Todas");
            foreach (var d in linhas.Select(l => l.Disciplina).Where(x => x != "").Distinct().OrderBy(x => x)) cbDisc.Items.Add(d);
            cbDisc.SelectedIndex = 0;
            cbDisc.SelectionChanged += (s, a) => Filtrar();
            var pf = new StackPanel { Orientation = Orientation.Horizontal };
            pf.Children.Add(new TextBlock { Text = "Filtrar", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            pf.Children.Add(txFiltro); pf.Children.Add(cbDisc);
            topo.Children.Add(pf);
            DockPanel.SetDock(topo, Dock.Top);
            dp.Children.Add(topo);

            txConta = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = P("Suave", 94, 94, 92), FontSize = 12 };
            DockPanel.SetDock(txConta, Dock.Bottom);
            dp.Children.Add(txConta);

            var g = new WGrid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] cab = { "", "Número", "Folha", "Arquivo" };
            for (int c = 0; c < cab.Length; c++) { var tb = Celula(cab[c], true); WGrid.SetColumn(tb, c); g.Children.Add(tb); }

            var repetidos = new HashSet<string>(linhas.Where(l => l.Valida).GroupBy(l => l.Arquivo, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var ck = new CheckBox { IsChecked = l.Incluir && l.Valida, IsEnabled = l.Valida, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                var num = Celula(l.Numero);
                var nome = Celula(l.NomeFolha);
                var arq = Celula(l.Valida ? l.Arquivo : "falta: " + string.Join(", ", l.Faltam));
                if (!l.Valida) { arq.Foreground = new SolidColorBrush(WColor.FromRgb(176, 58, 46)); arq.ToolTip = "Preencha na folha (ou nas informações do projeto): " + string.Join(", ", l.Faltam); }
                else if (repetidos.Contains(l.Arquivo)) { arq.Foreground = new SolidColorBrush(WColor.FromRgb(176, 110, 20)); arq.ToolTip = l.Arquivo + " — mesmo nome de outra prancha: a segunda substitui a primeira"; }
                else arq.FontFamily = new FontFamily("Consolas");
                int idx = i;
                ck.Checked += (s, a) => { linhas[idx].Incluir = true; Validar(); };
                ck.Unchecked += (s, a) => { linhas[idx].Incluir = false; Validar(); };
                cks.Add(ck);
                var els = new UIElement[] { ck, num, nome, arq };
                celulas.Add(els);
                for (int c = 0; c < els.Length; c++) { WGrid.SetRow(els[c], i + 1); WGrid.SetColumn(els[c], c); g.Children.Add(els[c]); }
            }
            dp.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = g });
            cLista.Child = dp;
            raiz.Children.Add(cLista);

            // ---- opções
            var cOp = Estilo.Cartao(this);
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock { Text = "Exportar", FontWeight = FontWeights.SemiBold });

            ckPdf = new CheckBox { Content = "PDF (um arquivo por prancha)", IsChecked = o.Pdf, Margin = new Thickness(0, 10, 0, 0) };
            ckPdf.Checked += (s, a) => Validar(); ckPdf.Unchecked += (s, a) => Validar();
            sp.Children.Add(ckPdf);
            cbCor = new ComboBox { Margin = new Thickness(20, 4, 0, 0) };
            cbCor.Items.Add("Colorido"); cbCor.Items.Add("Tons de cinza"); cbCor.Items.Add("Preto");
            cbCor.SelectedIndex = (int)o.Cor;
            sp.Children.Add(cbCor);

            ckDwg = new CheckBox { Content = "DWG (um arquivo por prancha)", IsChecked = o.Dwg, Margin = new Thickness(0, 12, 0, 0) };
            ckDwg.Checked += (s, a) => Validar(); ckDwg.Unchecked += (s, a) => Validar();
            sp.Children.Add(ckDwg);
            cbConfig = new ComboBox { Margin = new Thickness(20, 4, 0, 0) };
            var nomes = new List<string>(configs);
            if (!nomes.Contains(Exportacao.ConfigFJA)) nomes.Insert(0, Exportacao.ConfigFJA);
            foreach (var n in nomes) cbConfig.Items.Add(n == Exportacao.ConfigFJA && !configs.Contains(n) ? n + " (será criada)" : n);
            int sel = nomes.IndexOf(o.ConfigDwg);
            cbConfig.SelectedIndex = sel >= 0 ? sel : nomes.IndexOf(Exportacao.ConfigFJA);
            cbConfig.Tag = nomes;
            sp.Children.Add(cbConfig);
            var nt = Nota(Exportacao.ConfigFJA + ": opções padrão do Revit, cores True Color (RGB) e sem referências externas.");
            nt.Margin = new Thickness(20, 4, 0, 0);
            sp.Children.Add(nt);

            sp.Children.Add(Rotulo("Pasta de destino"));
            var pp = new DockPanel();
            var btPasta = Estilo.Botao(this, "Escolher…", "Discreto");
            btPasta.Margin = new Thickness(6, 0, 0, 0);
            btPasta.Click += (s, a) => EscolherPasta();
            DockPanel.SetDock(btPasta, Dock.Right);
            pp.Children.Add(btPasta);
            txPasta = new TextBox { Text = o.Pastas.TryGetValue(chave, out var pv) ? Convert.ToString(pv) : "", VerticalAlignment = VerticalAlignment.Center };
            txPasta.TextChanged += (s, a) => Validar();
            pp.Children.Add(txPasta);
            sp.Children.Add(pp);
            ckSub = new CheckBox { Content = "Separar em subpastas PDF e DWG", IsChecked = o.Subpastas, Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(ckSub);

            sp.Children.Add(Rotulo("Se o arquivo já existir"));
            cbExiste = new ComboBox();
            cbExiste.Items.Add("Substituir"); cbExiste.Items.Add("Pular (manter o que está na pasta)");
            cbExiste.SelectedIndex = o.Substituir ? 0 : 1;
            sp.Children.Add(cbExiste);

            ckAbrir = new CheckBox { Content = "Abrir a pasta ao terminar", IsChecked = o.AbrirPasta, Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(ckAbrir);
            sp.Children.Add(Nota("\nNome do arquivo = campo \"Prancha\" do carimbo: Project Number - Etapa (sigla) - Disciplina (abreviação) - Tipo de documento - Sheet Number - Current Revision."));
            cOp.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            WGrid.SetColumn(cOp, 2);
            raiz.Children.Add(cOp);

            // ---- rodapé
            var rod = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            var btCancelar = Estilo.Botao(this, "Cancelar", "Discreto");
            btCancelar.Click += (s, a) => Close();
            btExportar = Estilo.Botao(this, "Exportar", "Primario");
            btExportar.Margin = new Thickness(8, 0, 0, 0);
            btExportar.Click += (s, a) => Confirmar();
            bts.Children.Add(btCancelar); bts.Children.Add(btExportar);
            DockPanel.SetDock(bts, Dock.Right);
            rod.Children.Add(bts);
            txErro = new TextBlock { Foreground = new SolidColorBrush(WColor.FromRgb(176, 58, 46)), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            rod.Children.Add(txErro);
            WGrid.SetRow(rod, 1); WGrid.SetColumnSpan(rod, 3);
            raiz.Children.Add(rod);
        }

        void Filtrar()
        {
            if (txFiltro == null || cbDisc == null) return;
            string f = (txFiltro.Text ?? "").Trim();
            string d = cbDisc.SelectedIndex > 0 ? cbDisc.SelectedItem as string : null;
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                bool ok = (d == null || l.Disciplina == d) &&
                          (f == "" || (l.Numero + " " + l.NomeFolha + " " + l.Arquivo).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
                foreach (var e in celulas[i]) e.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void EscolherPasta()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Pasta para os PDFs e DWGs", ShowNewFolderButton = true })
            {
                if (Directory.Exists(txPasta.Text)) dlg.SelectedPath = txPasta.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) txPasta.Text = dlg.SelectedPath;
            }
        }

        void Validar()
        {
            if (txErro == null || btExportar == null || ckPdf == null || ckDwg == null || txPasta == null) return;
            int n = linhas.Count(l => l.Incluir && l.Valida);
            int inval = linhas.Count(l => !l.Valida);
            if (txConta != null)
                txConta.Text = n + (n == 1 ? " prancha marcada" : " pranchas marcadas") + " de " + linhas.Count +
                               (inval > 0 ? " · " + inval + " sem nome completo (parâmetros vazios), não exportáveis" : "");
            string erro = "";
            if (n == 0) erro = "Marque ao menos uma prancha.";
            else if (ckPdf.IsChecked != true && ckDwg.IsChecked != true) erro = "Escolha PDF, DWG ou os dois.";
            else if (string.IsNullOrWhiteSpace(txPasta.Text)) erro = "Escolha a pasta de destino.";
            else if (!Path.IsPathRooted(txPasta.Text.Trim())) erro = "A pasta de destino precisa ser um caminho completo.";
            txErro.Text = erro;
            btExportar.IsEnabled = erro == "";
            btExportar.Content = n > 0 ? "Exportar " + n : "Exportar";
        }

        void Confirmar()
        {
            Pasta = txPasta.Text.Trim();
            o.Pdf = ckPdf.IsChecked == true;
            o.Dwg = ckDwg.IsChecked == true;
            o.Cor = (CorPdf)Math.Max(0, cbCor.SelectedIndex);
            var nomes = (List<string>)cbConfig.Tag;
            o.ConfigDwg = cbConfig.SelectedIndex >= 0 ? nomes[cbConfig.SelectedIndex] : Exportacao.ConfigFJA;
            o.Subpastas = ckSub.IsChecked == true;
            o.Substituir = cbExiste.SelectedIndex == 0;
            o.AbrirPasta = ckAbrir.IsChecked == true;
            o.Pastas[chave] = Pasta;
            Confirmado = true;
            Close();
        }
    }

    /// <summary>Janela de progresso (não modal), atualizada entre uma prancha e outra; Cancelar para depois da prancha atual.</summary>
    public sealed class JanelaProgresso : Window
    {
        readonly TextBlock tx;
        readonly ProgressBar pb;
        public bool Cancelado { get; private set; }

        public JanelaProgresso(int total)
        {
            Title = "Exportar pranchas";
            Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Topmost = true;
            Estilo.Aplicar(this);
            var sp = new StackPanel { Margin = new Thickness(16) };
            tx = new TextBlock { Text = "Preparando…", TextTrimming = TextTrimming.CharacterEllipsis };
            pb = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Height = 10, Margin = new Thickness(0, 10, 0, 12) };
            var bt = Estilo.Botao(this, "Cancelar", "Discreto");
            bt.HorizontalAlignment = HorizontalAlignment.Right;
            bt.Click += (s, a) => { Cancelado = true; tx.Text = "Cancelando depois da prancha atual…"; bt.IsEnabled = false; };
            sp.Children.Add(tx); sp.Children.Add(pb); sp.Children.Add(bt);
            Content = sp;
        }

        public void Passo(int i, string texto)
        {
            if (!Cancelado) tx.Text = texto;
            pb.Value = i;
            // deixa a janela redesenhar e o clique em Cancelar chegar, entre uma exportação e outra
            Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
        }
    }
}
