using System.Drawing.Drawing2D;

namespace SetupPulse.App;

internal sealed class DarkTrayMenu : ContextMenuStrip
{
    internal static readonly Color Surface = Color.FromArgb(32, 32, 32);
    internal static readonly Color Foreground = Color.FromArgb(242, 242, 242);
    internal static readonly Color Muted = Color.FromArgb(153, 153, 160);
    internal static readonly Color Accent = Color.FromArgb(193, 139, 255);
    readonly Font menuFont = new("Segoe UI", 9.5f);

    internal DarkTrayMenu()
    {
        BackColor = Surface; ForeColor = Foreground; Font = menuFont;
        Renderer = new DarkRenderer();
        ShowImageMargin = false; ShowCheckMargin = true;
        Padding = new Padding(7, 7, 7, 7);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 16 || Height < 16) return;
        using var path = Rounded(new Rectangle(0, 0, Width, Height), 10 * DeviceDpi / 96f);
        var old = Region; Region = new Region(path); old?.Dispose();
    }

    static GraphicsPath Rounded(Rectangle rectangle, float radius)
    {
        var path = new GraphicsPath(); float diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) menuFont.Dispose();
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Color.FromArgb(56, 56, 60);
        public override Color MenuItemSelected => Color.FromArgb(58, 48, 72);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color SeparatorDark => Color.FromArgb(58, 58, 62);
        public override Color SeparatorLight => Surface;
    }

    sealed class DarkRenderer() : ToolStripProfessionalRenderer(new DarkColors())
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Surface);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            var bounds = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var path = Rounded(bounds, 5);
            using var brush = new SolidBrush(Color.FromArgb(58, 48, 72));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Muted : e.Item is ToolStripMenuItem { Checked: true } ? Accent : Foreground;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var box = e.ImageRectangle;
            float cx = box.Left + box.Width / 2f, cy = box.Top + box.Height / 2f;
            float unit = Math.Max(1, box.Width / 16f);
            using var pen = new Pen(Accent, 2 * unit) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, [new PointF(cx - 4 * unit, cy), new PointF(cx - unit, cy + 3 * unit), new PointF(cx + 5 * unit, cy - 4 * unit)]);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var path = Rounded(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10 * e.ToolStrip.DeviceDpi / 96f);
            using var pen = new Pen(Color.FromArgb(56, 56, 60));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }
    }
}
