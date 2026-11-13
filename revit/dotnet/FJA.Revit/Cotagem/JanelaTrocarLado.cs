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
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using FJA.Revit.Nucleo;
using WColor = System.Windows.Media.Color;
using WLine = System.Windows.Shapes.Line;

namespace FJA.Revit.Cotagem
{
    /// <summary>O que fazer com as indicações (bolhas/cabeças) de um grupo.</summary>
    public enum AcaoLado { NaoMexer, LadoA, LadoB, DoisLados, Trocar }

    /// <summary>Eixo ou nível selecionado, como aparece na vista ativa.</summary>
    public sealed class DatumNaVista
    {
        public DatumPlane D;
        public bool Eixo;            // Grid (senão Level)
        public DatumEnds PontaA;     // ponta de cima (linha vertical na tela) ou da esquerda (horizontal)
        public DatumEnds PontaB;     // ponta de baixo ou da direita
        public bool A, B;            // indicação ligada em cada ponta
    }

    /// <summary>Preferências da pessoa: %APPDATA%\FJA\trocar-lado.json.</summary>
    public sealed class OpcoesTrocarLado
    {
        public AcaoLado Eixos = AcaoLado.Trocar, Niveis = AcaoLado.Trocar;

        static string Arquivo => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "trocar-lado.json");

        public static OpcoesTrocarLado Carregar()
        {
            var o = new OpcoesTrocarLado();
            try
            {
                if (!File.Exists(Arquivo)) return o;
                var d = Json.LerObjeto(File.ReadAllText(Arquivo, Encoding.UTF8));
                if (Enum.TryParse(d.Txt("eixos") ?? "", out AcaoLado e)) o.Eixos = e;
                if (Enum.TryParse(d.Txt("niveis") ?? "", out AcaoLado n)) o.Niveis = n;
            }
            catch { }
            return o;
        }

        public void Gravar()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Arquivo));
                File.WriteAllText(Arquivo, Json.Gravar(new Dictionary<string, object>
                {
                    ["eixos"] = Eixos.ToString(),
                    ["niveis"] = Niveis.ToString(),
                }), Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>
    /// Janela do Trocar lado (2.23.0): para os eixos e os níveis selecionados, um cartão com as opções em
    /// blocos desenhados (um lado, o outro, os dois, trocar, não mexer). Estética do Sistema FJA
    /// (lib/fja_estilo.xaml): fundo claro, cartões brancos, vermelho FJ na escolha.
    /// </summary>
    public sealed class JanelaTrocarLado : Window
    {
        readonly OpcoesTrocarLado o;
        public bool Confirmado { get; private set; }
        public AcaoLado Eixos { get; private set; }
        public AcaoLado Niveis { get; private set; }

        readonly int nEixos, nNiveis;
        Button btAplicar;

        sealed class Bloco { public Border B; public AcaoLado A; public List<System.Windows.Shapes.Shape> Formas = new List<System.Windows.Shapes.Shape>(); public TextBlock T; }
        readonly List<Bloco> blocosE = new List<Bloco>(), blocosN = new List<Bloco>();

        public JanelaTrocarLado(List<DatumNaVista> sel, string nomeVista, OpcoesTrocarLado o)
        {
            this.o = o;
            nEixos = sel.Count(d => d.Eixo);
            nNiveis = sel.Count - nEixos;
            Eixos = nEixos > 0 ? o.Eixos : AcaoLado.NaoMexer;
            Niveis = nNiveis > 0 ? o.Niveis : AcaoLado.NaoMexer;

            Title = "Trocar lado";
            Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            AplicarEstilo();
            Montar(nomeVista);
            Pintar();
            PreviewKeyDown += (s, a) =>
            {
                if (a.Key == Key.Escape) Close();
                else if (a.Key == Key.Enter && btAplicar.IsEnabled) Confirmar();
            };
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
                Background = Cor(246, 246, 245);
                FontFamily = new FontFamily("Segoe UI");
                FontSize = 13;
            }
        }

        static SolidColorBrush Cor(byte r, byte g, byte b) => new SolidColorBrush(WColor.FromRgb(r, g, b));
        Brush Pincel(string k, byte r, byte g, byte b) => TryFindResource(k) as Brush ?? Cor(r, g, b);
        Brush Tinta => Pincel("Tinta", 29, 29, 29);
        Brush Suave => Pincel("Suave", 94, 94, 92);
        Brush Fraco => Pincel("Fraco", 143, 143, 140);
        Brush Linha => Pincel("Linha", 226, 226, 223);
        Brush Painel => Pincel("Painel", 255, 255, 255);
        Brush Hover => Pincel("Hover", 241, 240, 238);
        Brush Destaque => Pincel("Destaque", 142, 27, 27);
        Brush DestaqueFraco => Pincel("DestaqueFraco", 247, 236, 234);

        // ------------------------------------------------------------ montagem

        void Montar(string nomeVista)
        {
            var raiz = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
            Content = raiz;

            raiz.Children.Add(new TextBlock { Text = "Trocar lado", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Tinta });
            raiz.Children.Add(new TextBlock { Text = nomeVista, FontSize = 12, Foreground = Suave, Margin = new Thickness(0, 2, 0, 14), TextTrimming = TextTrimming.CharacterEllipsis });

            if (nEixos > 0)
                raiz.Children.Add(Cartao("Eixos", nEixos, true, blocosE,
                    new[] { (AcaoLado.LadoA, "Em cima"), (AcaoLado.LadoB, "Embaixo"), (AcaoLado.DoisLados, "Nos dois"), (AcaoLado.Trocar, "Trocar"), (AcaoLado.NaoMexer, "Não mexer") }));
            if (nNiveis > 0)
                raiz.Children.Add(Cartao("Níveis", nNiveis, false, blocosN,
                    new[] { (AcaoLado.LadoA, "À esquerda"), (AcaoLado.LadoB, "À direita"), (AcaoLado.DoisLados, "Nos dois"), (AcaoLado.Trocar, "Trocar"), (AcaoLado.NaoMexer, "Não mexer") }));

            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            var btCancelar = new Button { Content = "Cancelar", Padding = new Thickness(16, 0, 16, 0), MinHeight = 34 };
            btCancelar.Click += (s, a) => Close();
            btAplicar = new Button { Content = "Aplicar", Padding = new Thickness(22, 0, 22, 0), MinHeight = 34, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
            if (TryFindResource("Primario") is Style st) btAplicar.Style = st;
            btAplicar.Click += (s, a) => Confirmar();
            bts.Children.Add(btCancelar); bts.Children.Add(btAplicar);
            raiz.Children.Add(bts);
        }

        Border Cartao(string titulo, int qtd, bool eixo, List<Bloco> blocos, (AcaoLado a, string t)[] opcoes)
        {
            var c = new Border { Margin = new Thickness(0, 0, 0, 12) };
            if (TryFindResource("Cartao") is Style st) { c.Style = st; c.Margin = new Thickness(0, 0, 0, 12); }
            else { c.Background = Painel; c.BorderBrush = Linha; c.BorderThickness = new Thickness(1); c.CornerRadius = new CornerRadius(8); }

            var sp = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            var cab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            cab.Children.Add(new TextBlock { Text = titulo, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Tinta, VerticalAlignment = VerticalAlignment.Center });
            cab.Children.Add(new Border
            {
                Background = DestaqueFraco, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = qtd.ToString(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Destaque },
            });
            sp.Children.Add(cab);

            var linha = new UniformGrid5();
            foreach (var (a, t) in opcoes)
            {
                var bl = NovoBloco(a, t, eixo);
                blocos.Add(bl);
                linha.Children.Add(bl.B);
            }
            sp.Children.Add(linha);
            c.Child = sp;
            return c;
        }

        /// <summary>Cinco colunas iguais (UniformGrid sem depender de Primitives no using).</summary>
        sealed class UniformGrid5 : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid5() { Columns = 5; } }

        Bloco NovoBloco(AcaoLado a, string texto, bool eixo)
        {
            var bl = new Bloco { A = a };
            var cv = new Canvas { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center };
            Desenhar(cv, bl, a);
            if (!eixo) cv.RenderTransform = new RotateTransform(-90, 22, 22);   // nível: o "em cima" vira "à esquerda"

            bl.T = new TextBlock { Text = texto, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            var sp = new StackPanel();
            sp.Children.Add(cv); sp.Children.Add(bl.T);

            bl.B = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), Padding = new Thickness(4, 10, 4, 8),
                Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, SnapsToDevicePixels = true,
            };
            bl.B.MouseEnter += (s, e) => { if (!Escolhido(bl, eixo)) bl.B.Background = Hover; };
            bl.B.MouseLeave += (s, e) => Pintar();
            bl.B.MouseLeftButtonUp += (s, e) => { if (eixo) Eixos = a; else Niveis = a; Pintar(); };
            return bl;
        }

        bool Escolhido(Bloco bl, bool eixo) => (eixo ? Eixos : Niveis) == bl.A;

        /// <summary>Ícone: a linha do eixo (vertical) com as bolhas; para os níveis o desenho é girado.</summary>
        void Desenhar(Canvas cv, Bloco bl, AcaoLado a)
        {
            bool cima = a == AcaoLado.LadoA || a == AcaoLado.DoisLados || a == AcaoLado.Trocar;
            bool baixo = a == AcaoLado.LadoB || a == AcaoLado.DoisLados || a == AcaoLado.Trocar;
            const double x = 22, r = 6.5;
            double y0 = cima ? 3 + 2 * r : 4, y1 = baixo ? 41 - 2 * r : 40;
            var ln = new WLine { X1 = x, Y1 = y0, X2 = x, Y2 = y1, StrokeThickness = 1.6 };
            if (a == AcaoLado.NaoMexer) ln.StrokeDashArray = new DoubleCollection { 3, 2 };
            cv.Children.Add(ln); bl.Formas.Add(ln);
            void Bolha(double cy, bool tracejada)
            {
                var el = new System.Windows.Shapes.Ellipse { Width = 2 * r, Height = 2 * r, StrokeThickness = 1.6, Fill = Brushes.Transparent };
                if (tracejada) el.StrokeDashArray = new DoubleCollection { 2, 1.5 };
                Canvas.SetLeft(el, x - r); Canvas.SetTop(el, cy - r);
                cv.Children.Add(el); bl.Formas.Add(el);
            }
            if (cima) Bolha(3 + r, a == AcaoLado.Trocar);
            if (baixo) Bolha(41 - r, false);
            if (a == AcaoLado.Trocar)
            {
                // seta curva ao lado: de baixo para cima
                var seta = new System.Windows.Shapes.Path
                {
                    StrokeThickness = 1.5, Fill = Brushes.Transparent,
                    Data = System.Windows.Media.Geometry.Parse("M 31,33 C 39,28 39,16 31,11 M 31,11 L 35.5,11.5 M 31,11 L 32.5,15"),
                };
                cv.Children.Add(seta); bl.Formas.Add(seta);
            }
        }

        void Pintar()
        {
            foreach (var (lista, atual) in new[] { (blocosE, Eixos), (blocosN, Niveis) })
                foreach (var bl in lista)
                {
                    bool sel = bl.A == atual;
                    bl.B.Background = sel ? DestaqueFraco : Painel;
                    bl.B.BorderBrush = sel ? Destaque : Linha;
                    bl.T.Foreground = sel ? Destaque : Suave;
                    bl.T.FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal;
                    var traco = sel ? Destaque : (bl.A == AcaoLado.NaoMexer ? Fraco : Tinta);
                    foreach (var f in bl.Formas) f.Stroke = traco;
                }
            if (btAplicar != null) btAplicar.IsEnabled = Eixos != AcaoLado.NaoMexer || Niveis != AcaoLado.NaoMexer;
        }

        void Confirmar()
        {
            if (nEixos > 0) o.Eixos = Eixos;
            if (nNiveis > 0) o.Niveis = Niveis;
            Confirmado = true;
            Close();
        }
    }
}
