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
    /// Codificação de pontos em árvore: famílias da disciplina e, dentro de cada uma, os tipos.
    /// Na família: renomear (a sigla fica). No tipo: Description (que também vira o nome do tipo) e código
    /// (Type Mark). Ao lado de cada campo, em cinza, como está hoje. Aplicar por tipo, por família ou tudo.
    /// Numerar segue a ordem da árvore (famílias e tipos podem ser arrastados).
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

        sealed class NoFamilia
        {
            public string Id;
            public string Prefixo, FamAtual;
            public readonly List<NoTipo> Tipos = new List<NoTipo>();
            public Border Bloco, Cab;
            public StackPanel Filhos;
            public TextBox Txt;
            public TextBlock AtFam, Resumo, Seta;
            public Button Ok;
            public string Fam => Tipos.Count > 0 ? Tipos[0].Tp.Fam : FamAtual;
            public bool Pendente => Tipos.Any(t => t.Tp.Pendente);
        }

        sealed class NoTipo
        {
            public TipoPonto Tp;
            public NoFamilia Pai;
            public Border Borda;
            public TextBox Desc, Cod;
            public TextBlock AtTipo, AtCod, Sit;
            public Button Ok, Apagar;
        }

        readonly List<NoFamilia> familias = new List<NoFamilia>();
        IEnumerable<NoTipo> Tipos => familias.SelectMany(f => f.Tipos);
        object arrastando;
        ComboBox cbDisc; StackPanel arvore; TextBlock txtStatus; Button btNumerar, btRenumerar, btAplicar, btFechar, btExpandir;

        static readonly Brush Verde = new SolidColorBrush(WColor.FromRgb(46, 115, 64)), Ambar = new SolidColorBrush(WColor.FromRgb(138, 93, 5)),
            Vermelho = new SolidColorBrush(WColor.FromRgb(179, 38, 30)), Azul = new SolidColorBrush(WColor.FromRgb(47, 95, 138)),
            Cinza = new SolidColorBrush(WColor.FromRgb(94, 94, 92)), Roxo = new SolidColorBrush(WColor.FromRgb(121, 86, 169));

        // colunas do tipo: recuo | ≡ | description | como está | código | como está | qtde | origem | situação | ✓ | ✕
        static readonly double[] COLS_T = { 30, 18, -1, -0.9, 96, 84, 44, 130, 200, 34, 32 };
        // colunas da família: ▾ | ≡ | sigla + nome | como está | resumo | ✓ | (vazio, alinhado ao ✕)
        static readonly double[] COLS_F = { 18, 18, -1, -1, 300, 34, 32 };

        public JanelaCodificacao(Document doc, Disciplina inicial)
        {
            this.doc = doc;
            Title = "Codificação de pontos";
            Width = 1400; Height = 820; MinWidth = 1050; MinHeight = 500;
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
                if (alterado && Tipos.Any(t => t.Tp.Pendente) && !Confirmar("Fechar sem aplicar as alterações?")) a.Cancel = true;
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

        static WGrid Grade(double[] cols)
        {
            var g = new WGrid();
            foreach (double w in cols)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(-w, GridUnitType.Star) : new GridLength(w) });
            return g;
        }

        static void Por(WGrid g, UIElement e, int col)
        {
            WGrid.SetColumn(e, col);
            g.Children.Add(e);
        }

        TextBox Campo(string texto, string dica) =>
            new TextBox { Text = texto, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 8, 0), ToolTip = dica };

        TextBlock Cinzento(string t, int tam = 12) =>
            new TextBlock { Text = t, Foreground = Cinza, FontSize = tam, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0), ToolTip = t };

        TextBlock Alca(string dica) =>
            new TextBlock { Text = "≡", FontSize = 16, Foreground = Cinza, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = dica };

        Button BotaoOk(string dica)
        {
            var ok = Botao("✓", "Discreto");
            ok.Padding = new Thickness(6, 2, 6, 2);
            ok.ToolTip = dica;
            return ok;
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
            var esq = new WrapPanel();
            esq.Children.Add(new TextBlock { Text = "Disciplina", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            cbDisc = new ComboBox { Width = 280, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(cbDisc);
            btNumerar = Botao("Numerar os sem código", "Discreto");
            btNumerar.Margin = new Thickness(16, 0, 0, 0);
            btNumerar.ToolTip = "Dá o próximo código livre, na ordem da árvore, aos tipos sem código ou com código fora do padrão. Os demais ficam como estão.";
            btRenumerar = Botao("Renumerar tudo na ordem da árvore", "Discreto");
            btRenumerar.Margin = new Thickness(8, 0, 0, 0);
            btRenumerar.ToolTip = "Refaz todos os códigos em sequência, na ordem atual (arraste famílias e tipos para mudar). Muda códigos já usados em tags e legendas.";
            btExpandir = Botao("Recolher tudo", "Discreto");
            btExpandir.Margin = new Thickness(8, 0, 0, 0);
            esq.Children.Add(btNumerar); esq.Children.Add(btRenumerar); esq.Children.Add(btExpandir);
            topo.Child = esq;
            g.Children.Add(topo);

            var c = Cartao();
            WGrid.SetRow(c, 1);
            var dp = new DockPanel();
            var cab = new Border { Padding = new Thickness(12, 8, 12, 8), BorderBrush = Pincel("Linha", 226, 226, 223), BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(cab, Dock.Top);
            var gc = Grade(COLS_T);
            string[] tit = { "", "", "Description (= nome do tipo)", "Como está", "Código", "Como está", "Qtde", "Origem", "Situação", "", "" };
            for (int i = 0; i < tit.Length; i++)
            {
                var tb = new TextBlock { Text = tit[i], FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = Pincel("Suave", 94, 94, 92), Margin = new Thickness(0, 0, 8, 0) };
                if (i == 2) tb.ToolTip = "Ao aplicar, a Description também vira o nome do tipo. A família é renomeada na linha dela (a sigla fica).";
                if (i == 4) tb.ToolTip = "Código no padrão SIGLA.NN, gravado no Type Mark";
                Por(gc, tb, i);
            }
            cab.Child = gc;
            arvore = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = arvore };
            dp.Children.Add(cab); dp.Children.Add(sv);
            c.Child = dp;
            g.Children.Add(c);

            var rod = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            WGrid.SetRow(rod, 2);
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(bts, Dock.Right);
            btFechar = Botao("Fechar", "Discreto");
            btFechar.Margin = new Thickness(0, 0, 8, 0);
            btAplicar = Botao("Aplicar tudo", "Primario");
            btAplicar.ToolTip = "Grava todas as alterações: nomes das famílias, Description e nome dos tipos, e códigos";
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
                if (alterado && Tipos.Any(t => t.Tp.Pendente) && !Confirmar("Descartar as alterações de " + D.Codigo + "?"))
                {
                    trocando = true; cbDisc.SelectedIndex = Array.IndexOf(Disciplina.Todas, D); trocando = false;
                    return;
                }
                Carregar(d);
            };
            btNumerar.Click += (s, a) => Numerar(false);
            btRenumerar.Click += (s, a) =>
            {
                if (Confirmar("Renumerar todos os tipos de " + D.Codigo + " na ordem da árvore?\n\nCódigos já usados em tags e legendas vão mudar (só depois de aplicar).")) Numerar(true);
            };
            btExpandir.Click += (s, a) =>
            {
                bool abrir = familias.All(f => f.Filhos.Visibility != System.Windows.Visibility.Visible);
                foreach (var f in familias) Abrir(f, abrir);
                btExpandir.Content = abrir ? "Recolher tudo" : "Expandir tudo";
            };
            btAplicar.Click += (s, a) => Aplicar(Tipos.ToList());
            btFechar.Click += (s, a) => Close();
        }

        // ------------------------------------------------------------ árvore
        void Carregar(Disciplina d)
        {
            D = d;
            cod = new Codificacao(doc, d);
            familias.Clear();
            arvore.Children.Clear();
            List<TipoPonto> tipos;
            try { tipos = cod.Tipos(); reservas = cod.Reservas(); }
            catch (Exception e) { txtStatus.Text = "Erro ao ler os tipos: " + e.Message; return; }
            trocando = true;
            // famílias na ordem do primeiro tipo de cada uma (os tipos já vêm na ordem do código)
            foreach (var grp in tipos.GroupBy(t => t.Sym.Family.Id.ToString()))
            {
                var nf = CriarFamilia(grp.First());
                foreach (var tp in grp) CriarTipo(nf, tp);
                AtualizarResumo(nf);
            }
            trocando = false;
            alterado = false;
            Situacoes();
            if (tipos.Count == 0) txtStatus.Text = "Nenhum tipo de família '" + string.Join("' / '", d.Familias) + "' no modelo.";
            else
                txtStatus.Text = $"{familias.Count} família(s), {tipos.Count} tipo(s). Renomeie a família na linha dela (a sigla fica); no tipo, a Description também vira o nome do tipo; código {d.Codigo}.01, {d.Codigo}.02… no Type Mark. ✓ aplica a linha."
                    + (cod.LigadosAoSistema > 0 ? $" {cod.LigadosAoSistema} tipo(s) ligado(s) ao sistema ficaram de fora." : "");
        }

        NoFamilia CriarFamilia(TipoPonto t0)
        {
            var nf = new NoFamilia { Id = t0.Sym.Family.Id.ToString(), Prefixo = t0.Prefixo, FamAtual = t0.FamAtual };
            var bloco = new Border { BorderBrush = Pincel("Linha", 226, 226, 223), BorderThickness = new Thickness(0, 0, 0, 1), AllowDrop = true, Background = Brushes.Transparent };
            var sp = new StackPanel();
            var cabB = new Border { BorderThickness = new Thickness(0, 2, 0, 2), BorderBrush = Brushes.Transparent, Padding = new Thickness(0, 6, 0, 6), Background = Brushes.Transparent };
            var g = Grade(COLS_F);
            var seta = new TextBlock { Text = "▾", Foreground = Cinza, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Mostrar/esconder os tipos" };
            var alca = Alca("Arraste para mudar a ordem das famílias");
            var pf = new DockPanel { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var sig = new TextBlock { Text = t0.Prefixo.Trim(), VerticalAlignment = VerticalAlignment.Center, Foreground = Roxo, Margin = new Thickness(2, 0, 6, 0), FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(sig, Dock.Left);
            var txt = Campo(t0.Fam, $"Nome da família (a sigla {t0.Prefixo.Trim()} fica). Categoria: {t0.Sym.Category?.Name}");
            txt.Margin = new Thickness(0);
            txt.FontWeight = FontWeights.SemiBold;
            pf.Children.Add(sig); pf.Children.Add(txt);
            var at = Cinzento(t0.Sym.FamilyName);
            var resumo = Cinzento("", 12);
            var ok = BotaoOk("Aplicar esta família (nome e todos os tipos dela)");
            Por(g, seta, 0); Por(g, alca, 1); Por(g, pf, 2); Por(g, at, 3); Por(g, resumo, 4); Por(g, ok, 5);
            cabB.Child = g;
            var filhos = new StackPanel();
            sp.Children.Add(cabB); sp.Children.Add(filhos);
            bloco.Child = sp;
            nf.Bloco = bloco; nf.Cab = cabB; nf.Filhos = filhos; nf.Txt = txt; nf.AtFam = at; nf.Resumo = resumo; nf.Seta = seta; nf.Ok = ok;

            txt.TextChanged += (s, a) =>
            {
                string v = (txt.Text ?? "").Trim();
                foreach (var t in nf.Tipos) t.Tp.Fam = v;
                if (trocando) return;
                alterado = true;
                Situacoes();
            };
            seta.MouseLeftButtonUp += (s, a) => Abrir(nf, nf.Filhos.Visibility != System.Windows.Visibility.Visible);
            ok.Click += (s, a) => Aplicar(nf.Tipos.ToList());
            alca.PreviewMouseLeftButtonDown += (s, a) => Arrastar(nf, nf.Bloco);
            bloco.PreviewDragOver += (s, a) =>
            {
                if (!(arrastando is NoFamilia)) return;
                a.Effects = DragDropEffects.Move;
                Marca(cabB, a.GetPosition(nf.Bloco).Y > nf.Bloco.ActualHeight / 2);
                a.Handled = true;
            };
            bloco.DragLeave += (s, a) => Marca(cabB, null);
            bloco.PreviewDrop += (s, a) =>
            {
                if (!(arrastando is NoFamilia orig)) return;
                a.Handled = true;
                Marca(cabB, null);
                if (orig == nf) return;
                bool depois = a.GetPosition(nf.Bloco).Y > nf.Bloco.ActualHeight / 2;
                familias.Remove(orig);
                int i = familias.IndexOf(nf) + (depois ? 1 : 0);
                familias.Insert(i, orig);
                arvore.Children.Remove(orig.Bloco);
                arvore.Children.Insert(i, orig.Bloco);
            };
            arvore.Children.Add(bloco);
            familias.Add(nf);
            return nf;
        }

        void Abrir(NoFamilia nf, bool abrir)
        {
            nf.Filhos.Visibility = abrir ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            nf.Seta.Text = abrir ? "▾" : "▸";
        }

        void AtualizarResumo(NoFamilia nf)
        {
            int inst = nf.Tipos.Sum(t => t.Tp.Quantidade);
            int sem = nf.Tipos.Count(t => t.Tp.Codigo == "");
            nf.Resumo.Text = $"{nf.Tipos.Count} tipo(s) · {inst} no modelo" + (sem > 0 ? $" · {sem} sem código" : "");
        }

        static string TextoTipoAtual(TipoPonto tp) =>
            tp.TipoAtual + (tp.DescAtual == "" ? "  ·  sem description" : tp.DescAtual != tp.TipoAtual ? "  ·  description: " + tp.DescAtual : "");

        void CriarTipo(NoFamilia nf, TipoPonto tp)
        {
            var borda = new Border { BorderThickness = new Thickness(0, 2, 0, 2), BorderBrush = Brushes.Transparent, AllowDrop = true, Background = Brushes.Transparent, Padding = new Thickness(0, 1, 0, 1) };
            var g = Grade(COLS_T);
            var guia = new Border { BorderBrush = Pincel("LinhaForte", 201, 201, 197), BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(8, -2, 0, -2) };
            var alca = Alca("Arraste para mudar a ordem dos tipos nesta família");
            var desc = Campo(tp.Desc, "Description; ao aplicar, também vira o nome do tipo");
            var atTipo = Cinzento(TextoTipoAtual(tp));
            var cx = Campo(tp.Codigo, "Código (Type Mark)");
            cx.CharacterCasing = CharacterCasing.Upper;
            var atCod = Cinzento(tp.MarcaAtual == "" ? "sem código" : tp.MarcaAtual);
            var qt = Cinzento(tp.Quantidade.ToString());
            var org = Cinzento(tp.Origem);
            var sit = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var ok = BotaoOk("Aplicar só este tipo");
            var del = Botao("✕", "Discreto");
            del.Padding = new Thickness(6, 2, 6, 2);
            del.Foreground = Vermelho;
            del.ToolTip = "Apagar este tipo do modelo" + (tp.Quantidade > 0 ? $" (e os {tp.Quantidade} elemento(s) dele)" : "");
            Por(g, guia, 0); Por(g, alca, 1); Por(g, desc, 2); Por(g, atTipo, 3); Por(g, cx, 4); Por(g, atCod, 5); Por(g, qt, 6); Por(g, org, 7); Por(g, sit, 8); Por(g, ok, 9); Por(g, del, 10);
            borda.Child = g;
            var nt = new NoTipo { Tp = tp, Pai = nf, Borda = borda, Desc = desc, Cod = cx, AtTipo = atTipo, AtCod = atCod, Sit = sit, Ok = ok, Apagar = del };
            del.Click += (s, a) => ApagarTipo(nt);
            desc.TextChanged += (s, a) => { tp.Desc = (desc.Text ?? "").Trim(); if (!trocando) { alterado = true; Situacoes(); } };
            cx.TextChanged += (s, a) => { tp.Codigo = (cx.Text ?? "").Trim(); if (!trocando) { alterado = true; Situacoes(); AtualizarResumo(nf); } };
            ok.Click += (s, a) => Aplicar(new List<NoTipo> { nt });
            alca.PreviewMouseLeftButtonDown += (s, a) => Arrastar(nt, borda);
            borda.PreviewDragOver += (s, a) =>
            {
                if (!(arrastando is NoTipo o) || o.Pai != nf) return;
                a.Effects = DragDropEffects.Move;
                Marca(borda, a.GetPosition(borda).Y > borda.ActualHeight / 2);
                a.Handled = true;
            };
            borda.DragLeave += (s, a) => Marca(borda, null);
            borda.PreviewDrop += (s, a) =>
            {
                if (!(arrastando is NoTipo orig) || orig.Pai != nf) return;
                a.Handled = true;
                Marca(borda, null);
                if (orig == nt) return;
                bool depois = a.GetPosition(borda).Y > borda.ActualHeight / 2;
                nf.Tipos.Remove(orig);
                int i = nf.Tipos.IndexOf(nt) + (depois ? 1 : 0);
                nf.Tipos.Insert(i, orig);
                nf.Filhos.Children.Remove(orig.Borda);
                nf.Filhos.Children.Insert(i, orig.Borda);
            };
            nf.Filhos.Children.Add(borda);
            nf.Tipos.Add(nt);
        }

        void Arrastar(object no, Border b)
        {
            arrastando = no;
            try { DragDrop.DoDragDrop(b, new DataObject("fjaCodificacao", "x"), DragDropEffects.Move); }
            finally
            {
                arrastando = null;
                foreach (var f in familias)
                {
                    Marca(f.Cab, null);
                    foreach (var t in f.Tipos) Marca(t.Borda, null);
                }
            }
        }

        static void Marca(Border b, bool? depois)
        {
            b.BorderBrush = depois == null ? Brushes.Transparent : Roxo;
            b.BorderThickness = depois == null ? new Thickness(0, 2, 0, 2) : depois.Value ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
        }

        // ------------------------------------------------------------ apagar
        void ApagarTipo(NoTipo nt)
        {
            var tp = nt.Tp;
            bool unico = nt.Pai.Tipos.Count <= 1;
            try { unico = tp.Sym.Family.GetFamilySymbolIds().Count <= 1; } catch { }
            string oQue = unico ? $"a família {tp.Sym.FamilyName} (é o único tipo dela)" : $"o tipo {tp.Sym.FamilyName} : {tp.Sym.Name}";
            var td = new TaskDialog("Codificação")
            {
                MainInstruction = "Apagar " + oQue + "?",
                MainContent = (tp.Quantidade > 0
                        ? $"Os {tp.Quantidade} elemento(s) desse tipo no modelo também serão apagados" + (tp.Origem.Contains("link") ? " (inclusive cópias do Copiar do link)" : "") + ".

"
                        : "Não há elementos desse tipo no modelo.

")
                    + (tp.MarcaAtual != "" ? $"O código {tp.MarcaAtual} continua reservado (não é reaproveitado). " : "")
                    + "Dá para desfazer com Ctrl+Z no Revit depois de fechar esta janela.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (td.Show() != TaskDialogResult.Yes) return;
            string nome = tp.Sym.FamilyName + " : " + tp.Sym.Name;
            try
            {
                int n = cod.Apagar(tp.Sym);
                var nf = nt.Pai;
                if (unico)
                {
                    familias.Remove(nf);
                    arvore.Children.Remove(nf.Bloco);
                }
                else
                {
                    nf.Tipos.Remove(nt);
                    nf.Filhos.Children.Remove(nt.Borda);
                    AtualizarResumo(nf);
                }
                reservas = cod.Reservas();
                Aplicados++;
                Situacoes();
                txtStatus.Text = $"Apagado: {nome}" + (unico ? " (família inteira)" : "") + $". {n} elemento(s) saíram do modelo.";
            }
            catch (Exception e)
            {
                TaskDialog.Show("Codificação", "Não foi possível apagar " + nome + ": " + e.Message);
            }
        }

        // ------------------------------------------------------------ situação, numerar, aplicar
        int Situacoes()
        {
            var rx = cod.Padrao;
            var todos = Tipos.ToList();
            var cont = todos.Where(t => t.Tp.Codigo != "").GroupBy(t => t.Tp.Codigo).ToDictionary(gr => gr.Key, gr => gr.Count());
            var naTabela = new HashSet<string>(todos.Select(t => t.Tp.Sym.UniqueId));
            var nomesRep = new HashSet<string>(todos.Where(t => t.Tp.Desc != "").GroupBy(t => t.Pai.Id + "|" + t.Tp.Desc).Where(gr => gr.Count() > 1).Select(gr => gr.Key));
            int problemas = 0;
            foreach (var t in todos)
            {
                var tp = t.Tp;
                string c = tp.Codigo, txt; Brush cor;
                if (c == "") { txt = "sem código"; cor = Cinza; }
                else if (cont[c] > 1) { txt = "código repetido"; cor = Vermelho; problemas++; }
                else if (!rx.IsMatch(c)) { txt = "fora do padrão"; cor = Ambar; }
                else if (reservas.TryGetValue(c, out string uid) && uid != tp.Sym.UniqueId && !naTabela.Contains(uid)) { txt = "código já usado por um tipo apagado"; cor = Ambar; }
                else { txt = c == tp.MarcaAtual ? "código gravado" : "código muda"; cor = c == tp.MarcaAtual ? Verde : Azul; }
                var muda = new List<string>();
                if (tp.Desc != tp.DescAtual) muda.Add("description");
                if (tp.Desc != "" && tp.Desc != tp.TipoAtual) muda.Add("nome do tipo");
                if (nomesRep.Contains(t.Pai.Id + "|" + tp.Desc)) { muda.Add("NOME DE TIPO REPETIDO"); cor = Vermelho; problemas++; }
                if (muda.Count > 0) { txt += " · " + string.Join(", ", muda); if (cor == Verde) cor = Azul; }
                if (tp.Erro != null) { txt = "erro: " + tp.Erro; cor = Vermelho; }
                t.Sit.Text = txt;
                t.Sit.ToolTip = txt;
                t.Sit.Foreground = cor;
                t.Ok.IsEnabled = tp.Pendente;
                t.Cod.BorderBrush = c != "" && cont.TryGetValue(c, out int nc) && nc > 1 ? Vermelho : Pincel("Linha", 226, 226, 223);
            }
            foreach (var f in familias)
            {
                bool nomeMuda = f.Fam != "" && f.Fam != f.FamAtual;
                f.AtFam.Text = f.Prefixo + f.FamAtual + (nomeMuda ? "  →  " + f.Prefixo + f.Fam : "");
                f.AtFam.Foreground = nomeMuda ? Azul : Cinza;
                f.AtFam.ToolTip = f.AtFam.Text;
                f.Ok.IsEnabled = f.Pendente;
            }
            int pend = todos.Count(t => t.Tp.Pendente);
            btAplicar.IsEnabled = pend > 0;
            btAplicar.Content = pend > 0 ? $"Aplicar tudo ({pend} tipo(s))" : "Aplicar tudo";
            return problemas;
        }

        HashSet<string> Usados(IEnumerable<NoTipo> ignorar)
        {
            var ign = new HashSet<NoTipo>(ignorar);
            var todos = Tipos.ToList();
            var naTabela = new HashSet<string>(todos.Select(t => t.Tp.Sym.UniqueId));
            var u = new HashSet<string>(todos.Where(t => !ign.Contains(t)).Select(t => t.Tp.Codigo).Where(c => c != ""));
            foreach (var kv in reservas) if (!naTabela.Contains(kv.Value)) u.Add(kv.Key);   // de tipos apagados
            return u;
        }

        void Numerar(bool tudo)
        {
            var rx = cod.Padrao;
            var alvo = Tipos.Where(t => tudo || t.Tp.Codigo == "" || !rx.IsMatch(t.Tp.Codigo)).ToList();
            if (alvo.Count == 0) { txtStatus.Text = "Todos os tipos já têm código no padrão."; return; }
            var usados = Usados(alvo);
            trocando = true;
            foreach (var t in alvo)
            {
                string c = cod.ProximoLivre(usados);
                usados.Add(c);
                t.Tp.Codigo = c;
                t.Cod.Text = c;
            }
            trocando = false;
            alterado = true;
            foreach (var f in familias) AtualizarResumo(f);
            Situacoes();
            txtStatus.Text = $"{alvo.Count} código(s) proposto(s), na ordem da árvore. Confira e aplique (✓ no tipo, ✓ na família ou Aplicar tudo).";
        }

        void Aplicar(List<NoTipo> quais)
        {
            var alvo = quais.Where(t => t.Tp.Pendente).ToList();
            if (alvo.Count == 0) return;
            Situacoes();
            var rx = cod.Padrao;
            var cont = Tipos.Where(t => t.Tp.Codigo != "").GroupBy(t => t.Tp.Codigo).ToDictionary(gr => gr.Key, gr => gr.Count());
            if (alvo.Any(t => t.Tp.Codigo != "" && cont[t.Tp.Codigo] > 1))
            {
                txtStatus.Text = "Há códigos repetidos (em vermelho): corrija antes de aplicar.";
                return;
            }
            int fora = alvo.Count(t => t.Tp.Codigo != "" && !rx.IsMatch(t.Tp.Codigo) && t.Tp.Codigo != t.Tp.MarcaAtual);
            if (fora > 0 && !Confirmar(fora + " código(s) fora do padrão " + D.Codigo + ".NN. Aplicar assim mesmo?")) return;
            try
            {
                int n = cod.Aplicar(alvo.Select(t => t.Tp).ToList());
                Aplicados += n;
                reservas = cod.Reservas();
                // relê o que está gravado (família renomeada vale para todos os tipos dela)
                foreach (var f in familias)
                {
                    foreach (var t in f.Tipos)
                    {
                        var tp = t.Tp;
                        var mp = System.Text.RegularExpressions.Regex.Match(tp.Sym.FamilyName, @"^\s*(\([A-Za-z]+\)\s*)(.*)$");
                        tp.FamAtual = mp.Success ? mp.Groups[2].Value : tp.Sym.FamilyName;
                        tp.TipoAtual = tp.Sym.Name;
                        t.AtTipo.Text = TextoTipoAtual(tp);
                        t.AtTipo.ToolTip = t.AtTipo.Text;
                        t.AtCod.Text = tp.MarcaAtual == "" ? "sem código" : tp.MarcaAtual;
                    }
                    if (f.Tipos.Count > 0) f.FamAtual = f.Tipos[0].Tp.FamAtual;
                    AtualizarResumo(f);
                }
                alterado = Tipos.Any(t => t.Tp.Pendente);
                Situacoes();
                int erros = alvo.Count(t => t.Tp.Erro != null);
                txtStatus.Text = n + " tipo(s) atualizado(s) em " + D.Codigo + "."
                    + (erros > 0 ? " " + erros + " com erro (em vermelho; passe o mouse para ver)." : "");
            }
            catch (Exception e)
            {
                TaskDialog.Show("Codificação", "Não foi possível aplicar: " + e.Message);
            }
        }
    }
}
