using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WColor = System.Windows.Media.Color;

namespace FJA.Revit.Nucleo
{
    /// <summary>Seletor de cor da FJA (o mesmo desenho do Sistema FJA): área de cor e matiz, HEX/RGB,
    /// conta-gotas (qualquer ponto da tela), cores do projeto, padrão FJA, neutras, recentes e aviso de
    /// contraste com fundo branco.</summary>
    public sealed class SeletorCor : Window
    {
        /// <summary>Uma amostra: código curto (ou vazio), descrição e cor.</summary>
        public sealed class Amostra
        {
            public string Codigo, Dica;
            public byte[] Cor;
            public Amostra(string codigo, string dica, byte[] cor) { Codigo = codigo; Dica = dica; Cor = cor; }
        }

        // mesmas cores-padrão do Sistema FJA (CORES_PADRAO)
        static readonly (string, string, string)[] Padrao =
        {
            ("ARQ", "Arquitetura", "#bdbdbd"), ("EST", "Estruturas", "#7b8794"), ("HID", "Instalações hidráulicas", "#1e88e5"),
            ("ESG", "Esgoto", "#6d4c41"), ("DRE", "Drenagem", "#00acc1"), ("ELE", "Elétrica", "#fdd835"), ("SPD", "SPDA", "#9e9d24"),
            ("CAB", "Cabeamento e lógica", "#8e24aa"), ("AUT", "Automação", "#3949ab"), ("SON", "Sonorização", "#ec407a"),
            ("ACV", "Ar condicionado e exaustão", "#43a047"), ("GAS", "Gás", "#fb8c00"), ("INC", "Prevenção e combate a incêndio", "#e53935"),
            ("IMP", "Impermeabilização", "#00897b"),
        };
        static readonly string[] Neutras = { "#000000", "#3c3c3c", "#7a7a7a", "#bdbdbd", "#ffffff", "#8e1b1b" };
        const int MAXREC = 14;

        public byte[] Resultado { get; private set; }

        readonly byte[] original;
        double h, s, v;              // matiz 0-360, saturação e valor 0-1
        byte[] atual;
        bool ocupado;

        readonly Border bdSv, prevNova;
        readonly Canvas cvSv, cvHue;
        readonly Ellipse curSv;
        readonly Border curHue;
        readonly Grid gHue;
        readonly TextBox tbHex, tbR, tbG, tbB;
        readonly TextBlock tbContraste;
        readonly Border bdContraste;
        readonly List<Border> amostras = new List<Border>();

        /// <summary>Abre o seletor. Devolve a cor escolhida ou null se cancelar.</summary>
        public static byte[] Escolher(Window dono, byte[] inicial, string tituloProjeto = null, IList<Amostra> doProjeto = null)
        {
            var w = new SeletorCor(dono, inicial ?? new byte[] { 0, 0, 0 }, tituloProjeto, doProjeto);
            return w.ShowDialog() == true ? w.Resultado : null;
        }

        SeletorCor(Window dono, byte[] inicial, string tituloProjeto, IList<Amostra> doProjeto)
        {
            Title = "Escolher cor";
            Owner = dono; WindowStartupLocation = dono != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            if (dono != null) { Background = dono.Background; FontFamily = dono.FontFamily; FontSize = dono.FontSize; }
            original = inicial;

            var raiz = new StackPanel { Margin = new Thickness(14), Width = 300 };

            // área de cor (saturação × valor)
            var gSv = new Grid { Height = 160, Cursor = Cursors.Cross, ClipToBounds = true };
            bdSv = new Border { CornerRadius = new CornerRadius(5) };
            gSv.Children.Add(bdSv);
            gSv.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = new LinearGradientBrush(Colors.White, WColor.FromArgb(0, 255, 255, 255), 0) });
            gSv.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = new LinearGradientBrush(WColor.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
            cvSv = new Canvas();
            curSv = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
            curSv.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 2, ShadowDepth = 0, Opacity = .8 };
            cvSv.Children.Add(curSv);
            gSv.Children.Add(cvSv);
            Arrastar(gSv, (x, y) => { s = x; v = 1 - y; Aplicar("sv"); });
            raiz.Children.Add(gSv);

            // matiz + conta-gotas
            var l1 = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var btGota = new Button { Content = "⌖", Width = 30, Height = 26, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Conta-gotas: depois de clicar aqui, clique em qualquer ponto da tela para pegar a cor (Esc ou botão direito cancela)", FontSize = 16, Padding = new Thickness(0) };
            DockPanel.SetDock(btGota, Dock.Right);
            btGota.Click += (o, a) => ContaGotas();
            Closed += (o, a) => PararGota();
            l1.Children.Add(btGota);
            gHue = new Grid { Height = 14, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var arco = new GradientStopCollection();
            string[] hs = { "#ff0000", "#ffff00", "#00ff00", "#00ffff", "#0000ff", "#ff00ff", "#ff0000" };
            for (int i = 0; i < hs.Length; i++) arco.Add(new GradientStop((WColor)ColorConverter.ConvertFromString(hs[i]), i / 6.0));
            gHue.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = new LinearGradientBrush(arco, 0) });
            cvHue = new Canvas();
            curHue = new Border { Width = 8, Height = 18, CornerRadius = new CornerRadius(3), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), IsHitTestVisible = false };
            curHue.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 2, ShadowDepth = 0, Opacity = .8 };
            Canvas.SetTop(curHue, -2);
            cvHue.Children.Add(curHue);
            gHue.Children.Add(cvHue);
            Arrastar(gHue, (x, y) => { h = x * 360; Aplicar("hue"); });
            l1.Children.Add(gHue);
            raiz.Children.Add(l1);

            // valores
            var gv = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var w in new[] { 40.0, 0, 50, 50, 50 })
                gv.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            var prev = new Border { BorderBrush = new SolidColorBrush(WColor.FromRgb(201, 201, 197)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 28, VerticalAlignment = VerticalAlignment.Bottom, ToolTip = "Em cima: nova · embaixo: anterior", ClipToBounds = true };
            var pg = new Grid(); pg.RowDefinitions.Add(new RowDefinition()); pg.RowDefinitions.Add(new RowDefinition());
            prevNova = new Border();
            var prevVelha = new Border { Background = Pincel(original) };
            Grid.SetRow(prevVelha, 1);
            pg.Children.Add(prevNova); pg.Children.Add(prevVelha);
            prev.Child = pg;
            gv.Children.Add(prev);
            tbHex = Campo(gv, 1, "HEX", 7);
            tbR = Campo(gv, 2, "R", 3); tbG = Campo(gv, 3, "G", 3); tbB = Campo(gv, 4, "B", 3);
            tbHex.TextChanged += (o, a) => { if (ocupado) return; var c = LerHex(tbHex.Text); if (c != null) Definir(c, "hex"); };
            foreach (var t in new[] { tbR, tbG, tbB })
                t.TextChanged += (o, a) =>
                {
                    if (ocupado) return;
                    var c = new[] { tbR, tbG, tbB }.Select(x => int.TryParse(x.Text, out int n) ? (byte)Math.Max(0, Math.Min(255, n)) : (byte)0).ToArray();
                    Definir(c, "rgb");
                };
            raiz.Children.Add(gv);

            // contraste
            tbContraste = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };
            bdContraste = new Border { Child = tbContraste, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 8, 0, 0) };
            raiz.Children.Add(bdContraste);

            // amostras
            if (doProjeto != null && doProjeto.Any(a => a.Cor != null))
                Grupo(raiz, tituloProjeto ?? "Cores do projeto", doProjeto.Where(a => a.Cor != null));
            Grupo(raiz, "Padrão FJA", Padrao.Select(p => new Amostra(p.Item1, p.Item1 + " · " + p.Item2 + " (" + p.Item3 + ")", LerHex(p.Item3))));
            Grupo(raiz, "Neutras", Neutras.Select(x => new Amostra("", x, LerHex(x))));
            var rec = Recentes();
            if (rec.Count > 0) Grupo(raiz, "Recentes", rec.Select(x => new Amostra("", Hex(x), x)));

            // botões
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Padding = new Thickness(18, 5, 18, 5), IsDefault = true };
            var cancelar = new Button { Content = "Cancelar", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            if (dono?.TryFindResource("Primario") is Style sp) ok.Style = sp;
            if (dono?.TryFindResource("Discreto") is Style sd) { cancelar.Style = sd; cancelar.Width = double.NaN; cancelar.Padding = new Thickness(16, 5, 16, 5); btGota.Style = sd; btGota.Width = 30; }
            ok.Click += (o, a) => Confirmar();
            bts.Children.Add(cancelar); bts.Children.Add(ok);
            raiz.Children.Add(bts);

            Content = raiz;
            Definir(original, "init");
            Loaded += (o, a) => { tbHex.Focus(); tbHex.SelectAll(); };
        }

        void Confirmar()
        {
            Resultado = atual;
            GuardarRecente(atual);
            DialogResult = true;
        }

        // ------------------------------------------------------------------ montagem
        TextBox Campo(Grid g, int col, string rotulo, int max)
        {
            var sp = new StackPanel { Margin = new Thickness(col == 1 ? 8 : 6, 0, 0, 0) };
            sp.Children.Add(new TextBlock { Text = rotulo, FontSize = 11, Foreground = new SolidColorBrush(WColor.FromRgb(94, 94, 92)) });
            var tb = new TextBox { MaxLength = max, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0), FontFamily = new FontFamily("Consolas") };
            sp.Children.Add(tb);
            Grid.SetColumn(sp, col);
            g.Children.Add(sp);
            return tb;
        }

        void Grupo(Panel raiz, string titulo, IEnumerable<Amostra> itens)
        {
            raiz.Children.Add(new TextBlock { Text = titulo.ToUpperInvariant(), FontSize = 10.5, Margin = new Thickness(0, 10, 0, 4), Foreground = new SolidColorBrush(WColor.FromRgb(94, 94, 92)) });
            var wp = new WrapPanel();
            foreach (var a in itens)
            {
                var cor = a.Cor;
                var b = new Border
                {
                    MinWidth = 22, Height = 22, Margin = new Thickness(0, 0, 5, 5), CornerRadius = new CornerRadius(4),
                    Background = Pincel(cor), BorderBrush = new SolidColorBrush(WColor.FromArgb(50, 0, 0, 0)), BorderThickness = new Thickness(1),
                    ToolTip = a.Dica, Cursor = Cursors.Hand, Tag = cor,
                };
                if (!string.IsNullOrEmpty(a.Codigo))
                    b.Child = new TextBlock
                    {
                        Text = a.Codigo, FontFamily = new FontFamily("Consolas"), FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Foreground = Luminancia(cor) > .35 ? new SolidColorBrush(WColor.FromRgb(29, 29, 29)) : Brushes.White,
                        Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    };
                b.MouseLeftButtonDown += (o, e) =>
                {
                    Definir(cor, "amostra");
                    if (e.ClickCount > 1) Confirmar();
                };
                amostras.Add(b);
                wp.Children.Add(b);
            }
            raiz.Children.Add(wp);
        }

        static void Arrastar(FrameworkElement el, Action<double, double> fn)
        {
            Action<MouseEventArgs> mv = e =>
            {
                var p = e.GetPosition(el);
                fn(Math.Max(0, Math.Min(1, p.X / Math.Max(1, el.ActualWidth))), Math.Max(0, Math.Min(1, p.Y / Math.Max(1, el.ActualHeight))));
            };
            el.MouseLeftButtonDown += (o, e) => { el.CaptureMouse(); mv(e); e.Handled = true; };
            el.MouseMove += (o, e) => { if (el.IsMouseCaptured) mv(e); };
            el.MouseLeftButtonUp += (o, e) => el.ReleaseMouseCapture();
        }

        // ------------------------------------------------------------------ estado
        void Definir(byte[] c, string fonte)
        {
            double hAnt = h;
            Rgb2Hsv(c, out h, out s, out v);
            if (s == 0) h = hAnt;   // cinza: mantém a matiz da barra
            Aplicar(fonte, c);
        }

        void Aplicar(string fonte, byte[] dado = null)
        {
            atual = dado ?? Hsv2Rgb(h, s, v);
            ocupado = true;
            try
            {
                bdSv.Background = Pincel(Hsv2Rgb(h, 1, 1));
                double w = Math.Max(1, cvSv.ActualWidth > 0 ? cvSv.ActualWidth : 300), hh = cvSv.ActualHeight > 0 ? cvSv.ActualHeight : 160;
                Canvas.SetLeft(curSv, s * w - 7); Canvas.SetTop(curSv, (1 - v) * hh - 7);
                curSv.Fill = Pincel(atual);
                double wh = cvHue.ActualWidth > 0 ? cvHue.ActualWidth : 260;
                Canvas.SetLeft(curHue, h / 360 * wh - 4);
                prevNova.Background = Pincel(atual);
                if (fonte != "hex") tbHex.Text = Hex(atual);
                if (fonte != "rgb") { tbR.Text = atual[0].ToString(); tbG.Text = atual[1].ToString(); tbB.Text = atual[2].ToString(); }
                foreach (var b in amostras)
                {
                    bool on = b.Tag is byte[] t && t.SequenceEqual(atual);
                    b.BorderBrush = on ? new SolidColorBrush(WColor.FromRgb(142, 27, 27)) : new SolidColorBrush(WColor.FromArgb(50, 0, 0, 0));
                    b.BorderThickness = new Thickness(on ? 2 : 1);
                }
                double k = 1.05 / (Luminancia(atual) + .05);
                string ks = k.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + ":1";
                bool fraco = k < 3;
                tbContraste.Text = fraco ? "⚠ Contraste baixo com o fundo branco (" + ks + "): pode sumir na impressão." : "Contraste com o fundo branco: " + ks;
                tbContraste.Foreground = fraco ? new SolidColorBrush(WColor.FromRgb(138, 93, 5)) : new SolidColorBrush(WColor.FromRgb(94, 94, 92));
                bdContraste.Background = fraco ? new SolidColorBrush(WColor.FromRgb(248, 239, 217)) : Brushes.Transparent;
                bdContraste.Padding = fraco ? new Thickness(8, 4, 8, 4) : new Thickness(0);
            }
            finally { ocupado = false; }
            if (fonte == "init") Dispatcher.BeginInvoke(new Action(() => Aplicar("pos", atual)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ------------------------------------------------------------------ conta-gotas
        // Sem janela por cima e sem esconder o seletor (esconder e mostrar de novo uma janela modal derrubava o
        // Revit): enquanto o conta-gotas está ligado, um temporizador olha o botão do mouse e o Esc; no clique,
        // lê a cor do pixel sob o cursor direto da tela.
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out PONTO p);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int x, int y);
        struct PONTO { public int X, Y; }
        const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_ESCAPE = 0x1B;

        System.Windows.Threading.DispatcherTimer gota;

        static bool Apertado(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        void ContaGotas()
        {
            try
            {
                if (gota != null) { PararGota(); return; }
                bool soltou = false;   // espera soltar o clique do próprio botão
                tbContraste.Text = "Conta-gotas: clique em qualquer ponto da tela para pegar a cor (Esc ou botão direito cancela).";
                bdContraste.Background = new SolidColorBrush(WColor.FromRgb(232, 239, 246));
                bdContraste.Padding = new Thickness(8, 4, 8, 4);
                Mouse.OverrideCursor = Cursors.Cross;
                gota = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                gota.Tick += (o, e) =>
                {
                    try
                    {
                        bool esq = Apertado(VK_LBUTTON);
                        if (!soltou) { soltou = !esq; return; }
                        if (Apertado(VK_ESCAPE) || Apertado(VK_RBUTTON)) { PararGota(); Aplicar("pos", atual); return; }
                        if (!esq) return;
                        byte[] pego = null;
                        if (GetCursorPos(out PONTO pt))
                        {
                            IntPtr dc = GetDC(IntPtr.Zero);
                            try
                            {
                                uint c = GetPixel(dc, pt.X, pt.Y);
                                if (c != 0xFFFFFFFF) pego = new[] { (byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF) };
                            }
                            finally { ReleaseDC(IntPtr.Zero, dc); }
                        }
                        PararGota();
                        if (pego != null) Definir(pego, "gota"); else Aplicar("pos", atual);
                    }
                    catch { PararGota(); }
                };
                gota.Start();
            }
            catch { PararGota(); }
        }

        void PararGota()
        {
            try { gota?.Stop(); } catch { }
            gota = null;
            try { Mouse.OverrideCursor = null; } catch { }
        }

        // ------------------------------------------------------------------ recentes (%APPDATA%\FJA)
        static string ArqRecentes => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FJA", "cores-recentes.txt");

        static List<byte[]> Recentes()
        {
            try
            {
                if (!File.Exists(ArqRecentes)) return new List<byte[]>();
                return File.ReadAllText(ArqRecentes).Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(LerHex).Where(x => x != null).Take(MAXREC).ToList();
            }
            catch { return new List<byte[]>(); }
        }

        static void GuardarRecente(byte[] c)
        {
            try
            {
                string hx = Hex(c);
                var l = new List<string> { hx };
                l.AddRange(Recentes().Select(Hex).Where(x => x != hx));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ArqRecentes));
                File.WriteAllText(ArqRecentes, string.Join(",", l.Take(MAXREC)));
            }
            catch { }
        }

        // ------------------------------------------------------------------ cores
        public static string Hex(byte[] c) => "#" + c[0].ToString("x2") + c[1].ToString("x2") + c[2].ToString("x2");

        public static byte[] LerHex(string t)
        {
            t = (t ?? "").Trim().TrimStart('#');
            if (t.Length == 3) t = string.Concat(t.Select(ch => new string(ch, 2)));
            if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int n)) return null;
            return new[] { (byte)(n >> 16), (byte)((n >> 8) & 255), (byte)(n & 255) };
        }

        static SolidColorBrush Pincel(byte[] c) => new SolidColorBrush(WColor.FromRgb(c[0], c[1], c[2]));

        static double Luminancia(byte[] c)
        {
            Func<byte, double> f = x => { double y = x / 255.0; return y <= .03928 ? y / 12.92 : Math.Pow((y + .055) / 1.055, 2.4); };
            return .2126 * f(c[0]) + .7152 * f(c[1]) + .0722 * f(c[2]);
        }

        static void Rgb2Hsv(byte[] c, out double h, out double s, out double v)
        {
            double r = c[0] / 255.0, g = c[1] / 255.0, b = c[2] / 255.0;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
            h = 0;
            if (d > 0)
            {
                if (mx == r) h = ((g - b) / d) % 6;
                else if (mx == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60; if (h < 0) h += 360;
            }
            s = mx > 0 ? d / mx : 0; v = mx;
        }

        static byte[] Hsv2Rgb(double h, double s, double v)
        {
            Func<double, byte> f = n =>
            {
                double k = (n + h / 60) % 6;
                return (byte)Math.Round((v - v * s * Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1))) * 255);
            };
            return new[] { f(5), f(3), f(1) };
        }
    }
}
