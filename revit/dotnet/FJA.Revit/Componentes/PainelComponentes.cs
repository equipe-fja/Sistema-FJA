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
        readonly HashSet<string> fechados = new HashSet<string>(), abertos = new HashSet<string>();
        string selecionado;
        bool montando;

        // controles
        ComboBox cbFonte, cbFiltro;
        TextBlock lbProjeto;
        Button btConectar, btReconectar;
        TextBlock lbFonte, lbResumo, lbStatus;
        TextBox tbBusca;
        TreeView tv;
        StackPanel spDet;
        Button btSincTudo;

        static readonly Brush Verde = B(46, 115, 64), Ambar = B(138, 93, 5), Vermelho = B(179, 38, 30), Azul = B(47, 95, 138),
                              Cinza = B(143, 143, 140), Suave = B(94, 94, 92), Tinta = B(29, 29, 29);
        static readonly Brush Roxo = B(121, 86, 169);   // prévia: → envia ao sistema (← recebe = Azul)
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
            AplicarEstilo();
            Montar();
            // só vira o painel atual depois de montado (um erro na montagem não deixa um painel "atual" sem conteúdo)
            AppDomain.CurrentDomain.SetData(Registro.ATUAL, this);
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
            if (!dadosLidos) { if (!lendo) LerSistemaEmSegundoPlano(); return; }   // a leitura termina e marca "sujo" de novo
            sujo = false;
            try
            {
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

        DateTime lidoEm = DateTime.MinValue;

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
            // outro modelo e dados do sistema com mais de 5 minutos: relê (o sistema republica a biblioteca a cada alteração)
            if (k != docChave && dadosLidos && !lendo && (DateTime.Now - lidoEm).TotalMinutes > 5) LerSistemaEmSegundoPlano();
            if (k != docChave)
            {
                docChave = k; docTitulo = doc.Title;
                EscolherProjeto(doc);
            }
            tipos = Coleta.Tipos(doc, dados);
            Desenhar();
            Status(tipos.Count + " tipos de " + tipos.Select(t => t.Familia).Distinct().Count() + " famílias · " + doc.Title);
        }

        string projetoOrigem = "";

        /// <summary>Projeto do modelo (Nucleo/ProjetoDoModelo): o gravado no modelo ou, sem ele, o reconhecido por uma pista
        /// segura — nesse caso a ligação é gravada no modelo na hora (vale para as outras ferramentas e para a equipe).</summary>
        void EscolherProjeto(DB.Document doc)
        {
            if (dados == null) return;
            var proj = ProjetoDoModelo.Resolver(doc, dados.Projetos, out string origem, out bool gravado, out _);
            projetoOrigem = origem;
            if (proj != null && !gravado)
            {
                string erro = ProjetoDoModelo.GravarAgora(doc, proj);
                projetoOrigem += erro == "" ? " · ligação gravada no modelo" : " · ligação não gravada (" + erro + ")";
            }
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

        TextBlock txtAtu;
        bool lendo, pararGiro;

        static System.Windows.Shapes.Path Icone(string dados) => new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(dados), Stroke = Suave, StrokeThickness = 1.5, Width = 16, Height = 16, Stretch = Stretch.Uniform,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };

        Button BotaoIcone(UIElement conteudo, RoutedEventHandler clique, string dica)
        {
            var b = new Button { Content = conteudo, ToolTip = dica, Margin = new Thickness(2, 0, 0, 0), MinHeight = 28, Width = 28, Padding = new Thickness(0) };
            if (TryFindResource("Discreto") is Style st) b.Style = st;
            b.Click += clique;
            return b;
        }

        void Girar(bool on)
        {
            if (!(txtAtu?.RenderTransform is RotateTransform rt)) return;
            if (on) rt.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
            { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            else { rt.BeginAnimation(RotateTransform.AngleProperty, null); rt.Angle = 0; }
        }

        void Reler()
        {
            if (lendo) return;
            LerSistemaEmSegundoPlano();
        }

        /// <summary>Lê o sistema (rede) fora da linha da interface, com o ↻ girando; ao terminar, relê o modelo (no contexto da
        /// API, pelo ExternalEvent) e para o giro.</summary>
        void LerSistemaEmSegundoPlano()
        {
            lendo = true;
            Girar(true);
            bool dev = op.Dev;
            System.Threading.Tasks.Task.Run(() =>
            {
                DadosSistema d = null; string erro = "";
                try { d = DadosSistema.Carregar(dev); } catch (Exception e) { erro = e.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    dados = d; erroDados = erro; dadosLidos = true; lidoEm = DateTime.Now;
                    docChave = "";   // escolhe o projeto de novo com os dados novos
                    sujo = true; pararGiro = true;
                    Pedir(_ => { });
                }));
            });
        }

        Button Botao(string s, RoutedEventHandler clique, bool primario = false, string dica = null)
        {
            var b = new Button { Content = s, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), MinHeight = 26 };
            // "Discreto" é o estilo dos botões de ícone (largura fixa de 28): só para símbolos; os de texto ficam com o estilo padrão
            bool icone = !primario && s.Length <= 2;
            if ((primario || icone) && TryFindResource(primario ? "Primario" : "Discreto") is Style st) b.Style = st;
            if (icone) { b.FontFamily = new FontFamily("Segoe UI Symbol"); b.FontSize = 15; b.Padding = new Thickness(0); }
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
            // ↻ gira enquanto o sistema e o modelo são relidos
            txtAtu = new TextBlock
            {
                Text = "↻", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 15, Foreground = Suave,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var btAtu = BotaoIcone(txtAtu, (s, a) => Reler(), "Ler de novo o sistema e o modelo");
            DockPanel.SetDock(btAtu, Dock.Right);
            cab.Children.Add(btAtu);
            // ícones: localizar a seleção do modelo na árvore e carregar uma família (.rfa)
            var btLoc = BotaoIcone(Icone("M11,2 L11,6 M11,16 L11,20 M2,11 L6,11 M16,11 L20,11 M15,11 A4,4 0 1 1 7,11 A4,4 0 1 1 15,11 Z"),
                (s, a) => Localizar(), "Localizar seleção: mostra na árvore o tipo do elemento selecionado no modelo");
            DockPanel.SetDock(btLoc, Dock.Right);
            cab.Children.Add(btLoc);
            var btRfa = BotaoIcone(Icone("M2,6 L8,6 L10,8 L20,8 L20,18 L2,18 Z M11,10.5 L11,15.5 M8.5,13 L13.5,13"),
                (s, a) => CarregarRfa(), "Carregar família (.rfa) no modelo");
            DockPanel.SetDock(btRfa, Dock.Right);
            cab.Children.Add(btRfa);
            cbFonte = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 4, 0), ToolTip = "De onde vêm os dados do sistema" };
            cbFonte.Items.Add("Oficial"); cbFonte.Items.Add("Desenvolvimento");
            cbFonte.SelectedIndex = op.Dev ? 1 : 0;
            cbFonte.SelectionChanged += (s, a) => { op.Dev = cbFonte.SelectedIndex == 1; op.Gravar(); Reler(); };
            DockPanel.SetDock(cbFonte, Dock.Right);
            cab.Children.Add(cbFonte);
            cab.Children.Add(T("Componentes", Tinta, 15, true));
            Grid.SetRow(cab, 0); raiz.Children.Add(cab);

            // 1: projeto, resumo e sincronizar tudo
            var sp1 = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            lbFonte = T("", Suave, 11); lbFonte.TextWrapping = TextWrapping.Wrap;
            sp1.Children.Add(lbFonte);
            // projeto do modelo: sempre automático; sem projeto, "Conectar a um projeto…"; trocar é manutenção ("Reconectar…")
            var gProj = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            btConectar = Botao("Conectar a um projeto…", (s, a) => EscolherManual(false), true, "O modelo não foi reconhecido: escolha o projeto do sistema (a ligação fica gravada no modelo)");
            btConectar.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btConectar, Dock.Right);
            gProj.Children.Add(btConectar);
            // ícone de elo: manutenção (trocar ou desfazer a ligação com o projeto)
            btReconectar = BotaoIcone(Icone("M10,14 L14,10 M8.5,11.5 L6.5,13.5 A3,3 0 0 0 10.5,17.5 L12.5,15.5 M11.5,8.5 L13.5,6.5 A3,3 0 0 1 17.5,10.5 L15.5,12.5"),
                (s, a) => EscolherManual(true), "Reconectar (manutenção): trocar ou desfazer a ligação deste modelo com o projeto do sistema");
            btReconectar.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btReconectar, Dock.Right);
            gProj.Children.Add(btReconectar);
            lbProjeto = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
            gProj.Children.Add(lbProjeto);
            sp1.Children.Add(gProj);
            var lin = new DockPanel();
            // ícone: seta descendo do sistema para o modelo (o sistema é a referência), em todos os tipos com diferença
            btSincTudo = BotaoIcone(Icone("M11,2 L11,13 M7,9 L11,13 L15,9 M3,14 L3,19 L19,19 L19,14"),
                (s, a) => EntrarPrevia(), "Sincronizar tudo");
            btSincTudo.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btSincTudo, Dock.Right);
            lin.Children.Add(btSincTudo);
            lbResumo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            lbResumo.ToolTip = Legenda();   // o que cada cor quer dizer
            lin.Children.Add(lbResumo);
            sp1.Children.Add(lin);
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
            // barra das setas: ← traz do sistema; → leva ao sistema a Descrição (só tipos já no padrão, alterados no Revit)
            var linha2 = new StackPanel();
            linha2.Children.Add(g2);
            var barra = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            btLimpar = Botao("Limpar", (s, a) => { marcados.Clear(); AtualizarMarcas(); }, false, "Desmarca todos os tipos");
            btLimpar.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btLimpar, Dock.Right);
            barra.Children.Add(btLimpar);
            btLevar = Botao("→ Para o sistema", (s, a) => LevarAoSistema(Alvo()), false,
                "Leva ao sistema a Descrição alterada no Revit (só tipos já sincronizados antes, ligados a um item do projeto). Keynote e Type Mark não vão.");
            btLevar.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btLevar, Dock.Right);
            barra.Children.Add(btLevar);
            btTrazer = Botao("← Do sistema", (s, a) => Sincronizar(Alvo(), marcados.Count > 0 ? "os tipos marcados" : "esta seleção"), false,
                "Traz do sistema o keynote, o Type Mark e a descrição (o sistema é a referência)");
            btTrazer.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btTrazer, Dock.Right);
            barra.Children.Add(btTrazer);
            lbMarcados = T("", Suave, 11.5);
            barra.Children.Add(lbMarcados);
            linha2.Children.Add(barra);
            barraSetas = barra;
            // barra temporária da prévia do Sincronizar tudo: contagens, renomear, OK e Cancelar
            barraPrevia = new Border
            {
                Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 6, 8, 6),
                Background = B(247, 236, 234), BorderBrush = B(142, 27, 27), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            };
            var bp = new DockPanel();
            var btOk = Botao("OK", (s, a) => ConfirmarPrevia(), true, "Grava: ← recebe do sistema; → envia a Descrição ao sistema");
            btOk.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btOk, Dock.Right);
            bp.Children.Add(btOk);
            var btCan = Botao("Cancelar", (s, a) => SairPrevia(), false, "Volta sem gravar nada");
            btCan.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(btCan, Dock.Right);
            bp.Children.Add(btCan);
            var spInfo = new StackPanel();
            lbPrevia = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            lbPrevia.ToolTip = "← recebe do sistema: Keynote, Type Mark e Descrição do sistema (o sistema é a referência).\n→ envia ao sistema: a Descrição alterada no Revit (só tipos já no padrão); a descrição é do item central e muda em todos os projetos que usam o código. O tipo fica aguardando até o sistema aplicar.";
            spInfo.Children.Add(lbPrevia);
            ckRenPrevia = new CheckBox { Content = "Renomear os tipos como KEYNOTE - DESCRIÇÃO", IsChecked = op.Renomear, Margin = new Thickness(0, 4, 0, 0), FontSize = 11.5 };
            spInfo.Children.Add(ckRenPrevia);
            bp.Children.Add(spInfo);
            barraPrevia.Child = bp;
            linha2.Children.Add(barraPrevia);
            Grid.SetRow(linha2, 2); raiz.Children.Add(linha2);

            // 3: árvore
            tv = new TreeView { BorderThickness = new Thickness(1), BorderBrush = TryFindResource("Linha") as Brush ?? B(226, 226, 223), Background = Brushes.White };
            VirtualizingPanel.SetIsVirtualizing(tv, true);
            tv.SelectedItemChanged += (s, a) => { if (!montando) { selecionado = (tv.SelectedItem as TreeViewItem)?.Tag is No n ? n.Chave : null; MostrarDetalhe(); AtualizarMarcas(); } };
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
            fila.Pedir(app =>
            {
                try
                {
                    a(app);
                    if (sujo)
                    {
                        if (!dadosLidos) { if (!lendo) LerSistemaEmSegundoPlano(); }
                        else { sujo = false; Recarregar(app); }
                    }
                }
                finally { if (pararGiro) { pararGiro = false; lendo = false; Girar(false); } }
            });
            ev.Raise();
        }

        /// <summary>Conectar (modelo sem projeto) ou Reconectar (manutenção): escolha do projeto numa janela; a ligação
        /// fica gravada no modelo. No uso normal o projeto é sempre o reconhecido automaticamente.</summary>
        void EscolherManual(bool reconectar)
        {
            if (dados == null || docChave == "") return;
            var atual = dados.Projeto;
            var j = new Window
            {
                Title = reconectar ? "Reconectar o modelo a um projeto" : "Conectar o modelo a um projeto",
                Width = 460, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
                ResizeMode = ResizeMode.CanResize, Background = Background, FontFamily = FontFamily, FontSize = FontSize,
            };
            try { j.Owner = Window.GetWindow(this); } catch { }
            foreach (var d in Resources.MergedDictionaries) j.Resources.MergedDictionaries.Add(d);
            var raiz = new DockPanel { Margin = new Thickness(14) };
            var topo = new StackPanel();
            DockPanel.SetDock(topo, Dock.Top);
            topo.Children.Add(Paragrafo("Modelo: " + docTitulo, Tinta, 13));
            if (reconectar)
                topo.Children.Add(Paragrafo("Manutenção. O modelo está ligado a " + ((atual?.Txt("codigo") ?? "") + " · " + (atual?.Txt("nome") ?? "")).Trim(' ', '·') +
                    " (" + projetoOrigem + "). Trocar a ligação muda o projeto usado por todas as ferramentas neste modelo e, depois de sincronizar, para a equipe.", Ambar, 12));
            else
                topo.Children.Add(Paragrafo("O modelo não foi reconhecido (Número do projeto, pasta, nome do arquivo, código do cliente ou nome). Escolha o projeto: a ligação fica gravada no modelo.", Suave, 12));
            var busca = new TextBox { MinHeight = 26, Margin = new Thickness(0, 4, 0, 6), ToolTip = "Buscar por código ou nome" };
            topo.Children.Add(busca);
            raiz.Children.Add(topo);
            var rod = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(rod, Dock.Bottom);
            raiz.Children.Add(rod);
            var lista = new ListBox();
            raiz.Children.Add(lista);
            void Preencher()
            {
                lista.Items.Clear();
                string[] tm = Texto.NormSistema(busca.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in dados.Projetos.OrderBy(p => p.Txt("codigo") ?? ""))
                {
                    string rot = ((p.Txt("codigo") ?? "") + " · " + (p.Txt("nome") ?? "")).Trim(' ', '·');
                    if (!tm.All(Texto.NormSistema(rot + " " + p.Txt("codCliente")).Contains)) continue;
                    var li = new ListBoxItem { Content = rot + (p.Txt("semEspec") == "True" ? "   (sem especificação)" : ""), Tag = p, Padding = new Thickness(4, 3, 4, 3) };
                    if (atual != null && p.Txt("id") == atual.Txt("id")) li.FontWeight = FontWeights.SemiBold;
                    lista.Items.Add(li);
                }
            }
            busca.TextChanged += (s, a) => Preencher();
            Preencher();
            Dictionary<string, object> escolhido = null; bool desconectar = false, ok = false;
            if (reconectar)
            {
                var bd = Botao("Desconectar", (s, a) => { desconectar = true; ok = true; j.Close(); }, false, "Apaga a ligação: o modelo fica sem projeto");
                bd.Margin = new Thickness(0, 0, 18, 0);
                rod.Children.Add(bd);
            }
            rod.Children.Add(Botao("Cancelar", (s, a) => j.Close()));
            rod.Children.Add(Botao(reconectar ? "Reconectar" : "Conectar", (s, a) =>
            {
                escolhido = (lista.SelectedItem as ListBoxItem)?.Tag as Dictionary<string, object>;
                if (escolhido == null) return;
                if (reconectar && atual != null && escolhido.Txt("id") == atual.Txt("id")) { j.Close(); return; }
                ok = true; j.Close();
            }, true));
            lista.MouseDoubleClick += (s, a) => { if (lista.SelectedItem != null && !reconectar) { escolhido = (lista.SelectedItem as ListBoxItem)?.Tag as Dictionary<string, object>; ok = escolhido != null; j.Close(); } };
            j.Content = raiz;
            busca.Focus();
            j.ShowDialog();
            if (!ok) return;
            var proj = desconectar ? null : escolhido;
            Pedir(app =>
            {
                var doc = DocAtual(app);
                string erro = ProjetoDoModelo.GravarAgora(doc, proj);
                projetoOrigem = (proj == null ? "ligação desfeita" : reconectar ? "reconectado no painel" : "conectado no painel") + (erro == "" ? " · gravado no modelo" : " · não gravado (" + erro + ")");
                try { dados.CarregarProjeto(proj); erroDados = ""; }
                catch (Exception e) { erroDados = e.Message; try { dados.CarregarProjeto(null); } catch { } }
                sujo = true;
            });
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
                // fonte e origem do projeto ficam nas dicas; o texto só aparece para erro ou leitura
                cbFonte.ToolTip = dados != null ? dados.Rotulo + (dados.GeradoEm != "" ? " · publicado em " + dados.GeradoEm : "") : "De onde vêm os dados do sistema";
                lbFonte.Text = dados == null ? (dadosLidos ? "Sistema não lido: " + erroDados : "Lendo o sistema…") : erroDados;
                lbFonte.Visibility = lbFonte.Text == "" ? Visibility.Collapsed : Visibility.Visible;
                lbFonte.Foreground = dados == null && dadosLidos || erroDados != "" ? Vermelho : Suave;
                lbProjeto.ToolTip = "Projeto do modelo: " + projetoOrigem;
                var pj = dados?.Projeto;
                lbProjeto.Text = docChave == "" ? "" : pj != null ? ((pj.Txt("codigo") ?? "") + " · " + (pj.Txt("nome") ?? "")).Trim(' ', '·') : "Sem projeto";
                lbProjeto.Foreground = pj != null ? Tinta : Ambar;
                btConectar.Visibility = docChave != "" && dados != null && pj == null ? Visibility.Visible : Visibility.Collapsed;
                btReconectar.Visibility = docChave != "" && dados != null && pj != null ? Visibility.Visible : Visibility.Collapsed;

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
                btSincTudo.ToolTip = nSinc > 0
                    ? "Sincronizar tudo (" + nSinc + " tipo(s) com diferença): traz do sistema o keynote, o Type Mark e a descrição (o sistema é a referência)"
                    : "Sincronizar tudo: nenhum tipo com diferença";
                ToolTipService.SetShowOnDisabled(btSincTudo, true);
                if (btSincTudo.Content is System.Windows.Shapes.Path ps) ps.Stroke = nSinc > 0 ? Ambar : Cinza;

                // árvore
                string[] termos = Texto.NormSistema(tbBusca.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool buscando = termos.Length > 0 || cbFiltro.SelectedIndex > 0 || previa != null;
                // prévia do Sincronizar tudo: só os tipos afetados, tudo aberto
                var vis = previa != null ? tipos.Where(t => previa.ContainsKey(Coleta.Num(t.Id))).ToList()
                    : tipos.Where(t => PassaFiltro(t) && PassaBusca(t, termos)).ToList();
                visiveis_ = new HashSet<long>(vis.Select(t => Coleta.Num(t.Id)));
                tv.Items.Clear();
                caixas.Clear();
                marcados.RemoveWhere(id => !tipos.Any(t => Coleta.Num(t.Id) == id));
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
                        iff.IsExpanded = buscando && (termos.Length > 0 || previa != null) || abertos.Contains(nf.Chave);
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
            AtualizarMarcas();
        }

        TreeViewItem Item(No n, int visiveis)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            // caixa de seleção: as setas ← e → agem nos tipos marcados (família e categoria marcam todos os tipos delas)
            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Focusable = false };
            cb.Click += (s, a) =>
            {
                // família e categoria: só os tipos que aparecem na busca e no filtro atuais
                var lt = n.Nivel == 2 ? n.Tipos : n.Tipos.Where(x => visiveis_.Contains(Coleta.Num(x.Id))).ToList();
                bool todos = lt.All(t => marcados.Contains(Coleta.Num(t.Id)));
                foreach (var t in lt) { if (todos) marcados.Remove(Coleta.Num(t.Id)); else marcados.Add(Coleta.Num(t.Id)); }
                AtualizarMarcas();
            };
            caixas.Add(Tuple.Create(cb, n));
            sp.Children.Add(cb);
            if (previa != null) cb.Visibility = Visibility.Collapsed;
            if (n.Nivel == 2 && previa != null && previa.TryGetValue(Coleta.Num(n.Tipos[0].Id), out int dir))
            {
                // prévia: ← recebe do sistema (azul), → envia ao sistema (roxo)
                var tp = n.Tipos[0];
                var cor = dir == 2 ? Roxo : Azul;
                sp.Children.Add(T(dir == 2 ? "→ " : "← ", cor, 13, true));
                sp.Children.Add(T(tp.Nome, cor, 0, true));
                string o = dir == 2 ? "Descrição → sistema" : string.Join(", ", tp.Difs.Select(d => d[0])) + (tp.Sit == Situacao.Fora ? "entra no projeto" : "");
                if (o != "") sp.Children.Add(T("   " + o, Cinza, 11));
                sp.ToolTip = dir == 2 ? "Envia ao sistema a Descrição \"" + (tp.Desc ?? "").ToUpperInvariant() + "\" (" + tp.Fja + ")"
                    : "Recebe do sistema: " + string.Join("; ", tp.Difs.Select(d => d[0] + " → " + d[2])) + (tp.Sit == Situacao.Fora ? " (item entra na especificação do projeto)" : "");
            }
            else if (n.Nivel == 2)
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
            else if (previa != null)
            {
                int rec = n.Tipos.Count(t => previa.TryGetValue(Coleta.Num(t.Id), out int d) && d == 1), env = n.Tipos.Count(t => previa.TryGetValue(Coleta.Num(t.Id), out int d) && d == 2);
                sp.Children.Add(T(n.Titulo, Tinta, n.Nivel == 0 ? 13 : 0, n.Nivel == 0));
                if (rec > 0) sp.Children.Add(T("   ← " + rec, Azul, 11, true));
                if (env > 0) sp.Children.Add(T("   → " + env, Roxo, 11, true));
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

        // ------------------------------------------------------------ marcados e setas

        readonly HashSet<long> marcados = new HashSet<long>();
        HashSet<long> visiveis_ = new HashSet<long>();
        readonly List<Tuple<CheckBox, No>> caixas = new List<Tuple<CheckBox, No>>();
        TextBlock lbMarcados;
        Button btTrazer, btLevar, btLimpar;

        void AtualizarMarcas()
        {
            foreach (var c in caixas)
            {
                var lt = c.Item2.Nivel == 2 ? c.Item2.Tipos : c.Item2.Tipos.Where(x => visiveis_.Contains(Coleta.Num(x.Id))).ToList();
                int m = lt.Count(t => marcados.Contains(Coleta.Num(t.Id)));
                c.Item1.IsChecked = m == 0 ? false : m == lt.Count ? (bool?)true : null;
            }
            if (lbMarcados == null) return;
            var alvo = Alvo();
            lbMarcados.Text = marcados.Count > 0 ? marcados.Count + " marcado(s)" : "";
            int nT = alvo.Count(t => Coleta.Sincronizavel(t.Sit) && t.Item != null), nL = alvo.Count(Coleta.PodeLevar);
            btTrazer.IsEnabled = nT > 0 && dados != null;
            btTrazer.Content = "← Do sistema" + (nT > 0 ? " (" + nT + ")" : "");
            btLevar.IsEnabled = nL > 0 && dados != null;
            btLevar.Content = "→ Para o sistema" + (nL > 0 ? " (" + nL + ")" : "");
            btLimpar.Visibility = marcados.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Tipos em que as setas agem: os marcados; sem marcados, os do item escolhido na árvore.</summary>
        List<TipoInfo> Alvo() =>
            marcados.Count > 0 ? tipos.Where(t => marcados.Contains(Coleta.Num(t.Id))).ToList() : (NoSelecionado()?.Tipos ?? new List<TipoInfo>());

        /// <summary>Seta →: a Descrição do Revit vai para o sistema (só tipos já no padrão, alterados no Revit).</summary>
        void LevarAoSistema(List<TipoInfo> ts)
        {
            var alvo = ts.Where(Coleta.PodeLevar).ToList();
            if (alvo.Count == 0)
            {
                Status("→ vale só para tipos já sincronizados (ligados a um item do projeto) cuja Descrição foi alterada no Revit.");
                return;
            }
            Pedir(app =>
            {
                var doc = DocAtual(app);
                var linhas = alvo.GroupBy(t => t.Item.Txt("id")).Take(12).Select(g =>
                {
                    var t0 = g.First();
                    return "• " + t0.Fja + ": \"" + t0.DescSis + "\"  →  \"" + (t0.Desc ?? "").ToUpperInvariant() + "\"" + (g.Count() > 1 ? " (" + g.Count() + " tipos)" : "");
                }).ToList();
                var td = new RUI.TaskDialog("Componentes")
                {
                    MainInstruction = "Levar ao sistema a descrição de " + alvo.Select(t => t.Item.Txt("id")).Distinct().Count() + " código(s)?",
                    MainContent = string.Join("\n", linhas),
                    FooterText = "A descrição é do item da biblioteca central: muda em todos os projetos que usam o código. Keynote e Type Mark não vão (são numeração do sistema). O sistema aplica quando alguém estiver com ele aberto; até lá, os tipos ficam aguardando.",
                    CommonButtons = RUI.TaskDialogCommonButtons.Yes | RUI.TaskDialogCommonButtons.No,
                    DefaultButton = RUI.TaskDialogResult.No,
                };
                if (td.Show() != RUI.TaskDialogResult.Yes) return;
                List<string> av = null;
                Transacao(doc, "Componentes FJA: descrição para o sistema", () => av = Operacoes.LevarDescricao(doc, dados, alvo));
                sujo = true;
                Status("Descrição enviada ao sistema (aguardando).");
                Avisos("Para o sistema", av);
            });
        }

        // ------------------------------------------------------------ prévia do Sincronizar tudo

        /// <summary>Tipo → sentido na prévia: 1 = ← recebe do sistema, 2 = → envia a Descrição ao sistema. null fora da prévia.</summary>
        Dictionary<long, int> previa;
        DockPanel barraSetas;
        Border barraPrevia;
        TextBlock lbPrevia;
        CheckBox ckRenPrevia;

        /// <summary>Sincronizar tudo: antes de gravar, a árvore mostra só os tipos afetados — ← azul recebe do sistema,
        /// → roxo envia a Descrição ao sistema (regra da seta →) — e aparecem OK e Cancelar.</summary>
        void EntrarPrevia()
        {
            if (dados == null) return;
            var p = new Dictionary<long, int>();
            foreach (var t in tipos)
            {
                if (Coleta.PodeLevar(t)) p[Coleta.Num(t.Id)] = 2;
                else if (Coleta.Sincronizavel(t.Sit) && t.Item != null) p[Coleta.Num(t.Id)] = 1;
            }
            if (p.Count == 0) { Status("Nada a sincronizar."); return; }
            previa = p;
            int rec = p.Values.Count(v => v == 1), env = p.Values.Count(v => v == 2);
            lbPrevia.Inlines.Clear();
            lbPrevia.Inlines.Add(new System.Windows.Documents.Run("Sincronizar tudo:  "));
            if (rec > 0) lbPrevia.Inlines.Add(new System.Windows.Documents.Run("← " + rec + " recebe(m) do sistema   ") { Foreground = Azul });
            if (env > 0) lbPrevia.Inlines.Add(new System.Windows.Documents.Run("→ " + env + " envia(m) ao sistema") { Foreground = Roxo });
            ckRenPrevia.IsChecked = op.Renomear;
            ModoPrevia(true);
            Desenhar();
        }

        void ModoPrevia(bool on)
        {
            barraPrevia.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            barraSetas.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            foreach (var c in new Control[] { tbBusca, cbFiltro, cbFonte, btSincTudo }) c.IsEnabled = !on;
        }

        void SairPrevia()
        {
            previa = null;
            ModoPrevia(false);
            Desenhar();
        }

        void ConfirmarPrevia()
        {
            if (previa == null) return;
            var p = previa;
            var rec = tipos.Where(t => p.TryGetValue(Coleta.Num(t.Id), out int d) && d == 1).ToList();
            var env = tipos.Where(t => p.TryGetValue(Coleta.Num(t.Id), out int d) && d == 2).ToList();
            bool renomear = ckRenPrevia.IsChecked == true;
            op.Renomear = renomear; op.Gravar();
            SairPrevia();
            Pedir(app =>
            {
                var doc = DocAtual(app);
                var av = new List<string>();
                Transacao(doc, "Componentes FJA: sincronizar tudo", () =>
                {
                    if (rec.Count > 0) av.AddRange(Operacoes.Sincronizar(doc, dados, rec, renomear));
                    if (env.Count > 0) av.AddRange(Operacoes.LevarDescricao(doc, dados, env));
                });
                sujo = true;
                Status(rec.Count + " tipo(s) receberam do sistema; " + env.Count + " enviaram a Descrição ao sistema.");
                Avisos("Sincronizar tudo", av);
            });
        }

        // ------------------------------------------------------------ detalhes e ações

        void MostrarDetalhe()
        {
            if (spDet == null) return;
            spDet.Children.Clear();
            var n = NoSelecionado();
            if (n == null)
            {
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
            if (t.Sit == Situacao.Fora) sit.ToolTip = "O item não está na especificação do projeto. ← Do sistema inclui o item no projeto com o próximo keynote da sigla (o sistema confirma).";
            if (t.Sit == Situacao.Revit) sit.ToolTip = "Os valores foram alterados no Revit depois da última sincronização. ← Do sistema devolve os do sistema; → Para o sistema leva a Descrição.";
            spDet.Children.Add(sit);
            spDet.Children.Add(Linha("Categoria", t.Categoria));
            spDet.Children.Add(Linha("Instâncias", t.Instancias.Count.ToString()));
            spDet.Children.Add(Linha("Keynote", t.Kn));
            spDet.Children.Add(Linha("Type Mark", t.Tm));
            spDet.Children.Add(Linha("Descrição", t.Desc));
            if (t.Ligado)
            {
                spDet.Children.Add(Linha("Código", t.Fja + (t.Item != null ? " · " + (t.Item.Txt("desc") ?? "") : "")));
                foreach (var d in t.Difs)
                    spDet.Children.Add(Linha(d[0], (string.IsNullOrEmpty(d[1]) ? "(vazio)" : d[1]) + "  →  " + d[2], Ambar));
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
            if (previa != null) return;   // na prévia do Sincronizar tudo, só OK ou Cancelar
            var wp = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
            int nSinc = ts.Count(t => Coleta.Sincronizavel(t.Sit));
            // seta sugerida em destaque: alterado no Revit (e elegível) → "Para o sistema"; senão "Do sistema"
            bool sugereLevar = umTipo && Coleta.PodeLevar(ts[0]);
            if (nSinc > 0 && dados != null)
                wp.Children.Add(Botao(umTipo ? "← Do sistema" : "← Do sistema (" + nSinc + ")", (s, a) => Sincronizar(ts, umTipo ? "este tipo" : "esta seleção"), !sugereLevar,
                    "Traz para o modelo o keynote, o Type Mark e a descrição do sistema"));
            int nLev = ts.Count(Coleta.PodeLevar);
            if (nLev > 0 && dados != null)
                wp.Children.Add(Botao(umTipo ? "→ Para o sistema" : "→ Para o sistema (" + nLev + ")", (s, a) => LevarAoSistema(ts), sugereLevar || nSinc == 0,
                    "Leva ao sistema a Descrição alterada no Revit (o código é do item central: muda em todos os projetos)"));
            int ni = ts.Sum(t => t.Instancias.Count);
            if (ni > 0)
                wp.Children.Add(Botao("Selecionar no modelo", (s, a) => Selecionar(ts), false, ni + " instância(s)"));
            if (umTipo && ts[0].Ligado)
                wp.Children.Add(Botao("Desligar", (s, a) => Desligar(ts), false, "Apaga a ligação com o sistema (os parâmetros ficam como estão)"));
            if (wp.Children.Count > 0) spDet.Children.Add(wp);
        }

        void BlocoLigar(List<TipoInfo> ts, string familia)
        {
            if (previa != null) return;
            var cart = new Border { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10), Background = Brushes.White,
                BorderBrush = TryFindResource("Linha") as Brush ?? B(226, 226, 223), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            var sp = new StackPanel();
            cart.Child = sp;
            var titLig = T(ts.Count == 1 ? "Ligar a um código do sistema" : "Ligar os " + ts.Count + " tipos a um código", Tinta, 0, true);
            titLig.ToolTip = "Itens de elemento/família da biblioteca central. O código fica no Keynote e no Type Mark; fora da especificação do projeto, entra com o próximo keynote da sigla. No código novo, o FJ e o keynote são reservados agora e confirmados pelo sistema.";
            sp.Children.Add(titLig);
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
