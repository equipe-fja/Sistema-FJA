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
using Autodesk.Revit.DB;
using WColor = System.Windows.Media.Color;
using WGrid = System.Windows.Controls.Grid;
using WPoint = System.Windows.Point;
using WVisibility = System.Windows.Visibility;
using WLine = System.Windows.Shapes.Line;
using WEllipse = System.Windows.Shapes.Ellipse;
using WPath = System.Windows.Shapes.Path;
using Polyline = System.Windows.Shapes.Polyline;

namespace FJA.Revit.Detalhes
{
    public enum Saida { Cancelar, Criar, EscolherPonto }

    /// <summary>
    /// Janela da ferramenta Família do desenho: prévia do desenho selecionado (com o ponto de inserção) e as
    /// opções da família. Montada em código (a DLL é carregada da memória), estilo de lib/fja_estilo.xaml.
    /// </summary>
    public sealed class JanelaDesenho : Window
    {
        readonly Desenho d;
        readonly OpcoesFamilia o;
        public Saida Resultado { get; private set; } = Saida.Cancelar;

        Canvas tela;
        ComboBox cbTipo, cbOrigem, cbCores, cbAjuste;
        TextBox txNome, txPasta;
        CheckBox ckSubst, ckFora;
        TextBlock txInfo, txTamanho, txErro;
        StackPanel pnPasta, pnAvisos, pnAjuste;
        Button btCriar;
        bool montando = true;

        public JanelaDesenho(Desenho d, OpcoesFamilia o)
        {
            this.d = d; this.o = o;
            Title = "Família do desenho";
            Width = 1000; Height = 660; MinWidth = 820; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            AplicarEstilo();
            Montar();
            montando = false;
            Atualizar();
            Loaded += (s, a) => { txNome.Focus(); txNome.SelectAll(); };
            PreviewKeyDown += (s, a) => { if (a.Key == Key.Escape) Close(); };
        }

        // ------------------------------------------------------------ estilo
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
            if (estilo != null && Rec(estilo) is Style st)
            {
                b.Style = st;
                if (estilo == "Discreto") { b.Width = double.NaN; b.Padding = new Thickness(10, 4, 10, 4); }
            }
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

        // ------------------------------------------------------------ montagem
        void Montar()
        {
            var raiz = new WGrid { Margin = new Thickness(16) };
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = raiz;

            // prévia
            var cPrev = Cartao();
            var dpPrev = new DockPanel { Margin = new Thickness(12) };
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            txTamanho = new TextBlock { Foreground = Pincel("Suave", 94, 94, 92), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(txTamanho, Dock.Right);
            cab.Children.Add(txTamanho);
            cab.Children.Add(new TextBlock { Text = "Prévia", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(cab, Dock.Top);
            dpPrev.Children.Add(cab);
            txInfo = new TextBlock { Foreground = Pincel("Suave", 94, 94, 92), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(txInfo, Dock.Bottom);
            dpPrev.Children.Add(txInfo);
            tela = new Canvas { ClipToBounds = true, Background = Brushes.White };
            tela.SizeChanged += (s, a) => Desenhar();
            dpPrev.Children.Add(tela);
            cPrev.Child = dpPrev;
            WGrid.SetColumn(cPrev, 0);
            raiz.Children.Add(cPrev);

            // opções
            var cOp = Cartao();
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var pn = new StackPanel { Margin = new Thickness(14, 2, 14, 14) };
            sv.Content = pn;
            cOp.Child = sv;
            WGrid.SetColumn(cOp, 2);
            raiz.Children.Add(cOp);

            pn.Children.Add(Rotulo("Tipo de família"));
            cbTipo = new ComboBox();
            cbTipo.Items.Add("Item de detalhe (tamanho real)");
            cbTipo.Items.Add("Anotação genérica (tamanho do papel)");
            cbTipo.SelectedIndex = o.Tipo == TipoFamilia.Anotacao ? 1 : 0;
            cbTipo.SelectionChanged += (s, a) => Atualizar();
            pn.Children.Add(cbTipo);

            pnAjuste = new StackPanel();
            pnAjuste.Children.Add(Rotulo("Espessura na anotação"));
            cbAjuste = new ComboBox();
            cbAjuste.Items.Add("+0 (mesma pena do original)");
            for (int i = 1; i <= 6; i++) cbAjuste.Items.Add("+" + i + (i == 1 ? " pena" : " penas"));
            cbAjuste.SelectedIndex = Math.Max(0, Math.Min(6, o.AjustePena));
            cbAjuste.SelectionChanged += (s, a) => Atualizar();
            pnAjuste.Children.Add(cbAjuste);
            pnAjuste.Children.Add(Nota("A anotação usa a tabela de espessuras de anotação, e as linhas de detalhe a de modelo (que muda com a escala). Aumente até ficar igual ao original; o valor fica guardado."));
            pn.Children.Add(pnAjuste);

            pn.Children.Add(Rotulo("Nome da família"));
            txNome = new TextBox { Text = o.Nome ?? "", Padding = new Thickness(4, 3, 4, 3) };
            txNome.TextChanged += (s, a) => Atualizar();
            pn.Children.Add(txNome);
            pn.Children.Add(Nota("A sigla da disciplina é opcional, ex.: (LOG) CAIXA DE PASSAGEM."));

            pn.Children.Add(Rotulo("Ponto de inserção"));
            cbOrigem = new ComboBox();
            cbOrigem.Items.Add("Centro do desenho");
            cbOrigem.Items.Add("Canto inferior esquerdo");
            cbOrigem.Items.Add("Ponto escolhido no modelo");
            cbOrigem.SelectedIndex = (int)o.Origem;
            cbOrigem.SelectionChanged += (s, a) =>
            {
                if (montando) return;
                if (cbOrigem.SelectedIndex == 2 && o.PontoEscolhido == null) { Escolher(); return; }
                Atualizar();
            };
            pn.Children.Add(cbOrigem);
            var btPonto = Botao("Escolher no modelo…", "Discreto");
            btPonto.HorizontalAlignment = HorizontalAlignment.Left;
            btPonto.Margin = new Thickness(-10, 2, 0, 0);
            btPonto.Click += (s, a) => Escolher();
            pn.Children.Add(btPonto);

            pn.Children.Add(Rotulo("Cores"));
            cbCores = new ComboBox();
            cbCores.Items.Add("Como aparecem na vista (com sobreposições)");
            cbCores.Items.Add("Estilos originais (sem sobreposições)");
            cbCores.Items.Add("Tudo preto (mantém espessuras)");
            cbCores.SelectedIndex = o.Preto ? 2 : o.SemSobreposicao ? 1 : 0;
            cbCores.SelectionChanged += (s, a) => Atualizar();
            pn.Children.Add(cbCores);

            pn.Children.Add(Rotulo("Depois de criar"));
            ckSubst = new CheckBox { Content = "Substituir os elementos originais pela família", IsChecked = o.Substituir, Margin = new Thickness(0, 2, 0, 0) };
            ckSubst.Click += (s, a) => Atualizar();
            pn.Children.Add(ckSubst);
            pn.Children.Add(Nota("Os originais são apagados e a família entra no mesmo lugar (Ctrl+Z desfaz). Sem esta opção, você posiciona a família em seguida."));

            ckFora = new CheckBox { Content = "Salvar também o .rfa numa pasta", IsChecked = o.SalvarFora, Margin = new Thickness(0, 12, 0, 0) };
            ckFora.Click += (s, a) => { if (ckFora.IsChecked == true && string.IsNullOrWhiteSpace(txPasta.Text)) EscolherPasta(); Atualizar(); };
            pn.Children.Add(ckFora);
            pnPasta = new StackPanel { Margin = new Thickness(22, 4, 0, 0) };
            txPasta = new TextBox { Text = o.Pasta ?? "", Padding = new Thickness(4, 3, 4, 3), FontSize = 12 };
            txPasta.TextChanged += (s, a) => Atualizar();
            pnPasta.Children.Add(txPasta);
            var btPasta = Botao("Escolher pasta…", "Discreto");
            btPasta.HorizontalAlignment = HorizontalAlignment.Left;
            btPasta.Margin = new Thickness(-10, 2, 0, 0);
            btPasta.Click += (s, a) => { EscolherPasta(); Atualizar(); };
            pnPasta.Children.Add(btPasta);
            pn.Children.Add(pnPasta);

            pnAvisos = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            pn.Children.Add(pnAvisos);

            // rodapé
            var rod = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
            WGrid.SetRow(rod, 1); WGrid.SetColumnSpan(rod, 3);
            raiz.Children.Add(rod);
            var botoes = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(botoes, Dock.Right);
            var btCancelar = Botao("Cancelar");
            btCancelar.Margin = new Thickness(0, 0, 8, 0);
            btCancelar.Click += (s, a) => Close();
            btCriar = Botao("Criar", "Primario");
            btCriar.MinWidth = 150;
            btCriar.IsDefault = true;
            btCriar.Click += (s, a) => Criar();
            botoes.Children.Add(btCancelar);
            botoes.Children.Add(btCriar);
            rod.Children.Add(botoes);
            txErro = new TextBlock { Foreground = new SolidColorBrush(WColor.FromRgb(179, 38, 30)), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            rod.Children.Add(txErro);
        }

        void Escolher()
        {
            Ler();
            Resultado = Saida.EscolherPonto;
            Close();
        }

        void EscolherPasta()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Pasta para salvar o .rfa", ShowNewFolderButton = true })
            {
                if (Directory.Exists(txPasta.Text)) dlg.SelectedPath = txPasta.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) txPasta.Text = dlg.SelectedPath;
            }
            if (string.IsNullOrWhiteSpace(txPasta.Text)) ckFora.IsChecked = false;
        }

        void Ler()
        {
            o.Tipo = cbTipo.SelectedIndex == 1 ? TipoFamilia.Anotacao : TipoFamilia.Detalhe;
            o.Nome = txNome.Text ?? "";
            o.Origem = (OrigemFamilia)Math.Max(0, cbOrigem.SelectedIndex);
            if (o.Origem == OrigemFamilia.Escolhida && o.PontoEscolhido == null) o.Origem = OrigemFamilia.Centro;
            o.Preto = cbCores.SelectedIndex == 2;
            o.SemSobreposicao = cbCores.SelectedIndex == 1;
            o.AjustePena = Math.Max(0, cbAjuste.SelectedIndex);
            o.Substituir = ckSubst.IsChecked == true;
            o.SalvarFora = ckFora.IsChecked == true;
            o.Pasta = txPasta.Text ?? "";
        }

        string Validar()
        {
            string nome = (txNome.Text ?? "").Trim();
            if (nome.Length == 0) return "Dê um nome à família.";
            if (Criacao.NomeValido(nome) != nome) return "O nome não pode ter \\ : { } [ ] | ; < > ? ` ~ / * \"";
            if (ckFora.IsChecked == true && !Directory.Exists(txPasta.Text ?? "")) return "Escolha uma pasta que exista para salvar o .rfa.";
            if (cbTipo.SelectedIndex == 1 && d.Pecas.Any(p => p.Tipo == TipoPeca.Componente
                && (d.Doc.GetElement(p.Origem) as FamilyInstance)?.Category?.BuiltInCategory == BuiltInCategory.OST_DetailComponents))
                return "Componentes de detalhe não cabem numa anotação genérica: use item de detalhe ou tire-os da seleção.";
            return null;
        }

        void Criar()
        {
            string erro = Validar();
            if (erro != null) { txErro.Text = erro; return; }
            Ler();
            o.Nome = o.Nome.Trim();
            Resultado = Saida.Criar;
            Close();
        }

        static string Mm(double pes) => (pes * 304.8).ToString(pes * 304.8 >= 100 ? "0" : "0.0");

        void Atualizar()
        {
            if (montando) return;
            Ler();
            pnPasta.Visibility = ckFora.IsChecked == true ? WVisibility.Visible : WVisibility.Collapsed;
            btCriar.Content = ckSubst.IsChecked == true ? "Criar e substituir" : "Criar e posicionar";
            txErro.Text = Validar() is string e && (txNome.Text ?? "").Trim().Length > 0 ? e : "";
            btCriar.IsEnabled = Validar() == null;

            bool anot = o.Tipo == TipoFamilia.Anotacao;
            pnAjuste.Visibility = anot ? WVisibility.Visible : WVisibility.Collapsed;
            double k = anot ? 1.0 / d.Escala : 1.0;
            txTamanho.Text = (anot ? "No papel: " : "Tamanho real: ") + Mm(d.Largura * k) + " × " + Mm(d.Altura * k) + " mm";
            txInfo.Text = "Vista: " + d.Vista.Name + " (1:" + d.Escala + ") · " + d.Resumo() + "."
                + (anot ? " Na anotação, as medidas são divididas pela escala da vista." : "");

            pnAvisos.Children.Clear();
            var avisos = new List<string>(d.Avisos);
            if (o.Preto && d.Conta(TipoPeca.Componente) > 0) avisos.Add("Componentes aninhados mantêm as cores das próprias famílias.");
            if (!o.Preto && !o.SemSobreposicao && d.Sobrepostos > 0)
                avisos.Add("Há cor, espessura ou padrão vindos de sobreposição ou filtro da vista: a família leva o que aparece, com estilos no formato \"PENA 2 VERDE 140-195-25 SOLID\".");
            if (avisos.Count > 0)
            {
                pnAvisos.Children.Add(new TextBlock { Text = "Avisos", FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) });
                foreach (var a in avisos) pnAvisos.Children.Add(Nota("• " + a));
            }
            Desenhar();
        }

        // ------------------------------------------------------------ prévia
        Brush Cor(byte r, byte g, byte b, byte alfa = 255) =>
            o.Preto ? new SolidColorBrush(WColor.FromArgb(alfa, 0, 0, 0)) : new SolidColorBrush(WColor.FromArgb(alfa, r, g, b));

        void Desenhar()
        {
            if (tela == null) return;
            tela.Children.Clear();
            double W = tela.ActualWidth, H = tela.ActualHeight;
            if (W < 20 || H < 20 || d.Vazio) return;
            XYZ org = o.PontoDeOrigem(d);
            double x0 = Math.Min(d.MinX, org.X), y0 = Math.Min(d.MinY, org.Y), x1 = Math.Max(d.MaxX, org.X), y1 = Math.Max(d.MaxY, org.Y);
            double lw = Math.Max(x1 - x0, 1e-6), lh = Math.Max(y1 - y0, 1e-6);
            const double margem = 24;
            double esc = Math.Min((W - 2 * margem) / lw, (H - 2 * margem) / lh);
            double ox = (W - lw * esc) / 2, oy = (H - lh * esc) / 2;
            Func<XYZ, WPoint> P = p => new WPoint(ox + (p.X - x0) * esc, H - (oy + (p.Y - y0) * esc));

            // regiões primeiro (ficam por baixo)
            foreach (var p in d.Pecas.Where(x => x.Tipo == TipoPeca.Regiao))
            {
                var geo = new PathGeometry { FillRule = FillRule.EvenOdd };
                foreach (var t in p.Tracos)
                {
                    if (t.Count < 3) continue;
                    var fig = new PathFigure { StartPoint = P(t[0]), IsClosed = true };
                    fig.Segments.Add(new PolyLineSegment(t.Skip(1).Select(P), true));
                    geo.Figures.Add(fig);
                }
                byte[] c = (o.SemSobreposicao ? p.CorOriginal : p.CorRegiao) ?? new byte[] { 0, 0, 0 };
                var path = new WPath { Data = geo };
                if (p.Mascara) { path.Fill = Brushes.White; path.Stroke = new SolidColorBrush(WColor.FromRgb(200, 200, 200)); path.StrokeDashArray = new DoubleCollection { 3, 3 }; path.StrokeThickness = 0.8; }
                else path.Fill = Cor(c[0], c[1], c[2], (byte)(p.Solida ? 255 : 70));
                tela.Children.Add(path);
            }
            foreach (var p in d.Pecas.Where(x => x.Tipo == TipoPeca.Regiao && Est(x) != null && !Est(x).Invisivel))
                foreach (var t in p.Tracos) Linha(t.Concat(t.Take(1)).ToList(), Est(p), P, esc);
            foreach (var p in d.Pecas.Where(x => x.Tipo == TipoPeca.Curva))
                foreach (var t in p.Tracos) Linha(t, Est(p), P, esc);
            foreach (var p in d.Pecas.Where(x => x.Tipo == TipoPeca.Componente))
                foreach (var t in p.Tracos)
                {
                    if (t.Count < 2) continue;
                    tela.Children.Add(new Polyline { Points = new PointCollection(t.Select(P)), Stroke = Cor(60, 60, 60), StrokeThickness = 1 });
                }
            foreach (var p in d.Pecas.Where(x => x.Tipo == TipoPeca.Texto))
            {
                double fs = Math.Max(4, p.AlturaTexto * esc * 1.35);
                var tb = new TextBlock { Text = (p.Texto ?? "").TrimEnd('\r', '\n'), FontSize = fs, Foreground = Cor(30, 30, 30), FontFamily = new FontFamily("Arial") };
                var pt = P(p.Ponto);
                Canvas.SetLeft(tb, pt.X); Canvas.SetTop(tb, pt.Y);
                if (Math.Abs(p.Angulo) > 1e-3) tb.RenderTransform = new RotateTransform(-p.Angulo * 180 / Math.PI);
                tela.Children.Add(tb);
            }

            // ponto de inserção
            var po = P(org);
            var verm = new SolidColorBrush(WColor.FromRgb(179, 38, 30));
            tela.Children.Add(new WLine { X1 = po.X - 9, Y1 = po.Y, X2 = po.X + 9, Y2 = po.Y, Stroke = verm, StrokeThickness = 1.4 });
            tela.Children.Add(new WLine { X1 = po.X, Y1 = po.Y - 9, X2 = po.X, Y2 = po.Y + 9, Stroke = verm, StrokeThickness = 1.4 });
            var circ = new WEllipse { Width = 8, Height = 8, Stroke = verm, StrokeThickness = 1.4 };
            Canvas.SetLeft(circ, po.X - 4); Canvas.SetTop(circ, po.Y - 4);
            tela.Children.Add(circ);
        }

        EstiloLinha Est(Peca p) => o.SemSobreposicao ? (p.EstiloOriginal ?? p.Estilo) : p.Estilo;

        void Linha(List<XYZ> t, EstiloLinha es, Func<XYZ, WPoint> P, double esc)
        {
            if (t == null || t.Count < 2) return;
            if (es != null && es.Invisivel) return;
            var pl = new Polyline
            {
                Points = new PointCollection(t.Select(P)),
                Stroke = es != null ? Cor(es.R, es.G, es.B) : Cor(0, 0, 0),
                StrokeThickness = 0.6 + 0.35 * Math.Max(0, (es?.Peso ?? 1) - 1),
                StrokeLineJoin = PenLineJoin.Round,
            };
            if (es != null && es.Tracejado) pl.StrokeDashArray = new DoubleCollection { 5, 3 };
            tela.Children.Add(pl);
        }
    }
}
