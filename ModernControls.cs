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
        AccentSoft = isDark ? Blend(accent.Primary, Color.FromArgb(18, 24, 26), 0.24) : Blend(accent.Primary, Color.White, 0.18);
        AccentText = isDark ? Blend(accent.Primary, Color.White, 0.72) : Blend(accent.Primary, Color.Black, 0.70);

        if (isDark)
        {
            Background = Blend(accent.Primary, Color.FromArgb(8, 14, 16), 0.20);
            Shell = Blend(accent.Primary, Color.FromArgb(18, 26, 28), 0.16);
            Surface = Blend(accent.Primary, Color.FromArgb(29, 34, 35), 0.12);
            SurfaceSoft = Blend(accent.Primary, Color.FromArgb(10, 16, 18), 0.10);
            Border = Blend(accent.Primary, Color.FromArgb(64, 76, 78), 0.20);
            TextPrimary = Color.FromArgb(233, 242, 239);
            TextSecondary = Color.FromArgb(164, 181, 177);
            TextMuted = Color.FromArgb(126, 144, 140);
            Success = Color.FromArgb(74, 222, 128);
            Warning = Color.FromArgb(251, 191, 36);
            Danger = Color.FromArgb(248, 113, 113);
            return;
        }

        Background = Blend(accent.Primary, Color.FromArgb(248, 253, 251), 0.22);
        Shell = Blend(accent.Primary, Color.FromArgb(250, 255, 253), 0.11);
        Surface = Blend(accent.Primary, Color.FromArgb(252, 255, 254), 0.07);
        SurfaceSoft = Blend(accent.Primary, Color.White, 0.035);
        Border = Blend(accent.Primary, Color.FromArgb(211, 224, 222), 0.22);
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
                return roundedPanel.CurrentFillColor;
            }

            if (current is GradientCard)
            {
                return EstimateHeroBackground();
            }

            if (current.BackColor.A > 0)
            {
                return current.BackColor;
            }

            current = current.Parent;
        }

        return Background;
    }

    public static Color EstimateHeroBackground()
    {
        return Blend(HeroGradientStart, HeroGradientEnd, 0.50);
    }

    public static Color HeroGradientStart =>
        IsDark
            ? Blend(Accent, Color.FromArgb(26, 29, 30), 0.22)
            : Blend(Accent, Color.FromArgb(250, 255, 253), 0.12);

    public static Color HeroGradientEnd =>
        IsDark
            ? Blend(Accent, Color.FromArgb(8, 17, 18), 0.28)
            : Blend(Accent, Color.FromArgb(231, 247, 242), 0.20);

    public static Color SelectedRow =>
        IsDark
            ? Blend(Accent, Surface, 0.28)
            : Blend(Accent, Surface, 0.14);
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

    public Color CurrentFillColor => ThemeRole switch
    {
        "shell" => AppTheme.Shell,
        "surface-soft" => AppTheme.SurfaceSoft,
        "accent-soft" => AppTheme.AccentSoft,
        _ => FillColor
    };

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
        var themedFill = CurrentFillColor;
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
            AppTheme.HeroGradientStart,
            AppTheme.HeroGradientEnd,
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

internal sealed class ThemedSelectButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public ThemedSelectButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        Size = new Size(150, 32);
    }

    public int CornerRadius { get; set; } = 13;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var previousRegion = Region;
        Region = null;
        previousRegion?.Dispose();
        Invalidate();
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(AppTheme.ResolveBackground(Parent));

        var fillColor = _hovered || _pressed
            ? AppTheme.Blend(AppTheme.Accent, AppTheme.SurfaceSoft, AppTheme.IsDark ? 0.18 : 0.10)
            : AppTheme.SurfaceSoft;
        var borderColor = _hovered
            ? AppTheme.Blend(AppTheme.Accent, AppTheme.Border, 0.40)
            : AppTheme.Border;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedGeometry.CreatePath(bounds, CornerRadius);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(borderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        var textBounds = new Rectangle(14, 0, Math.Max(0, Width - 42), Height);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            AppTheme.TextPrimary,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);

        var centerX = Width - 21;
        var centerY = Height / 2 + 1;
        using var caretPen = new Pen(AppTheme.TextSecondary, 2)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLine(caretPen, centerX - 5, centerY - 3, centerX, centerY + 2);
        e.Graphics.DrawLine(caretPen, centerX, centerY + 2, centerX + 5, centerY - 3);
    }
}

internal sealed class ThemedLogBox : Control
{
    private const int ScrollBarWidth = 8;
    private readonly List<string> _lines = [];
    private int _scrollOffset;

    public ThemedLogBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.ResizeRedraw,
            true);
        Font = new Font("Cascadia Mono", 9F);
        BackColor = Color.Transparent;
    }

    public void AddLine(string line)
    {
        _lines.Insert(0, line);
        if (_lines.Count > 500)
        {
            _lines.RemoveRange(500, _lines.Count - 500);
        }

        _scrollOffset = 0;
        Invalidate();
    }

    public IReadOnlyList<string> GetLinesChronological()
    {
        return _lines.AsEnumerable().Reverse().ToArray();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var visibleLines = GetVisibleLineCount();
        var maxOffset = Math.Max(0, _lines.Count - visibleLines);
        _scrollOffset = Math.Clamp(_scrollOffset - Math.Sign(e.Delta) * 3, 0, maxOffset);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(AppTheme.SurfaceSoft);

        var textArea = new Rectangle(0, 0, Math.Max(0, Width - 18), Height);
        var lineHeight = Math.Max(18, TextRenderer.MeasureText("Ag", Font).Height + 2);
        var visibleLines = GetVisibleLineCount();
        using var textBrush = new SolidBrush(AppTheme.TextSecondary);

        for (var index = 0; index < visibleLines; index++)
        {
            var sourceIndex = _scrollOffset + index;
            if (sourceIndex >= _lines.Count)
            {
                break;
            }

            var y = index * lineHeight + 2;
            TextRenderer.DrawText(
                e.Graphics,
                _lines[sourceIndex],
                Font,
                new Rectangle(0, y, textArea.Width, lineHeight),
                AppTheme.TextSecondary,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }

        DrawScrollBar(e.Graphics, visibleLines);
    }

    private int GetVisibleLineCount()
    {
        var lineHeight = Math.Max(18, TextRenderer.MeasureText("Ag", Font).Height + 2);
        return Math.Max(1, Height / lineHeight);
    }

    private void DrawScrollBar(Graphics graphics, int visibleLines)
    {
        if (_lines.Count <= visibleLines)
        {
            return;
        }

        var track = new Rectangle(Width - ScrollBarWidth - 2, 8, ScrollBarWidth, Math.Max(8, Height - 16));
        using var trackBrush = new SolidBrush(AppTheme.Blend(AppTheme.Border, AppTheme.SurfaceSoft, 0.42));
        using var trackPath = RoundedGeometry.CreatePath(track, 5);
        graphics.FillPath(trackBrush, trackPath);

        var ratio = visibleLines / (float)_lines.Count;
        var thumbHeight = Math.Max(24, (int)(track.Height * ratio));
        var maxOffset = Math.Max(1, _lines.Count - visibleLines);
        var travel = Math.Max(1, track.Height - thumbHeight);
        var thumbY = track.Y + (int)(travel * (_scrollOffset / (float)maxOffset));
        var thumb = new Rectangle(track.X, thumbY, track.Width, thumbHeight);
        using var thumbBrush = new SolidBrush(AppTheme.Blend(AppTheme.Accent, AppTheme.TextSecondary, 0.30));
        using var thumbPath = RoundedGeometry.CreatePath(thumb, 5);
        graphics.FillPath(thumbBrush, thumbPath);
    }
}

internal sealed class ThemedMenuColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => AppTheme.Surface;

    public override Color ImageMarginGradientBegin => AppTheme.Surface;

    public override Color ImageMarginGradientMiddle => AppTheme.Surface;

    public override Color ImageMarginGradientEnd => AppTheme.Surface;

    public override Color MenuItemSelected => AppTheme.AccentSoft;

    public override Color MenuItemBorder => AppTheme.Blend(AppTheme.Accent, AppTheme.Border, 0.36);

    public override Color CheckBackground => AppTheme.AccentSoft;

    public override Color CheckSelectedBackground => AppTheme.AccentSoft;

    public override Color CheckPressedBackground => AppTheme.AccentSoft;
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
