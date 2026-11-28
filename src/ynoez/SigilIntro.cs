using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ynoez.Services;

namespace ynoez;

/// <summary>
/// Abertura do aplicativo: um ponto no centro cresce, "desabrocha" em partículas que giram para fora
/// e desenham o sigilo; no fim o contorno nítido surge sobre elas. Desenho feito em OnRender, sem
/// elementos visuais por partícula. A animação para sozinha quando o controle deixa de ser visível.
/// Um clique pula direto para o resultado final.
/// </summary>
internal sealed class SigilIntro : FrameworkElement
{
    /// <summary>Instante (s) em que o sigilo está formado e o formulário de entrada pode aparecer.</summary>
    public const double FormRevealSeconds = 3.6;

    private const double SeedStart = 0.15;
    private const double BlobStart = 0.25;
    private const double BloomStart = 1.3;
    private const double BloomDuration = 2.4;
    private const double CrispStart = 3.4;
    private const double CrispDuration = 1.0;
    private const double MaxDelay = 0.35;
    private const double IdleAfterSeconds = 6.0;
    private const int AlphaLevels = 12;

    private struct Particle
    {
        public double Tx, Ty;      // alvo no sigilo (pixels da imagem de referência)
        public double Ux, Uy;      // posição inicial dentro da bolha central (disco unitário)
        public double Delay;       // atraso do desabrochar, fração de BloomDuration
        public double Spin;        // giro (rad) que se desfaz ao chegar ao alvo
        public double Size;        // fator de tamanho
        public double Phase;       // fase do brilho
        public int Kind;           // 0-1 ponto, 2 traço "/", 3 traço "\"
    }

    private static readonly Brush[] DotBrushes = CreateDotBrushes();
    private static readonly Pen[] TickPens = CreateTickPens();
    private static readonly Brush GlowBrush = CreateGlowBrush();
    private static readonly Pen OutlinePen = CreateOutlinePen();
    private static readonly Geometry OutlineGeometry = CreateOutline();
    private static readonly Particle[] Particles = CreateParticles();

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _offset;
    private double _lastRenderAt = -1;
    private bool _running;
    private bool _formed;

    /// <summary>Disparado uma única vez, quando o sigilo termina de se formar (ou é pulado).</summary>
    public event EventHandler? Formed;

    public SigilIntro()
    {
        Focusable = false;
        Loaded += (_, _) => UpdateRunning();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateRunning();
        MouseLeftButtonDown += (_, _) => Skip();
    }

    /// <summary>Pula a animação e mostra o sigilo completo.</summary>
    public void Skip()
    {
        var target = CrispStart + CrispDuration + 0.2;
        var now = Elapsed;
        if (now < target) _offset += target - now;
        InvalidateVisual();
    }

    private double Elapsed => _clock.Elapsed.TotalSeconds + _offset;

    private void UpdateRunning()
    {
        if (IsLoaded && IsVisible) Start();
        else Stop();
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = Elapsed;
        if (!_formed && t >= FormRevealSeconds)
        {
            _formed = true;
            Formed?.Invoke(this, EventArgs.Empty);
        }

        // Depois que tudo terminou, só o brilho sutil continua: 30 quadros por segundo bastam.
        if (t > IdleAfterSeconds && t - _lastRenderAt < 0.033) return;
        _lastRenderAt = t;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth;
        var h = ActualHeight;
        // Retângulo transparente: permite receber o clique de "pular".
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (w < 40 || h < 40) return;

        var t = Elapsed;
        var scale = Math.Min(w / SigilData.Width, h / SigilData.Height) * 0.92;
        var left = (w - SigilData.Width * scale) / 2;
        var top = (h - SigilData.Height * scale) / 2;
        var cx = w / 2;
        var cy = h / 2;

        var seed = Clamp01((t - SeedStart) / 0.5);
        var blobGrow = EaseOutCubic(Clamp01((t - BlobStart) / 1.0));
        var blobRadius = 1.5 + 12.0 * blobGrow * Math.Max(0.6, scale);
        var bloom = Clamp01((t - BloomStart) / BloomDuration);
        var bloomEase = EaseOutCubic(bloom);
        var crisp = SmoothStep(Clamp01((t - CrispStart) / CrispDuration));

        // Brilho de fundo: pequeno e forte no início, amplo e discreto depois.
        var glowRadius = blobRadius * 2.5 + (0.55 * Math.Min(w, h) - blobRadius * 2.5) * bloomEase;
        var glowAlpha = 0.30 * seed * (1.0 - bloomEase) + (0.07 + 0.08 * crisp) * bloomEase;
        if (glowAlpha > 0.003)
        {
            dc.PushOpacity(Clamp01(glowAlpha));
            dc.DrawEllipse(GlowBrush, null, new Point(cx, cy), glowRadius, glowRadius);
            dc.Pop();
        }

        for (var i = 0; i < Particles.Length; i++)
        {
            var p = Particles[i];
            var progress = Clamp01((bloom - p.Delay) / (1.0 - MaxDelay));
            var e = EaseOutCubic(progress);

            var sx = blobRadius * p.Ux;
            var sy = blobRadius * p.Uy;
            var tx = left + p.Tx * scale - cx;
            var ty = top + p.Ty * scale - cy;
            var rx = sx + (tx - sx) * e;
            var ry = sy + (ty - sy) * e;

            var angle = p.Spin * (1.0 - e);
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var x = cx + rx * cos - ry * sin;
            var y = cy + rx * sin + ry * cos;

            var alpha = seed * (1.0 - 0.7 * crisp);
            if (crisp > 0.0)
                alpha += 0.2 * crisp * (0.5 + 0.5 * Math.Sin(t * 2.2 + p.Phase));
            var level = (int)(Clamp01(alpha) * (AlphaLevels - 1) + 0.5);
            if (level <= 0) continue;

            var radius = p.Size * (0.5 + scale) * (1.0 + 0.4 * (1.0 - e));
            if (p.Kind < 2)
            {
                dc.DrawEllipse(DotBrushes[level], null, new Point(x, y), radius, radius);
            }
            else
            {
                var half = radius * 1.3;
                var a = p.Kind == 2 ? new Point(x - half, y + half) : new Point(x - half, y - half);
                var b = p.Kind == 2 ? new Point(x + half, y - half) : new Point(x + half, y + half);
                dc.DrawLine(TickPens[level], a, b);
            }
        }

        if (crisp > 0.0)
        {
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, top));
            dc.PushOpacity(crisp);
            dc.DrawGeometry(Brushes.White, OutlinePen, OutlineGeometry);
            dc.Pop();
            dc.Pop();
        }
    }

    private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;

    private static double SmoothStep(double x) => x * x * (3.0 - 2.0 * x);

    private static double EaseOutCubic(double x)
    {
        var inverse = 1.0 - x;
        return 1.0 - inverse * inverse * inverse;
    }

    private static Brush[] CreateDotBrushes()
    {
        var brushes = new Brush[AlphaLevels];
        for (var i = 0; i < AlphaLevels; i++)
        {
            var alpha = (byte)Math.Round(255.0 * i / (AlphaLevels - 1));
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
            brush.Freeze();
            brushes[i] = brush;
        }
        return brushes;
    }

    private static Pen[] CreateTickPens()
    {
        var pens = new Pen[AlphaLevels];
        for (var i = 0; i < AlphaLevels; i++)
        {
            var pen = new Pen(DotBrushes[i], 1.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            pens[i] = pen;
        }
        return pens;
    }

    private static Brush CreateGlowBrush()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Pen CreateOutlinePen()
    {
        // 1 px na escala da imagem: compensa a borda interna dos contornos vetorizados.
        var pen = new Pen(Brushes.White, 1.0) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    private static Geometry CreateOutline()
    {
        try
        {
            var geometry = Geometry.Parse(SigilData.Outline);
            geometry.Freeze();
            return geometry;
        }
        catch (Exception ex)
        {
            // Sem o contorno nítido a animação de partículas ainda funciona.
            AppLog.Write("Contorno do sigilo inválido; usando apenas partículas: " + ex.Message);
            return Geometry.Empty;
        }
    }

    private static Particle[] CreateParticles()
    {
        var random = new Random(20261128);
        var tokens = SigilData.Points.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<Particle>(tokens.Length);
        var centerX = SigilData.Width / 2.0;
        var centerY = SigilData.Height / 2.0;
        var maxDistance = Math.Sqrt(centerX * centerX + centerY * centerY);

        foreach (var token in tokens)
        {
            var comma = token.IndexOf(',');
            if (comma <= 0) continue;
            if (!int.TryParse(token.Substring(0, comma), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) continue;
            if (!int.TryParse(token.Substring(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) continue;

            var theta = random.NextDouble() * Math.PI * 2.0;
            var radius = Math.Sqrt(random.NextDouble());
            var distance = Math.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY)) / maxDistance;

            list.Add(new Particle
            {
                Tx = x,
                Ty = y,
                Ux = radius * Math.Cos(theta),
                Uy = radius * Math.Sin(theta),
                Delay = 0.18 * distance + 0.17 * random.NextDouble(),
                Spin = (random.NextDouble() * 2.0 - 1.0) * 1.4,
                Size = 0.7 + random.NextDouble() * 0.6,
                Phase = random.NextDouble() * Math.PI * 2.0,
                Kind = random.Next(0, 4)
            });
        }

        return list.ToArray();
    }
}
