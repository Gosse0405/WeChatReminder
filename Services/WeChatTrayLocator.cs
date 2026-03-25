using System.Drawing;
using System.Windows.Automation;
using WeChatReminder.Models;

namespace WeChatReminder.Services;

public class WeChatTrayLocator
{
    private readonly object _cacheLock = new();
    private AutomationElement? _cachedTrayElement;

    public bool TryGetWeChatTrayIcon(out WeChatTrayIconTarget? target)
    {
        target = null;

        try
        {
            return TryResolveWeChatTrayIcon(out _, out target);
        }
        catch (Exception ex)
        {
            ClearCache();
            AppLogger.LogError("tray-locator", "TryGetWeChatTrayIcon failed.", ex);
            return false;
        }
    }

    public bool TryClickWeChatTrayIcon()
    {
        if (!TryResolveWeChatTrayIcon(out _, out var target) || target == null)
            return false;

        Native.NativeMethods.ClickScreenPoint(target.ClickPoint.X, target.ClickPoint.Y);
        return true;
    }

    public bool TryActivateWeChatTrayIcon(out string activationMethod)
    {
        activationMethod = "not-found";

        if (!TryResolveWeChatTrayIcon(out var element, out var target) || element == null || target == null)
            return false;

        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokePatternObj) &&
                invokePatternObj is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                activationMethod = "uia-invoke";
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("tray-locator", "InvokePattern activation failed.", ex);
        }

        Native.NativeMethods.ClickScreenPoint(target.ClickPoint.X, target.ClickPoint.Y);
        activationMethod = "mouse-click";
        return true;
    }

    public bool IsTrayIconActuallyVisible(WeChatTrayIconTarget target)
    {
        try
        {
            if (Native.NativeMethods.TryGetCursorPosition(out int cursorX, out int cursorY))
            {
                var hoverRect = target.FullRect;
                hoverRect.Inflate(6, 4);

                if (hoverRect.Contains(cursorX, cursorY))
                    return false;
            }

            IntPtr fg = Native.NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                string fgClass = Native.NativeMethods.GetWindowClassName(fg);

                bool isPopupLike =
                    fgClass.Equals("#32768", StringComparison.OrdinalIgnoreCase) ||
                    fgClass.Contains("Menu", StringComparison.OrdinalIgnoreCase) ||
                    fgClass.Contains("Popup", StringComparison.OrdinalIgnoreCase) ||
                    fgClass.Contains("Xaml", StringComparison.OrdinalIgnoreCase);

                if (isPopupLike && Native.NativeMethods.TryGetWindowRectangle(fg, out Rectangle fgRect))
                {
                    var blockRect = target.FullRect;
                    blockRect.Inflate(8, 6);

                    if (fgRect.IntersectsWith(blockRect))
                        return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("tray-locator", "IsTrayIconActuallyVisible failed.", ex);
            return false;
        }
    }

    private bool TryGetCachedTarget(out WeChatTrayIconTarget? target)
    {
        target = null;

        AutomationElement? element;
        lock (_cacheLock)
        {
            element = _cachedTrayElement;
        }

        if (element == null)
            return false;

        if (!TryCreateTargetFromElement(element, out bool exactMatch, out bool fuzzyMatch, out var candidate) ||
            (!exactMatch && !fuzzyMatch))
        {
            ClearCache();
            return false;
        }

        target = candidate;
        return true;
    }

    private bool TryResolveWeChatTrayIcon(out AutomationElement? selectedElement, out WeChatTrayIconTarget? target)
    {
        selectedElement = null;
        target = null;

        if (TryGetCachedElementAndTarget(out selectedElement, out target))
            return true;

        var root = AutomationElement.RootElement;
        if (root == null)
            return false;

        var taskbar = root.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));

        if (taskbar == null)
            return false;

        var trayButtonCondition = new OrCondition(
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"));

        var candidates = taskbar.FindAll(TreeScope.Descendants, trayButtonCondition);

        AutomationElement? exactElement = null;
        WeChatTrayIconTarget? exactTarget = null;
        AutomationElement? fuzzyElement = null;
        WeChatTrayIconTarget? fuzzyTarget = null;

        foreach (AutomationElement element in candidates)
        {
            if (!TryCreateTargetFromElement(element, out bool exactMatch, out bool fuzzyMatch, out var candidate))
                continue;

            if (exactMatch)
            {
                exactElement = element;
                exactTarget = candidate;
                break;
            }

            if (fuzzyMatch && fuzzyTarget == null)
            {
                fuzzyElement = element;
                fuzzyTarget = candidate;
            }
        }

        selectedElement = exactElement ?? fuzzyElement;
        target = exactTarget ?? fuzzyTarget;

        if (selectedElement == null || target == null)
        {
            ClearCache();
            return false;
        }

        CacheElement(selectedElement);
        return true;
    }

    private bool TryGetCachedElementAndTarget(out AutomationElement? element, out WeChatTrayIconTarget? target)
    {
        target = null;

        lock (_cacheLock)
        {
            element = _cachedTrayElement;
        }

        if (element == null)
            return false;

        if (!TryCreateTargetFromElement(element, out bool exactMatch, out bool fuzzyMatch, out var candidate) ||
            (!exactMatch && !fuzzyMatch))
        {
            ClearCache();
            element = null;
            return false;
        }

        target = candidate;
        return true;
    }

    private static bool TryCreateTargetFromElement(
        AutomationElement element,
        out bool exactMatch,
        out bool fuzzyMatch,
        out WeChatTrayIconTarget? target)
    {
        exactMatch = false;
        fuzzyMatch = false;
        target = null;

        try
        {
            string name = (element.Current.Name ?? string.Empty).Trim();
            string helpText = (element.Current.HelpText ?? string.Empty).Trim();
            string className = (element.Current.ClassName ?? string.Empty).Trim();
            string automationId = (element.Current.AutomationId ?? string.Empty).Trim();
            var rect = element.Current.BoundingRectangle;

            bool isTrayButton =
                className.Equals("SystemTray.NormalButton", StringComparison.OrdinalIgnoreCase) ||
                automationId.Equals("NotifyItemIcon", StringComparison.OrdinalIgnoreCase) ||
                automationId.Equals("SystemTrayIcon", StringComparison.OrdinalIgnoreCase);

            if (!isTrayButton)
                return false;

            if (rect.IsEmpty || rect.Width < 8 || rect.Height < 8)
                return false;

            string allText = $"{name} {helpText} {className} {automationId}".ToLowerInvariant();

            if (allText.Contains("show hidden icons") || allText.Contains("显示隐藏的图标"))
                return false;

            if (allText.Contains("wechatreminder") || allText.Contains("微信提醒助手"))
                return false;

            exactMatch =
                name.Equals("微信", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                helpText.Equals("微信", StringComparison.OrdinalIgnoreCase) ||
                helpText.Equals("Weixin", StringComparison.OrdinalIgnoreCase);

            fuzzyMatch =
                exactMatch ||
                name.Contains("微信", StringComparison.OrdinalIgnoreCase) ||
                helpText.Contains("微信", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Weixin", StringComparison.OrdinalIgnoreCase) ||
                helpText.Contains("Weixin", StringComparison.OrdinalIgnoreCase);

            if (!fuzzyMatch)
                return false;

            int left = (int)rect.Left;
            int top = (int)rect.Top;
            int width = Math.Max(1, (int)rect.Width);
            int height = Math.Max(1, (int)rect.Height);

            int insetX = Math.Min(8, Math.Max(3, width / 12));
            int insetY = Math.Min(8, Math.Max(3, height / 8));

            int sampleLeft = left + insetX;
            int sampleTop = top + insetY;
            int sampleWidth = Math.Max(8, width - insetX * 2);
            int sampleHeight = Math.Max(8, height - insetY * 2);

            int clickX = width >= 40 ? left + 18 : left + width / 2;
            int clickY = top + height / 2;

            target = new WeChatTrayIconTarget
            {
                Name = name,
                FullRect = new Rectangle(left, top, width, height),
                SampleRect = new Rectangle(sampleLeft, sampleTop, sampleWidth, sampleHeight),
                ClickPoint = new Point(clickX, clickY)
            };

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("tray-locator", "Failed to inspect tray automation element.", ex);
            return false;
        }
    }

    private void CacheElement(AutomationElement element)
    {
        lock (_cacheLock)
        {
            _cachedTrayElement = element;
        }
    }

    private void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedTrayElement = null;
        }
    }
}
