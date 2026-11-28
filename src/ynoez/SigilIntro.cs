using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ynoez;

/// <summary>
/// Animação vetorial do sigilo: ponto central, expansão de partículas e desenho final nítido.
/// Não depende de arquivo externo e pode ser pulada com um clique.
/// </summary>
internal sealed class SigilIntro : FrameworkElement
{
    public const double FormRevealSeconds = 3.6;
    private const int ParticleCount = 1050;
    private const double BloomStart = 0.95;
    private const double BloomDuration = 2.45;
    private const double CrispStart = 3.10;
    private const double CrispDuration = 0.90;

    private readonly record struct Particle(Point Target, double StartX, double StartY, double Delay, double Spin, double Size);
    private static readonly Geometry Shape = CreateShape();
    private static readonly Pen OutlinePen = CreateOutlinePen();
    private static readonly Brush Glow = CreateGlow();
    private static readonly Brush[] ParticleBrushes = CreateBrushes();
    private static readonly Particle[] Particles = BuildParticles();

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _offset;
    private double _lastFrame = -1;
    private bool _running;
    private bool _formed;

    public event EventHandler? Formed;

    public SigilIntro()
    {
        IsHitTestVisible = true;
        Focusable = false;
        Loaded += (_, _) => UpdateRunning();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateRunning();
        MouseLeftButtonDown += (_, _) => Skip();
    }

    public void Skip()
    {
        var finalTime = FormRevealSeconds + 0.05;
        if (Elapsed < finalTime)
            _offset += finalTime - Elapsed;
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
        if (t > 5.0 && t - _lastFrame < 1.0 / 30.0) return;
        _lastFrame = t;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (width < 40 || height < 40) return;

        var t = Elapsed;
        var scale = Math.Min(width / SigilData.Width, height / SigilData.Height) * 0.90;
        var left = (width - SigilData.Width * scale) / 2.0;
        var top = (height - SigilData.Height * scale) / 2.0;
        var center = new Point(width / 2.0, height / 2.0);

        var seed = Ease(Clamp(t / 0.34));
        var blob = Ease(Clamp((t - 0.20) / 0.72));
        var bloom = Ease(Clamp((t - BloomStart) / BloomDuration));
        var crisp = Smooth(Clamp((t - CrispStart) / CrispDuration));
        var blobRadius = 1.5 + 12.0 * blob;
        var glowRadius = blobRadius * 2.3 + (Math.Min(width, height) * 0.48 - blobRadius * 2.3) * bloom;
        var glowOpacity = (0.28 * seed * (1.0 - bloom)) + (0.045 + 0.075 * crisp) * bloom;

        if (glowOpacity > 0.002)
        {
            dc.PushOpacity(glowOpacity);
            dc.DrawEllipse(Glow, null, center, glowRadius, glowRadius);
            dc.Pop();
        }

        if (t < 1.20)
        {
            var dotRadius = 1.2 + 4.0 * seed;
            dc.DrawEllipse(Brushes.White, null, center, dotRadius, dotRadius);
        }

        foreach (var particle in Particles)
        {
            var progress = Clamp((bloom - particle.Delay) / (1.0 - 0.26));
            var eased = Ease(progress);
            var start = new Point(center.X + particle.StartX * blobRadius, center.Y + particle.StartY * blobRadius);
            var target = new Point(left + particle.Target.X * scale, top + particle.Target.Y * scale);
            var x = start.X + (target.X - start.X) * eased;
            var y = start.Y + (target.Y - start.Y) * eased;
            var angle = particle.Spin * (1.0 - eased);
            var dx = x - center.X;
            var dy = y - center.Y;
            x = center.X + dx * Math.Cos(angle) - dy * Math.Sin(angle);
            y = center.Y + dx * Math.Sin(angle) + dy * Math.Cos(angle);

            var alpha = Clamp(seed * (1.0 - crisp * 0.76) + crisp * 0.18);
            var level = (int)Math.Round(alpha * (ParticleBrushes.Length - 1));
            if (level <= 0) continue;
            var radius = particle.Size * (0.55 + scale * 0.45);
            dc.DrawEllipse(ParticleBrushes[level], null, new Point(x, y), radius, radius);
        }

        if (crisp > 0.001)
        {
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, top));
            dc.PushOpacity(crisp);
            dc.DrawGeometry(Brushes.White, OutlinePen, Shape);
            dc.Pop();
            dc.Pop();
        }
    }

    private static Geometry CreateShape()
    {
        var geometry = Geometry.Parse(SigilData.Outline);
        geometry.Freeze();
        return geometry;
    }

    private static Pen CreateOutlinePen()
    {
        var pen = new Pen(Brushes.White, 0.8) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    private static Brush CreateGlow()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(230, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush[] CreateBrushes()
    {
        var brushes = new Brush[12];
        for (var i = 0; i < brushes.Length; i++)
        {
            var alpha = (byte)Math.Round(255.0 * i / (brushes.Length - 1));
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
            brush.Freeze();
            brushes[i] = brush;
        }
        return brushes;
    }

    private static Particle[] BuildParticles()
    {
        var random = new Random(0x594E4F45);
        var bounds = Shape.Bounds;
        var particles = new List<Particle>(ParticleCount);
        var attempts = 0;
        while (particles.Count < ParticleCount && attempts++ < ParticleCount * 150)
        {
            var point = new Point(bounds.Left + random.NextDouble() * bounds.Width,
                bounds.Top + random.NextDouble() * bounds.Height);
            if (!Shape.FillContains(point)) continue;
            particles.Add(new Particle(point,
                random.NextDouble() * 2 - 1,
                random.NextDouble() * 2 - 1,
                random.NextDouble() * 0.26,
                (random.NextDouble() - 0.5) * Math.PI * 2.0,
                0.55 + random.NextDouble() * 1.1));
        }
        return particles.ToArray();
    }

    private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
    private static double Ease(double x) => 1.0 - Math.Pow(1.0 - x, 3.0);
    private static double Smooth(double x) => x * x * (3.0 - 2.0 * x);
}
