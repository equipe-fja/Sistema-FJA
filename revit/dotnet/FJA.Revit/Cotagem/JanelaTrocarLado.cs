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
using FJA.Revit.Nucleo;
using WColor = System.Windows.Media.Color;

namespace FJA.Revit.Cotagem
{
    /// <summary>O que fazer com as indicações (bolhas/cabeças) de um grupo de linhas.</summary>
    public enum AcaoLado { NaoMexer, LadoA, LadoB, DoisLados, Trocar }

    /// <summary>Eixo ou nível como aparece na vista ativa.</summary>
    public sealed class DatumNaVista
    {
        public DatumPlane D;
        public bool Vertical;        // linha vertical na tela (indicações em cima/embaixo); senão horizontal (esquerda/direita)
        public DatumEnds PontaA;     // ponta de cima (vertical) ou da esquerda (horizontal)
        public DatumEnds PontaB;     // ponta de baixo ou da direita
        public bool A, B;            // indicação ligada em cada ponta
        public bool Selecionado;
    }

    /// <summary>Preferências da pessoa: %APPDATA%\FJA\trocar-lado.json.</summary>
    public sealed class OpcoesTrocarLado
    {
        public AcaoLado Verticais = AcaoLado.Trocar, Horizontais = AcaoLado.Trocar;
        public bool TodosDaVista;

        static string Arquivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "trocar-lado.json");

        public static OpcoesTrocarLado Carregar()
        {
            var o = new OpcoesTrocarLado();
            try
            {
                if (!File.Exists(Arquivo)) return o;
                var d = Json.LerObjeto(File.ReadAllText(Arquivo, Encoding.UTF8));
                if (Enum.TryParse(d.Txt("verticais") ?? "", out AcaoLado v)) o.Verticais = v;
                if (Enum.TryParse(d.Txt("horizontais") ?? "", out AcaoLado h)) o.Horizontais = h;
                o.TodosDaVista = d.Txt("todosDaVista") == "True" || d.Txt("todosDaVista") == "true";
            }
            catch { }
            return o;
        }

        public void Gravar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                File.WriteAllText(Arquivo, Json.Gravar(new Dictionary<string, object>
                {
                    ["verticais"] = Verticais.ToString(),
                    ["horizontais"] = Horizontais.ToString(),
                    ["todosDaVista"] = TodosDaVista ? "true" : "false",
                }), Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>
    /// Janela do Trocar lado: alcance (selecionados ou todos da vista) e, para as linhas verticais e as horizontais
    /// na tela, ligar tudo de um lado, tudo do outro, dos dois lados, trocar ou não mexer. Montada em código,
    /// estilo de lib/fja_estilo.xaml (como a JanelaCotagem).
    /// </summary>
    public sealed class JanelaTrocarLado : Window
    {
        readonly List<DatumNaVista> todos;
        readonly OpcoesTrocarLado o;
        public bool Confirmado { get; private set; }

        RadioButton rbSel, rbTodos;
        readonly RadioButton[] rbVert = new RadioButton[5], rbHor = new RadioButton[5];
        TextBlock txVert, txHor, txErro;
        StackPanel spVert, spHor;
        Button btAplicar;

        public JanelaTrocarLado(List<DatumNaVista> todos, string nomeVista, OpcoesTrocarLado o)
        {
            this.todos = todos; this.o = o;
            Title = "Trocar lado da indicação";
            Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            AplicarEstilo();
            Montar(nomeVista);
            Atualizar();
            PreviewKeyDown += (s, a) =>
            {
                if (a.Key == Key.Escape) Close();
                else if (a.Key == Key.Enter && btAplicar.IsEnabled) Confirmar();
            };
        }

        /// <summary>As linhas que entram, conforme o alcance escolhido.</summary>
        public List<DatumNaVista> Alvo => rbTodos.IsChecked == true ? todos : todos.Where(d => d.Selecionado).ToList();

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
            var b = new Border { Margin = new Thickness(0, 0, 0, 12) };
            if (Rec("Cartao") is Style st) { b.Style = st; b.Margin = new Thickness(0, 0, 0, 12); }
            else { b.Background = Brushes.White; b.BorderBrush = Pincel("Linha", 226, 226, 223); b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(6); }
            return b;
        }

        Button Botao(string texto, string estilo)
        {
            var b = new Button { Content = texto, Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
            if (Rec(estilo) is Style st) b.Style = st;
            return b;
        }

        TextBlock Titulo(string t) => new TextBlock { Text = t, FontWeight = FontWeights.SemiBold };

        TextBlock Nota(string t) => new TextBlock
        {
            Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0),
            Foreground = Pincel("Fraco", 143, 143, 140),
        };

        void Montar(string nomeVista)
        {
            var raiz = new StackPanel { Margin = new Thickness(16) };
            Content = raiz;
            raiz.Children.Add(new TextBlock { Text = "Vista ativa: " + nomeVista, Margin = new Thickness(0, 0, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Pincel("Suave", 94, 94, 92) });

            // ---- alcance
            int nSel = todos.Count(d => d.Selecionado);
            var cA = Cartao();
            var spA = new StackPanel { Margin = new Thickness(12) };
            spA.Children.Add(Titulo("Quais eixos e níveis"));
            rbSel = new RadioButton { Content = "Os selecionados (" + nSel + ")", GroupName = "alc", Margin = new Thickness(0, 8, 0, 0), IsEnabled = nSel > 0 };
            rbTodos = new RadioButton { Content = "Todos os visíveis nesta vista (" + todos.Count + ")", GroupName = "alc", Margin = new Thickness(0, 6, 0, 0) };
            if (nSel > 0 && !o.TodosDaVista) rbSel.IsChecked = true; else rbTodos.IsChecked = true;
            rbSel.Checked += (s, a) => Atualizar();
            rbTodos.Checked += (s, a) => Atualizar();
            spA.Children.Add(rbSel); spA.Children.Add(rbTodos);
            cA.Child = spA;
            raiz.Children.Add(cA);

            // ---- grupos
            raiz.Children.Add(Grupo("Linhas verticais na tela", new[] { "Em cima", "Embaixo" }, rbVert, o.Verticais, "v", out txVert, out spVert));
            raiz.Children.Add(Grupo("Linhas horizontais na tela", new[] { "À esquerda", "À direita" }, rbHor, o.Horizontais, "h", out txHor, out spHor));

            raiz.Children.Add(Nota("As pontas passam para 2D nesta vista antes da mudança, para não alterar as outras vistas. Ctrl+Z desfaz."));

            // ---- rodapé
            var rod = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var bts = new StackPanel { Orientation = Orientation.Horizontal };
            var btCancelar = Botao("Cancelar", "Discreto");
            btCancelar.Click += (s, a) => Close();
            btAplicar = Botao("Aplicar", "Primario");
            btAplicar.Margin = new Thickness(8, 0, 0, 0);
            btAplicar.Click += (s, a) => Confirmar();
            bts.Children.Add(btCancelar); bts.Children.Add(btAplicar);
            DockPanel.SetDock(bts, Dock.Right);
            rod.Children.Add(bts);
            txErro = new TextBlock { Foreground = new SolidColorBrush(WColor.FromRgb(176, 58, 46)), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            rod.Children.Add(txErro);
            raiz.Children.Add(rod);
        }

        Border Grupo(string titulo, string[] lados, RadioButton[] rbs, AcaoLado atual, string nomeGrupo, out TextBlock resumo, out StackPanel corpo)
        {
            var c = Cartao();
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(Titulo(titulo));
            resumo = new TextBlock { Margin = new Thickness(0, 2, 0, 6), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Pincel("Suave", 94, 94, 92) };
            sp.Children.Add(resumo);
            string[] textos = { "Não mexer", "Ligar tudo " + lados[0].ToLowerInvariant(), "Ligar tudo " + lados[1].ToLowerInvariant(), "Ligar dos dois lados", "Trocar de lado" };
            var ordem = new[] { AcaoLado.LadoA, AcaoLado.LadoB, AcaoLado.Trocar, AcaoLado.DoisLados, AcaoLado.NaoMexer };
            var wrap = new WrapPanel();
            foreach (var a in ordem)
            {
                var rb = new RadioButton { Content = textos[(int)a], GroupName = nomeGrupo, Margin = new Thickness(0, 4, 18, 0), IsChecked = a == atual };
                rb.Checked += (s, e) => Atualizar();
                rbs[(int)a] = rb;
                wrap.Children.Add(rb);
            }
            sp.Children.Add(wrap);
            sp.Children.Add(Nota(nomeGrupo == "v"
                ? "Trocar de lado: só nas linhas com indicação de um lado; as com indicação nos dois lados, ou sem nenhuma, ficam como estão."
                : "Níveis em cortes e fachadas, e eixos horizontais nas plantas."));
            c.Child = sp;
            corpo = sp;
            return c;
        }

        static AcaoLado Escolha(RadioButton[] rbs)
        {
            for (int i = 0; i < rbs.Length; i++) if (rbs[i]?.IsChecked == true) return (AcaoLado)i;
            return AcaoLado.NaoMexer;
        }

        public AcaoLado Verticais => Escolha(rbVert);
        public AcaoLado Horizontais => Escolha(rbHor);

        static string Resumo(List<DatumNaVista> g, string a, string b)
        {
            if (g.Count == 0) return "Nenhuma no alcance escolhido.";
            int eixos = g.Count(d => d.D is Autodesk.Revit.DB.Grid), niveis = g.Count - eixos;
            var qual = new List<string>();
            if (eixos > 0) qual.Add(eixos + (eixos == 1 ? " eixo" : " eixos"));
            if (niveis > 0) qual.Add(niveis + (niveis == 1 ? " nível" : " níveis"));
            var est = new List<string>();
            int sa = g.Count(d => d.A && !d.B), sb = g.Count(d => d.B && !d.A), dois = g.Count(d => d.A && d.B), nen = g.Count(d => !d.A && !d.B);
            if (sa > 0) est.Add(sa + " " + a);
            if (sb > 0) est.Add(sb + " " + b);
            if (dois > 0) est.Add(dois + " nos dois lados");
            if (nen > 0) est.Add(nen + " sem indicação");
            return string.Join(" e ", qual) + " — hoje: " + string.Join(", ", est) + ".";
        }

        void Atualizar()
        {
            if (txVert == null || txHor == null || btAplicar == null) return;
            var alvo = Alvo;
            var v = alvo.Where(d => d.Vertical).ToList();
            var h = alvo.Where(d => !d.Vertical).ToList();
            txVert.Text = Resumo(v, "em cima", "embaixo");
            txHor.Text = Resumo(h, "à esquerda", "à direita");
            spVert.IsEnabled = v.Count > 0; spVert.Opacity = v.Count > 0 ? 1 : 0.55;
            spHor.IsEnabled = h.Count > 0; spHor.Opacity = h.Count > 0 ? 1 : 0.55;

            string erro = "";
            bool algo = (v.Count > 0 && Verticais != AcaoLado.NaoMexer) || (h.Count > 0 && Horizontais != AcaoLado.NaoMexer);
            if (alvo.Count == 0) erro = "Nenhum eixo ou nível no alcance escolhido.";
            else if (!algo) erro = "Escolha o que fazer em pelo menos um dos grupos.";
            txErro.Text = erro;
            btAplicar.IsEnabled = erro == "";
        }

        void Confirmar()
        {
            o.Verticais = Verticais;
            o.Horizontais = Horizontais;
            o.TodosDaVista = rbTodos.IsChecked == true;
            Confirmado = true;
            Close();
        }
    }
}
