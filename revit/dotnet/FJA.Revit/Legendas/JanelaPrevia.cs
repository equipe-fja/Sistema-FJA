using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using PreviewControl = Autodesk.Revit.UI.PreviewControl;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using FJA.Revit.Nucleo;
using WColor = System.Windows.Media.Color;
using WGrid = System.Windows.Controls.Grid;
using RView = Autodesk.Revit.DB.View;

namespace FJA.Revit.Legendas
{
    /// <summary>
    /// Ferramenta Legendas: disciplina, legendas já criadas por ela (e "+ Nova legenda"), a lista de itens da
    /// legenda escolhida (incluir, texto, escala, ordem por arrastar, itens acrescentados), opções da tabela e cor,
    /// e a legenda desenhada pelo próprio Revit ao lado.
    /// Cada "Atualizar prévia" refaz a legenda dentro de um grupo de transações desfeito na atualização
    /// seguinte; "Criar/Gravar legenda" grava; trocar de legenda ou fechar sem gravar desfaz.
    /// Montada em código (sem XAML compilado: a DLL é carregada da memória, ver docs/revit-csharp.md).
    /// </summary>
    public sealed class JanelaPrevia : Window
    {
        readonly Document doc;
        Disciplina D;
        Montagem montagem;
        Coleta coleta;
        List<Item> itensIniciais = new List<Item>();
        readonly Dictionary<string, List<Item>> cacheItens = new Dictionary<string, List<Item>>();
        RView legenda;          // legenda escolhida (null = nova, ainda não gravada)
        string nomeNova;        // nome da legenda nova
        bool alterado;          // a pessoa mexeu em algo desde que escolheu a legenda
        bool trocando;          // ignorar eventos enquanto a lista/opções são preenchidas
        ComboBox cbDisc; ListBox lbLegendas; Button btNova; TextBlock txtLegenda;

        sealed class Linha
        {
            public Item It;
            public Border Borda;
            public CheckBox Chk;
            public TextBox Txt;
            public TextBlock Med, Pct;
            public Slider Sl;
            public string Id => It.Chave;
        }

        readonly List<Linha> linhas = new List<Linha>();
        TransactionGroup tg;
        PreviewControl prev;
        ResultadoLegenda res;
        public bool Gravado { get; private set; }
        public RView VistaGerada { get; private set; }

        readonly Dictionary<string, double[]> baseMm = new Dictionary<string, double[]>();   // mm a 100%
        readonly Dictionary<string, double[]> caixas = new Dictionary<string, double[]>();  // cx, cy, w, h (pés)
        Dictionary<string, int> pctFeito = new Dictionary<string, int>();
        readonly Dictionary<string, List<ElementId>> idsCaixa = new Dictionary<string, List<ElementId>>();
        HashSet<string> sujos = new HashSet<string>();
        readonly DispatcherTimer timer;
        string tabelaFeita, ordemFeita;
        string corFeita;
        bool aberta;
        readonly double inicioAbertura;

        // cor
        ModoCor modo = ModoCor.Modelo;
        byte[] corEscolhida;
        Tuple<byte[], string> corSistema;
        bool mudandoCor;

        // controles
        Slider slPasso; TextBlock txtPasso; TextBox txtLargura; CheckBox chkH, chkV, chkM; ComboBox cbTexto;
        ComboBox cbCor; Border swCor; Button btAtualizar, btGerar, btCancelar, btTodos100, btAcrescentar;
        StackPanel lista; TextBlock txtItens, txtStatus, txtPrevVazia; WGrid hostPrev;
        Linha arrastando;

        static readonly byte[] COR_CAIXA = { 200, 30, 30 };

        public JanelaPrevia(Document doc, Disciplina inicial, double inicioAbertura)
        {
            this.doc = doc;
            this.inicioAbertura = inicioAbertura;
            Title = "Legendas";
            Width = 1480; Height = 860; MinWidth = 1150; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            AplicarEstilo();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (s, a) => { timer.Stop(); DesenharCaixas(); };
            Montar();
            LerPreferencias();
            Closing += (s, a) =>
            {
                if (alterado && tg != null && !Confirmar("Fechar sem gravar as alterações de \"" + NomeAtual() + "\"?")) { a.Cancel = true; return; }
                Desfazer();
            };
            trocando = true;
            foreach (var d in Disciplina.Todas) cbDisc.Items.Add(d.Codigo + " · " + d.Titulo.Replace("LEGENDA DE ", "").ToLowerInvariant());
            cbDisc.SelectedIndex = Math.Max(0, Array.IndexOf(Disciplina.Todas, inicial));
            trocando = false;
            ContentRendered += Primeira;
        }

        void Primeira(object s, EventArgs a)
        {
            ContentRendered -= Primeira;
            CarregarDisciplina(Disciplina.Todas[cbDisc.SelectedIndex]);
        }

        string NomeAtual() => legenda != null ? legenda.Name : (nomeNova ?? "legenda nova");

        bool Confirmar(string pergunta)
        {
            var td = new TaskDialog("Legendas") { MainInstruction = pergunta, CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No, DefaultButton = Autodesk.Revit.UI.TaskDialogResult.No };
            return td.Show() == Autodesk.Revit.UI.TaskDialogResult.Yes;
        }

        // ------------------------------------------------------------ disciplina e legendas
        void CarregarDisciplina(Disciplina d)
        {
            Desfazer();
            D = d;
            montagem = new Montagem(doc, d);
            coleta = new Coleta(doc, d);
            corSistema = null;
            if (!cacheItens.TryGetValue(d.Codigo, out var lista0))
            {
                txtStatus.Text = "Lendo os itens " + d.Codigo + " do modelo…";
                double t = Tempos.Agora;
                try { lista0 = coleta.Itens(); }
                catch (Exception e) { lista0 = new List<Item>(); txtStatus.Text = "Erro ao ler os itens: " + e.Message; }
                Tempos.Marca($"LER OS ITENS {d.Codigo} DO MODELO ({lista0.Count})", t);
                cacheItens[d.Codigo] = lista0;
                // símbolos fixos (ex.: eletrodutos ELE): cria as famílias que faltam, fora da prévia
                if (d.Fixos != null)
                {
                    double tf = Tempos.Agora;
                    var errosFixos = Simbolos.GarantirFixos(doc, d, Path.Combine(Path.GetTempPath(), "FJA_LEG"));
                    if (errosFixos.Count > 0) TaskDialog.Show(d.Botao, "Não foi possível criar os símbolos fixos:\n\n" + string.Join("\n", errosFixos));
                    Tempos.Marca("símbolos fixos", tf);
                }
            }
            itensIniciais = lista0;
            var legs = montagem.ListarLegendas();
            PreencherLegendas(legs, null);
            if (legs.Count > 0) lbLegendas.SelectedIndex = 0;
            else if (lista0.Count > 0) NovaLegenda(d.Alvo);
            else
            {
                LimparLista();
                txtLegenda.Text = "Nenhuma legenda " + d.Codigo;
                txtStatus.Text = "Nenhuma família '" + string.Join("' / '", d.Familias) + "' posicionada no modelo.";
            }
        }

        void PreencherLegendas(List<RView> legs, string nova)
        {
            trocando = true;
            lbLegendas.Items.Clear();
            foreach (var v in legs) lbLegendas.Items.Add(new ListBoxItem { Content = v.Name, Tag = v, Padding = new Thickness(8, 6, 8, 6), ToolTip = v.Name });
            if (nova != null)
                lbLegendas.Items.Add(new ListBoxItem { Content = nova + "  (nova)", Tag = nova, Padding = new Thickness(8, 6, 8, 6), FontStyle = FontStyles.Italic });
            trocando = false;
        }

        object selAnterior;

        void LegendaClicada()
        {
            if (trocando || !(lbLegendas.SelectedItem is ListBoxItem li)) return;
            if (li.Tag == selAnterior) return;
            if (alterado && tg != null && !Confirmar("Descartar as alterações de \"" + NomeAtual() + "\"?"))
            {
                trocando = true;
                lbLegendas.SelectedItem = lbLegendas.Items.Cast<ListBoxItem>().FirstOrDefault(x => x.Tag == selAnterior);
                trocando = false;
                return;
            }
            selAnterior = li.Tag;
            if (li.Tag is RView v) Selecionar(v, null);
            else Selecionar(null, (string)li.Tag);
        }

        void NovaLegenda(string sugestao = null)
        {
            if (alterado && tg != null && !Confirmar("Descartar as alterações de \"" + NomeAtual() + "\"?")) return;
            string nome = Escolha.Nome(this, "Nova legenda " + D.Codigo, "Nome da vista de legenda:", montagem.NomeLivre(sugestao ?? D.Alvo));
            if (string.IsNullOrWhiteSpace(nome)) return;
            nome = montagem.NomeLivre(nome.Trim());
            PreencherLegendas(montagem.ListarLegendas(), nome);
            trocando = true;
            lbLegendas.SelectedIndex = lbLegendas.Items.Count - 1;
            trocando = false;
            selAnterior = nome;
            Selecionar(null, nome);
        }

        void LimparLista()
        {
            linhas.Clear();
            lista.Children.Clear();
            baseMm.Clear();
            caixas.Clear();
            pctFeito = new Dictionary<string, int>();
            res = null;
            TituloLista();
        }

        /// <summary>Abre uma legenda (ou uma nova) na lista e na prévia.</summary>
        void Selecionar(RView v, string nova)
        {
            Desfazer();
            legenda = v;
            nomeNova = v == null ? nova : null;
            trocando = true;
            LimparLista();
            var itens = itensIniciais.Select(i => i.Novo()).ToList();
            var cfg = v != null ? montagem.LerConfig(v) : new Dictionary<string, object>();
            var desm = montagem.AplicarConfig(itens, cfg);
            foreach (var it in itens) CriarLinha(it, desm.Contains(it.Chave));
            TituloLista();
            // opções gravadas nesta legenda (ou as últimas usadas)
            var o = cfg.Obj("opcoes");
            double Num(string k, double pad) { try { return o.ContainsKey(k) ? Convert.ToDouble(o[k], CultureInfo.InvariantCulture) : pad; } catch { return pad; } }
            bool Sim(string k) => o.ContainsKey(k) && o[k] is bool bb && bb;
            slPasso.Value = Num("passo", Montagem.MM_ROW_STEP);
            txtLargura.Text = Texto.Mm(Num("largura", Montagem.MM_LARGURA) / 10.0);
            chkH.IsChecked = Sim("linhasH"); chkV.IsChecked = Sim("linhaV"); chkM.IsChecked = Sim("margens");
            // Type Mark: 0 não mostra, 1 coluna própria, 2 antes do texto (legendas antigas: "texto" 1 = antes)
            int tm = o.ContainsKey("typeMark") ? (int)Num("typeMark", 0) : ((int)Num("texto", 0) == 1 ? 2 : 0);
            cbTexto.SelectedIndex = tm >= 0 && tm <= 2 ? tm : 0;
            var oc = o.Lista("cor");
            if (oc.Count == 3) corEscolhida = oc.Select(x => (byte)Convert.ToInt32(x)).ToArray();
            int m = o.ContainsKey("modoCor") ? (int)Num("modoCor", 0) : (int)modoPadrao;
            if (m < 0 || m > 3) m = 0;
            mudandoCor = true; cbCor.SelectedIndex = m; mudandoCor = false;
            AplicarModo((ModoCor)m, true);
            txtPasso.Text = Texto.Mm(slPasso.Value) + " mm";
            trocando = false;
            txtLegenda.Text = v != null ? v.Name : nova + "  (nova)";
            btGerar.Content = v != null ? "Gravar legenda" : "Criar legenda";
            alterado = false;
            if (linhas.Count > 0) Atualizar();
            else txtStatus.Text = "Nenhum item.";
        }

        // ------------------------------------------------------------ estilo e montagem da janela
        void AplicarEstilo()
        {
            try
            {
                string ext = AppDomain.CurrentDomain.GetData("FJA.Extensao") as string;
                string arq = ext != null ? Path.Combine(ext, "lib", "fja_estilo.xaml") : null;
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

        object Rec(string k) => Resources.Contains(k) || TryFindResource(k) != null ? TryFindResource(k) : null;
        Brush Pincel(string k, byte r, byte g, byte b) => Rec(k) as Brush ?? new SolidColorBrush(WColor.FromRgb(r, g, b));

        Border Cartao()
        {
            var b = new Border();
            if (Rec("Cartao") is Style st) b.Style = st;
            else
            {
                b.Background = Brushes.White;
                b.BorderBrush = Pincel("Linha", 226, 226, 223);
                b.BorderThickness = new Thickness(1);
                b.CornerRadius = new CornerRadius(6);
            }
            return b;
        }

        Button Botao(string texto, string estilo = null)
        {
            var b = new Button { Content = texto, Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
            if (estilo != null && Rec(estilo) is Style st)
            {
                b.Style = st;
                if (estilo == "Discreto")
                {
                    // o estilo Discreto do fja_estilo.xaml é de botão de ícone (28 px): aqui o botão tem texto
                    b.Width = double.NaN;
                    b.Padding = new Thickness(10, 4, 10, 4);
                    b.Cursor = Cursors.Hand;
                }
            }
            return b;
        }

        static StackPanel Horizontal(Thickness margem) =>
            new StackPanel { Orientation = Orientation.Horizontal, Margin = margem, VerticalAlignment = VerticalAlignment.Center };

        static TextBlock Rotulo(string t, Thickness margem) =>
            new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = margem };

        void Montar()
        {
            var g = new WGrid { Margin = new Thickness(16) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // disciplina e legendas
            var cLeg = Cartao();
            WGrid.SetRowSpan(cLeg, 2);
            var dl = new DockPanel { Margin = new Thickness(12) };
            var rd = new TextBlock { Text = "Disciplina", Margin = new Thickness(2, 0, 0, 4), Foreground = Pincel("Suave", 94, 94, 92), FontSize = 12 };
            DockPanel.SetDock(rd, Dock.Top);
            cbDisc = new ComboBox { Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(cbDisc, Dock.Top);
            var rl = new TextBlock { Text = "Legendas criadas", Margin = new Thickness(2, 0, 0, 4), Foreground = Pincel("Suave", 94, 94, 92), FontSize = 12 };
            DockPanel.SetDock(rl, Dock.Top);
            btNova = Botao("+ Nova legenda", "Discreto");
            btNova.HorizontalAlignment = HorizontalAlignment.Left;
            btNova.Margin = new Thickness(0, 8, 0, 0);
            btNova.ToolTip = "Cria outra legenda desta disciplina (outra vista), com os seus próprios itens, ordem e opções";
            DockPanel.SetDock(btNova, Dock.Bottom);
            lbLegendas = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            dl.Children.Add(rd); dl.Children.Add(cbDisc); dl.Children.Add(rl); dl.Children.Add(btNova); dl.Children.Add(lbLegendas);
            cLeg.Child = dl;
            g.Children.Add(cLeg);

            // ajustes
            var topo = Cartao();
            topo.Padding = new Thickness(14, 10, 14, 10);
            topo.Margin = new Thickness(0, 0, 0, 12);
            WGrid.SetColumn(topo, 2);
            WGrid.SetColumnSpan(topo, 3);
            var wp = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var p1 = Horizontal(new Thickness(0, 4, 24, 4));
            p1.Children.Add(Rotulo("Altura mínima da linha", new Thickness(0, 0, 8, 0)));
            slPasso = new Slider { Width = 140, Minimum = 4, Maximum = 14, Value = Montagem.MM_ROW_STEP, TickFrequency = 0.1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            txtPasso = new TextBlock { Width = 56, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            p1.Children.Add(slPasso); p1.Children.Add(txtPasso);
            var p2 = Horizontal(new Thickness(0, 4, 24, 4));
            p2.Children.Add(Rotulo("Largura da legenda", new Thickness(0, 0, 8, 0)));
            txtLargura = new TextBox { Width = 54, Text = "17,5", Padding = new Thickness(4, 2, 4, 2), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            p2.Children.Add(txtLargura); p2.Children.Add(Rotulo("cm", new Thickness(6, 0, 0, 0)));
            var p3 = Horizontal(new Thickness(0, 4, 24, 4));
            p3.Children.Add(Rotulo("Cor dos símbolos", new Thickness(0, 0, 8, 0)));
            swCor = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), BorderBrush = new SolidColorBrush(WColor.FromRgb(154, 154, 150)), BorderThickness = new Thickness(1), Background = Brushes.Black, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, ToolTip = "Clique para escolher outra cor", VerticalAlignment = VerticalAlignment.Center };
            cbCor = new ComboBox { Width = 230, VerticalAlignment = VerticalAlignment.Center };
            foreach (var t in new[] { "Cor do modelo (como nas vistas)", "Cor do projeto no Sistema FJA", "Escolher cor…", "Preto" }) cbCor.Items.Add(t);
            p3.Children.Add(swCor); p3.Children.Add(cbCor);
            chkH = new CheckBox { Content = "Linhas entre itens", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 16, 4) };
            chkV = new CheckBox { Content = "Linha entre símbolo e texto", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 16, 4) };
            chkM = new CheckBox { Content = "Margens (contorno)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 24, 4) };
            var p4 = Horizontal(new Thickness(0, 4, 24, 4));
            p4.Children.Add(Rotulo("Type Mark", new Thickness(0, 0, 8, 0)));
            cbTexto = new ComboBox { Width = 210, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Código do tipo (Type Mark, ferramenta Codificação). O texto do item é a Description, que a Codificação mantém igual ao nome do tipo." };
            foreach (var t in new[] { "Não mostrar", "Em coluna própria", "Antes do texto (LOG.01 – …)" }) cbTexto.Items.Add(t);
            cbTexto.SelectedIndex = 0;
            p4.Children.Add(cbTexto);
            foreach (UIElement el in new UIElement[] { p1, p2, p3, p4, chkH, chkV, chkM }) wp.Children.Add(el);
            topo.Child = wp;
            g.Children.Add(topo);

            // itens
            var cIt = Cartao();
            WGrid.SetRow(cIt, 1); WGrid.SetColumn(cIt, 2);
            var dp = new DockPanel();
            var cab = new Border { Padding = new Thickness(14, 10, 14, 10), BorderBrush = Pincel("Linha", 226, 226, 223), BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(cab, Dock.Top);
            var dc = new DockPanel();
            btTodos100 = Botao("Voltar todos a 100%", "Discreto");
            var btOrdem = Botao("Ordenar pelo Type Mark", "Discreto");
            btOrdem.Margin = new Thickness(0, 0, 8, 0);
            btOrdem.ToolTip = "Põe os itens na ordem do código (LOG.01, LOG.02…); os sem código ficam no fim. Depois dá para arrastar.";
            btOrdem.Click += (s, a) => { OrdenarPorCodigo(); Mudou(); };
            DockPanel.SetDock(btOrdem, Dock.Right);
            btAcrescentar = Botao("+ Acrescentar símbolo…", "Discreto");
            btAcrescentar.Margin = new Thickness(0, 0, 8, 0);
            btAcrescentar.ToolTip = "Acrescentar um símbolo (anotação genérica) ou detail item do projeto; a cor é aplicada como nos demais";
            DockPanel.SetDock(btTodos100, Dock.Right); DockPanel.SetDock(btAcrescentar, Dock.Right);
            txtItens = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var ajuda = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(8, 0, 0, 0),
                BorderBrush = Pincel("LinhaForte", 201, 201, 197), BorderThickness = new Thickness(1), Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Help,
                Child = new TextBlock { Text = "?", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Pincel("Fraco", 143, 143, 140), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                ToolTip = new ToolTip
                {
                    MaxWidth = 380,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Marque os itens e ajuste o texto; arraste pelo ≡ para mudar a ordem.\n\n" +
                               "Escala de cada símbolo: de 25% a 300% (100% = como aparece na prancha). Ao mexer, a prévia mostra uma caixa vermelha " +
                               "no tamanho novo; Atualizar prévia refaz os símbolos que mudaram. A medida é a do símbolo no papel.\n\n" +
                               "+ Acrescentar símbolo: inclui uma anotação genérica ou um detail item do projeto, com a cor da legenda; a escala também vale para ele (vira uma família (LEG) própria). " +
                               "Para retirar, clique com o botão direito na linha.\n\n" +
                               "Itens em laranja não aparecem em nenhuma planta da disciplina colocada em prancha.",
                    },
                },
            };
            ToolTipService.SetInitialShowDelay(ajuda, 100);
            ToolTipService.SetShowDuration(ajuda, 60000);
            var tituloItens = new StackPanel { Orientation = Orientation.Horizontal };
            tituloItens.Children.Add(txtItens); tituloItens.Children.Add(ajuda);
            dc.Children.Add(btTodos100); dc.Children.Add(btOrdem); dc.Children.Add(btAcrescentar); dc.Children.Add(tituloItens);
            cab.Child = dc;
            lista = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lista };
            dp.Children.Add(cab); dp.Children.Add(sv);
            cIt.Child = dp;
            g.Children.Add(cIt);

            // prévia
            var cPv = Cartao();
            WGrid.SetRow(cPv, 1); WGrid.SetColumn(cPv, 4);
            var dpv = new DockPanel();
            var cabPv = new Border { Padding = new Thickness(14, 8, 10, 8), BorderBrush = Pincel("Linha", 226, 226, 223), BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(cabPv, Dock.Top);
            var dcp = new DockPanel();
            btAtualizar = Botao("Atualizar prévia", "Discreto");
            btAtualizar.Margin = new Thickness(0, 0, 8, 0);
            btGerar = Botao("Criar legenda", "Primario");
            var bp = new StackPanel { Orientation = Orientation.Horizontal };
            bp.Children.Add(btAtualizar); bp.Children.Add(btGerar);
            DockPanel.SetDock(bp, Dock.Right);
            txtLegenda = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            dcp.Children.Add(bp); dcp.Children.Add(txtLegenda);
            cabPv.Child = dcp;
            var gp = new WGrid();
            hostPrev = new WGrid { Background = Brushes.White };
            txtPrevVazia = new TextBlock { Text = "Gerando a prévia…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Pincel("Fraco", 143, 143, 140) };
            gp.Children.Add(hostPrev); gp.Children.Add(txtPrevVazia);
            dpv.Children.Add(cabPv); dpv.Children.Add(gp);
            cPv.Child = dpv;
            g.Children.Add(cPv);

            // rodapé
            var rod = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            WGrid.SetRow(rod, 2); WGrid.SetColumnSpan(rod, 5);
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(bts, Dock.Right);
            btCancelar = Botao("Fechar", "Discreto");
            btCancelar.ToolTip = "Fecha a ferramenta; o que não foi gravado é desfeito";
            bts.Children.Add(btCancelar);
            txtStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Pincel("Suave", 94, 94, 92) };
            rod.Children.Add(bts); rod.Children.Add(txtStatus);
            g.Children.Add(rod);
            Content = g;

            txtPasso.Text = Texto.Mm(slPasso.Value) + " mm";
            slPasso.ValueChanged += (s, a) => Mudou();
            txtLargura.TextChanged += (s, a) => Mudou();
            foreach (var c in new[] { chkH, chkV, chkM }) c.Click += (s, a) => Mudou();
            btAtualizar.Click += (s, a) => Atualizar();
            btGerar.Click += (s, a) => Gerar();
            btCancelar.Click += (s, a) => Close();
            btTodos100.Click += (s, a) => { foreach (var ln in linhas) { ln.Sl.Value = 100; sujos.Add(ln.Id); } };
            btAcrescentar.Click += (s, a) => Acrescentar();
            cbCor.SelectionChanged += (s, a) => { if (!mudandoCor && !trocando) { alterado = true; AplicarModo((ModoCor)cbCor.SelectedIndex, false); } };
            cbDisc.SelectionChanged += (s, a) =>
            {
                if (trocando || cbDisc.SelectedIndex < 0) return;
                var d = Disciplina.Todas[cbDisc.SelectedIndex];
                if (d == D) return;
                if (alterado && tg != null && !Confirmar("Descartar as alterações de \"" + NomeAtual() + "\"?"))
                {
                    trocando = true; cbDisc.SelectedIndex = Array.IndexOf(Disciplina.Todas, D); trocando = false;
                    return;
                }
                selAnterior = null;
                CarregarDisciplina(d);
            };
            lbLegendas.SelectionChanged += (s, a) => LegendaClicada();
            cbTexto.SelectionChanged += (s, a) =>
            {
                if (trocando) return;
                Mudou();
            };
            btNova.Click += (s, a) => NovaLegenda();
            swCor.MouseLeftButtonUp += (s, a) => EscolherCor(true);
        }

        // ------------------------------------------------------------ lista de itens
        void TituloLista()
        {
            int fora = linhas.Count(l => l.It.EmPrancha == false), extra = linhas.Count(l => l.It.Extra);
            var partes = new List<string>();
            if (extra > 0) partes.Add(extra + " acrescentado(s)");
            if (fora > 0) partes.Add(fora + " fora das pranchas, em laranja");
            txtItens.Text = "Itens (" + linhas.Count + (partes.Count > 0 ? " · " + string.Join(" · ", partes) : "") + ")";
        }

        Linha CriarLinha(Item it, bool desmarcado)
        {
            var cinza = new SolidColorBrush(WColor.FromRgb(94, 94, 92));
            var borda = new Border { BorderThickness = new Thickness(0, 2, 0, 2), BorderBrush = Brushes.Transparent, AllowDrop = true, Background = Brushes.Transparent };
            var g = new WGrid { Margin = new Thickness(2, 1, 4, 1) };
            foreach (double w in new double[] { 18, 26, -1, 96, 120, 46 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            var alca = new TextBlock { Text = "≡", FontSize = 16, Foreground = cinza, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = "Arraste para mudar a ordem na legenda" };
            var chk = new CheckBox { IsChecked = !desmarcado, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Incluir na legenda" };
            var txt = new TextBox { Text = it.Rotulo, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2) };
            bool fora = it.EmPrancha == false;
            string vistas = string.Join("/", D.Vistas ?? new string[0]);
            if (it.Extra)
                txt.ToolTip = $"Acrescentado: {it.Sym.FamilyName} : {it.Sym.Name} ({it.Sym.Category?.Name})\nClique com o botão direito para retirar.";
            else
            {
                string dica = (string.IsNullOrEmpty(it.Codigo) ? "" : it.Codigo + "  ·  ") + $"{it.Sym.FamilyName}  ·  {it.Quantidade} elemento(s) no modelo";
                if (it.EmPrancha == true) dica += $"  ·  aparece em {it.Vistas} planta(s) {vistas} em prancha";
                else if (fora) dica += $"\nNão aparece em nenhuma planta {vistas} colocada em prancha (desmarque se não deve entrar)";
                txt.ToolTip = dica;
            }
            if (fora) { txt.Foreground = new SolidColorBrush(WColor.FromRgb(178, 107, 0)); txt.FontStyle = FontStyles.Italic; }
            var med = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), Foreground = cinza, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var sl = new Slider { Minimum = 25, Maximum = 300, Value = 100, TickFrequency = 5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Escala do símbolo (100% = como aparece na prancha). Duplo clique volta a 100%." };
            var pct = new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.SemiBold };
            if (it.Extra) med.Text = "acrescentado";
            var els = new UIElement[] { alca, chk, txt, med, sl, pct };
            for (int i = 0; i < els.Length; i++) { WGrid.SetColumn(els[i], i); g.Children.Add(els[i]); }
            borda.Child = g;
            var ln = new Linha { It = it, Borda = borda, Chk = chk, Txt = txt, Med = med, Sl = sl, Pct = pct };
            chk.Click += (s, a) => { sujos.Add(ln.Id); timer.Stop(); timer.Start(); Mudou(); };
            txt.TextChanged += (s, a) => Mudou();
            sl.ValueChanged += (s, a) => Escala(ln);
            sl.MouseDoubleClick += (s, a) => sl.Value = 100;
            if (it.Extra)
            {
                var mi = new MenuItem { Header = "Retirar este item da lista" };
                mi.Click += (s, a) => { linhas.Remove(ln); lista.Children.Remove(ln.Borda); TituloLista(); Mudou(); };
                borda.ContextMenu = new ContextMenu();
                borda.ContextMenu.Items.Add(mi);
            }
            alca.PreviewMouseLeftButtonDown += (s, a) => Arrastar(ln);
            borda.PreviewDragOver += (s, a) => Sobre(ln, a);
            borda.DragLeave += (s, a) => Marca(ln, null);
            borda.PreviewDrop += (s, a) => Soltar(ln, a);
            lista.Children.Add(borda);
            linhas.Add(ln);
            return ln;
        }

        void Escala(Linha ln)
        {
            if (!trocando) alterado = true;
            int v = (int)Math.Round(ln.Sl.Value);
            ln.Pct.Text = v + "%";
            if (baseMm.TryGetValue(ln.Id, out var b))
                ln.Med.Text = Texto.Mm(b[0] * v / 100.0) + " × " + Texto.Mm(b[1] * v / 100.0) + " mm";
            sujos.Add(ln.Id);
            timer.Stop();
            timer.Start();
            Pendente();
        }

        // ------------------------------------------------------------ ordem (arrastar)
        void Arrastar(Linha ln)
        {
            arrastando = ln;
            try { DragDrop.DoDragDrop(ln.Borda, new DataObject("fjaItem", ln.Id), DragDropEffects.Move); }
            finally
            {
                arrastando = null;
                foreach (var x in linhas) Marca(x, null);
            }
        }

        static bool Depois(Linha ln, DragEventArgs a) => a.GetPosition(ln.Borda).Y > ln.Borda.ActualHeight / 2;

        void Marca(Linha ln, bool? depois)
        {
            ln.Borda.BorderBrush = depois == null ? Brushes.Transparent : new SolidColorBrush(WColor.FromRgb(121, 86, 169));
            ln.Borda.BorderThickness = depois == null ? new Thickness(0, 2, 0, 2) : depois.Value ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
        }

        void Sobre(Linha ln, DragEventArgs a)
        {
            a.Effects = arrastando == null ? DragDropEffects.None : DragDropEffects.Move;
            if (arrastando != null) Marca(ln, Depois(ln, a));
            a.Handled = true;
        }

        void Soltar(Linha alvo, DragEventArgs a)
        {
            a.Handled = true;
            Marca(alvo, null);
            var orig = arrastando;
            if (orig == null || orig == alvo) return;
            bool depois = Depois(alvo, a);
            linhas.Remove(orig);
            int i = linhas.IndexOf(alvo) + (depois ? 1 : 0);
            linhas.Insert(i, orig);
            lista.Children.Remove(orig.Borda);
            lista.Children.Insert(i, orig.Borda);
            Mudou();
        }

        string Ordem() => string.Join("|", linhas.Select(l => l.Id));

        // ------------------------------------------------------------ itens acrescentados
        void Acrescentar()
        {
            var ja = new HashSet<string>(linhas.Select(l => l.Id));
            var opcoes = coleta.SimbolosParaExtra().Select(s => Coleta.ItemExtra(s)).Where(it => !ja.Contains(it.Chave)).ToList();
            if (opcoes.Count == 0)
            {
                TaskDialog.Show(Title, "O projeto não tem outros símbolos (anotações genéricas) nem detail items para acrescentar.");
                return;
            }
            var escolhidos = Escolha.Mostrar(this, "Acrescentar à legenda: símbolos e detail items",
                opcoes.Select(it => $"{it.Sym.Category?.Name} · {it.Sym.FamilyName} : {it.Sym.Name}").ToList());
            if (escolhidos == null || escolhidos.Count == 0) return;
            foreach (int i in escolhidos) sujos.Add(CriarLinha(opcoes[i], false).Id);
            TituloLista();
            Mudou();
        }

        string ConfigJson()
        {
            var rot = new Dictionary<string, object>();
            var desm = new List<object>();
            var extras = new List<object>();
            foreach (var ln in linhas)
            {
                string t = (ln.Txt.Text ?? "").Trim().ToUpperInvariant();
                if (ln.It.Extra) extras.Add(new Dictionary<string, object> { { "uid", ln.It.Sym.UniqueId }, { "rotulo", t } });
                else if (t != "" && t != (ln.It.Padrao ?? "").ToUpperInvariant()) rot[ln.Id] = t;
                if (ln.Chk.IsChecked != true) desm.Add(ln.Id);
            }
            return Json.Gravar(new Dictionary<string, object>
            {
                { "ordem", linhas.Select(l => (object)l.Id).ToList() }, { "rotulos", rot }, { "desmarcados", desm }, { "extras", extras },
                { "opcoes", new Dictionary<string, object>
                    {
                        { "passo", Math.Round(slPasso.Value, 1) }, { "largura", LarguraMm() }, { "linhasH", chkH.IsChecked == true },
                        { "linhaV", chkV.IsChecked == true }, { "margens", chkM.IsChecked == true }, { "modoCor", (int)modo },
                        { "typeMark", cbTexto.SelectedIndex },
                        { "cor", corEscolhida != null ? corEscolhida.Select(x => (object)(int)x).ToList() : null },
                    } },
            });
        }

        // ------------------------------------------------------------ cor
        static string ArqPreferencias => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "legenda-previa.json");

        void LerPreferencias()
        {
            var p = new Dictionary<string, object>();
            try { if (File.Exists(ArqPreferencias)) p = Json.LerObjeto(File.ReadAllText(ArqPreferencias, Encoding.UTF8)); } catch { }
            var c = p.Lista("corEscolhida");
            if (c.Count == 3) corEscolhida = c.Select(x => (byte)Convert.ToInt32(x)).ToArray();
            int m = 0;
            try { m = Convert.ToInt32(p.ContainsKey("modo") ? p["modo"] : 0); } catch { }
            if (m < 0 || m > 3) m = 0;
            modoPadrao = (ModoCor)m;
        }

        ModoCor modoPadrao = ModoCor.Modelo;

        void GravarPreferencias()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArqPreferencias));
                var p = new Dictionary<string, object> { { "modo", (int)modo } };
                if (corEscolhida != null) p["corEscolhida"] = corEscolhida.Select(x => (object)(int)x).ToList();
                File.WriteAllText(ArqPreferencias, Json.Gravar(p), Encoding.UTF8);
            }
            catch { }
        }

        byte[] CorModelo() => Cores.Dominante(linhas.Count > 0 ? linhas.Select(l => l.It) : itensIniciais, D);

        byte[] CorAtual()
        {
            switch (modo)
            {
                case ModoCor.Modelo: return CorModelo();
                case ModoCor.Preto: return null;
                case ModoCor.Sistema: return corSistema?.Item1;
                default: return corEscolhida;
            }
        }

        void Amostra()
        {
            var c = CorAtual() ?? new byte[] { 0, 0, 0 };
            swCor.Background = new SolidColorBrush(WColor.FromRgb(c[0], c[1], c[2]));
        }

        void VoltarModo(ModoCor m)
        {
            mudandoCor = true;
            cbCor.SelectedIndex = (int)m;
            mudandoCor = false;
        }

        void AplicarModo(ModoCor m, bool inicial)
        {
            string antes = Cores.Chave(CorAtual()) + modo;
            if (m == ModoCor.Sistema)
            {
                if (corSistema == null)
                {
                    double t = Tempos.Agora;
                    try { corSistema = CorSistema.Ler(doc, D.Codigo); }
                    catch (Exception e) { corSistema = Tuple.Create<byte[], string>(null, "não foi possível ler o sistema: " + e.Message); }
                    Tempos.Marca("cor do projeto no Sistema FJA", t);
                }
                cbCor.ToolTip = (corSistema.Item1 != null ? "Cor de " : "Usando a cor do modelo: ") + corSistema.Item2;
                if (corSistema.Item1 == null)
                {
                    if (!inicial) TaskDialog.Show(Title, "Não achei a cor no Sistema FJA: " + corSistema.Item2 + ".\n\nOs símbolos ficam com a cor do modelo.");
                    m = ModoCor.Modelo;
                    VoltarModo(m);
                }
            }
            if (m == ModoCor.Escolhida && corEscolhida == null && !inicial)
            {
                if (!EscolherCor(false)) { VoltarModo(modo); return; }
            }
            if (m == ModoCor.Escolhida && corEscolhida == null) m = ModoCor.Modelo;
            modo = m;
            var cm = CorModelo();
            if (m == ModoCor.Modelo)
                cbCor.ToolTip = $"A cor mais usada nos elementos {string.Join("/", D.Vistas ?? new string[0])} do modelo (filtros, sobreposições, estilo de objeto): RGB {cm[0]}-{cm[1]}-{cm[2]}";
            else if (m == ModoCor.Preto) cbCor.ToolTip = "Símbolos em preto (sem sobreposição de cor)";
            else if (m == ModoCor.Escolhida) cbCor.ToolTip = $"Cor escolhida: RGB {corEscolhida[0]}-{corEscolhida[1]}-{corEscolhida[2]}";
            Amostra();
            if (!inicial)
            {
                GravarPreferencias();
                if (Cores.Chave(CorAtual()) + modo != antes) Repintar();
            }
        }

        bool EscolherCor(bool aplicar)
        {
            var c = CorAtual() ?? corEscolhida ?? CorModelo();
            // amostras desta legenda: a cor dos símbolos no modelo e a da disciplina no Sistema FJA
            var amostras = new List<SeletorCor.Amostra>();
            try { var cm = CorModelo(); if (cm != null) amostras.Add(new SeletorCor.Amostra("MOD", "Cor dos símbolos no modelo (" + SeletorCor.Hex(cm) + ")", cm)); } catch { }
            if (corSistema == null)
            {
                try { corSistema = CorSistema.Ler(doc, D.Codigo); }
                catch (Exception e) { corSistema = Tuple.Create<byte[], string>(null, "não foi possível ler o sistema: " + e.Message); }
            }
            if (corSistema?.Item1 != null) amostras.Add(new SeletorCor.Amostra(D.Codigo, "Cor de " + corSistema.Item2 + " (" + SeletorCor.Hex(corSistema.Item1) + ")", corSistema.Item1));
            if (corEscolhida != null) amostras.Add(new SeletorCor.Amostra("", "Última cor escolhida nesta legenda (" + SeletorCor.Hex(corEscolhida) + ")", corEscolhida));
            byte[] r;
            try { r = SeletorCor.Escolher(this, c, "Desta legenda", amostras); }
            catch (Exception e) { TaskDialog.Show(Title, "O seletor de cor falhou: " + e.Message); return false; }
            if (r == null) return false;
            corEscolhida = r;
            if (aplicar)
            {
                VoltarModo(ModoCor.Escolhida);
                AplicarModo(ModoCor.Escolhida, false);
            }
            GravarPreferencias();
            return true;
        }

        Opcoes OpcoesCor(Opcoes op)
        {
            op.Modo = modo;
            op.Cor = modo == ModoCor.Sistema ? corSistema?.Item1 : modo == ModoCor.Escolhida ? corEscolhida : null;
            return op;
        }

        /// <summary>Troca a cor dos símbolos na prévia na hora, sem refazer a legenda.</summary>
        void Repintar()
        {
            if (res == null || tg == null) return;
            var op = OpcoesCor(new Opcoes());
            if (modo == ModoCor.Modelo)
            {
                var cu = Cores.Dominante(res.Itens, D);
                foreach (var it in res.Itens) it.CorLegenda = cu;
            }
            using (var t = new Transaction(doc, "Prévia: cor"))
            {
                try
                {
                    t.Start();
                    foreach (var it in res.Itens)
                        foreach (var i in it.SymIds)
                            if (doc.GetElement(i) != null) Cores.Pintar(res.Vista, i, Montagem.CorDoItem(it, op));
                    t.Commit();
                    corFeita = Cores.Chave(CorAtual()) + modo;
                }
                catch (Exception e)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    txtStatus.Text = "Não foi possível trocar a cor: " + e.Message;
                }
            }
        }

        // ------------------------------------------------------------ mudanças pendentes
        double LarguraMm()
        {
            if (double.TryParse((txtLargura.Text ?? "").Trim().Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return Math.Min(Math.Max(v, 5.0), 120.0) * 10.0;
            return Montagem.MM_LARGURA;
        }

        string Tabela() => string.Join("|", LarguraMm(), chkH.IsChecked == true, chkV.IsChecked == true, chkM.IsChecked == true, Math.Round(slPasso.Value, 1), cbTexto.SelectedIndex);

        /// <summary>Itens com código primeiro, na ordem do código; os sem código depois, na ordem atual.</summary>
        void OrdenarPorCodigo()
        {
            var rx = new System.Text.RegularExpressions.Regex(@"^(.*?)(\d+)$");
            var ord = linhas.Select((l, i) => new { l, i })
                .OrderBy(x => string.IsNullOrEmpty(x.l.It.Codigo) ? 1 : 0)
                .ThenBy(x => { var m = rx.Match(x.l.It.Codigo ?? ""); return m.Success ? m.Groups[1].Value : x.l.It.Codigo ?? ""; }, StringComparer.Ordinal)
                .ThenBy(x => { var m = rx.Match(x.l.It.Codigo ?? ""); return m.Success ? int.Parse(m.Groups[2].Value) : 0; })
                .ThenBy(x => x.i).Select(x => x.l).ToList();
            linhas.Clear();
            lista.Children.Clear();
            foreach (var l in ord) { linhas.Add(l); lista.Children.Add(l.Borda); }
        }

        void Mudou()
        {
            if (trocando) return;
            alterado = true;
            txtPasso.Text = Texto.Mm(slPasso.Value) + " mm";
            Pendente();
        }

        bool HaMudanca() =>
            linhas.Any(l => (int)Math.Round(l.Sl.Value) != (pctFeito.TryGetValue(l.Id, out int p) ? p : 100))
            || Tabela() != tabelaFeita || Ordem() != ordemFeita || Cores.Chave(CorAtual()) + modo != corFeita;

        void Pendente()
        {
            if (res != null && HaMudanca())
                txtStatus.Text = "Há ajustes que ainda não estão na prévia (caixas vermelhas = tamanho novo): clique em Atualizar prévia.";
        }

        // ------------------------------------------------------------ caixas vermelhas
        void DesenharCaixas()
        {
            if (res == null || tg == null) return;
            var alvos = linhas.Where(l => sujos.Contains(l.Id)).ToList();
            sujos = new HashSet<string>();
            if (alvos.Count == 0) return;
            // reaproveita as 4 linhas de cada caixa (só muda a geometria): menos trabalho para o Revit e
            // para a prévia, que redesenha uma vez por transação
            using (var t = ResolverFalhas.Iniciar(doc, "Prévia: caixas"))
            {
                try
                {
                    var apagar = new List<ElementId>();
                    foreach (var ln in alvos)
                    {
                        idsCaixa.TryGetValue(ln.Id, out var ids);
                        ids = (ids ?? new List<ElementId>()).Where(i => doc.GetElement(i) != null).ToList();
                        int v = (int)Math.Round(ln.Sl.Value), feito = pctFeito.TryGetValue(ln.Id, out int p) ? p : 100;
                        if (!caixas.TryGetValue(ln.Id, out var cx) || ln.Chk.IsChecked != true || v == feito)
                        {
                            apagar.AddRange(ids);
                            idsCaixa.Remove(ln.Id);
                            continue;
                        }
                        double f = (double)v / feito, hw = Math.Max(cx[2] * f, 0.01) / 2, hh = Math.Max(cx[3] * f, 0.01) / 2;
                        var pts = new[] { new XYZ(cx[0] - hw, cx[1] - hh, 0), new XYZ(cx[0] + hw, cx[1] - hh, 0), new XYZ(cx[0] + hw, cx[1] + hh, 0), new XYZ(cx[0] - hw, cx[1] + hh, 0) };
                        if (ids.Count == 4)
                        {
                            for (int i = 0; i < 4; i++)
                                ((DetailCurve)doc.GetElement(ids[i])).GeometryCurve = Line.CreateBound(pts[i], pts[(i + 1) % 4]);
                        }
                        else
                        {
                            apagar.AddRange(ids);
                            ids = new List<ElementId>();
                            for (int i = 0; i < 4; i++)
                            {
                                var c = doc.Create.NewDetailCurve(res.Vista, Line.CreateBound(pts[i], pts[(i + 1) % 4]));
                                Cores.Pintar(res.Vista, c.Id, COR_CAIXA);
                                ids.Add(c.Id);
                            }
                        }
                        idsCaixa[ln.Id] = ids;
                    }
                    if (apagar.Count > 0) doc.Delete(apagar);
                    t.Commit();
                }
                catch (Exception e)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    txtStatus.Text = "Não foi possível desenhar as caixas: " + e.Message;
                }
            }
        }

        void ApagarCaixas()
        {
            var todos = idsCaixa.Values.SelectMany(x => x).Where(i => doc.GetElement(i) != null).ToList();
            idsCaixa.Clear();
            if (todos.Count == 0) return;
            using (var t = new Transaction(doc, "Prévia: caixas")) { t.Start(); doc.Delete(todos); t.Commit(); }
        }

        // ------------------------------------------------------------ prévia
        void FecharPrevia()
        {
            if (prev != null)
            {
                try { hostPrev.Children.Clear(); prev.Dispose(); } catch { }
                prev = null;
            }
            txtPrevVazia.Visibility = System.Windows.Visibility.Visible;
        }

        void Desfazer()
        {
            timer?.Stop();
            FecharPrevia();
            idsCaixa.Clear();
            sujos = new HashSet<string>();
            if (tg != null)
            {
                try { if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack(); } catch { }
                tg.Dispose();
                tg = null;
            }
        }

        void Atualizar()
        {
            double tAt = Tempos.Agora;
            if (aberta) Tempos.Limpar();
            txtStatus.Text = "Gerando a prévia…";
            Desfazer();
            var escolhidos = new List<Item>();
            var tamanhos = new Dictionary<string, double>();
            var pcts = new Dictionary<string, int>();
            foreach (var ln in linhas)
            {
                if (ln.Chk.IsChecked != true) continue;
                var it = ln.It.Copia();
                string rot = (ln.Txt.Text ?? "").Trim();
                if (rot != "") it.Rotulo = rot.ToUpperInvariant();
                escolhidos.Add(it);
                int v = (int)Math.Round(ln.Sl.Value);
                pcts[ln.Id] = v;
                if (v != 100 && baseMm.TryGetValue(ln.Id, out var b))
                {
                    string fk = it.ChaveLeg;
                    if (!tamanhos.ContainsKey(fk)) tamanhos[fk] = Math.Max(b[0], b[1]) * v / 100.0;
                }
            }
            if (escolhidos.Count == 0) { res = null; txtStatus.Text = "Nenhum item marcado."; return; }
            tg = new TransactionGroup(doc, "Prévia " + D.Botao);
            tg.Start();
            ResultadoLegenda r;
            try
            {
                var op = OpcoesCor(new Opcoes
                {
                    Tamanhos = tamanhos, PassoMm = slPasso.Value, LarguraMm = LarguraMm(), TypeMark = cbTexto.SelectedIndex,
                    LinhasH = chkH.IsChecked == true, LinhaV = chkV.IsChecked == true, Margens = chkM.IsChecked == true,
                    Config = ConfigJson(),
                });
                r = montagem.Atualizar(escolhidos, op, legenda, nomeNova);
            }
            catch (Exception e)
            {
                Desfazer();
                res = null;
                txtStatus.Text = "Erro ao gerar a prévia: " + e.Message;
                return;
            }
            if (!r.Ok)
            {
                Desfazer();
                res = null;
                txtStatus.Text = r.Msg ?? "Não foi possível gerar a legenda.";
                return;
            }
            res = r;
            var porId = r.Itens.ToDictionary(it => it.Chave);
            caixas.Clear();
            foreach (var ln in linhas)
            {
                if (!porId.TryGetValue(ln.Id, out var it)) { ln.Med.Text = ln.It.Extra ? "acrescentado" : ""; continue; }
                var bb = it.SymIds.Count > 0 ? Simbolos.Caixa(doc, it.SymIds, r.Vista) : null;
                if (bb != null) caixas[ln.Id] = new[] { (bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2, bb[2] - bb[0], bb[3] - bb[1] };
                int v = pcts.TryGetValue(ln.Id, out int p) ? p : 100;
                if (it.Mm != null && (v == 100 || !baseMm.ContainsKey(ln.Id)))
                    baseMm[ln.Id] = new[] { it.Mm[0] * 100.0 / v, it.Mm[1] * 100.0 / v };
                ln.Med.Text = it.Mm != null ? Texto.Mm(it.Mm[0]) + " × " + Texto.Mm(it.Mm[1]) + " mm" : "sem símbolo";
                string modoTxt;
                switch (it.Modo)
                {
                    case "leg": modoTxt = "família (LEG)"; break;
                    case "simbolos": modoTxt = "anotações"; break;
                    case "planta": modoTxt = "desenho da planta"; break;
                    case "componente": modoTxt = "componente de legenda"; break;
                    case "extra": modoTxt = "símbolo acrescentado"; break;
                    default: modoTxt = it.Modo; break;
                }
                ln.Med.ToolTip = "Símbolo: " + modoTxt + (it.Redimensionado ? " · redimensionado pelo DWG" : "");
            }
            pctFeito = pcts;
            tabelaFeita = Tabela();
            ordemFeita = Ordem();
            corFeita = Cores.Chave(CorAtual()) + modo;
            try
            {
                double tp = Tempos.Agora;
                prev = new PreviewControl(doc, r.Vista.Id);
                hostPrev.Children.Add(prev);
                txtPrevVazia.Visibility = System.Windows.Visibility.Collapsed;
                Tempos.Marca("abrir a prévia do Revit", tp);
            }
            catch (Exception e)
            {
                txtStatus.Text = "Legenda gerada, mas a prévia não abriu: " + e.Message;
                return;
            }
            var partes = new List<string> { r.Itens.Count + " itens" };
            if (r.Colunas != null && r.Colunas[1] > 0) partes.Add($"2 colunas ({r.Colunas[0]} + {r.Colunas[1]})");
            int nRed = r.Itens.Count(it => it.Redimensionado);
            if (nRed > 0) partes.Add(nRed + " símbolo(s) redimensionado(s)");
            Tempos.Marca("PRÉVIA COMPLETA", tAt);
            bool primeira = !aberta;
            if (primeira) Tempos.Marca("TOTAL DA ABERTURA (desde o clique no botão)", inicioAbertura);
            aberta = true;
            string detalhe = GravarTempos(primeira ? "abertura" : "atualizar");
            double seg = primeira ? Tempos.Agora - inicioAbertura : Tempos.Agora - tAt;
            partes.Add((primeira ? "aberta em " : "atualizada em ") + Texto.Mm(seg) + " s");
            txtStatus.Text = "Prévia: " + string.Join(" · ", partes) + ". Use a roda do mouse para aproximar.";
            txtStatus.ToolTip = "Tempo de cada etapa (C#):\n" + detalhe;
            if (r.LegErros.Count > 0)
            {
                txtStatus.Text += "\n" + string.Join("\n", r.LegErros.Take(3));
                txtStatus.ToolTip = string.Join("\n", r.LegErros) + "\n\n" + txtStatus.ToolTip;
            }
        }

        string GravarTempos(string quando)
        {
            string txt = Tempos.Texto();
            try
            {
                string ext = AppDomain.CurrentDomain.GetData("FJA.Extensao") as string;
                if (ext != null)
                {
                    string pasta = Path.Combine(Path.GetDirectoryName(ext.TrimEnd('\\', '/')), "logs");
                    Directory.CreateDirectory(pasta);
                    string cab = $"\n=== {DateTime.Now:dd/MM/yyyy HH:mm:ss}  {Environment.MachineName}  {doc.Title}  ({quando}, C#)\n";
                    File.AppendAllText(Path.Combine(pasta, "previa-legenda-tempos.txt"), cab + txt + "\n", Encoding.UTF8);
                }
            }
            catch { }
            return txt;
        }

        void Gerar()
        {
            if (res == null || tg == null || HaMudanca())
            {
                Atualizar();
                if (res == null) return;
            }
            timer.Stop();
            FecharPrevia();
            RView salva = res.Vista;
            try
            {
                ApagarCaixas();
                tg.Assimilate();
                tg.Dispose();
                tg = null;
                Gravado = true;
                VistaGerada = salva;
            }
            catch (Exception e)
            {
                TaskDialog.Show(Title, "Não foi possível gravar: " + e.Message);
                Desfazer();
                return;
            }
            // continua na mesma legenda, agora gravada (a escala volta a valer 100% no tamanho novo)
            alterado = false;
            PreencherLegendas(montagem.ListarLegendas(), null);
            var li = lbLegendas.Items.Cast<ListBoxItem>().FirstOrDefault(x => x.Tag is RView v && v.Id == salva.Id);
            trocando = true;
            lbLegendas.SelectedItem = li;
            trocando = false;
            selAnterior = li?.Tag;
            Selecionar(salva, null);
            txtStatus.Text = "Legenda \"" + salva.Name + "\" gravada. " + txtStatus.Text;
        }
    }

    /// <summary>Lista com busca e seleção múltipla (para acrescentar símbolos).</summary>
    static class Escolha
    {
        /// <summary>Pede um nome (texto). Devolve null se cancelar.</summary>
        public static string Nome(Window dono, string titulo, string pergunta, string sugestao)
        {
            var w = new Window
            {
                Title = titulo, Width = 520, SizeToContent = SizeToContent.Height, Owner = dono, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, Background = dono.Background, FontFamily = dono.FontFamily, FontSize = dono.FontSize,
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = pergunta, Margin = new Thickness(0, 0, 0, 6) });
            var tb = new TextBox { Text = sugestao ?? "", Padding = new Thickness(4, 3, 4, 3) };
            sp.Children.Add(tb);
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "Criar", Padding = new Thickness(16, 5, 16, 5), IsDefault = true };
            var cancelar = new Button { Content = "Cancelar", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            if (dono.TryFindResource("Primario") is Style sp1) ok.Style = sp1;
            if (dono.TryFindResource("Discreto") is Style sd) { cancelar.Style = sd; cancelar.Width = double.NaN; cancelar.Padding = new Thickness(16, 5, 16, 5); }
            bts.Children.Add(cancelar); bts.Children.Add(ok);
            sp.Children.Add(bts);
            w.Content = sp;
            ok.Click += (s, a) => w.DialogResult = true;
            w.Loaded += (s, a) => { tb.Focus(); tb.SelectAll(); };
            return w.ShowDialog() == true ? tb.Text : null;
        }

        public static List<int> Mostrar(Window dono, string titulo, List<string> opcoes)
        {
            var w = new Window
            {
                Title = titulo, Width = 640, Height = 560, Owner = dono, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false, Background = dono.Background, FontFamily = dono.FontFamily, FontSize = dono.FontSize,
            };
            var dp = new DockPanel { Margin = new Thickness(14) };
            var busca = new TextBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 3, 4, 3), ToolTip = "Filtrar" };
            DockPanel.SetDock(busca, Dock.Top);
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(bts, Dock.Bottom);
            var ok = new Button { Content = "Acrescentar", Padding = new Thickness(16, 5, 16, 5), IsDefault = true };
            var cancelar = new Button { Content = "Cancelar", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            if (dono.TryFindResource("Primario") is Style sp) ok.Style = sp;
            if (dono.TryFindResource("Discreto") is Style sd) { cancelar.Style = sd; cancelar.Width = double.NaN; cancelar.Padding = new Thickness(16, 5, 16, 5); }
            bts.Children.Add(cancelar); bts.Children.Add(ok);
            var lb = new ListBox { SelectionMode = SelectionMode.Extended };
            Action filtrar = () =>
            {
                string f = Texto.Norm(busca.Text);
                lb.Items.Clear();
                for (int i = 0; i < opcoes.Count; i++)
                    if (f == "" || Texto.Norm(opcoes[i]).Contains(f)) lb.Items.Add(new ListBoxItem { Content = opcoes[i], Tag = i });
            };
            busca.TextChanged += (s, a) => filtrar();
            filtrar();
            dp.Children.Add(busca); dp.Children.Add(bts); dp.Children.Add(lb);
            w.Content = dp;
            List<int> r = null;
            ok.Click += (s, a) => { r = lb.SelectedItems.Cast<ListBoxItem>().Select(x => (int)x.Tag).ToList(); w.DialogResult = true; };
            lb.MouseDoubleClick += (s, a) => { if (lb.SelectedItem != null) { r = lb.SelectedItems.Cast<ListBoxItem>().Select(x => (int)x.Tag).ToList(); w.DialogResult = true; } };
            w.Loaded += (s, a) => busca.Focus();
            return w.ShowDialog() == true ? r : null;
        }
    }
}
