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
        readonly List<KeyValuePair<string, HashSet<int>>> conjuntos;
        readonly RegraNome regra;
        readonly List<string> disponiveis;
        readonly Action<RegraNome> recalcular;
        readonly Func<DadosEmissao> carregarEm;
        /// <summary>Dados do projeto no sistema (lidos ao marcar "Registrar emissão no sistema").</summary>
        public DadosEmissao DadosEm { get; private set; }
        /// <summary>O que gravar no sistema (null ou Registrar = false: nada).</summary>
        public PedidoEm Emissao { get; private set; }
        CheckBox ckReg;
        StackPanel spEm;
        ComboBox cbEm;
        TextBlock txEmStatus;
        readonly List<KeyValuePair<CheckBox, DiscEm>> ckDiscs = new List<KeyValuePair<CheckBox, DiscEm>>();
        readonly List<KeyValuePair<CheckBox, DestEm>> ckDests = new List<KeyValuePair<CheckBox, DestEm>>();
        readonly List<TextBlock> arqs = new List<TextBlock>();
        StackPanel spRegra;
        public bool Confirmado { get; private set; }
        /// <summary>Pasta final da exportação (a de destino, ou a pasta nova dentro dela).</summary>
        public string Pasta { get; private set; }
        /// <summary>Nome da pasta nova (AA-MM-DD descrição), ou null.</summary>
        public string PastaNova { get; private set; }

        readonly List<CheckBox> cks = new List<CheckBox>();
        readonly List<UIElement[]> celulas = new List<UIElement[]>();
        TextBox txFiltro, txPasta, txDescricao;
        ComboBox cbDisc, cbConjunto, cbCor, cbConfig, cbExiste;
        CheckBox ckPdf, ckDwg, ckSub, ckAbrir, ckNova;
        TextBlock txErro, txConta, txNomeNova;
        Button btExportar;

        public JanelaExportar(List<LinhaPrancha> linhas, OpcoesExportar o, List<string> configs, string chave,
                              List<KeyValuePair<string, HashSet<int>>> conjuntos,
                              RegraNome regra, List<string> disponiveis, Action<RegraNome> recalcular, Func<DadosEmissao> carregarEm)
        {
            this.carregarEm = carregarEm;
            this.linhas = linhas; this.o = o; this.configs = configs; this.chave = chave; this.conjuntos = conjuntos;
            this.regra = regra; this.disponiveis = disponiveis; this.recalcular = recalcular;
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
            cbConjunto = new ComboBox { Width = 200, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Conjunto de folhas (sheet set)" };
            cbConjunto.Items.Add("Todas as folhas");
            foreach (var c in conjuntos) cbConjunto.Items.Add(c.Key);
            cbConjunto.SelectedIndex = 0;
            cbConjunto.SelectionChanged += (s, a) =>
            {
                // escolher um conjunto: mostra só as folhas dele, todas marcadas
                if (cbConjunto.SelectedIndex > 0)
                {
                    var idx = conjuntos[cbConjunto.SelectedIndex - 1].Value;
                    for (int i = 0; i < cks.Count; i++) cks[i].IsChecked = idx.Contains(i) && linhas[i].Valida;
                }
                Filtrar();
            };
            txFiltro = new TextBox { Width = 150, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Filtrar pelo número, nome da folha ou nome do arquivo" };
            txFiltro.TextChanged += (s, a) => Filtrar();
            cbDisc = new ComboBox { Width = 90, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Disciplina (abreviação)" };
            cbDisc.Items.Add("Todas");
            foreach (var d in linhas.Select(l => l.Disciplina).Where(x => x != "").Distinct().OrderBy(x => x)) cbDisc.Items.Add(d);
            cbDisc.SelectedIndex = 0;
            cbDisc.SelectionChanged += (s, a) => Filtrar();
            var pf = new StackPanel { Orientation = Orientation.Horizontal };
            pf.Children.Add(cbConjunto); pf.Children.Add(txFiltro); pf.Children.Add(cbDisc);
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

            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var ck = new CheckBox { IsChecked = l.Incluir && l.Valida, IsEnabled = l.Valida, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                var num = Celula(l.Numero);
                var nome = Celula(l.NomeFolha);
                var arq = Celula("");
                arqs.Add(arq);
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
            PintarNomes();

            // ---- opções
            var cOp = Estilo.Cartao(this);
            var sp = new StackPanel { Margin = new Thickness(12) };

            // regra do nome do arquivo (= campo "Prancha" do carimbo), gravada no modelo
            sp.Children.Add(new TextBlock { Text = "Nome do arquivo", FontWeight = FontWeights.SemiBold });
            spRegra = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(spRegra);
            MontarRegra();

            var tit = new TextBlock { Text = "Exportar", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) };
            sp.Children.Add(tit);

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
            ckNova = new CheckBox { Content = "Criar pasta nova", IsChecked = o.NovaPasta, Margin = new Thickness(0, 8, 0, 0) };
            ckNova.Checked += (s, a) => Validar(); ckNova.Unchecked += (s, a) => Validar();
            sp.Children.Add(ckNova);
            txDescricao = new TextBox
            {
                Text = o.Descricoes.TryGetValue(chave, out var dv) ? Convert.ToString(dv) : "",
                Margin = new Thickness(20, 4, 0, 0), ToolTip = "Descrição, ex.: EMISSÃO FINAL EXECUTIVO - ARQUITETURA - 1ª PARTE",
            };
            txDescricao.TextChanged += (s, a) => Validar();
            sp.Children.Add(txDescricao);
            txNomeNova = new TextBlock { Margin = new Thickness(20, 4, 0, 0), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = P("Suave", 94, 94, 92) };
            sp.Children.Add(txNomeNova);
            ckSub = new CheckBox { Content = "Separar em subpastas PDF e DWG", IsChecked = o.Subpastas, Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(ckSub);

            // emissão no sistema (Projeto › Emissões): só com a opção marcada
            ckReg = new CheckBox { Content = "Registrar emissão no sistema", IsChecked = false, Margin = new Thickness(0, 14, 0, 0) };
            ckReg.Checked += (s, a) => AbrirEmissao();
            ckReg.Unchecked += (s, a) => { spEm.Visibility = Visibility.Collapsed; Validar(); };
            sp.Children.Add(ckReg);
            spEm = new StackPanel { Margin = new Thickness(20, 4, 0, 0), Visibility = Visibility.Collapsed };
            sp.Children.Add(spEm);

            sp.Children.Add(Rotulo("Se o arquivo já existir"));
            cbExiste = new ComboBox();
            cbExiste.Items.Add("Substituir"); cbExiste.Items.Add("Pular (manter o que está na pasta)");
            cbExiste.SelectedIndex = o.Substituir ? 0 : 1;
            sp.Children.Add(cbExiste);

            ckAbrir = new CheckBox { Content = "Abrir a pasta ao terminar", IsChecked = o.AbrirPasta, Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(ckAbrir);
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

        /// <summary>Texto e cor da coluna Arquivo (falta parâmetro = vermelho; nome repetido = laranja) e caixas habilitadas.</summary>
        void PintarNomes()
        {
            var repetidos = new HashSet<string>(linhas.Where(l => l.Valida).GroupBy(l => l.Arquivo, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i]; var arq = arqs[i];
                arq.Text = l.Valida ? l.Arquivo : "(nome vazio)";
                arq.FontFamily = new FontFamily("Consolas");
                string dica = l.Faltam.Count > 0 ? "\nEm branco: " + string.Join(", ", l.Faltam) : "";
                if (l.Valida && repetidos.Contains(l.Arquivo)) { arq.Foreground = new SolidColorBrush(WColor.FromRgb(176, 110, 20)); arq.ToolTip = l.Arquivo + " — mesmo nome de outra prancha" + dica; }
                else { arq.Foreground = l.Valida ? P("Tinta", 30, 30, 28) : P("Fraco", 143, 143, 140); arq.ToolTip = (l.Arquivo ?? "") + dica; }
                cks[i].IsEnabled = l.Valida;
                if (!l.Valida) cks[i].IsChecked = false;
            }
        }

        const string FORMATO_PARTE = "FJA.ParteNome";
        /// <summary>Texto fixo recém-acrescentado: recebe o foco quando a regra é remontada.</summary>
        ParteNome focar;

        /// <summary>Editor da regra (2.40.0): uma linha por parte (parâmetro ou texto fixo; arrastar pela linha para
        /// reordenar), com o separador que vem depois dela e o ✕ para tirar; o "+" abaixo da última acrescenta.</summary>
        void MontarRegra()
        {
            spRegra.Children.Clear();
            var realce = new SolidColorBrush(WColor.FromArgb(40, 142, 27, 27));
            for (int i = 0; i < regra.Partes.Count; i++)
            {
                var p = regra.Partes[i]; int k = i;
                var lin = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, AllowDrop = true, Cursor = Cursors.SizeAll };
                var bx = new Button { Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), ToolTip = "Tirar do nome", IsEnabled = regra.Partes.Count > 1, FontSize = 11, Cursor = Cursors.Arrow };
                if (TryFindResource("Discreto") is Style st) bx.Style = st;
                bx.Click += (s, a) => { regra.Partes.RemoveAt(k); MudouRegra(); };
                DockPanel.SetDock(bx, Dock.Right);
                lin.Children.Add(bx);

                // separador depois desta parte (na última não há o que separar: fica oculto, mantendo o alinhamento)
                var cbSep = SeletorSep(regra.SepDe(p), v => { p.Sep = v; MudouRegra(false); });
                cbSep.Visibility = k < regra.Partes.Count - 1 ? Visibility.Visible : Visibility.Hidden;
                DockPanel.SetDock(cbSep, Dock.Right);
                lin.Children.Add(cbSep);

                var alca = new TextBlock { Text = "⋮⋮", Foreground = P("Fraco", 143, 143, 140), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                DockPanel.SetDock(alca, Dock.Left);
                lin.Children.Add(alca);
                if (p.Fixo)
                {
                    // texto fixo: digitado na própria linha; o nome das pranchas se atualiza a cada tecla
                    var rot = new TextBlock { Text = "Texto", FontSize = 11, Foreground = P("Fraco", 143, 143, 140), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                    DockPanel.SetDock(rot, Dock.Left);
                    lin.Children.Add(rot);
                    var tx = new TextBox { Text = p.Texto, Cursor = Cursors.IBeam, Margin = new Thickness(0, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center,
                                           ToolTip = "Texto fixo, igual em todas as pranchas (vazio = não entra no nome)" };
                    tx.TextChanged += (s, a) => { p.Texto = tx.Text ?? ""; MudouRegra(false); };
                    lin.Children.Add(tx);
                    if (ReferenceEquals(p, focar)) { focar = null; Dispatcher.BeginInvoke(new Action(() => { tx.Focus(); Keyboard.Focus(tx); })); }
                }
                else
                    lin.Children.Add(new TextBlock { Text = p.Param, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = p.Param });

                // arrastar: começa depois de mover alguns pixels com o botão apertado (fora do ✕, do separador e do texto)
                Point? ini = null;
                lin.PreviewMouseLeftButtonDown += (s, a) => { ini = a.OriginalSource is DependencyObject d && EmBotao(d) ? (Point?)null : a.GetPosition(spRegra); };
                lin.PreviewMouseMove += (s, a) =>
                {
                    if (ini == null || a.LeftButton != MouseButtonState.Pressed) return;
                    var v = a.GetPosition(spRegra) - ini.Value;
                    if (Math.Abs(v.Y) < 4 && Math.Abs(v.X) < 4) return;
                    ini = null;
                    DragDrop.DoDragDrop(lin, new DataObject(FORMATO_PARTE, k), DragDropEffects.Move);
                };
                lin.PreviewMouseLeftButtonUp += (s, a) => ini = null;
                lin.DragEnter += (s, a) => { if (a.Data.GetDataPresent(FORMATO_PARTE)) lin.Background = realce; };
                lin.DragLeave += (s, a) => lin.Background = Brushes.Transparent;
                lin.Drop += (s, a) =>
                {
                    lin.Background = Brushes.Transparent;
                    if (!(a.Data.GetData(FORMATO_PARTE) is int de) || de == k) return;
                    var t = regra.Partes[de];
                    regra.Partes.RemoveAt(de);
                    regra.Partes.Insert(k, t);   // solta no lugar da linha de destino
                    MudouRegra();
                };
                spRegra.Children.Add(lin);
            }

            // "+" sempre abaixo da última parte: menu com o texto fixo e os parâmetros que ainda não estão no nome
            var btMais = new Button { Content = "+", Width = 26, Height = 22, Padding = new Thickness(0), FontSize = 14, Margin = new Thickness(0, 4, 0, 0),
                                      HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Acrescentar parâmetro ou texto fixo" };
            if (TryFindResource("Discreto") is Style st2) btMais.Style = st2;
            btMais.Click += (s, a) =>
            {
                var menu = new ContextMenu { PlacementTarget = btMais, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 420 };
                var miTexto = new MenuItem { Header = "Texto fixo", FontWeight = FontWeights.SemiBold };
                miTexto.Click += (s2, a2) => { var n = new ParteNome { Texto = "" }; regra.Partes.Add(n); focar = n; MudouRegra(); };
                menu.Items.Add(miTexto);
                var livres = disponiveis.Where(n => !regra.Partes.Any(x => !x.Fixo && string.Equals(x.Param, n, StringComparison.OrdinalIgnoreCase))).ToList();
                if (livres.Count > 0) menu.Items.Add(new Separator());
                foreach (var n in livres)
                {
                    var mi = new MenuItem { Header = n.Replace("_", "__") };   // "_" sozinho viraria tecla de atalho
                    mi.Click += (s2, a2) => { regra.Partes.Add(new ParteNome { Param = n }); MudouRegra(); };
                    menu.Items.Add(mi);
                }
                menu.IsOpen = true;
            };
            spRegra.Children.Add(btMais);
        }

        /// <summary>Opções de separador entre uma parte e a seguinte (valor, rótulo).</summary>
        static readonly KeyValuePair<string, string>[] SEPARADORES =
        {
            new KeyValuePair<string, string>("-", "-"), new KeyValuePair<string, string>("_", "_"), new KeyValuePair<string, string>(".", "."),
            new KeyValuePair<string, string>(" ", "espaço"), new KeyValuePair<string, string>("", "nenhum"),
        };

        /// <summary>Separador depois de uma parte (2.41.0: digitável; a lista só sugere). Escolher "espaço" ou "nenhum"
        /// na lista põe o valor de verdade na caixa.</summary>
        ComboBox SeletorSep(string atual, Action<string> mudou)
        {
            var cb = new ComboBox { Width = 76, Height = 22, Margin = new Thickness(4, 0, 4, 0), IsEditable = true, IsTextSearchEnabled = false,
                                    ToolTip = "Separador depois desta parte: digite ou escolha (-, _, ., espaço, nenhum)", VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var o in SEPARADORES) cb.Items.Add(o.Value);
            cb.Text = atual ?? "";
            cb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, a) =>
            {
                string t = cb.Text ?? "";
                // rótulo escolhido na lista ("espaço", "nenhum"): troca pelo valor; o TextChanged seguinte grava
                var rot = SEPARADORES.FirstOrDefault(o => o.Value == t && o.Key != o.Value);
                if (rot.Value != null) { Dispatcher.BeginInvoke(new Action(() => cb.Text = rot.Key)); return; }
                mudou(t);
            }));
            return cb;
        }

        static bool EmBotao(DependencyObject d)
        {
            for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is Button || d is TextBox || d is ComboBox) return true;   // clicar no ✕ ou digitar no texto fixo não arrasta
            return false;
        }

        /// <summary>Marcou "Registrar emissão no sistema": lê o sistema (uma vez) e monta emissão, disciplinas e destinatários.</summary>
        void AbrirEmissao()
        {
            spEm.Visibility = Visibility.Visible;
            if (DadosEm == null)
            {
                var cur = Cursor; Cursor = Cursors.Wait;
                try { DadosEm = carregarEm(); } finally { Cursor = cur; }
                spEm.Children.Clear();
                txEmStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = P("Suave", 94, 94, 92) };
                spEm.Children.Add(txEmStatus);
                if (!DadosEm.Ok) { txEmStatus.Text = DadosEm.Erro; txEmStatus.Foreground = new SolidColorBrush(WColor.FromRgb(176, 58, 46)); Validar(); return; }
                txEmStatus.Text = DadosEm.NomeProjeto;

                cbEm = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
                cbEm.Items.Add("Nova emissão");
                foreach (var r in DadosEm.Rascunhos) cbEm.Items.Add(r);
                cbEm.SelectedIndex = 0;
                cbEm.SelectionChanged += (s, a) =>
                {
                    if (cbEm.SelectedItem is RascunhoEm r && txDescricao != null) txDescricao.Text = r.Descricao;
                    Validar();
                };
                spEm.Children.Add(cbEm);

                // disciplinas: as do projeto, já marcadas as das pranchas marcadas (parâmetro Disciplina da folha)
                spEm.Children.Add(Rotulo("Disciplinas"));
                var detect = new HashSet<string>(linhas.Where(l => l.Incluir).Select(l => DadosEm.Resolver(l.Disciplina)).Where(c => c != null));
                var wp = new WrapPanel();
                foreach (var d in DadosEm.Disciplinas.Where(x => x.DoProjeto || detect.Contains(x.Cod)))
                {
                    var ck = new CheckBox { Content = d.Sigla, IsChecked = detect.Contains(d.Cod), Margin = new Thickness(0, 2, 10, 2), ToolTip = d.Nome };
                    ckDiscs.Add(new KeyValuePair<CheckBox, DiscEm>(ck, d));
                    wp.Children.Add(ck);
                }
                spEm.Children.Add(wp);

                spEm.Children.Add(Rotulo("Destinatários"));
                var spD = new StackPanel();
                foreach (var d in DadosEm.Envolvidos)
                {
                    var ck = new CheckBox { Content = d.Nome, Margin = new Thickness(0, 2, 0, 2), ToolTip = string.Join(" · ", new[] { d.Empresa, d.Email }.Where(x => !string.IsNullOrEmpty(x))) };
                    if (string.IsNullOrEmpty(ck.ToolTip as string)) ck.ToolTip = null;
                    ckDests.Add(new KeyValuePair<CheckBox, DestEm>(ck, d));
                    spD.Children.Add(ck);
                }
                spEm.Children.Add(new ScrollViewer { Content = spD, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            }
            Validar();
        }

        void MudouRegra(bool remontar = true)
        {
            if (remontar) Dispatcher.BeginInvoke(new Action(MontarRegra));
            recalcular(regra);
            PintarNomes();
            Validar();
        }

        void Filtrar()
        {
            if (txFiltro == null || cbDisc == null) return;
            string f = (txFiltro.Text ?? "").Trim();
            string d = cbDisc.SelectedIndex > 0 ? cbDisc.SelectedItem as string : null;
            var conj = cbConjunto != null && cbConjunto.SelectedIndex > 0 ? conjuntos[cbConjunto.SelectedIndex - 1].Value : null;
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                bool ok = (conj == null || conj.Contains(i)) && (d == null || l.Disciplina == d) &&
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
                               (inval > 0 ? " · " + inval + " sem nome" : "");
            bool nova = ckNova != null && ckNova.IsChecked == true;
            bool reg = ckReg != null && ckReg.IsChecked == true;
            if (txDescricao != null) txDescricao.IsEnabled = nova || reg;
            if (txNomeNova != null)
            {
                txNomeNova.Visibility = nova ? Visibility.Visible : Visibility.Collapsed;
                txNomeNova.Text = NomeArquivo.PastaEmissao(txDescricao.Text);
            }
            string erro = "";
            if (n == 0) erro = "Marque ao menos uma prancha.";
            else if (ckPdf.IsChecked != true && ckDwg.IsChecked != true) erro = "Escolha PDF, DWG ou os dois.";
            else if (string.IsNullOrWhiteSpace(txPasta.Text)) erro = "Escolha a pasta de destino.";
            else if (!Path.IsPathRooted(txPasta.Text.Trim())) erro = "A pasta de destino precisa ser um caminho completo.";
            else if (nova && string.IsNullOrWhiteSpace(txDescricao.Text)) erro = "Escreva a descrição da pasta nova.";
            else if (reg && DadosEm != null && !DadosEm.Ok) erro = "Sistema: " + DadosEm.Erro + ".";
            else if (reg && string.IsNullOrWhiteSpace(txDescricao.Text)) erro = "Escreva a descrição da emissão.";
            txErro.Text = erro;
            btExportar.IsEnabled = erro == "";
            btExportar.Content = n > 0 ? "Exportar " + n : "Exportar";
        }

        void Confirmar()
        {
            // parâmetros em branco: saem do nome; avisa antes de exportar
            var brancos = linhas.Where(l => l.Incluir && l.Valida && l.Faltam.Count > 0).ToList();
            if (brancos.Count > 0)
            {
                var txt = string.Join("\n", brancos.Take(15).Select(l => l.Numero + ": " + string.Join(", ", l.Faltam) + "  →  " + l.Arquivo));
                if (brancos.Count > 15) txt += "\n… e mais " + (brancos.Count - 15);
                var r = MessageBox.Show(this,
                    (brancos.Count == 1 ? "1 prancha tem" : brancos.Count + " pranchas têm") + " parâmetros em branco (ficam fora do nome):\n\n" + txt + "\n\nExportar assim mesmo?",
                    "Exportar pranchas", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (r != MessageBoxResult.Yes) return;
            }
            string baseP = txPasta.Text.Trim();
            o.Pastas[chave] = baseP;
            o.NovaPasta = ckNova.IsChecked == true;
            if (!string.IsNullOrWhiteSpace(txDescricao.Text)) o.Descricoes[chave] = txDescricao.Text.Trim();
            PastaNova = o.NovaPasta ? NomeArquivo.PastaEmissao(txDescricao.Text) : null;
            Pasta = PastaNova != null ? Path.Combine(baseP, PastaNova) : baseP;
            if (ckReg.IsChecked == true && DadosEm != null && DadosEm.Ok)
                Emissao = new PedidoEm
                {
                    Registrar = true,
                    Rascunho = cbEm?.SelectedItem as RascunhoEm,
                    Descricao = txDescricao.Text.Trim(),
                    Disciplinas = ckDiscs.Where(k => k.Key.IsChecked == true).Select(k => k.Value.Cod).ToList(),
                    Destinatarios = ckDests.Where(k => k.Key.IsChecked == true).Select(k => k.Value).ToList(),
                };
            o.Pdf = ckPdf.IsChecked == true;
            o.Dwg = ckDwg.IsChecked == true;
            o.Cor = (CorPdf)Math.Max(0, cbCor.SelectedIndex);
            var nomes = (List<string>)cbConfig.Tag;
            o.ConfigDwg = cbConfig.SelectedIndex >= 0 ? nomes[cbConfig.SelectedIndex] : Exportacao.ConfigFJA;
            o.Subpastas = ckSub.IsChecked == true;
            o.Substituir = cbExiste.SelectedIndex == 0;
            o.AbrirPasta = ckAbrir.IsChecked == true;
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
