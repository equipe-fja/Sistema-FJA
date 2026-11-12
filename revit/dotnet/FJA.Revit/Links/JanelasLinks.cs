using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Autodesk.Revit.DB;
using UIDocument = Autodesk.Revit.UI.UIDocument;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;
using WColor = System.Windows.Media.Color;
using WGrid = System.Windows.Controls.Grid;
using WBinding = System.Windows.Data.Binding;

namespace FJA.Revit.Links
{
    /// <summary>Base das janelas de links: estilo da suíte (lib/fja_estilo.xaml), botões, cartões e lista.</summary>
    public abstract class JanelaLinksBase : Window
    {
        protected static readonly Brush Verde = Pinc(46, 115, 64), Ambar = Pinc(138, 93, 5), Vermelho = Pinc(179, 38, 30),
            Azul = Pinc(47, 95, 138), Cinza = Pinc(94, 94, 92);

        static Brush Pinc(byte r, byte g, byte b) { var p = new SolidColorBrush(WColor.FromRgb(r, g, b)); p.Freeze(); return p; }

        protected void AplicarEstilo()
        {
            try
            {
                string ext = AppDomain.CurrentDomain.GetData("FJA.Extensao") as string;
                string arq = ext != null ? Path.Combine(ext, "lib", "fja_estilo.xaml") : null;
                if (arq != null && File.Exists(arq))
                    Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(arq, Encoding.UTF8)));
            }
            catch { }
            Background = TryFindResource("Fundo") as Brush ?? new SolidColorBrush(WColor.FromRgb(246, 246, 245));
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
        }

        protected Button Botao(string texto, string estilo = null)
        {
            var b = new Button { Content = texto, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, MinHeight = 28 };
            if (estilo != null && TryFindResource(estilo) is Style st) { b.Style = st; b.Width = double.NaN; }
            return b;
        }

        protected Border Cartao(UIElement filho)
        {
            var b = new Border { Child = filho, Padding = new Thickness(6) };
            if (TryFindResource("Cartao") is Style st) b.Style = st;
            else { b.Background = Brushes.White; b.BorderBrush = Pinc(226, 226, 223); b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(6); }
            return b;
        }

        protected static StackPanel Rotulado(string rotulo, FrameworkElement c)
        {
            var sp = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = rotulo, Foreground = Pinc(94, 94, 92), FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
            sp.Children.Add(c);
            return sp;
        }

        protected Border Cabecalho(string titulo, out TextBlock info, params UIElement[] direita)
        {
            var g = new WGrid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (var _ in direita) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fj = new Border { Width = 34, Height = 34, BorderBrush = Pinc(201, 201, 197), BorderThickness = new Thickness(1), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            fj.Child = new TextBlock { Text = "FJ", FontFamily = new FontFamily("Arial"), FontWeight = FontWeights.Bold, FontSize = 15, Foreground = Pinc(142, 27, 27), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(fj);
            var sp = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = titulo, FontSize = 18, FontWeight = FontWeights.SemiBold });
            info = new TextBlock { Foreground = Pinc(94, 94, 92), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
            sp.Children.Add(info);
            WGrid.SetColumn(sp, 1); g.Children.Add(sp);
            for (int i = 0; i < direita.Length; i++) { WGrid.SetColumn(direita[i], 2 + i); g.Children.Add(direita[i]); }
            return new Border { Background = Brushes.White, BorderBrush = Pinc(226, 226, 223), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 12, 18, 12), Child = g };
        }

        protected static DataGrid Lista()
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = Pinc(237, 237, 235), BorderThickness = new Thickness(0), Background = Brushes.White,
                RowBackground = Brushes.White, AlternatingRowBackground = Brushes.White, CanUserAddRows = false, CanUserDeleteRows = false,
                CanUserResizeRows = false, RowHeight = 26, EnableRowVirtualization = true,
            };
            VirtualizingPanel.SetIsVirtualizing(dg, true);
            return dg;
        }

        protected static void Coluna(DataGrid dg, string titulo, string prop, double largura, bool estrela = false)
        {
            dg.Columns.Add(new DataGridTextColumn
            {
                Header = titulo, Binding = new WBinding(prop),
                Width = estrela ? new DataGridLength(largura, DataGridLengthUnitType.Star) : new DataGridLength(largura),
            });
        }

        protected static void ColunaSituacao(DataGrid dg, string titulo, double largura)
        {
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new WBinding("Situacao"));
            f.SetBinding(TextBlock.ForegroundProperty, new WBinding("Cor"));
            f.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            f.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            f.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0));
            dg.Columns.Add(new DataGridTemplateColumn { Header = titulo, CellTemplate = new DataTemplate { VisualTree = f }, Width = new DataGridLength(largura) });
        }

        protected static ComboBox Combo(double largura) => new ComboBox { Width = largura, MinHeight = 26 };

        protected static void Aviso(string titulo, string texto, string detalhe = null)
        {
            var td = new TaskDialog(titulo) { MainContent = texto };
            if (!string.IsNullOrEmpty(detalhe)) td.ExpandedContent = detalhe;
            td.Show();
        }

        protected static bool Confirmar(string titulo, string texto)
        {
            var td = new TaskDialog(titulo) { MainContent = texto, CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, DefaultButton = TaskDialogResult.No };
            return td.Show() == TaskDialogResult.Yes;
        }

        protected static string Usuario(Document doc)
        {
            try { return doc.Application.Username; } catch { return ""; }
        }

        protected static string Detalhe(IEnumerable<string> linhas, int max = 40)
        {
            var l = linhas.ToList();
            if (l.Count == 0) return null;
            var t = string.Join("\n", l.Take(max));
            if (l.Count > max) t += "\n… e mais " + (l.Count - max);
            return t;
        }
    }

    /// <summary>Estado que sobrevive entre a janela e a seleção no modelo (a janela fecha, a pessoa escolhe, a janela volta).</summary>
    public sealed class EstadoCopiar
    {
        public string Link, Modulo, Workset;
        public HashSet<string> Escolhidos;
        public bool Rede = true, Reconectar = true;
    }

    // ================================================================== Copiar do link
    public sealed class JanelaCopiar : JanelaLinksBase
    {
        public sealed class Linha
        {
            public string Sistema { get; set; }
            public string Categoria { get; set; }
            public string Tipo { get; set; }
            public string Nivel { get; set; }
            public string Dim { get; set; }
            public int Qtde { get; set; }
            public string Situacao { get; set; }
            public Brush Cor { get; set; }
            public List<Origem> Itens;
            public int Novos, Copiados, Hosp;
        }

        readonly UIDocument uidoc;
        readonly Document doc;
        public EstadoCopiar Estado;
        public string Acao;
        readonly List<LinkInfo> links;
        readonly List<Modulo> mods;
        readonly List<Workset> ws;
        List<Origem> origens = new List<Origem>();
        List<Linha> linhas = new List<Linha>(), vista = new List<Linha>();
        bool pronto;

        ComboBox cbLink, cbMod, cbCat, cbNivel, cbSis, cbEstado, cbWs;
        TextBox tbBusca;
        TextBlock txtInfo, txtResumo, txtRodape;
        DataGrid dg;
        Button btCopiar, btLimpar;
        CheckBox chkRec, chkRede;
        StackPanel spSis;

        public JanelaCopiar(UIDocument uidoc, EstadoCopiar est)
        {
            this.uidoc = uidoc; doc = uidoc.Document;
            Estado = est ?? new EstadoCopiar();
            links = LinkInfo.Todos(doc);
            mods = Modulo.Todos();
            ws = Motor.Worksets(doc);
            Title = "Copiar do link — Ferramentas FJA";
            Width = 1220; Height = 780; MinWidth = 980; MinHeight = 520;
            AplicarEstilo();
            Montar();
            pronto = true;
            Carregar();
        }

        void Montar()
        {
            cbLink = Combo(340); cbMod = Combo(200);
            var raiz = new DockPanel();
            var cab = Cabecalho("Copiar do link", out txtInfo, Rotulado("Link", cbLink), Rotulado("Módulo", cbMod));
            DockPanel.SetDock(cab, Dock.Top); raiz.Children.Add(cab);
            txtInfo.Text = "Modelo: " + doc.Title + " · copia na mesma posição; face based vêm sem hospedeiro; redes podem vir soltas e ser reconectadas";

            foreach (var l in links) cbLink.Items.Add(new ComboBoxItem { Content = l.Rotulo, IsEnabled = l.Carregado });
            int sl = links.FindIndex(l => l.Uid == Estado.Link && l.Carregado);
            cbLink.SelectedIndex = sl >= 0 ? sl : links.FindIndex(l => l.Carregado);
            foreach (var m in mods) cbMod.Items.Add(new ComboBoxItem { Content = m.Ativo ? m.Nome : m.Nome + " (em breve)", IsEnabled = m.Ativo, ToolTip = m.Descricao });
            int sm = mods.FindIndex(m => m.Id == Estado.Modulo && m.Ativo);
            cbMod.SelectedIndex = sm >= 0 ? sm : Math.Max(0, mods.FindIndex(m => m.Ativo));
            cbLink.SelectionChanged += (s, a) => { if (pronto) { Estado.Escolhidos = null; Carregar(); } };
            cbMod.SelectionChanged += (s, a) => { if (pronto) Carregar(); };

            // filtros
            var fil = new DockPanel { Margin = new Thickness(18, 12, 18, 0), LastChildFill = true };
            tbBusca = new TextBox { Width = 240, MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Sistema, categoria, família, tipo ou nível" };
            cbCat = Combo(190); cbNivel = Combo(160); cbSis = Combo(220); cbEstado = Combo(210);
            spSis = new StackPanel { Orientation = Orientation.Horizontal };
            spSis.Children.Add(cbSis);
            foreach (var c in new FrameworkElement[] { tbBusca, spSis, cbCat, cbNivel, cbEstado })
            {
                c.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(c, Dock.Left); fil.Children.Add(c);
            }
            btLimpar = Botao("Limpar escolha no modelo", "Discreto");
            btLimpar.Click += (s, a) => { Estado.Escolhidos = null; Carregar(); };
            DockPanel.SetDock(btLimpar, Dock.Right); fil.Children.Add(btLimpar);
            txtResumo = new TextBlock { Foreground = Cinza, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 8, 0) };
            fil.Children.Add(txtResumo);
            DockPanel.SetDock(fil, Dock.Top); raiz.Children.Add(fil);
            tbBusca.TextChanged += (s, a) => Filtrar();
            foreach (var c in new[] { cbCat, cbNivel, cbSis, cbEstado }) c.SelectionChanged += (s, a) => { if (pronto) Filtrar(); };

            // rodapé
            var rod = new DockPanel { LastChildFill = true };
            var spEsq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            cbWs = Combo(200);
            if (ws.Count > 0)
            {
                spEsq.Children.Add(new TextBlock { Text = "Workset", Foreground = Cinza, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                spEsq.Children.Add(cbWs);
                WorksetId ativo = doc.GetWorksetTable().GetActiveWorksetId();
                for (int i = 0; i < ws.Count; i++) cbWs.Items.Add(ws[i].Name);
                int iw = ws.FindIndex(w => w.Name == Estado.Workset);
                cbWs.SelectedIndex = iw >= 0 ? iw : Math.Max(0, ws.FindIndex(w => w.Id == ativo));
            }
            chkRec = new CheckBox { Content = "Reconectar depois de copiar", IsChecked = Estado.Reconectar, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Refaz as ligações do link entre as cópias e liga pontas coincidentes" };
            chkRede = new CheckBox { Content = "Escolher a rede conectada", IsChecked = Estado.Rede, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ao escolher no modelo, inclui tudo o que está conectado aos elementos clicados" };
            spEsq.Children.Add(chkRec); spEsq.Children.Add(chkRede);
            DockPanel.SetDock(spEsq, Dock.Left); rod.Children.Add(spEsq);
            var btFechar = Botao("Fechar", "Discreto"); btFechar.Click += (s, a) => Close();
            btCopiar = Botao("Copiar", "Primario"); btCopiar.IsEnabled = false; btCopiar.Click += (s, a) => Copiar();
            var btEsc = Botao("Escolher no modelo…"); btEsc.ToolTip = "Selecionar elementos do link na vista (a janela fecha e volta)";
            btEsc.Click += (s, a) => { GuardarEstado(); Acao = "escolher"; Close(); };
            foreach (var b in new[] { btFechar, btCopiar, btEsc }) { DockPanel.SetDock(b, Dock.Right); rod.Children.Add(b); }
            txtRodape = new TextBlock { Foreground = Cinza, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 8, 0) };
            rod.Children.Add(txtRodape);
            var bRod = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(WColor.FromRgb(226, 226, 223)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 10, 18, 10), Child = rod };
            DockPanel.SetDock(bRod, Dock.Bottom); raiz.Children.Add(bRod);

            dg = Lista();
            dg.SelectionChanged += (s, a) => AtualizarBotao();
            var cart = Cartao(dg); cart.Margin = new Thickness(18, 10, 18, 8);
            raiz.Children.Add(cart);
            Content = raiz;
        }

        LinkInfo Link => cbLink.SelectedIndex >= 0 && cbLink.SelectedIndex < links.Count ? links[cbLink.SelectedIndex] : null;
        Modulo Mod => cbMod.SelectedIndex >= 0 && cbMod.SelectedIndex < mods.Count ? mods[cbMod.SelectedIndex] : null;

        void GuardarEstado()
        {
            Estado.Link = Link?.Uid; Estado.Modulo = Mod?.Id;
            Estado.Workset = ws.Count > 0 && cbWs.SelectedIndex >= 0 ? ws[cbWs.SelectedIndex].Name : null;
            Estado.Reconectar = chkRec.IsChecked == true; Estado.Rede = chkRede.IsChecked == true;
        }

        void Carregar()
        {
            var l = Link; var m = Mod;
            origens = new List<Origem>();
            Cursor = Cursors.Wait;
            try
            {
                if (l != null && l.Carregado && m != null && m.Ativo)
                {
                    origens = Motor.Elementos(doc, l, m);
                    if (Estado.Escolhidos != null) origens = origens.Where(o => Estado.Escolhidos.Contains(o.Uid)).ToList();
                }
            }
            finally { Cursor = null; }
            bool redes = m != null && m.Redes;
            spSis.Visibility = redes ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            chkRec.Visibility = chkRede.Visibility = redes ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            btLimpar.Visibility = Estado.Escolhidos != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            // colunas
            dg.Columns.Clear();
            if (redes) Coluna(dg, "Sistema", "Sistema", 2, true);
            Coluna(dg, "Categoria", "Categoria", 170);
            Coluna(dg, "Família : tipo", "Tipo", 3, true);
            if (redes) Coluna(dg, "Dimensão", "Dim", 110);
            Coluna(dg, "Nível", "Nivel", 120);
            Coluna(dg, "Qtde", "Qtde", 56);
            ColunaSituacao(dg, "Situação", 250);
            // linhas
            linhas = origens.GroupBy(o => redes ? o.Sistema + "|" + o.Cat + "|" + o.Fam + "|" + o.Tipo + "|" + o.Dim + "|" + o.Nivel : o.Cat + "|" + o.Fam + "|" + o.Tipo + "|" + o.Nivel)
                .Select(g =>
                {
                    var f = g.First();
                    var ln = new Linha
                    {
                        Sistema = string.IsNullOrEmpty(f.Sistema) ? "(sem sistema)" : f.Sistema, Categoria = f.Cat, Tipo = f.Fam + " : " + f.Tipo,
                        Nivel = string.IsNullOrEmpty(f.Nivel) ? "—" : f.Nivel, Dim = f.Dim, Qtde = g.Count(), Itens = g.ToList(),
                        Novos = g.Count(o => o.Estado == "novo"), Copiados = g.Count(o => o.Estado == "copiado"), Hosp = g.Count(o => o.Estado == "hospedeiro"),
                    };
                    var p = new List<string>();
                    if (ln.Novos > 0) p.Add(ln.Novos + (ln.Novos == 1 ? " novo" : " novos"));
                    if (ln.Copiados > 0) p.Add(ln.Copiados + " já copiado" + (ln.Copiados == 1 ? "" : "s"));
                    if (ln.Hosp > 0) p.Add(ln.Hosp + " sem hospedeiro");
                    ln.Situacao = string.Join(" · ", p);
                    ln.Cor = ln.Hosp > 0 && ln.Novos == 0 ? Vermelho : ln.Novos > 0 ? Azul : Verde;
                    return ln;
                })
                .OrderBy(x => redes ? x.Sistema : "").ThenBy(x => x.Categoria).ThenBy(x => x.Tipo).ThenBy(x => x.Nivel).ToList();
            pronto = false;
            Recarregar(cbCat, "Todas as categorias", origens.Select(o => o.Cat));
            Recarregar(cbNivel, "Todos os níveis", origens.Select(o => string.IsNullOrEmpty(o.Nivel) ? "—" : o.Nivel));
            Recarregar(cbSis, "Todos os sistemas", origens.Select(o => string.IsNullOrEmpty(o.Sistema) ? "(sem sistema)" : o.Sistema));
            int ie = Math.Max(0, cbEstado.SelectedIndex);
            cbEstado.Items.Clear();
            int nv = origens.Count(o => o.Estado == "novo"), cp = origens.Count(o => o.Estado == "copiado"), hp = origens.Count(o => o.Estado == "hospedeiro");
            cbEstado.Items.Add("Todas as situações (" + origens.Count + ")");
            cbEstado.Items.Add("Novos (" + nv + ")");
            cbEstado.Items.Add("Já copiados (" + cp + ")");
            cbEstado.Items.Add("Precisam de hospedeiro (" + hp + ")");
            cbEstado.SelectedIndex = ie;
            pronto = true;
            var partes = new List<string> { origens.Count + " elementos" + (Estado.Escolhidos != null ? " escolhidos no modelo" : "") };
            if (nv > 0) partes.Add(nv + " novos"); if (cp > 0) partes.Add(cp + " já copiados"); if (hp > 0) partes.Add(hp + " precisam de hospedeiro");
            txtResumo.Text = l == null ? "Este modelo não tem links do Revit." : string.Join(" · ", partes);
            Filtrar();
        }

        static void Recarregar(ComboBox cb, string todos, IEnumerable<string> valores)
        {
            var ant = cb.SelectedItem as string;
            cb.Items.Clear();
            cb.Items.Add(todos);
            foreach (var v in valores.Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)) cb.Items.Add(v);
            cb.SelectedItem = ant != null && cb.Items.Contains(ant) ? ant : todos;
        }

        void Filtrar()
        {
            if (!pronto) return;
            string q = (tbBusca.Text ?? "").Trim().ToLowerInvariant();
            string cat = cbCat.SelectedIndex > 0 ? cbCat.SelectedItem as string : null;
            string niv = cbNivel.SelectedIndex > 0 ? cbNivel.SelectedItem as string : null;
            string sis = cbSis.SelectedIndex > 0 && spSis.Visibility == System.Windows.Visibility.Visible ? cbSis.SelectedItem as string : null;
            int ie = cbEstado.SelectedIndex;
            vista = linhas.Where(ln =>
                (cat == null || ln.Categoria == cat) && (niv == null || ln.Nivel == niv) && (sis == null || ln.Sistema == sis) &&
                (ie <= 0 || (ie == 1 && ln.Novos > 0) || (ie == 2 && ln.Copiados > 0) || (ie == 3 && ln.Hosp > 0)) &&
                (q == "" || (ln.Sistema + " " + ln.Categoria + " " + ln.Tipo + " " + ln.Nivel + " " + ln.Dim).ToLowerInvariant().Contains(q))).ToList();
            dg.ItemsSource = vista;
            AtualizarBotao();
        }

        List<Origem> Alvo(out bool comSelecao)
        {
            var sel = dg.SelectedItems.Cast<Linha>().ToList();
            comSelecao = sel.Count > 0;
            return (comSelecao ? sel : vista).SelectMany(ln => ln.Itens).Where(o => o.Estado == "novo").ToList();
        }

        void AtualizarBotao()
        {
            var alvo = Alvo(out bool tem);
            btCopiar.IsEnabled = alvo.Count > 0;
            btCopiar.Content = alvo.Count > 0 ? "Copiar " + alvo.Count + (alvo.Count == 1 ? " elemento" : " elementos") : "Copiar";
            txtRodape.Text = tem ? "Copia os novos das linhas selecionadas." : "Sem seleção: copia os novos de todas as linhas visíveis (Ctrl/Shift para escolher linhas).";
        }

        void Copiar()
        {
            var alvo = Alvo(out _);
            var l = Link; var m = Mod;
            if (alvo.Count == 0 || l == null || m == null) return;
            GuardarEstado();
            WorksetId w = ws.Count > 0 && cbWs.SelectedIndex >= 0 ? ws[cbWs.SelectedIndex].Id : null;
            string por = Usuario(doc);
            Motor.ResultadoCopia res;
            Reconexao.Relatorio rel = null;
            Cursor = Cursors.Wait;
            using (var t = new Transaction(doc, "Copiar do link (" + m.Nome + "): " + alvo.Count + " elementos"))
            {
                t.Start();
                Motor.OpcoesTransacao(t);
                try
                {
                    res = Motor.Copiar(doc, l, alvo, m, w, por);
                    if (m.Redes && chkRec.IsChecked == true && res.Copiados.Count > 0)
                    {
                        doc.Regenerate();
                        rel = Reconexao.Reconectar(doc, res.Copiados.Select(kv => kv.Value).ToList());
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    Cursor = null;
                    Aviso("Copiar do link", "A cópia foi desfeita: " + ex.Message);
                    return;
                }
            }
            Cursor = null;
            var novos = res.Copiados.Select(kv => kv.Value.Id).ToList();
            try { if (novos.Count > 0) uidoc.Selection.SetElementIds(novos); } catch { }
            string msg = novos.Count + (novos.Count == 1 ? " elemento copiado" : " elementos copiados") + " e selecionado" + (novos.Count == 1 ? "" : "s") + " no modelo";
            if (rel != null) msg += " · " + rel.Resumo();
            if (res.Falhas.Count > 0) msg += " · " + res.Falhas.Count + " não copiado" + (res.Falhas.Count == 1 ? "" : "s");
            var det = new List<string>();
            det.AddRange(res.Falhas.Select(kv => "Não copiado: " + kv.Key.Cat + " · " + kv.Key.Fam + " : " + kv.Key.Tipo + " — " + kv.Value));
            det.AddRange(res.Avisos);
            if (rel != null) det.AddRange(rel.Soltas.Select(s => "Ponta solta: elemento " + s.Elemento + " — " + s.Motivo));
            Carregar();
            txtRodape.Text = msg + ".";
            if (det.Count > 0) Aviso("Copiar do link", msg + ".", Detalhe(det));
        }
    }

    // ================================================================== Verificar alterações
    public sealed class JanelaVerificar : JanelaLinksBase
    {
        public sealed class Linha
        {
            public string Categoria { get; set; }
            public string Sistema { get; set; }
            public string Tipo { get; set; }
            public string Nivel { get; set; }
            public string Situacao { get; set; }
            public string Difs { get; set; }
            public Brush Cor { get; set; }
            public Motor.Item It;
        }

        static readonly string[][] ESTADOS =
        {
            new[] { "link", "Alterado no link" }, new[] { "projeto", "Alterado no projeto" }, new[] { "ambos", "Alterado nos dois" },
            new[] { "apagado", "Apagado no link" }, new[] { "novo", "Novo no link" }, new[] { "semlink", "Link ausente" }, new[] { "ok", "Igual" },
        };

        readonly UIDocument uidoc;
        readonly Document doc;
        List<Motor.Item> itens = new List<Motor.Item>();
        List<Linha> vista = new List<Linha>();
        bool pronto;
        ComboBox cbLink, cbCat, cbEstado;
        TextBox tbBusca;
        TextBlock txtInfo, txtResumo, txtRodape;
        DataGrid dg;
        Button btAtualizar, btAceitar, btApagar, btNovos, btMostrar, btReconectar;
        Button btColorir, btLimparCores;
        CheckBox chkIguais;

        public JanelaVerificar(UIDocument uidoc)
        {
            this.uidoc = uidoc; doc = uidoc.Document;
            Title = "Verificar alterações — Ferramentas FJA";
            Width = 1320; Height = 800; MinWidth = 1040; MinHeight = 520;
            AplicarEstilo();
            Montar();
            pronto = true;
            Carregar();
        }

        static Brush CorDe(string e) => e == "link" ? Ambar : e == "projeto" ? Azul : e == "ambos" || e == "apagado" ? Vermelho : e == "ok" ? Verde : Cinza;
        static string Rotulo(string e) => ESTADOS.First(x => x[0] == e)[1];

        void Montar()
        {
            cbLink = Combo(340);
            var btDeNovo = Botao("Verificar de novo", "Discreto"); btDeNovo.Margin = new Thickness(12, 14, 0, 0);
            btDeNovo.ToolTip = "Recarregue o link antes, para comparar com a versão mais nova";
            btDeNovo.Click += (s, a) => Carregar();
            var raiz = new DockPanel();
            var cab = Cabecalho("Verificar alterações", out txtInfo, Rotulado("Link", cbLink), btDeNovo);
            txtInfo.Text = "Modelo: " + doc.Title + " · compara as cópias do Copiar do link com a versão do link carregada agora (recarregue o link antes de verificar)";
            DockPanel.SetDock(cab, Dock.Top); raiz.Children.Add(cab);

            var fil = new DockPanel { Margin = new Thickness(18, 12, 18, 0) };
            tbBusca = new TextBox { Width = 280, MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center };
            cbCat = Combo(200); cbEstado = Combo(240);
            foreach (var c in new FrameworkElement[] { tbBusca, cbCat, cbEstado }) { c.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(c, Dock.Left); fil.Children.Add(c); }
            // cores de diagnóstico na vista ativa
            btLimparCores = Botao("Limpar cores", "Discreto");
            btLimparCores.ToolTip = "Devolve a aparência que as cópias tinham antes de colorir (vista ativa)";
            btLimparCores.Click += (s, a) => LimparCores();
            btColorir = Botao("Colorir na vista");
            btColorir.ToolTip = "Pinta temporariamente, na vista ativa, as cópias da lista (ou as selecionadas) na cor da situação";
            btColorir.Click += (s, a) => Colorir();
            chkIguais = new CheckBox { Content = "incluir iguais", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Pinta também as cópias iguais ao link (verde)" };
            var leg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            foreach (var e in new[] { new[] { "link", "no link" }, new[] { "projeto", "no projeto" }, new[] { "ambos", "nos dois" }, new[] { "apagado", "apagado" }, new[] { "ok", "igual" } })
            {
                var c = CoresDiagnostico.COR[e[0]];
                leg.Children.Add(new Border { Width = 11, Height = 11, Background = new SolidColorBrush(WColor.FromRgb(c.Red, c.Green, c.Blue)), Margin = new Thickness(6, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
                leg.Children.Add(new TextBlock { Text = e[1], Foreground = Cinza, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            }
            foreach (var c in new FrameworkElement[] { btLimparCores, btColorir, chkIguais, leg }) { DockPanel.SetDock(c, Dock.Right); fil.Children.Add(c); }
            txtResumo = new TextBlock { Foreground = Cinza, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 0) };
            fil.Children.Add(txtResumo);
            DockPanel.SetDock(fil, Dock.Top); raiz.Children.Add(fil);
            tbBusca.TextChanged += (s, a) => Filtrar();
            foreach (var c in new[] { cbLink, cbCat, cbEstado }) c.SelectionChanged += (s, a) => { if (pronto) Filtrar(); };

            var rod = new DockPanel();
            var btFechar = Botao("Fechar", "Discreto"); btFechar.Click += (s, a) => Close();
            btAtualizar = Botao("Atualizar pelo link", "Primario"); btAtualizar.Click += (s, a) => Atualizar();
            btAtualizar.ToolTip = "Deixa a cópia igual à origem no link. Peças: ajusta no lugar; redes: copia o trecho de novo e reconecta";
            btNovos = Botao("Copiar novos"); btNovos.Click += (s, a) => CopiarNovos();
            btApagar = Botao("Apagar cópias"); btApagar.ToolTip = "Apaga do modelo as cópias cuja origem foi apagada no link"; btApagar.Click += (s, a) => Apagar();
            btAceitar = Botao("Aceitar como está"); btAceitar.ToolTip = "A cópia fica como está; a referência passa a ser o estado atual do link"; btAceitar.Click += (s, a) => Aceitar();
            btReconectar = Botao("Reconectar"); btReconectar.ToolTip = "Refaz as ligações do link entre as cópias e liga pontas coincidentes (sem seleção: todas as cópias)"; btReconectar.Click += (s, a) => Reconectar();
            btMostrar = Botao("Mostrar no modelo", "Discreto"); btMostrar.Click += (s, a) => Mostrar();
            foreach (var b in new[] { btFechar, btAtualizar, btNovos, btApagar, btAceitar, btReconectar, btMostrar }) { DockPanel.SetDock(b, Dock.Right); rod.Children.Add(b); }
            txtRodape = new TextBlock { Foreground = Cinza, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            rod.Children.Add(txtRodape);
            var bRod = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(WColor.FromRgb(226, 226, 223)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 10, 18, 10), Child = rod };
            DockPanel.SetDock(bRod, Dock.Bottom); raiz.Children.Add(bRod);

            dg = Lista();
            Coluna(dg, "Categoria", "Categoria", 150);
            Coluna(dg, "Sistema", "Sistema", 150);
            Coluna(dg, "Família : tipo", "Tipo", 3, true);
            Coluna(dg, "Nível", "Nivel", 110);
            ColunaSituacao(dg, "Situação", 150);
            Coluna(dg, "O que mudou", "Difs", 4, true);
            dg.SelectionChanged += (s, a) => Botoes();
            var cart = Cartao(dg); cart.Margin = new Thickness(18, 10, 18, 8);
            raiz.Children.Add(cart);
            Content = raiz;
        }

        void Carregar()
        {
            Cursor = Cursors.Wait;
            try { itens = Motor.Verificar(doc); }
            catch (Exception ex) { itens = new List<Motor.Item>(); Aviso("Verificar alterações", "Não foi possível verificar: " + ex.Message); }
            finally { Cursor = null; }
            pronto = false;
            var antL = cbLink.SelectedItem as string; var antC = cbCat.SelectedItem as string; int antE = cbEstado.SelectedIndex;
            cbLink.Items.Clear(); cbLink.Items.Add("Todos os links");
            foreach (var n in itens.Select(i => i.Link != null ? i.Link.Nome : "(link ausente)").Distinct().OrderBy(x => x)) cbLink.Items.Add(n);
            cbLink.SelectedItem = antL != null && cbLink.Items.Contains(antL) ? antL : "Todos os links";
            cbCat.Items.Clear(); cbCat.Items.Add("Todas as categorias");
            foreach (var c in itens.Select(i => i.Cat).Distinct().OrderBy(x => x)) cbCat.Items.Add(c);
            cbCat.SelectedItem = antC != null && cbCat.Items.Contains(antC) ? antC : "Todas as categorias";
            var cont = itens.GroupBy(i => i.Estado).ToDictionary(g => g.Key, g => g.Count());
            cbEstado.Items.Clear();
            cbEstado.Items.Add("Precisam de atenção (" + itens.Count(i => i.Estado != "ok") + ")");
            cbEstado.Items.Add("Todas as situações (" + itens.Count + ")");
            foreach (var e in ESTADOS) cbEstado.Items.Add(e[1] + " (" + (cont.TryGetValue(e[0], out int n) ? n : 0) + ")");
            cbEstado.SelectedIndex = antE >= 0 ? antE : 0;
            pronto = true;
            txtResumo.Text = itens.Count == 0 ? "Nenhuma cópia registrada neste modelo (use Links › Copiar do link)."
                : string.Join(" · ", ESTADOS.Where(e => cont.ContainsKey(e[0])).Select(e => cont[e[0]] + " " + e[1].ToLowerInvariant()));
            Filtrar();
        }

        void Filtrar()
        {
            if (!pronto) return;
            string q = (tbBusca.Text ?? "").Trim().ToLowerInvariant();
            string lk = cbLink.SelectedIndex > 0 ? cbLink.SelectedItem as string : null;
            string cat = cbCat.SelectedIndex > 0 ? cbCat.SelectedItem as string : null;
            int ie = cbEstado.SelectedIndex;
            var ordem = ESTADOS.Select((e, i) => new { e, i }).ToDictionary(x => x.e[0], x => x.i);
            vista = itens.Where(i =>
                    (lk == null || (i.Link != null ? i.Link.Nome : "(link ausente)") == lk) && (cat == null || i.Cat == cat) &&
                    !(ie == 0 && i.Estado == "ok") && !(ie >= 2 && i.Estado != ESTADOS[ie - 2][0]) &&
                    (q == "" || (i.Cat + " " + i.Sistema + " " + i.Fam + " " + i.Tipo + " " + i.Nivel + " " + i.Difs).ToLowerInvariant().Contains(q)))
                .OrderBy(i => ordem.TryGetValue(i.Estado, out int o) ? o : 9).ThenBy(i => i.Cat).ThenBy(i => i.Sistema).ThenBy(i => i.Fam).ThenBy(i => i.Tipo)
                .Select(i => new Linha
                {
                    It = i, Categoria = i.Cat, Sistema = i.Sistema, Tipo = i.Fam + " : " + i.Tipo, Nivel = string.IsNullOrEmpty(i.Nivel) ? "—" : i.Nivel,
                    Situacao = Rotulo(i.Estado), Cor = CorDe(i.Estado), Difs = i.Estado == "apagado" ? "origem apagada no link" : i.Difs,
                }).ToList();
            dg.ItemsSource = vista;
            Botoes();
        }

        List<Motor.Item> Sel() => dg.SelectedItems.Cast<Linha>().Select(l => l.It).ToList();

        void Botoes()
        {
            var s = Sel();
            int alt = s.Count(i => i.Estado == "link" || i.Estado == "projeto" || i.Estado == "ambos");
            btAtualizar.IsEnabled = btAceitar.IsEnabled = alt > 0;
            btAtualizar.Content = alt > 0 ? "Atualizar pelo link (" + alt + ")" : "Atualizar pelo link";
            btAceitar.Content = alt > 0 ? "Aceitar como está (" + alt + ")" : "Aceitar como está";
            btApagar.IsEnabled = s.Any(i => i.Estado == "apagado");
            btNovos.IsEnabled = s.Any(i => i.Estado == "novo");
            btMostrar.IsEnabled = s.Any(i => i.Copia != null);
            btReconectar.IsEnabled = itens.Any(i => i.Copia != null && Motor.Conectores(i.Copia) != null);
            txtRodape.Text = s.Count > 0 ? s.Count + " selecionado(s)." : "Selecione linhas (Ctrl/Shift; Ctrl+A para todas) e escolha a ação.";
        }

        bool Transacao(string nome, Action corpo)
        {
            Cursor = Cursors.Wait;
            using (var t = new Transaction(doc, nome))
            {
                t.Start();
                Motor.OpcoesTransacao(t);
                try { corpo(); t.Commit(); Cursor = null; return true; }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    Cursor = null;
                    Aviso("Verificar alterações", "Nada foi alterado: " + ex.Message);
                    return false;
                }
            }
        }

        void Atualizar()
        {
            var alvo = Sel().Where(i => i.Estado == "link" || i.Estado == "projeto" || i.Estado == "ambos").ToList();
            if (alvo.Count == 0) return;
            if (alvo.Any(i => i.Estado != "link" && i.DifProj.Any(d => !d.Contains("ligaç"))) &&
                !Confirmar("Verificar alterações", "Algumas cópias foram mexidas neste modelo. Atualizar pelo link desfaz essas mudanças (a cópia volta para onde está a origem no link). Continuar?"))
                return;
            (int aj, int rec, List<string> falhas) r = (0, 0, null);
            if (!Transacao("Verificar alterações: atualizar pelo link", () => r = Motor.Atualizar(doc, alvo, Usuario(doc)))) return;
            Carregar();
            string msg = (r.aj + r.rec) + " atualizado(s)" + (r.rec > 0 ? " (" + r.rec + " copiado(s) de novo do link e reconectado(s))" : "") + (r.falhas.Count > 0 ? " · " + r.falhas.Count + " com falha" : "") + ".";
            txtRodape.Text = msg;
            if (r.falhas.Count > 0) Aviso("Verificar alterações", msg, Detalhe(r.falhas));
        }

        void Aceitar()
        {
            var alvo = Sel().Where(i => i.Estado == "link" || i.Estado == "projeto" || i.Estado == "ambos").ToList();
            if (alvo.Count == 0) return;
            string por = Usuario(doc);
            if (!Transacao("Verificar alterações: aceitar como está", () => { foreach (var it in alvo) Motor.Aceitar(it, por); })) return;
            Carregar();
            txtRodape.Text = alvo.Count + " aceito(s): as cópias ficaram como estão e passam a valer como referência.";
        }

        void Apagar()
        {
            var alvo = Sel().Where(i => i.Estado == "apagado").ToList();
            if (alvo.Count == 0 || !Confirmar("Verificar alterações", "Apagar do modelo " + alvo.Count + " cópia(s) cuja origem foi apagada no link?")) return;
            if (!Transacao("Verificar alterações: apagar cópias", () => doc.Delete(alvo.Select(i => i.Copia.Id).ToList()))) return;
            Carregar();
            txtRodape.Text = alvo.Count + " cópia(s) apagada(s).";
        }

        void CopiarNovos()
        {
            var alvo = Sel().Where(i => i.Estado == "novo").ToList();
            if (alvo.Count == 0) return;
            int n = 0, f = 0; Reconexao.Relatorio rel = null;
            WorksetId ws = doc.IsWorkshared ? doc.GetWorksetTable().GetActiveWorksetId() : null;
            if (!Transacao("Verificar alterações: copiar novos (" + alvo.Count + ")", () =>
            {
                var todas = new List<Element>();
                foreach (var g in alvo.GroupBy(i => i.Link.Uid + "|" + i.Mod.Id))
                {
                    var res = Motor.Copiar(doc, g.First().Link, g.Select(i => i.Origem).ToList(), g.First().Mod, ws, Usuario(doc));
                    n += res.Copiados.Count; f += res.Falhas.Count;
                    if (g.First().Mod.Redes) todas.AddRange(res.Copiados.Select(kv => kv.Value));
                }
                if (todas.Count > 0) { doc.Regenerate(); rel = Reconexao.Reconectar(doc, todas); }
            })) return;
            Carregar();
            txtRodape.Text = n + " copiado(s)" + (f > 0 ? " · " + f + " não copiado(s)" : "") + (rel != null ? " · " + rel.Resumo() : "") + ".";
        }

        void Reconectar()
        {
            var s = Sel().Where(i => i.Copia != null).Select(i => i.Copia).ToList();
            var alvo = s.Count > 0 ? s : Reconexao.CopiasComConectores(doc);
            if (alvo.Count == 0) return;
            Reconexao.Relatorio rel = null;
            if (!Transacao("Verificar alterações: reconectar", () => rel = Reconexao.Reconectar(doc, alvo))) return;
            Carregar();
            txtRodape.Text = rel.Resumo() + ".";
            if (rel.Soltas.Count > 0)
            {
                try { uidoc.Selection.SetElementIds(rel.Soltas.Select(x => x.Elemento).Distinct().ToList()); } catch { }
                Aviso("Reconectar", rel.Resumo() + ". Os elementos com pontas soltas ficaram selecionados no modelo.",
                    Detalhe(rel.Soltas.Select(x => "Elemento " + x.Elemento + " — " + x.Motivo)));
            }
        }

        void Colorir()
        {
            var v = doc.ActiveView;
            if (!CoresDiagnostico.VistaAceita(v)) { Aviso("Verificar alterações", "A vista ativa não aceita cores (abra uma planta, corte ou 3D)."); return; }
            var s = Sel();
            var alvo = s.Count > 0 ? s : vista.Select(l => l.It).ToList();
            int n = 0;
            if (!Transacao("Verificar alterações: colorir para diagnóstico", () => n = CoresDiagnostico.Colorir(doc, v, alvo, chkIguais.IsChecked == true))) return;
            try { uidoc.RefreshActiveView(); } catch { }
            txtRodape.Text = n + " cópia(s) coloridas na vista \"" + v.Name + "\". Ao terminar, use Limpar cores (a aparência anterior volta).";
        }

        void LimparCores()
        {
            var v = doc.ActiveView;
            if (!CoresDiagnostico.VistaAceita(v)) return;
            int n = 0;
            if (!Transacao("Verificar alterações: limpar cores", () => n = CoresDiagnostico.Limpar(doc, v))) return;
            try { uidoc.RefreshActiveView(); } catch { }
            txtRodape.Text = n + " cópia(s) voltaram à aparência anterior na vista \"" + v.Name + "\".";
        }

        void Mostrar()
        {
            var ids = Sel().Where(i => i.Copia != null).Select(i => i.Copia.Id).ToList();
            if (ids.Count == 0) return;
            uidoc.Selection.SetElementIds(ids);
            try { uidoc.ShowElements(ids); } catch { }
            txtRodape.Text = ids.Count + " cópia(s) selecionada(s) no modelo.";
        }
    }
}
