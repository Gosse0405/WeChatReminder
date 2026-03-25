using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WeChatReminder.Services;

public class TrayIconService : IDisposable
{
    private static readonly Uri AppIconUri = new("pack://application:,,,/Assets/logo.ico", UriKind.Absolute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Font _menuFont;
    private readonly ToolStripMenuItem _hotkeyItem;
    private readonly ToolStripMenuItem _detailedLogItem;
    private Icon? _trayIcon;

    public event EventHandler? TestReminderRequested;
    public event EventHandler? ConfigureShortcutRequested;
    public event EventHandler? ToggleDetailedLoggingRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(string hotkeyText, bool detailedLoggingEnabled)
    {
        _menuFont = new Font("Microsoft YaHei UI", 12F, FontStyle.Regular);

        _menu = new ContextMenuStrip
        {
            Renderer = new CleanMenuRenderer(_menuFont),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            BackColor = Color.FromArgb(248, 249, 251),
            ForeColor = Color.FromArgb(28, 30, 33),
            Font = _menuFont,
            Padding = new Padding(5, 5, 5, 5),
            AutoSize = false,
            Size = new Size(240, 146)
        };

        _menu.Opening += Menu_Opening;
        _menu.Closed += Menu_Closed;

        var testItem = new ToolStripMenuItem("测试提醒弹窗");
        testItem.Click += (s, e) => TestReminderRequested?.Invoke(this, EventArgs.Empty);

        _hotkeyItem = new ToolStripMenuItem();
        _hotkeyItem.Click += (s, e) => ConfigureShortcutRequested?.Invoke(this, EventArgs.Empty);
        UpdateShortcutMenuText(hotkeyText);

        _detailedLogItem = new ToolStripMenuItem();
        _detailedLogItem.Click += (s, e) => ToggleDetailedLoggingRequested?.Invoke(this, EventArgs.Empty);
        UpdateDetailedLoggingMenuText(detailedLoggingEnabled);

        var exitItem = new ToolStripMenuItem("退出程序");
        exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

        StyleMenuItem(testItem);
        StyleMenuItem(_hotkeyItem);
        StyleMenuItem(_detailedLogItem);
        StyleMenuItem(exitItem);

        _menu.Items.Add(testItem);
        _menu.Items.Add(_hotkeyItem);
        _menu.Items.Add(_detailedLogItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _trayIcon = CreateTrayIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = "微信提醒助手",
            ContextMenuStrip = _menu
        };

        _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
    }

    public void UpdateShortcutMenuText(string hotkeyText)
    {
        _hotkeyItem.Text = $"打开微信快捷键: {hotkeyText}";
    }

    public void UpdateDetailedLoggingMenuText(bool enabled)
    {
        _detailedLogItem.Text = enabled ? "详细日志: 已开启" : "详细日志: 已关闭";
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        TestReminderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Menu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ApplyRoundedMenuRegion(_menu, 3);
    }

    private void Menu_Closed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        _menu.Region?.Dispose();
        _menu.Region = null;
    }

    private void StyleMenuItem(ToolStripMenuItem item)
    {
        item.AutoSize = false;
        item.Size = new Size(240, 32);
        item.Margin = new Padding(0);
        item.Padding = Padding.Empty;
        item.BackColor = Color.FromArgb(248, 249, 251);
        item.ForeColor = Color.FromArgb(28, 30, 33);
        item.TextAlign = ContentAlignment.MiddleLeft;
        item.Font = _menuFont;
    }

    private void ApplyRoundedMenuRegion(ContextMenuStrip menu, int radius)
    {
        try
        {
            if (menu.Width <= 0 || menu.Height <= 0)
                return;

            using var path = CreateRoundedRectangle(new Rectangle(0, 0, menu.Width, menu.Height), radius);
            menu.Region?.Dispose();
            menu.Region = new Region(path);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("tray", "ApplyRoundedMenuRegion failed.", ex);
        }
    }

    private Icon CreateTrayIcon()
    {
        Icon? packagedIcon = TryLoadPackagedIcon();
        if (packagedIcon != null)
            return packagedIcon;

        return CreateFallbackTrayIcon();
    }

    private Icon? TryLoadPackagedIcon()
    {
        try
        {
            var resourceInfo = System.Windows.Application.GetResourceStream(AppIconUri);
            if (resourceInfo?.Stream == null)
                return null;

            using var iconStream = resourceInfo.Stream;
            using var memoryStream = new MemoryStream();
            iconStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var icon = new Icon(memoryStream);
            return (Icon)icon.Clone();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("tray", "Load packaged tray icon failed.", ex);
            return null;
        }
    }

    private Icon CreateFallbackTrayIcon()
    {
        int size = 64;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var bgRect = new Rectangle(3, 3, 58, 58);
        using (var path = CreateRoundedRectangle(bgRect, 18))
        using (var brush = new LinearGradientBrush(
            new Point(3, 3),
            new Point(61, 61),
            Color.FromArgb(6, 183, 91),
            Color.FromArgb(16, 150, 72)))
        {
            g.FillPath(brush, path);
        }

        using (var path = CreateRoundedRectangle(bgRect, 18))
        using (var pen = new Pen(Color.FromArgb(130, 255, 255, 255), 2))
        {
            g.DrawPath(pen, path);
        }

        using (var bubbleBrush = new SolidBrush(Color.White))
        {
            g.FillEllipse(bubbleBrush, 12, 14, 30, 24);

            Point[] tail =
            {
                new Point(18, 33),
                new Point(14, 44),
                new Point(28, 36)
            };
            g.FillPolygon(bubbleBrush, tail);
        }

        using (var bubbleBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
        {
            g.FillEllipse(bubbleBrush, 33, 24, 17, 14);
        }

        using (var redBrush = new SolidBrush(Color.FromArgb(239, 68, 68)))
        {
            g.FillEllipse(redBrush, 41, 7, 15, 15);
        }

        using (var whitePen = new Pen(Color.FromArgb(235, 255, 255, 255), 2))
        {
            g.DrawEllipse(whitePen, 41, 7, 15, 15);
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    public void ShowStartedTip()
    {
        ShowBalloon("微信提醒助手", "程序已启动，正在后台监控。");
    }

    public void ShowBalloon(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _menu.Opening -= Menu_Opening;
        _menu.Closed -= Menu_Closed;
        _menu.Region?.Dispose();
        _menu.Dispose();

        _trayIcon?.Dispose();
        _trayIcon = null;
        _menuFont.Dispose();
    }

    private class CleanMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Font _font;

        public CleanMenuRenderer(Font font) : base(new CleanColorTable())
        {
            _font = font;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Color.FromArgb(248, 249, 251));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);

            using var pen = new Pen(Color.FromArgb(220, 223, 228));
            var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, rect);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var rect = new Rectangle(7, 3, e.Item.Width - 14, e.Item.Height - 2);

            Color fill = e.Item.Selected
                ? Color.FromArgb(6, 183, 91)
                : Color.FromArgb(248, 249, 251);

            using var path = CreateRoundedRect(rect, 3);
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color textColor = e.Item.Selected
                ? Color.White
                : Color.FromArgb(28, 30, 33);

            var rect = new Rectangle(15, 0, e.Item.Width - 30, e.Item.Height);

            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                _font,
                rect,
                textColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.ContentRectangle.Top + e.Item.ContentRectangle.Height / 2;
            using var pen = new Pen(Color.FromArgb(226, 228, 232));
            e.Graphics.DrawLine(pen, 14, y, e.Item.Width - 14, y);
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }

    private class CleanColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(220, 223, 228);
        public override Color ToolStripDropDownBackground => Color.FromArgb(248, 249, 251);
        public override Color ImageMarginGradientBegin => Color.FromArgb(248, 249, 251);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(248, 249, 251);
        public override Color ImageMarginGradientEnd => Color.FromArgb(248, 249, 251);
        public override Color MenuItemSelected => Color.FromArgb(6, 183, 91);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(6, 183, 91);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(6, 183, 91);
    }
}
