using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using FJA.Revit.Legendas;
using FJA.Revit.Nucleo;
using DB = Autodesk.Revit.DB;
using RUI = Autodesk.Revit.UI;
using WColor = System.Windows.Media.Color;

namespace FJA.Revit.Componentes
{
    /// <summary>Fila de trabalhos no contexto da API do Revit (o painel fica aberto ao lado do modelo).</summary>
    sealed class Fila : RUI.IExternalEventHandler
    {
        readonly Queue<Action<RUI.UIApplication>> q = new Queue<Action<RUI.UIApplication>>();
        public void Pedir(Action<RUI.UIApplication> a) { lock (q) q.Enqueue(a); }
        public void Execute(RUI.UIApplication app)
        {
            while (true)
            {
                Action<RUI.UIApplication> a;
                lock (q) { if (q.Count == 0) return; a = q.Dequeue(); }
                try { a(app); }
                catch (Exception e) { RUI.TaskDialog.Show("Componentes", e.Message); }
            }
        }
        public string GetName() => "Componentes FJA";
    }

    /// <summary>Preferências da pessoa: %APPDATA%\FJA\componentes.json.</summary>
    sealed class OpcoesComponentes
    {
        public bool Dev, Renomear, RenomearFamilia = true;
        static string Arq => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "componentes.json");
        public static OpcoesComponentes Carregar()
        {
            var o = new OpcoesComponentes();
            try
            {
                if (!File.Exists(Arq)) return o;
                var d = Json.LerObjeto(File.ReadAllText(Arq, Encoding.UTF8));
                o.Dev = d.Txt("fonte") == "dev";
                o.Renomear = d.Txt("renomear") == "True";
                o.RenomearFamilia = d.Txt("renomearFamilia") != "False";
            }
            catch { }
            return o;
        }
        public void Gravar()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Arq));
                File.WriteAllText(Arq, Json.Gravar(new Dictionary<string, object> { ["fonte"] = Dev ? "dev" : "oficial", ["renomear"] = Renomear, ["renomearFamilia"] = RenomearFamilia }), Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>
    /// Painel encaixável Componentes: árvore Categoria › Família › Tipo das famílias carregáveis do modelo, com busca
    /// e cor pela situação no Sistema FJA (verde = sincronizado), detalhes do nó escolhido, ligar a um código da
    /// biblioteca e Sincronizar (o sistema é a referência). Ver docs/revit-componentes.md.
    /// </summary>
    public sealed class PainelComponentes : UserControl
    {
        readonly Fila fila = new Fila();
        readonly RUI.ExternalEvent ev;
        readonly OpcoesComponentes op = OpcoesComponentes.Carregar();

        /// <summary>Quando o painel está numa janela (painel encaixável ainda não criado): se a janela está visível.</summary>
        public Func<bool> EstaVisivel;

        DadosSistema dados;
        string erroDados = "";
        bool dadosLidos, sujo = true;
        string docChave = "", docTitulo = "";
        List<TipoInfo> tipos = new List<TipoInfo>();
        readonly Dictionary<string, string> projetoEscolhido = new Dictionary<string, string>();   // documento → id do projeto escolhido à mão
        readonly HashSet<string> fechados = new HashSet<string>(), abertos = new HashSet<string>();
        string selecionado;
        bool montando;

        // controles
        ComboBox cbFonte, cbProjeto, cbFiltro;
        TextBlock lbFonte, lbResumo, lbStatus;
        TextBox tbBusca;
        TreeView tv;
        StackPanel spDet;
        Button btSincTudo;

        static readonly Brush Verde = B(46, 115, 64), Ambar = B(138, 93, 5), Vermelho = B(179, 38, 30), Azul = B(47, 95, 138),
                              Cinza = B(143, 143, 140), Suave = B(94, 94, 92), Tinta = B(29, 29, 29);
        static Brush B(byte r, byte g, byte b) { var x = new SolidColorBrush(WColor.FromRgb(r, g, b)); x.Freeze(); return x; }

        public static Brush Cor(Situacao s)
        {
            switch (s)
            {
                case Situacao.Ok: return Verde;
                case Situacao.Desatualizado: case Situacao.Revit: case Situacao.Fora: return Ambar;
                case Situacao.Aguardando: case Situacao.SemDados: return Azul;
                case Situacao.Orfao: return Vermelho;
                default: return Cinza;
            }
        }

        /// <summary>Precisa ser criado no contexto da API (comando ou Idling): cria o ExternalEvent e assina os eventos.</summary>
        public PainelComponentes(RUI.UIApplication app)
        {
            ev = RUI.ExternalEvent.Create(fila);
            AppDomain.CurrentDomain.SetData(Registro.ATUAL, this);
            AplicarEstilo();
            Montar();
            app.Idling += Ocioso;
            app.ViewActivated += VistaAtivada;
            app.Application.DocumentChanged += DocumentoMudou;
        }

        bool Ativo => ReferenceEquals(AppDomain.CurrentDomain.GetData(Registro.ATUAL), this);

        void Soltar(RUI.UIApplication app)
        {
            try { app.Idling -= Ocioso; } catch { }
            try { app.ViewActivated -= VistaAtivada; } catch { }
            try { app.Application.DocumentChanged -= DocumentoMudou; } catch { }
        }

        // ------------------------------------------------------------ eventos do Revit

        void Ocioso(object s, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            var app = s as RUI.UIApplication;
            if (app == null) return;
            if (!Ativo) { Soltar(app); return; }
            if (!sujo) return;
            bool visivel;
            if (EstaVisivel != null) visivel = EstaVisivel();
            else try { visivel = app.GetDockablePane(Registro.Id).IsShown(); } catch { visivel = false; }
            if (!visivel) return;
            sujo = false;
            try
            {
                if (!dadosLidos) CarregarDados();
                Recarregar(app);
            }
            catch (Exception ex) { Status("Erro ao ler: " + ex.Message); }
        }

        void VistaAtivada(object s, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            if (!Ativo) return;
            try { if (Chave(e.Document) != docChave) sujo = true; } catch { }
        }

        void DocumentoMudou(object s, Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
        {
            if (!Ativo || sujo) return;
            try
            {
                var doc = e.GetDocument();
                if (Chave(doc) != docChave) return;
                if (e.GetDeletedElementIds().Count > 0) { sujo = true; return; }
                foreach (var id in e.GetAddedElementIds().Concat(e.GetModifiedElementIds()))
                {
                    var el = doc.GetElement(id);
                    if (el is DB.FamilySymbol || el is DB.Family || el is DB.FamilyInstance) { sujo = true; return; }
                }
            }
            catch { sujo = true; }
        }

        static string Chave(DB.Document d) => d == null ? "" : (string.IsNullOrEmpty(d.PathName) ? d.Title : d.PathName);

        // ------------------------------------------------------------ dados

        void CarregarDados()
        {
            dadosLidos = true;
            dados = null; erroDados = "";
            try { dados = DadosSistema.Carregar(op.Dev); }
            catch (Exception e) { erroDados = e.Message; }
            docChave = "";   // força escolher o projeto de novo
        }

        /// <summary>Lê o modelo ativo (no contexto da API) e redesenha.</summary>
        void Recarregar(RUI.UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                tipos = new List<TipoInfo>(); docChave = ""; docTitulo = "";
                Desenhar();
                Status(doc == null ? "Abra um projeto." : "O painel trabalha com projetos (não com famílias abertas).");
                return;
            }
            string k = Chave(doc);
            if (k != docChave)
            {
                docChave = k; docTitulo = doc.Title;
                EscolherProjeto();
            }
            tipos = Coleta.Tipos(doc, dados);
            Desenhar();
            Status(tipos.Count + " tipos de " + tipos.Select(t => t.Familia).Distinct().Count() + " famílias · " + doc.Title);
        }

        void EscolherProjeto()
        {
            if (dados == null) return;
            Dictionary<string, object> proj = null;
            if (projetoEscolhido.TryGetValue(docChave, out var pid))
                proj = dados.Projetos.FirstOrDefault(p => p.Txt("id") == pid);
            if (proj == null && !projetoEscolhido.ContainsKey(docChave)) proj = dados.ProjetoDoDocumento(docTitulo);
            try { dados.CarregarProjeto(proj); }
            catch (Exception e) { erroDados = e.Message; try { dados.CarregarProjeto(null); } catch { } }
        }

        // ------------------------------------------------------------ montagem

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
            Background = TryFindResource("Fundo") as Brush ?? B(246, 246, 245);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
        }

        static TextBlock T(string s, Brush cor = null, double tam = 0, bool negrito = false)
        {
            var t = new TextBlock { Text = s ?? "", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (cor != null) t.Foreground = cor;
            if (tam > 0) t.FontSize = tam;
            if (negrito) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        Button Botao(string s, RoutedEventHandler clique, bool primario = false, string dica = null)
        {
            var b = new Button { Content = s, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), MinHeight = 26 };
            if (TryFindResource(primario ? "Primario" : "Discreto") is Style st) b.Style = st;
            if (dica != null) b.ToolTip = dica;
            b.Click += clique;
            return b;
        }

        void Montar()
        {
            var raiz = new Grid { Margin = new Thickness(8) };
            foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1.4, GridUnitType.Star), new GridLength(6), new GridLength(1, GridUnitType.Star), GridLength.Auto })
                raiz.RowDefinitions.Add(new RowDefinition { Height = h });

            // 0: título, fonte e atualizar
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            var btAtu = Botao("↻", (s, a) => { dadosLidos = false; sujo = true; Pedir(_ => { }); }, false, "Ler de novo o sistema e o modelo");
            btAtu.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btAtu, Dock.Right);
            cab.Children.Add(btAtu);
            cbFonte = new ComboBox { Width = 132, ToolTip = "De onde vêm os dados do sistema" };
            cbFonte.Items.Add("Oficial"); cbFonte.Items.Add("Desenvolvimento");
            cbFonte.SelectedIndex = op.Dev ? 1 : 0;
            cbFonte.SelectionChanged += (s, a) => { op.Dev = cbFonte.SelectedIndex == 1; op.Gravar(); dadosLidos = false; sujo = true; Pedir(_ => { }); };
            DockPanel.SetDock(cbFonte, Dock.Right);
            cab.Children.Add(cbFonte);
            cab.Children.Add(T("Componentes", Tinta, 15, true));
            Grid.SetRow(cab, 0); raiz.Children.Add(cab);

            // 1: projeto, resumo e sincronizar tudo
            var sp1 = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            lbFonte = T("", Suave, 11); lbFonte.TextWrapping = TextWrapping.Wrap;
            sp1.Children.Add(lbFonte);
            cbProjeto = new ComboBox { Margin = new Thickness(0, 4, 0, 4), ToolTip = "Projeto do sistema a que este modelo pertence (pelo código no nome do arquivo; dá para trocar)" };
            cbProjeto.SelectionChanged += ProjetoMudou;
            sp1.Children.Add(cbProjeto);
            var lin = new DockPanel();
            btSincTudo = Botao("Sincronizar tudo", (s, a) => Sincronizar(tipos, "todos os tipos do modelo"), true,
                "Traz para o modelo o que está no sistema (keynote, Type Mark e descrição) em todos os tipos ligados com diferença");
            btSincTudo.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btSincTudo, Dock.Right);
            lin.Children.Add(btSincTudo);
            lbResumo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            lin.Children.Add(lbResumo);
            sp1.Children.Add(lin);
            var wpAc = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            wpAc.Children.Add(Botao("Carregar família…", (s, a) => CarregarRfa(), false, "Carrega uma família (.rfa) no modelo; depois, ligue os tipos a um código"));
            wpAc.Children.Add(Botao("Localizar seleção", (s, a) => Localizar(), false, "Mostra na árvore o tipo do elemento selecionado no modelo"));
            sp1.Children.Add(wpAc);
            Grid.SetRow(sp1, 1); raiz.Children.Add(sp1);

            // 2: busca e filtro
            var g2 = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tbBusca = new TextBox { MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Buscar por categoria, família, tipo, keynote, descrição ou código FJ" };
            var marca = new TextBlock { Text = "Buscar…", Foreground = Cinza, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            tbBusca.TextChanged += (s, a) => { marca.Visibility = tbBusca.Text == "" ? Visibility.Visible : Visibility.Collapsed; Desenhar(); };
            g2.Children.Add(tbBusca); g2.Children.Add(marca);
            cbFiltro = new ComboBox { Width = 132, Margin = new Thickness(6, 0, 0, 0) };
            foreach (var f in new[] { "Todos", "Sincronizados", "Com diferença", "Sem ligação", "Aguardando / erro" }) cbFiltro.Items.Add(f);
            cbFiltro.SelectedIndex = 0;
            cbFiltro.SelectionChanged += (s, a) => Desenhar();
            Grid.SetColumn(cbFiltro, 1); g2.Children.Add(cbFiltro);
            Grid.SetRow(g2, 2); raiz.Children.Add(g2);

            // 3: árvore
            tv = new TreeView { BorderThickness = new Thickness(1), BorderBrush = TryFindResource("Linha") as Brush ?? B(226, 226, 223), Background = Brushes.White };
            VirtualizingPanel.SetIsVirtualizing(tv, true);
            tv.SelectedItemChanged += (s, a) => { if (!montando) { selecionado = (tv.SelectedItem as TreeViewItem)?.Tag is No n ? n.Chave : null; MostrarDetalhe(); } };
            Grid.SetRow(tv, 3); raiz.Children.Add(tv);

            var div = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(div, 4); raiz.Children.Add(div);

            // 5: detalhes
            spDet = new StackPanel { Margin = new Thickness(2, 2, 2, 8) };
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = spDet };
            Grid.SetRow(sv, 5); raiz.Children.Add(sv);

            // 6: situação
            lbStatus = T("", Suave, 11);
            lbStatus.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetRow(lbStatus, 6); raiz.Children.Add(lbStatus);

            Content = raiz;
            MostrarDetalhe();
        }

        void Status(string s) => lbStatus.Text = s ?? "";

        void Pedir(Action<RUI.UIApplication> a)
        {
            fila.Pedir(app => { a(app); if (sujo) { sujo = false; if (!dadosLidos) CarregarDados(); Recarregar(app); } });
            ev.Raise();
        }

        void ProjetoMudou(object s, SelectionChangedEventArgs a)
        {
            if (montando || dados == null) return;
            var proj = (cbProjeto.SelectedItem as ComboBoxItem)?.Tag as Dictionary<string, object>;
            projetoEscolhido[docChave] = proj?.Txt("id") ?? "";
            try { dados.CarregarProjeto(proj); erroDados = ""; }
            catch (Exception e) { erroDados = e.Message; try { dados.CarregarProjeto(null); } catch { } }
            sujo = true;
            Pedir(_ => { });
        }

        // ------------------------------------------------------------ árvore

        /// <summary>Nó da árvore: categoria, família ou tipo.</summary>
        sealed class No
        {
            public string Chave, Titulo;
            public int Nivel;              // 0 categoria, 1 família, 2 tipo
            public List<TipoInfo> Tipos = new List<TipoInfo>();
        }

        bool PassaFiltro(TipoInfo t)
        {
            switch (cbFiltro?.SelectedIndex ?? 0)
            {
                case 1: return t.Sit == Situacao.Ok;
                case 2: return Coleta.Sincronizavel(t.Sit);
                case 3: return t.Sit == Situacao.Sem;
                case 4: return t.Sit == Situacao.Aguardando || t.Sit == Situacao.Orfao || t.Sit == Situacao.SemDados;
                default: return true;
            }
        }

        static bool PassaBusca(TipoInfo t, string[] termos)
        {
            if (termos.Length == 0) return true;
            string s = Texto.NormSistema(string.Join(" ", t.Categoria, t.Familia, t.Nome, t.Kn, t.Tm, t.Desc, t.Fja));
            return termos.All(s.Contains);
        }

        void Desenhar()
        {
            if (tv == null) return;
            montando = true;
            try
            {
                // projeto e fonte
                lbFonte.Text = dados != null ? dados.Rotulo + (dados.GeradoEm != "" ? " · publicado em " + dados.GeradoEm : "")
                    : (dadosLidos ? "Sistema não lido: " + erroDados : "Lendo o sistema…");
                if (erroDados != "" && dados != null) lbFonte.Text += " · " + erroDados;
                cbProjeto.Items.Clear();
                cbProjeto.Items.Add(new ComboBoxItem { Content = "— sem projeto do sistema —", Tag = null });
                int sel = 0;
                if (dados != null)
                    foreach (var p in dados.Projetos.OrderBy(p => p.Txt("codigo") ?? ""))
                    {
                        cbProjeto.Items.Add(new ComboBoxItem { Content = ((p.Txt("codigo") ?? "") + " · " + (p.Txt("nome") ?? "")).Trim(' ', '·'), Tag = p });
                        if (dados.Projeto != null && p.Txt("id") == dados.Projeto.Txt("id")) sel = cbProjeto.Items.Count - 1;
                    }
                cbProjeto.SelectedIndex = sel;
                cbProjeto.IsEnabled = dados != null && docChave != "";

                // resumo
                lbResumo.Inlines.Clear();
                void Conta(Brush cor, int n, string rot)
                {
                    if (n == 0) return;
                    lbResumo.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = cor });
                    lbResumo.Inlines.Add(new System.Windows.Documents.Run(n + " " + rot + "   ") { Foreground = Suave });
                }
                Conta(Verde, tipos.Count(t => t.Sit == Situacao.Ok), "sincronizados");
                Conta(Ambar, tipos.Count(t => Coleta.Sincronizavel(t.Sit)), "com diferença");
                Conta(Azul, tipos.Count(t => t.Sit == Situacao.Aguardando || t.Sit == Situacao.SemDados), "aguardando");
                Conta(Vermelho, tipos.Count(t => t.Sit == Situacao.Orfao), "sem item");
                Conta(Cinza, tipos.Count(t => t.Sit == Situacao.Sem), "sem ligação");
                int nSinc = tipos.Count(t => Coleta.Sincronizavel(t.Sit));
                btSincTudo.IsEnabled = nSinc > 0 && dados != null;
                btSincTudo.Content = nSinc > 0 ? "Sincronizar tudo (" + nSinc + ")" : "Sincronizar tudo";

                // árvore
                string[] termos = Texto.NormSistema(tbBusca.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool buscando = termos.Length > 0 || cbFiltro.SelectedIndex > 0;
                var vis = tipos.Where(t => PassaFiltro(t) && PassaBusca(t, termos)).ToList();
                tv.Items.Clear();
                TreeViewItem achado = null;
                foreach (var gc in vis.GroupBy(t => t.Categoria).OrderBy(g => g.Key))
                {
                    var nc = new No { Chave = "c|" + gc.Key, Titulo = gc.Key, Nivel = 0, Tipos = tipos.Where(t => t.Categoria == gc.Key).ToList() };
                    var ic = Item(nc, gc.Count());
                    ic.IsExpanded = buscando || !fechados.Contains(nc.Chave);
                    foreach (var gf in gc.GroupBy(t => t.Familia).OrderBy(g => g.Key))
                    {
                        var nf = new No { Chave = "f|" + gc.Key + "|" + gf.Key, Titulo = gf.Key, Nivel = 1, Tipos = tipos.Where(t => t.Categoria == gc.Key && t.Familia == gf.Key).ToList() };
                        var iff = Item(nf, gf.Count());
                        iff.IsExpanded = buscando && termos.Length > 0 || abertos.Contains(nf.Chave);
                        foreach (var t in gf.OrderBy(t => t.Nome))
                        {
                            var nt = new No { Chave = "t|" + Coleta.Num(t.Id), Titulo = t.Nome, Nivel = 2, Tipos = new List<TipoInfo> { t } };
                            var it = Item(nt, 1);
                            iff.Items.Add(it);
                            if (nt.Chave == selecionado) achado = it;
                        }
                        ic.Items.Add(iff);
                        if (nf.Chave == selecionado) achado = iff;
                    }
                    tv.Items.Add(ic);
                    if (nc.Chave == selecionado) achado = ic;
                }
                if (achado != null)
                {
                    for (var p = achado.Parent as TreeViewItem; p != null; p = p.Parent as TreeViewItem) p.IsExpanded = true;
                    achado.IsSelected = true;
                    achado.BringIntoView();
                }
                else selecionado = null;
                if (vis.Count == 0)
                    tv.Items.Add(new TreeViewItem { Header = T(tipos.Count == 0 ? (docChave == "" ? "Abra um projeto." : "Nenhuma família carregável no modelo.") : "Nada encontrado.", Cinza), IsEnabled = false });
            }
            finally { montando = false; }
            MostrarDetalhe();
        }

        TreeViewItem Item(No n, int visiveis)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (n.Nivel == 2)
            {
                var t = n.Tipos[0];
                sp.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Cor(t.Sit), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                var nome = T(t.Nome, t.Sit == Situacao.Sem ? Tinta : Cor(t.Sit));
                if (t.Sit == Situacao.Ok) nome.FontWeight = FontWeights.SemiBold;
                sp.Children.Add(nome);
                string extra = !string.IsNullOrEmpty(t.Kn) ? t.Kn : "";
                if (t.Instancias.Count == 0) extra += (extra != "" ? " · " : "") + "sem instâncias";
                if (extra != "") sp.Children.Add(T("   " + extra, Cinza, 11));
                sp.ToolTip = Coleta.Rotulo(t.Sit) + (t.Fja != "" ? " · " + t.Fja : "");
            }
            else
            {
                int ok = n.Tipos.Count(t => t.Sit == Situacao.Ok), lig = n.Tipos.Count(t => t.Ligado);
                var dif = n.Tipos.Any(t => Coleta.Sincronizavel(t.Sit) || t.Sit == Situacao.Orfao);
                sp.Children.Add(T(n.Titulo, Tinta, n.Nivel == 0 ? 13 : 0, n.Nivel == 0));
                sp.Children.Add(T("   " + ok + "/" + n.Tipos.Count + (visiveis != n.Tipos.Count ? " (" + visiveis + " na busca)" : ""),
                    ok == n.Tipos.Count && n.Tipos.Count > 0 ? Verde : dif ? Ambar : Cinza, 11));
                sp.ToolTip = ok + " sincronizado(s), " + (lig - ok) + " ligado(s) com pendência, " + (n.Tipos.Count - lig) + " sem ligação";
            }
            var it = new TreeViewItem { Header = sp, Tag = n, Padding = new Thickness(1, 2, 1, 2) };
            if (n.Nivel < 2)
            {
                it.Expanded += (s, a) => { if (montando || !ReferenceEquals(a.OriginalSource, it)) return; if (n.Nivel == 0) fechados.Remove(n.Chave); else abertos.Add(n.Chave); };
                it.Collapsed += (s, a) => { if (montando || !ReferenceEquals(a.OriginalSource, it)) return; if (n.Nivel == 0) fechados.Add(n.Chave); else abertos.Remove(n.Chave); };
            }
            return it;
        }

        No NoSelecionado() => (tv?.SelectedItem as TreeViewItem)?.Tag as No;

        // ------------------------------------------------------------ detalhes e ações

        void MostrarDetalhe()
        {
            if (spDet == null) return;
            spDet.Children.Clear();
            var n = NoSelecionado();
            if (n == null)
            {
                spDet.Children.Add(Paragrafo("Escolha uma categoria, família ou tipo na árvore.", Suave));
                spDet.Children.Add(Legenda());
                return;
            }
            if (n.Nivel == 2) DetalheTipo(n.Tipos[0]);
            else DetalheGrupo(n);
        }

        static TextBlock Paragrafo(string s, Brush cor = null, double tam = 0)
        {
            var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            if (cor != null) t.Foreground = cor;
            if (tam > 0) t.FontSize = tam;
            return t;
        }

        UIElement Legenda()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (s, txt) in new[] {
                (Situacao.Ok, "sincronizado: ligado a um keynote do projeto e igual ao sistema"),
                (Situacao.Desatualizado, "com diferença: o sistema mudou, foi alterado no Revit ou o item está fora da especificação do projeto"),
                (Situacao.Aguardando, "aguardando: pedido ainda não processado pelo sistema"),
                (Situacao.Orfao, "sem item: o código não existe mais no sistema"),
                (Situacao.Sem, "sem ligação com o sistema") })
            {
                var l = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
                l.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Cor(s), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                var tb = T(txt, Suave, 11); tb.TextWrapping = TextWrapping.Wrap; tb.TextTrimming = TextTrimming.None;
                l.Children.Add(tb);
                sp.Children.Add(l);
            }
            return sp;
        }

        UIElement Linha(string rot, string valor, Brush cor = null)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(T(rot, Suave, 11.5));
            var v = new TextBlock { Text = string.IsNullOrEmpty(valor) ? "—" : valor, TextWrapping = TextWrapping.Wrap, Foreground = cor ?? Tinta };
            Grid.SetColumn(v, 1); g.Children.Add(v);
            return g;
        }

        void DetalheTipo(TipoInfo t)
        {
            spDet.Children.Add(Paragrafo(t.Familia + " : " + t.Nome, Tinta, 13.5));
            var sit = Paragrafo("● " + Coleta.Rotulo(t.Sit), Cor(t.Sit)); sit.FontWeight = FontWeights.SemiBold;
            spDet.Children.Add(sit);
            spDet.Children.Add(Linha("Categoria", t.Categoria));
            spDet.Children.Add(Linha("Instâncias", t.Instancias.Count.ToString()));
            spDet.Children.Add(Linha("Keynote", t.Kn));
            spDet.Children.Add(Linha("Type Mark", t.Tm));
            spDet.Children.Add(Linha("Descrição", t.Desc));
            if (t.Ligado)
            {
                spDet.Children.Add(Linha("Código", t.Fja + (t.Item != null ? " · " + (t.Item.Txt("desc") ?? "") : "")));
                if (t.Sit == Situacao.Fora) spDet.Children.Add(Paragrafo("O item não está na especificação do projeto. Sincronizar inclui o item no projeto com o próximo keynote da sigla (o sistema confirma).", Ambar, 11.5));
                foreach (var d in t.Difs)
                    spDet.Children.Add(Linha(d[0], (string.IsNullOrEmpty(d[1]) ? "(vazio)" : d[1]) + "  →  " + d[2], Ambar));
                if (t.Sit == Situacao.Revit) spDet.Children.Add(Paragrafo("Os valores foram alterados no Revit depois da última sincronização. Sincronizar devolve os do sistema.", Ambar, 11.5));
            }
            Acoes(new List<TipoInfo> { t }, true);
            if (dados != null) BlocoLigar(new List<TipoInfo> { t }, t.Familia);
        }

        void DetalheGrupo(No n)
        {
            spDet.Children.Add(Paragrafo(n.Titulo, Tinta, 13.5));
            if (n.Nivel == 1) spDet.Children.Add(Linha("Categoria", n.Tipos.FirstOrDefault()?.Categoria));
            spDet.Children.Add(Linha("Tipos", n.Tipos.Count.ToString()));
            spDet.Children.Add(Linha("Sincronizados", n.Tipos.Count(t => t.Sit == Situacao.Ok).ToString(), Verde));
            int dif = n.Tipos.Count(t => Coleta.Sincronizavel(t.Sit));
            if (dif > 0) spDet.Children.Add(Linha("Com diferença", dif.ToString(), Ambar));
            spDet.Children.Add(Linha("Sem ligação", n.Tipos.Count(t => t.Sit == Situacao.Sem).ToString()));
            spDet.Children.Add(Linha("Instâncias", n.Tipos.Sum(t => t.Instancias.Count).ToString()));
            Acoes(n.Tipos, false);
            if (n.Nivel == 1 && dados != null) BlocoLigar(n.Tipos, n.Titulo);
        }

        void Acoes(List<TipoInfo> ts, bool umTipo)
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
            int nSinc = ts.Count(t => Coleta.Sincronizavel(t.Sit));
            if (nSinc > 0 && dados != null)
                wp.Children.Add(Botao(umTipo ? "Sincronizar" : "Sincronizar (" + nSinc + ")", (s, a) => Sincronizar(ts, umTipo ? "este tipo" : "esta seleção"), true,
                    "Traz para o modelo o keynote, o Type Mark e a descrição do sistema"));
            int ni = ts.Sum(t => t.Instancias.Count);
            if (ni > 0)
                wp.Children.Add(Botao("Selecionar no modelo", (s, a) => Selecionar(ts), false, ni + " instância(s)"));
            if (umTipo && ts[0].Ligado)
                wp.Children.Add(Botao("Desligar", (s, a) => Desligar(ts), false, "Apaga a ligação com o sistema (os parâmetros ficam como estão)"));
            if (wp.Children.Count > 0) spDet.Children.Add(wp);
        }

        void BlocoLigar(List<TipoInfo> ts, string familia)
        {
            var cart = new Border { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10), Background = Brushes.White,
                BorderBrush = TryFindResource("Linha") as Brush ?? B(226, 226, 223), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            var sp = new StackPanel();
            cart.Child = sp;
            sp.Children.Add(T(ts.Count == 1 ? "Ligar a um código do sistema" : "Ligar os " + ts.Count + " tipos a um código", Tinta, 0, true));
            sp.Children.Add(Paragrafo("Itens de elemento/família da biblioteca central. O código fica no Keynote e no Type Mark; fora da especificação do projeto, entra com o próximo keynote da sigla.", Suave, 11));
            var busca = new TextBox { MinHeight = 24, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Buscar por código FJ, sigla, keynote ou descrição" };
            var lista = new ListBox { Height = 150, Margin = new Thickness(0, 0, 0, 6) };
            // sugestão: a sigla entre parênteses no nome da família, ex.: "(ESQ) JANELA…"
            var m = System.Text.RegularExpressions.Regex.Match(familia ?? "", @"^\s*\(([A-Za-z]{2,4})\)");
            busca.Text = m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
            void Preencher()
            {
                lista.Items.Clear();
                string[] tm = Texto.NormSistema(busca.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var it in dados.Elementos().OrderBy(x => x.Txt("fja") ?? ""))
                {
                    var ln = dados.LinhaProjeto(it.Txt("id"));
                    string kn = ln?.Txt("kn") ?? "";
                    string s = Texto.NormSistema(string.Join(" ", it.Txt("fja"), it.Txt("sigla"), it.Txt("desc"), kn, ln?.Txt("desc")));
                    if (!tm.All(s.Contains)) continue;
                    var l = new StackPanel();
                    l.Children.Add(T((it.Txt("fja") ?? "") + (kn != "" ? "  ·  " + kn : ""), kn != "" ? Verde : Suave, 11, true));
                    var d = T(ln?.Txt("desc") ?? it.Txt("desc"), Tinta); d.TextWrapping = TextWrapping.Wrap; d.TextTrimming = TextTrimming.None;
                    l.Children.Add(d);
                    lista.Items.Add(new ListBoxItem { Content = l, Tag = it, ToolTip = kn != "" ? "Já está na especificação do projeto como " + kn : "Ainda não está na especificação do projeto" });
                    if (lista.Items.Count >= 300) break;
                }
            }
            busca.TextChanged += (s, a) => Preencher();
            Preencher();
            var spExist = new StackPanel();
            spExist.Children.Add(busca);
            spExist.Children.Add(lista);

            // código novo: sigla (ou sigla nova), descrição e especificação
            var spNovo = new StackPanel { Visibility = Visibility.Collapsed };
            var cbSig = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var sg in dados.SiglasElemento())
                cbSig.Items.Add(new ComboBoxItem { Content = sg.Txt("sigla") + " · " + (sg.Txt("nome") ?? ""), Tag = sg });
            cbSig.Items.Add(new ComboBoxItem { Content = "+ Nova sigla…", Tag = "nova" });
            if (m.Success)
                foreach (ComboBoxItem ci in cbSig.Items)
                    if ((ci.Tag as Dictionary<string, object>)?.Txt("sigla") == m.Groups[1].Value.ToUpperInvariant()) cbSig.SelectedItem = ci;
            var gSig = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            gSig.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            gSig.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tbSig = new TextBox { MaxLength = 3, MinHeight = 24, CharacterCasing = CharacterCasing.Upper, ToolTip = "Sigla nova (3 letras)", Margin = new Thickness(0, 0, 4, 0) };
            var tbSigNome = new TextBox { MinHeight = 24, ToolTip = "Nome da tabela da sigla nova (ex.: Portas de madeira)" };
            Grid.SetColumn(tbSigNome, 1);
            gSig.Children.Add(tbSig); gSig.Children.Add(tbSigNome);
            cbSig.SelectionChanged += (s, a) => gSig.Visibility = (cbSig.SelectedItem as ComboBoxItem)?.Tag as string == "nova" ? Visibility.Visible : Visibility.Collapsed;
            var tbDesc = new TextBox { MinHeight = 24, Margin = new Thickness(0, 0, 0, 4), CharacterCasing = CharacterCasing.Upper, ToolTip = "Descrição do código novo" };
            var tbTexto = new TextBox { MinHeight = 44, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, ToolTip = "Especificação (texto-base; opcional)" };
            spNovo.Children.Add(T("Sigla", Suave, 11)); spNovo.Children.Add(cbSig); spNovo.Children.Add(gSig);
            spNovo.Children.Add(T("Descrição", Suave, 11)); spNovo.Children.Add(tbDesc);
            spNovo.Children.Add(T("Especificação (opcional)", Suave, 11)); spNovo.Children.Add(tbTexto);
            spNovo.Children.Add(Paragrafo("O código (FJ) e o keynote são reservados agora e confirmados pelo sistema quando alguém estiver com ele aberto.", Suave, 11));

            var modo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var rbEx = new RadioButton { Content = "Código existente", IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
            var rbNovo = new RadioButton { Content = "Código novo" };
            RoutedEventHandler trocar = (s, a) =>
            {
                bool novo = rbNovo.IsChecked == true;
                spExist.Visibility = novo ? Visibility.Collapsed : Visibility.Visible;
                spNovo.Visibility = novo ? Visibility.Visible : Visibility.Collapsed;
            };
            rbEx.Checked += trocar; rbNovo.Checked += trocar;
            modo.Children.Add(rbEx); modo.Children.Add(rbNovo);
            sp.Children.Add(modo);
            sp.Children.Add(spExist);
            sp.Children.Add(spNovo);

            var ren = new CheckBox { Content = "Renomear os tipos como KEYNOTE - DESCRIÇÃO", IsChecked = op.Renomear, Margin = new Thickness(0, 0, 0, 4) };
            ren.Click += (s, a) => { op.Renomear = ren.IsChecked == true; op.Gravar(); };
            sp.Children.Add(ren);
            bool umaFamilia = ts.Select(t => t.Familia).Distinct().Count() == 1;
            var renFam = new CheckBox { Content = "Renomear a família como (SIGLA) DESCRIÇÃO", IsChecked = op.RenomearFamilia, Margin = new Thickness(0, 0, 0, 6), IsEnabled = umaFamilia };
            renFam.Click += (s, a) => { op.RenomearFamilia = renFam.IsChecked == true; op.Gravar(); };
            sp.Children.Add(renFam);
            var bt = Botao("Ligar", (s, a) =>
            {
                bool rf = umaFamilia && renFam.IsChecked == true, rt = ren.IsChecked == true;
                if (rbNovo.IsChecked == true)
                {
                    var tag = (cbSig.SelectedItem as ComboBoxItem)?.Tag;
                    string sigla; Dictionary<string, object> nova = null;
                    if (tag as string == "nova")
                    {
                        sigla = System.Text.RegularExpressions.Regex.Replace(tbSig.Text ?? "", "[^A-Za-z]", "").ToUpperInvariant();
                        if (sigla.Length != 3) { Status("A sigla de elemento/família tem 3 letras."); return; }
                        if (dados.SiglaExiste(sigla)) { Status("A sigla " + sigla + " já existe: escolha na lista."); return; }
                        if ((tbSigNome.Text ?? "").Trim() == "") { Status("Informe o nome da tabela da sigla nova."); return; }
                        nova = new Dictionary<string, object> { ["sigla"] = sigla, ["nome"] = tbSigNome.Text.Trim(), ["tipo"] = "elemento" };
                    }
                    else sigla = (tag as Dictionary<string, object>)?.Txt("sigla");
                    if (string.IsNullOrEmpty(sigla)) { Status("Escolha a sigla do código novo."); return; }
                    if ((tbDesc.Text ?? "").Trim() == "") { Status("Informe a descrição do código novo."); return; }
                    LigarNovo(ts, sigla, tbDesc.Text, tbTexto.Text, nova, rt, rf);
                    return;
                }
                var item = (lista.SelectedItem as ListBoxItem)?.Tag as Dictionary<string, object>;
                if (item == null) { Status("Escolha um código na lista."); return; }
                Ligar(ts, item, rt, rf);
            }, true);
            bt.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(bt);
            spDet.Children.Add(cart);
        }

        // ------------------------------------------------------------ gravações (no contexto da API)

        DB.Document DocAtual(RUI.UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || Chave(doc) != docChave)
                throw new InvalidOperationException("O modelo ativo mudou. Clique em ↻ e tente de novo.");
            return doc;
        }

        void Transacao(DB.Document doc, string nome, Action corpo)
        {
            using (var t = new DB.Transaction(doc, nome))
            {
                t.Start();
                try { corpo(); t.Commit(); }
                catch { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); throw; }
            }
        }

        static void Avisos(string titulo, List<string> avisos)
        {
            if (avisos == null || avisos.Count == 0) return;
            var td = new RUI.TaskDialog("Componentes") { MainInstruction = titulo, MainContent = string.Join("\n", avisos.Take(25).Select(x => "• " + x)) };
            if (avisos.Count > 25) td.FooterText = "… e mais " + (avisos.Count - 25) + ".";
            td.Show();
        }

        void Ligar(List<TipoInfo> ts, Dictionary<string, object> item, bool renomear, bool renomearFamilia)
        {
            Pedir(app =>
            {
                var doc = DocAtual(app);
                List<string> av = null;
                Transacao(doc, "Componentes FJA: ligar a " + item.Txt("fja"), () => av = Operacoes.Ligar(doc, dados, ts, item, renomear, renomearFamilia));
                sujo = true;
                Status(ts.Count + " tipo(s) ligado(s) a " + item.Txt("fja") + ".");
                Avisos("Ligado a " + item.Txt("fja"), av);
            });
        }

        void Sincronizar(List<TipoInfo> ts, string alcance)
        {
            var alvo = ts.Where(t => Coleta.Sincronizavel(t.Sit) && t.Item != null).ToList();
            if (alvo.Count == 0) { Status("Nada a sincronizar em " + alcance + "."); return; }
            Pedir(app =>
            {
                var doc = DocAtual(app);
                int nRevit = alvo.Count(t => t.Sit == Situacao.Revit), nFora = alvo.Count(t => t.Sit == Situacao.Fora);
                var linhas = alvo.Take(14).Select(t => "• " + t.Familia + " : " + t.Nome + " — " +
                    (t.Sit == Situacao.Fora ? "entra no projeto (" + t.Fja + ")" : string.Join("; ", t.Difs.Select(d => d[0] + " → " + d[2])))).ToList();
                if (alvo.Count > 14) linhas.Add("… e mais " + (alvo.Count - 14) + ".");
                var td = new RUI.TaskDialog("Componentes")
                {
                    MainInstruction = "Sincronizar " + alvo.Count + " tipo(s) com o sistema?",
                    MainContent = string.Join("\n", linhas),
                    FooterText = (nRevit > 0 ? nRevit + " tipo(s) foram alterados no Revit: os valores do sistema prevalecem. " : "") +
                                 (nFora > 0 ? nFora + " tipo(s) usam itens fora da especificação do projeto: os itens entram no projeto (pedido ao sistema)." : ""),
                    CommonButtons = RUI.TaskDialogCommonButtons.Yes | RUI.TaskDialogCommonButtons.No,
                    DefaultButton = RUI.TaskDialogResult.Yes,
                    VerificationText = "Renomear os tipos como KEYNOTE - DESCRIÇÃO",
                };
                if (td.Show() != RUI.TaskDialogResult.Yes) return;
                bool renomear = td.WasVerificationChecked();
                List<string> av = null;
                Transacao(doc, "Componentes FJA: sincronizar", () => av = Operacoes.Sincronizar(doc, dados, alvo, renomear));
                sujo = true;
                Status(alvo.Count + " tipo(s) sincronizado(s).");
                Avisos("Sincronização concluída", av);
            });
        }

        void LigarNovo(List<TipoInfo> ts, string sigla, string desc, string texto, Dictionary<string, object> siglaNova, bool renomear, bool renomearFamilia)
        {
            Pedir(app =>
            {
                var doc = DocAtual(app);
                List<string> av = null;
                Transacao(doc, "Componentes FJA: código novo " + sigla, () =>
                    av = Operacoes.LigarNovo(doc, dados, ts, sigla, desc, texto, siglaNova, renomear, renomearFamilia));
                sujo = true;
                Status(ts.Count + " tipo(s) ligado(s) a um código novo de " + sigla + " (aguardando o sistema).");
                Avisos("Código novo", av);
            });
        }

        void CarregarRfa()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Família do Revit (*.rfa)|*.rfa", Title = "Carregar família no modelo" };
            if (dlg.ShowDialog() != true) return;
            string caminho = dlg.FileName;
            Pedir(app =>
            {
                var doc = DocAtual(app);
                DB.Family fam = null; bool ja = false;
                Transacao(doc, "Componentes FJA: carregar " + System.IO.Path.GetFileName(caminho), () => fam = Operacoes.CarregarFamilia(doc, caminho, out ja));
                string cat = fam.FamilyCategory?.Name ?? "";
                tbBusca.Text = ""; cbFiltro.SelectedIndex = 0;   // antes de escolher o nó: limpar redesenha a árvore
                selecionado = "f|" + cat + "|" + fam.Name;
                fechados.Remove("c|" + cat);
                abertos.Add(selecionado);
                sujo = true;
                Status((ja ? "Família recarregada: " : "Família carregada: ") + fam.Name + " (" + fam.GetFamilySymbolIds().Count + " tipo(s)). Ligue os tipos a um código.");
            });
        }

        void Localizar()
        {
            Pedir(app =>
            {
                var ui = app.ActiveUIDocument;
                if (ui == null) return;
                DB.ElementId tipo = null;
                foreach (var id in ui.Selection.GetElementIds())
                {
                    var e = ui.Document.GetElement(id);
                    if (e is DB.FamilySymbol fs) { tipo = fs.Id; break; }
                    if (e is DB.FamilyInstance fi) { tipo = fi.GetTypeId(); break; }
                }
                if (tipo == null) { Status("Selecione no modelo um elemento de família carregável."); return; }
                if (Chave(ui.Document) != docChave) sujo = true;
                tbBusca.Text = ""; cbFiltro.SelectedIndex = 0;   // antes de escolher o nó: limpar redesenha a árvore
                selecionado = "t|" + Coleta.Num(tipo);
                if (!sujo) Desenhar();
                if (selecionado == null && !sujo) Status("O tipo do elemento selecionado não está na árvore (família de anotação, detalhe ou in-place).");
            });
        }

        void Desligar(List<TipoInfo> ts)
        {
            Pedir(app =>
            {
                var doc = DocAtual(app);
                var r = RUI.TaskDialog.Show("Componentes", "Desligar " + ts[0].Familia + " : " + ts[0].Nome + " do sistema?\nKeynote, Type Mark e descrição ficam como estão.",
                    RUI.TaskDialogCommonButtons.Yes | RUI.TaskDialogCommonButtons.No);
                if (r != RUI.TaskDialogResult.Yes) return;
                Transacao(doc, "Componentes FJA: desligar", () => Operacoes.Desligar(doc, ts));
                sujo = true;
                Status("Ligação apagada.");
            });
        }

        void Selecionar(List<TipoInfo> ts)
        {
            var ids = ts.SelectMany(t => t.Instancias).ToList();
            Pedir(app =>
            {
                var ui = app.ActiveUIDocument;
                if (ui == null || Chave(ui.Document) != docChave) throw new InvalidOperationException("O modelo ativo mudou. Clique em ↻ e tente de novo.");
                ui.Selection.SetElementIds(ids.Where(i => ui.Document.GetElement(i) != null).ToList());
                Status(ids.Count + " instância(s) selecionada(s).");
            });
        }
    }
}
