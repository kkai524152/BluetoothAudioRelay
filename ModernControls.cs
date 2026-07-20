using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BluetoothAudioRelay;

internal static class AppTheme
{
    public static Color Background { get; private set; } = Color.FromArgb(210, 239, 230);
    public static Color Shell { get; private set; } = Color.FromArgb(235, 249, 244);
    public static Color Surface { get; private set; } = Color.FromArgb(242, 252, 248);
    public static Color SurfaceSoft { get; private set; } = Color.FromArgb(248, 255, 252);
    public static Color Border { get; private set; } = Color.FromArgb(199, 224, 216);
    public static Color TextPrimary { get; private set; } = Color.FromArgb(35, 53, 68);
    public static Color TextSecondary { get; private set; } = Color.FromArgb(119, 137, 136);
    public static Color TextMuted { get; private set; } = Color.FromArgb(146, 164, 160);
    public static Color Accent { get; private set; } = Color.FromArgb(29, 188, 163);
    public static Color AccentHover { get; private set; } = Color.FromArgb(21, 166, 144);
    public static Color AccentSoft { get; private set; } = Color.FromArgb(212, 245, 237);
    public static Color AccentText { get; private set; } = Color.FromArgb(8, 102, 89);
    public static Color Success { get; private set; } = Color.FromArgb(22, 163, 74);
    public static Color Warning { get; private set; } = Color.FromArgb(217, 119, 6);
    public static Color Danger { get; private set; } = Color.FromArgb(220, 38, 38);
    public static bool IsDark { get; private set; }

    public static void Apply(bool isDark, AccentPalette accent)
    {
        IsDark = isDark;
        Accent = accent.Primary;
        AccentHover = accent.Hover;
        AccentSoft = isDark ? Blend(accent.Primary, Color.FromArgb(24, 34, 32), 0.20) : Blend(accent.Primary, Color.White, 0.18);
        AccentText = isDark ? Blend(accent.Primary, Color.White, 0.72) : Blend(accent.Primary, Color.Black, 0.70);

        if (isDark)
        {
            Background = Color.FromArgb(8, 45, 39);
            Shell = Color.FromArgb(20, 33, 30);
            Surface = Color.FromArgb(31, 40, 37);
            SurfaceSoft = Color.FromArgb(15, 22, 20);
            Border = Color.FromArgb(68, 82, 77);
            TextPrimary = Color.FromArgb(233, 242, 239);
            TextSecondary = Color.FromArgb(164, 181, 177);
            TextMuted = Color.FromArgb(126, 144, 140);
            Success = Color.FromArgb(74, 222, 128);
            Warning = Color.FromArgb(251, 191, 36);
            Danger = Color.FromArgb(248, 113, 113);
            return;
        }

        Background = Color.FromArgb(198, 235, 224);
        Shell = Color.FromArgb(235, 249, 244);
        Surface = Color.FromArgb(242, 252, 248);
        SurfaceSoft = Color.FromArgb(248, 255, 252);
        Border = Color.FromArgb(199, 224, 216);
        TextPrimary = Color.FromArgb(35, 53, 68);
        TextSecondary = Color.FromArgb(119, 137, 136);
        TextMuted = Color.FromArgb(146, 164, 160);
        Success = Color.FromArgb(22, 163, 74);
        Warning = Color.FromArgb(217, 119, 6);
        Danger = Color.FromArgb(220, 38, 38);
    }

    public static Color Blend(Color foreground, Color background, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var inverse = 1 - amount;
        return Color.FromArgb(
            (int)(foreground.R * amount + background.R * inverse),
            (int)(foreground.G * amount + background.G * inverse),
            (int)(foreground.B * amount + background.B * inverse));
    }

    public static Color ResolveBackground(Control? control)
    {
        var current = control;
        while (current is not null)
        {
            if (current is RoundedPanel roundedPanel)
            {
                return roundedPanel.FillColor;
            }

            if (current is GradientCard)
            {
                return Accent;
            }

            if (current.BackColor.A > 0)
            {
                return current.BackColor;
            }

            current = current.Parent;
        }

        return Background;
    }
}

internal sealed record AccentPalette(string Key, string DisplayName, Color Primary, Color Hover);

internal static class AccentPalettes
{
    public static readonly AccentPalette[] All =
    [
        new("emerald", "翡翠绿", Color.FromArgb(29, 188, 163), Color.FromArgb(21, 166, 144)),
        new("sky", "天空蓝", Color.FromArgb(14, 165, 233), Color.FromArgb(2, 132, 199)),
        new("indigo", "靛蓝", Color.FromArgb(99, 102, 241), Color.FromArgb(79, 70, 229)),
        new("rose", "玫瑰红", Color.FromArgb(244, 63, 94), Color.FromArgb(225, 29, 72)),
        new("amber", "琥珀橙", Color.FromArgb(245, 158, 11), Color.FromArgb(217, 119, 6)),
        new("slate", "石板灰", Color.FromArgb(71, 85, 105), Color.FromArgb(51, 65, 85))
    ];

    public static AccentPalette Default => All[0];

    public static AccentPalette Find(string? key)
    {
        return All.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }
}

internal static class RoundedGeometry
{
    public static GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    public RoundedPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    public int CornerRadius { get; set; } = 22;

    public Color FillColor { get; set; } = AppTheme.Surface;

    public Color BorderColor { get; set; } = AppTheme.Border;

    public int BorderWidth { get; set; } = 1;

    public string ThemeRole { get; set; } = "surface";

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateRoundedRegion(this, CornerRadius);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(AppTheme.ResolveBackground(Parent));

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedGeometry.CreatePath(bounds, CornerRadius);
        var themedFill = ThemeRole switch
        {
            "shell" => AppTheme.Shell,
            "surface-soft" => AppTheme.SurfaceSoft,
            "accent-soft" => AppTheme.AccentSoft,
            _ => FillColor
        };
        var themedBorder = ThemeRole == "accent-soft" ? AppTheme.Blend(AppTheme.Accent, AppTheme.Border, 0.28) : BorderColor;

        using var fill = new SolidBrush(themedFill);
        e.Graphics.FillPath(fill, path);

        if (BorderWidth > 0)
        {
            using var pen = new Pen(themedBorder, BorderWidth);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private static void UpdateRoundedRegion(Control control, int radius)
    {
        using var path = RoundedGeometry.CreatePath(new Rectangle(0, 0, control.Width, control.Height), radius);
        var previousRegion = control.Region;
        control.Region = new Region(path);
        previousRegion?.Dispose();
    }
}

internal sealed class GradientCard : Panel
{
    public GradientCard()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    public int CornerRadius { get; set; } = 26;

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = RoundedGeometry.CreatePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        var previousRegion = Region;
        Region = new Region(path);
        previousRegion?.Dispose();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(AppTheme.ResolveBackground(Parent));

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedGeometry.CreatePath(bounds, CornerRadius);
        using var brush = new LinearGradientBrush(
            bounds,
            AppTheme.IsDark ? AppTheme.Shell : Color.FromArgb(229, 247, 240),
            AppTheme.IsDark ? Color.FromArgb(17, 51, 45) : Color.FromArgb(214, 242, 233),
            LinearGradientMode.Horizontal);
        e.Graphics.FillPath(brush, path);

        using var accentGlow = new LinearGradientBrush(
            bounds,
            Color.FromArgb(AppTheme.IsDark ? 60 : 42, AppTheme.Accent),
            Color.FromArgb(0, AppTheme.Accent),
            LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillPath(accentGlow, path);
    }
}

internal sealed class ModernButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public ModernButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Height = 44;
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
    }

    public bool Primary { get; set; }

    public int CornerRadius { get; set; } = 13;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = RoundedGeometry.CreatePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        var previousRegion = Region;
        Region = new Region(path);
        previousRegion?.Dispose();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        pevent.Graphics.Clear(AppTheme.ResolveBackground(Parent));

        var fillColor = Primary ? AppTheme.Accent : AppTheme.SurfaceSoft;
        var textColor = Primary ? Color.White : AppTheme.TextPrimary;
        var borderColor = Primary ? AppTheme.Accent : AppTheme.Border;

        if (_pressed)
        {
            fillColor = Primary ? AppTheme.AccentHover : AppTheme.Blend(AppTheme.Border, AppTheme.SurfaceSoft, 0.40);
        }
        else if (_hovered)
        {
            fillColor = Primary ? AppTheme.AccentHover : AppTheme.AccentSoft;
            borderColor = Primary ? AppTheme.AccentHover : AppTheme.Blend(AppTheme.Accent, AppTheme.Border, 0.35);
        }

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedGeometry.CreatePath(bounds, CornerRadius);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(borderColor);
        pevent.Graphics.FillPath(fill, path);
        pevent.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            bounds,
            textColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(2, 2, 60, 60);
        using var path = RoundedGeometry.CreatePath(bounds, 16);
        using var background = new LinearGradientBrush(
            bounds,
            AppTheme.Accent,
            AppTheme.AccentHover,
            LinearGradientMode.ForwardDiagonal);
        graphics.FillPath(background, path);

        using var pen = new Pen(Color.White, 5)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(pen, 17, 17, 30, 30, 205, 130);
        graphics.DrawLine(pen, 17, 33, 17, 45);
        graphics.DrawLine(pen, 47, 33, 47, 45);
        graphics.DrawLine(pen, 17, 45, 23, 45);
        graphics.DrawLine(pen, 41, 45, 47, 45);

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(iconHandle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}

internal sealed class StatusDot : Control
{
    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(12, 12);
    }

    public Color DotColor { get; set; } = AppTheme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(DotColor);
        e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
    }
}
