using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BluetoothAudioRelay;

internal static class AppTheme
{
    public static readonly Color Background = Color.FromArgb(244, 247, 251);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceSoft = Color.FromArgb(248, 250, 253);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color TextPrimary = Color.FromArgb(28, 39, 55);
    public static readonly Color TextSecondary = Color.FromArgb(100, 116, 139);
    public static readonly Color Accent = Color.FromArgb(36, 107, 253);
    public static readonly Color AccentHover = Color.FromArgb(27, 88, 218);
    public static readonly Color AccentSoft = Color.FromArgb(232, 240, 255);
    public static readonly Color Teal = Color.FromArgb(13, 148, 136);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);

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
                return Teal;
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
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillPath(fill, path);

        if (BorderWidth > 0)
        {
            using var pen = new Pen(BorderColor, BorderWidth);
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
            Color.FromArgb(33, 91, 236),
            Color.FromArgb(13, 148, 136),
            LinearGradientMode.Horizontal);
        e.Graphics.FillPath(brush, path);
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
            fillColor = Primary ? Color.FromArgb(22, 72, 182) : Color.FromArgb(226, 232, 240);
        }
        else if (_hovered)
        {
            fillColor = Primary ? AppTheme.AccentHover : AppTheme.AccentSoft;
            borderColor = Primary ? AppTheme.AccentHover : Color.FromArgb(190, 207, 235);
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
            AppTheme.Teal,
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
