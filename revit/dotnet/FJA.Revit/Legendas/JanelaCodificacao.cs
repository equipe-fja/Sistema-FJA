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
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;
using WColor = System.Windows.Media.Color;
using WGrid = System.Windows.Controls.Grid;

namespace FJA.Revit.Legendas
{
    /// <summary>
    /// Codificação de pontos: disciplina, tabela de tipos (família : tipo, quantidade, origem, Type Mark atual,
    /// código, situação), numerar os sem código, editar, ordem por arrastar, aplicar no Type Mark.
    /// Montada em código (a DLL é carregada da memória).
    /// </summary>
    public sealed class JanelaCodificacao : Window
    {
        readonly Document doc;
        Disciplina D;
        Codificacao cod;
        Dictionary<string, string> reservas = new Dictionary<string, string>();
        bool trocando, alterado;
        public int Aplicados { get; private set; }

        sealed class Linha
        {
            public TipoPonto Tp;
            public Border Borda;
            public TextBox Cod, Desc;
            public TextBlock Sit;
        }

        readonly List<Linha> linhas = new List<Linha>();
        Linha arrastando;
        ComboBox cbDisc; StackPanel lista; TextBlock txtTitulo, txtStatus; Button btNumerar, btRenumerar, btAplicar, btFechar;

        static readonly Brush Verde = new SolidColorBrush(WColor.FromRgb(46, 115, 64)), Ambar = new SolidColorBrush(WColor.FromRgb(138, 93, 5)),
            Vermelho = new SolidColorBrush(WColor.FromRgb(179, 38, 30)), Azul = new SolidColorBrush(WColor.FromRgb(47, 95, 138)),
            Cinza = new SolidColorBrush(WColor.FromRgb(94, 94, 92));

        public JanelaCodificacao(Document doc, Disciplina inicial)
        {
            this.doc = doc;
            Title = "Codificação de pontos";
            Width = 1180; Height = 780; MinWidth = 900; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            AplicarEstilo();
            Montar();
            trocando = true;
            foreach (var d in Disciplina.Todas) cbDisc.Items.Add(d.Codigo + " · " + d.Titulo.Replace("LEGENDA DE ", "").ToLowerInvariant());
            cbDisc.SelectedIndex = Math.Max(0, Array.IndexOf(Disciplina.Todas, inicial));
            trocando = false;
            Carregar(Disciplina.Todas[cbDisc.SelectedIndex]);
            Closing += (s, a) =>
            {
                if (alterado && !Confirmar("Fechar sem aplicar os códigos alterados?")) a.Cancel = true;
            };
        }

        // ------------------------------------------------------------ estilo
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
                if (estilo == "Discreto") { b.Width = double.NaN; b.Padding = new Thickness(10, 4, 10, 4); b.Cursor = Cursors.Hand; }
            }
            return b;
        }

        bool Confirmar(string pergunta)
        {
            var td = new TaskDialog("Codificação") { MainInstruction = pergunta, CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, DefaultButton = TaskDialogResult.No };
            return td.Show() == TaskDialogResult.Yes;
        }

        static readonly double[] COLS = { 18, -1, 50, 140, 100, 104, 230, 170 };

        WGrid LinhaGrade()
        {
            var g = new WGrid { Margin = new Thickness(2, 1, 4, 1) };
            foreach (double w in COLS)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            return g;
        }

        void Montar()
        {
            var g = new WGrid { Margin = new Thickness(16) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topo = Cartao();
            topo.Padding = new Thickness(14, 10, 14, 10);
            topo.Margin = new Thickness(0, 0, 0, 12);
            var dt = new DockPanel();
            var esq = new StackPanel { Orientation = Orientation.Horizontal };
            esq.Children.Add(new TextBlock { Text = "Disciplina", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            cbDisc = new ComboBox { Width = 280, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(cbDisc);
            btNumerar = Botao("Numerar os sem código", "Discreto");
            btNumerar.Margin = new Thickness(16, 0, 0, 0);
            btNumerar.ToolTip = "Dá o próximo código livre, na ordem da lista, aos tipos sem código ou com código fora do padrão. Os demais ficam como estão.";
            btRenumerar = Botao("Renumerar tudo na ordem da lista", "Discreto");
            btRenumerar.Margin = new Thickness(8, 0, 0, 0);
            btRenumerar.ToolTip = "Refaz todos os códigos em sequência, na ordem atual da lista (arraste para mudar). Use com cuidado: muda os códigos já usados em tags e legendas.";
            esq.Children.Add(btNumerar); esq.Children.Add(btRenumerar);
            dt.Children.Add(esq);
            topo.Child = dt;
            g.Children.Add(topo);

            var c = Cartao();
            WGrid.SetRow(c, 1);
            var dp = new DockPanel();
            var cab = new Border { Padding = new Thickness(12, 8, 12, 8), BorderBrush = Pincel("Linha", 226, 226, 223), BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(cab, Dock.Top);
            var gc = LinhaGrade();
            string[] titulos = { "", "Família : tipo", "Qtde", "Origem", "Type Mark atual", "Código", "Description", "Situação" };
            for (int i = 0; i < titulos.Length; i++)
            {
                var tb = new TextBlock { Text = titulos[i], FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Pincel("Suave", 94, 94, 92), Margin = new Thickness(i == 5 || i == 6 ? 4 : 0, 0, 8, 0) };
                if (i == 6) tb.ToolTip = "Nome do tipo; se a família tiver um tipo só, o nome da família (sem o prefixo da disciplina). Pode ser editada.";
                if (i == 1) txtTitulo = tb;
                WGrid.SetColumn(tb, i);
                gc.Children.Add(tb);
            }
            cab.Child = gc;
            lista = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lista };
            dp.Children.Add(cab); dp.Children.Add(sv);
            c.Child = dp;
            g.Children.Add(c);

            var rod = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            WGrid.SetRow(rod, 2);
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(bts, Dock.Right);
            btFechar = Botao("Fechar", "Discreto");
            btFechar.Margin = new Thickness(0, 0, 8, 0);
            btAplicar = Botao("Aplicar", "Primario");
            btAplicar.ToolTip = "Grava o código no Type Mark e a Description de cada tipo alterado";
            bts.Children.Add(btFechar); bts.Children.Add(btAplicar);
            txtStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Cinza };
            rod.Children.Add(bts); rod.Children.Add(txtStatus);
            g.Children.Add(rod);
            Content = g;

            cbDisc.SelectionChanged += (s, a) =>
            {
                if (trocando || cbDisc.SelectedIndex < 0) return;
                var d = Disciplina.Todas[cbDisc.SelectedIndex];
                if (d == D) return;
                if (alterado && !Confirmar("Descartar os códigos alterados de " + D.Codigo + "?"))
                {
                    trocando = true; cbDisc.SelectedIndex = Array.IndexOf(Disciplina.Todas, D); trocando = false;
                    return;
                }
                Carregar(d);
            };
            btNumerar.Click += (s, a) => Numerar(false);
            btRenumerar.Click += (s, a) =>
            {
                if (Confirmar("Renumerar todos os tipos de " + D.Codigo + " na ordem da lista?\n\nCódigos já usados em tags e legendas vão mudar (só depois de Aplicar).")) Numerar(true);
            };
            btAplicar.Click += (s, a) => Aplicar();
            btFechar.Click += (s, a) => Close();
        }

        // ------------------------------------------------------------ dados
        void Carregar(Disciplina d)
        {
            D = d;
            cod = new Codificacao(doc, d);
            linhas.Clear();
            lista.Children.Clear();
            List<TipoPonto> tipos;
            try { tipos = cod.Tipos(); reservas = cod.Reservas(); }
            catch (Exception e) { txtStatus.Text = "Erro ao ler os tipos: " + e.Message; return; }
            trocando = true;
            foreach (var tp in tipos) CriarLinha(tp);
            trocando = false;
            alterado = false;
            txtTitulo.Text = $"Família : tipo ({tipos.Count})";
            Situacoes();
            if (tipos.Count == 0) txtStatus.Text = "Nenhum tipo de família '" + string.Join("' / '", d.Familias) + "' no modelo.";
            else
                txtStatus.Text = $"Padrão: {d.Codigo}.01, {d.Codigo}.02… gravado no Type Mark; a Description acompanha o nome do tipo (ou da família, se tiver um tipo só). Arraste pelo ≡ para mudar a ordem antes de numerar."
                    + (cod.LigadosAoSistema > 0 ? $" {cod.LigadosAoSistema} tipo(s) ligado(s) ao sistema ficaram de fora." : "");
        }

        void CriarLinha(TipoPonto tp)
        {
            var borda = new Border { BorderThickness = new Thickness(0, 2, 0, 2), BorderBrush = Brushes.Transparent, AllowDrop = true, Background = Brushes.Transparent };
            var g = LinhaGrade();
            var alca = new TextBlock { Text = "≡", FontSize = 16, Foreground = Cinza, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = "Arraste para mudar a ordem" };
            var nome = new TextBlock { Text = tp.Nome, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tp.Nome + "\n" + tp.Sym.Category?.Name };
            var qt = new TextBlock { Text = tp.Quantidade.ToString(), VerticalAlignment = VerticalAlignment.Center, Foreground = Cinza };
            var org = new TextBlock { Text = tp.Origem, VerticalAlignment = VerticalAlignment.Center, Foreground = Cinza, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tp.Origem, Margin = new Thickness(0, 0, 8, 0) };
            var at = new TextBlock { Text = tp.MarcaAtual == "" ? "—" : tp.MarcaAtual, VerticalAlignment = VerticalAlignment.Center, Foreground = Cinza };
            var cx = new TextBox { Text = tp.Codigo, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 10, 0), CharacterCasing = CharacterCasing.Upper };
            var ds = new TextBox { Text = tp.Desc, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 10, 0),
                ToolTip = "Description atual: " + (tp.DescAtual == "" ? "(vazia)" : tp.DescAtual) };
            var sit = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var els = new UIElement[] { alca, nome, qt, org, at, cx, ds, sit };
            for (int i = 0; i < els.Length; i++) { WGrid.SetColumn(els[i], i); g.Children.Add(els[i]); }
            borda.Child = g;
            var ln = new Linha { Tp = tp, Borda = borda, Cod = cx, Desc = ds, Sit = sit };
            ds.TextChanged += (s, a) => { ln.Tp.Desc = (ds.Text ?? "").Trim(); if (!trocando) { alterado = true; Situacoes(); } };
            cx.TextChanged += (s, a) => { ln.Tp.Codigo = (cx.Text ?? "").Trim(); if (!trocando) { alterado = true; Situacoes(); } };
            alca.PreviewMouseLeftButtonDown += (s, a) =>
            {
                arrastando = ln;
                try { DragDrop.DoDragDrop(ln.Borda, new DataObject("fjaTipo", tp.Sym.UniqueId), DragDropEffects.Move); }
                finally { arrastando = null; foreach (var x in linhas) Marca(x, null); }
            };
            borda.PreviewDragOver += (s, a) =>
            {
                a.Effects = arrastando == null ? DragDropEffects.None : DragDropEffects.Move;
                if (arrastando != null) Marca(ln, a.GetPosition(ln.Borda).Y > ln.Borda.ActualHeight / 2);
                a.Handled = true;
            };
            borda.DragLeave += (s, a) => Marca(ln, null);
            borda.PreviewDrop += (s, a) =>
            {
                a.Handled = true;
                Marca(ln, null);
                var orig = arrastando;
                if (orig == null || orig == ln) return;
                bool depois = a.GetPosition(ln.Borda).Y > ln.Borda.ActualHeight / 2;
                linhas.Remove(orig);
                int i = linhas.IndexOf(ln) + (depois ? 1 : 0);
                linhas.Insert(i, orig);
                lista.Children.Remove(orig.Borda);
                lista.Children.Insert(i, orig.Borda);
            };
            lista.Children.Add(borda);
            linhas.Add(ln);
        }

        static void Marca(Linha ln, bool? depois)
        {
            ln.Borda.BorderBrush = depois == null ? Brushes.Transparent : new SolidColorBrush(WColor.FromRgb(121, 86, 169));
            ln.Borda.BorderThickness = depois == null ? new Thickness(0, 2, 0, 2) : depois.Value ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
        }

        /// <summary>Situação de cada linha: sem código, fora do padrão, repetido, usado antes por tipo apagado,
        /// igual (já gravado) ou novo (muda ao aplicar).</summary>
        int Situacoes()
        {
            var rx = cod.Padrao;
            var cont = linhas.Where(l => l.Tp.Codigo != "").GroupBy(l => l.Tp.Codigo).ToDictionary(gr => gr.Key, gr => gr.Count());
            var naTabela = new HashSet<string>(linhas.Select(l => l.Tp.Sym.UniqueId));
            int problemas = 0;
            foreach (var l in linhas)
            {
                string c = l.Tp.Codigo, txt; Brush cor;
                if (c == "") { txt = "sem código"; cor = Cinza; }
                else if (cont[c] > 1) { txt = "repetido"; cor = Vermelho; problemas++; }
                else if (!rx.IsMatch(c)) { txt = "fora do padrão"; cor = Ambar; }
                else if (reservas.TryGetValue(c, out string uid) && uid != l.Tp.Sym.UniqueId && !naTabela.Contains(uid)) { txt = "já usado por um tipo apagado"; cor = Ambar; }
                else if (c == l.Tp.MarcaAtual) { txt = "gravado"; cor = Verde; }
                else { txt = l.Tp.MarcaAtual == "" ? "novo (ao aplicar)" : "muda ao aplicar"; cor = Azul; }
                if (l.Tp.Desc != l.Tp.DescAtual) txt += " · description muda";
                if (l.Tp.Quantidade == 0) txt += " · sem instância";
                l.Sit.Text = txt;
                l.Sit.ToolTip = txt;
                l.Sit.Foreground = cor;
                l.Cod.BorderBrush = cor == Vermelho ? Vermelho : Pincel("Linha", 226, 226, 223);
            }
            int pend = linhas.Count(l => l.Tp.Codigo != l.Tp.MarcaAtual || l.Tp.Desc != l.Tp.DescAtual);
            btAplicar.IsEnabled = pend > 0;
            btAplicar.Content = pend > 0 ? $"Aplicar ({pend} tipo(s))" : "Aplicar";
            return problemas;
        }

        HashSet<string> Usados(IEnumerable<Linha> ignorar)
        {
            var ign = new HashSet<Linha>(ignorar);
            var naTabela = new HashSet<string>(linhas.Select(l => l.Tp.Sym.UniqueId));
            var u = new HashSet<string>(linhas.Where(l => !ign.Contains(l)).Select(l => l.Tp.Codigo).Where(c => c != ""));
            foreach (var kv in reservas) if (!naTabela.Contains(kv.Value)) u.Add(kv.Key);   // de tipos apagados
            return u;
        }

        void Numerar(bool tudo)
        {
            var rx = cod.Padrao;
            var alvo = linhas.Where(l => tudo || l.Tp.Codigo == "" || !rx.IsMatch(l.Tp.Codigo)).ToList();
            if (alvo.Count == 0) { txtStatus.Text = "Todos os tipos já têm código no padrão."; return; }
            var usados = Usados(alvo);
            trocando = true;
            foreach (var l in alvo)
            {
                string c = cod.ProximoLivre(usados);
                usados.Add(c);
                l.Tp.Codigo = c;
                l.Cod.Text = c;
            }
            trocando = false;
            alterado = true;
            Situacoes();
            txtStatus.Text = $"{alvo.Count} código(s) proposto(s). Confira e clique em Aplicar.";
        }

        void Aplicar()
        {
            if (Situacoes() > 0) { txtStatus.Text = "Há códigos repetidos (em vermelho): corrija antes de aplicar."; return; }
            var rx = cod.Padrao;
            int fora = linhas.Count(l => l.Tp.Codigo != "" && !rx.IsMatch(l.Tp.Codigo) && l.Tp.Codigo != l.Tp.MarcaAtual);
            if (fora > 0 && !Confirmar(fora + " código(s) fora do padrão " + D.Codigo + ".NN. Aplicar assim mesmo?")) return;
            try
            {
                int n = cod.Aplicar(linhas.Select(l => l.Tp).ToList());
                Aplicados += n;
                reservas = cod.Reservas();
                alterado = false;
                foreach (var l in linhas)
                {
                    var g = (WGrid)l.Borda.Child;
                    ((TextBlock)g.Children[4]).Text = l.Tp.MarcaAtual == "" ? "—" : l.Tp.MarcaAtual;
                    l.Desc.ToolTip = "Description atual: " + (l.Tp.DescAtual == "" ? "(vazia)" : l.Tp.DescAtual);
                }
                Situacoes();
                txtStatus.Text = n + " parâmetro(s) gravado(s) em " + D.Codigo + " (Type Mark e Description). Os tags e a ferramenta Legendas já usam os códigos novos.";
            }
            catch (Exception e)
            {
                TaskDialog.Show("Codificação", "Não foi possível aplicar: " + e.Message);
            }
        }
    }
}
