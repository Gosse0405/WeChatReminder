using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WeChatReminder.Native;

public static class NativeMethods
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private enum WINDOWCOMPOSITIONATTRIB
    {
        WCA_ACCENT_POLICY = 19
    }

    private enum ACCENT_STATE
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint GW_OWNER = 4;
    private const uint GA_ROOT = 2;

    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const int SW_SHOWNORMAL = 1;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    public static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public static string GetWindowClassName(IntPtr hWnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public static bool TryShowAndActivateWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        if (GetRootWindow(GetForegroundWindow()) == hWnd)
            return true;

        for (int i = 0; i < 2; i++)
        {
            RestoreWindow(hWnd);
            TryPromoteWindowToForeground(hWnd);

            Thread.Sleep(28);

            IntPtr foreground = GetRootWindow(GetForegroundWindow());
            if (foreground == hWnd)
                return true;
        }

        try
        {
            RestoreWindow(hWnd);
            SwitchToThisWindow(hWnd, true);
            Thread.Sleep(45);
            if (GetRootWindow(GetForegroundWindow()) == hWnd)
                return true;
        }
        catch
        {
        }

        RestoreWindow(hWnd);
        TryPromoteWindowToForeground(hWnd);
        Thread.Sleep(32);

        return GetRootWindow(GetForegroundWindow()) == hWnd;
    }

    public static bool TryShowAndActivateWindowFast(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        if (GetRootWindow(GetForegroundWindow()) == hWnd)
            return true;

        RestoreWindow(hWnd);
        TryPromoteWindowToForeground(hWnd);
        Thread.Sleep(12);

        if (GetRootWindow(GetForegroundWindow()) == hWnd)
            return true;

        try
        {
            SwitchToThisWindow(hWnd, true);
            Thread.Sleep(18);
            return GetRootWindow(GetForegroundWindow()) == hWnd;
        }
        catch
        {
            return false;
        }
    }

    public static void ClickScreenPoint(int x, int y)
    {
        TryGetCursorPosition(out int oldX, out int oldY);

        try
        {
            SetCursorPos(x, y);
            Thread.Sleep(40);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(40);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }
        finally
        {
            if (oldX != 0 || oldY != 0)
            {
                Thread.Sleep(40);
                SetCursorPos(oldX, oldY);
            }
        }
    }

    public static bool TryGetCursorPosition(out int x, out int y)
    {
        x = 0;
        y = 0;

        if (GetCursorPos(out POINT pt))
        {
            x = pt.X;
            y = pt.Y;
            return true;
        }

        return false;
    }

    public static bool IsWindowHandleValid(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && IsWindow(hWnd);
    }

    public static IntPtr GetRootWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return IntPtr.Zero;

        return GetAncestor(hWnd, GA_ROOT);
    }

    public static bool DoesWindowBelongToProcess(IntPtr hWnd, params string[] processNames)
    {
        if (hWnd == IntPtr.Zero || processNames.Length == 0)
            return false;

        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0)
                return false;

            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return IsMatchingProcessName(process.ProcessName, processNames);
        }
        catch
        {
            return false;
        }
    }

    public static IntPtr GetTopLevelWindowFromPoint(int x, int y)
    {
        var pt = new POINT { X = x, Y = y };
        IntPtr hWnd = WindowFromPoint(pt);
        if (hWnd == IntPtr.Zero)
            return IntPtr.Zero;

        return GetRootWindow(hWnd);
    }

    public static bool TryGetWindowRectangle(IntPtr hWnd, out Rectangle rect)
    {
        rect = Rectangle.Empty;

        if (hWnd == IntPtr.Zero)
            return false;

        if (!GetWindowRect(hWnd, out RECT r))
            return false;

        rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return true;
    }

    public static bool IsPopupMenuForeground()
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
                return false;

            string className = GetWindowClassName(hWnd);

            if (className.Equals("#32768", StringComparison.OrdinalIgnoreCase))
                return true;

            if (className.Contains("Menu", StringComparison.OrdinalIgnoreCase))
                return true;

            if (className.Contains("Popup", StringComparison.OrdinalIgnoreCase))
                return true;

            if (className.Contains("Xaml", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryEnableAcrylicBlur(IntPtr hWnd, byte opacity = 216)
    {
        if (hWnd == IntPtr.Zero)
            return false;

        IntPtr accentPtr = IntPtr.Zero;

        try
        {
            var accent = new ACCENT_POLICY
            {
                AccentState = (int)ACCENT_STATE.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = ((uint)opacity << 24) | 0x00F8FAFC,
                AnimationId = 0
            };

            accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = (int)WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
                Data = accentPtr,
                SizeOfData = Marshal.SizeOf<ACCENT_POLICY>()
            };

            return SetWindowCompositionAttribute(hWnd, ref data) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (accentPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(accentPtr);
        }
    }

    public static bool IsLikelyWeChatMainWindow(IntPtr hWnd, params string[] processNames)
    {
        if (hWnd == IntPtr.Zero || processNames.Length == 0 || !IsWindowHandleValid(hWnd))
            return false;

        if (!DoesWindowBelongToProcess(hWnd, processNames))
            return false;

        string className = GetWindowClassName(hWnd);
        if (ShouldSkipWeChatWindowClass(className))
            return false;

        bool isQtWindow = className.StartsWith("Qt", StringComparison.OrdinalIgnoreCase) ||
                          className.Contains("QWindow", StringComparison.OrdinalIgnoreCase);
        if (!isQtWindow)
            return false;

        bool isProcessMainWindow = IsProcessMainWindowHandle(hWnd);
        bool isVisible = IsWindowVisible(hWnd);
        bool isMinimized = IsIconic(hWnd);
        if (!isVisible && !isMinimized && !isProcessMainWindow)
            return false;

        if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
            return false;

        string title = GetWindowTitle(hWnd).Trim();
        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        bool looksLikeWeChatTitle = LooksLikeWeChatWindowTitle(title);

        if (isProcessMainWindow && !isVisible && !isMinimized)
        {
            if (!TryGetWindowRectangle(hWnd, out Rectangle hiddenRect))
                return looksLikeWeChatTitle || hasTitle;

            bool hiddenWindowHasUsableSize = hiddenRect.Width >= 260 && hiddenRect.Height >= 320;
            return hiddenWindowHasUsableSize || looksLikeWeChatTitle || hasTitle;
        }

        if (isMinimized)
            return hasTitle || looksLikeWeChatTitle || isProcessMainWindow;

        if (!TryGetWindowRectangle(hWnd, out Rectangle rect))
            return looksLikeWeChatTitle || hasTitle || isProcessMainWindow;

        bool hasUsableSize = rect.Width >= 260 && rect.Height >= 320;
        if (looksLikeWeChatTitle)
            return hasUsableSize || isProcessMainWindow;

        if (hasTitle && hasUsableSize)
            return true;

        return isProcessMainWindow && hasUsableSize;
    }

    public static IReadOnlyList<IntPtr> FindWeChatWindows(params string[] processNames)
    {
        if (processNames.Length == 0)
            return Array.Empty<IntPtr>();

        var candidates = new List<WindowCandidate>();

        EnumWindows((hWnd, lParam) =>
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0)
                    return true;

                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                if (!IsMatchingProcessName(process.ProcessName, processNames))
                    return true;

                if (!IsLikelyWeChatMainWindow(hWnd, processNames))
                    return true;

                bool isVisible = IsWindowVisible(hWnd);
                bool isMinimized = IsIconic(hWnd);
                bool hasOwner = GetWindow(hWnd, GW_OWNER) != IntPtr.Zero;

                string className = GetWindowClassName(hWnd);
                if (ShouldSkipWeChatWindowClass(className))
                    return true;

                string title = GetWindowTitle(hWnd);
                bool isQtWindow = className.StartsWith("Qt", StringComparison.OrdinalIgnoreCase) ||
                                  className.Contains("QWindow", StringComparison.OrdinalIgnoreCase);
                bool looksLikeMainWindow =
                    title == "寰俊" ||
                    title == "Weixin" ||
                    title.Contains("寰俊") ||
                    title.Contains("Weixin", StringComparison.OrdinalIgnoreCase) ||
                    isQtWindow;

                if (!isVisible && !isMinimized && !looksLikeMainWindow)
                    return true;

                int score = 0;
                if (isVisible) score += 300;
                if (isMinimized) score += 180;
                if (!isVisible && !isMinimized) score += 120;
                if (!hasOwner) score += 40;

                if (title == "微信" || title == "Weixin") score += 200;
                if (title.Contains("微信")) score += 150;
                if (title.Contains("Weixin", StringComparison.OrdinalIgnoreCase)) score += 120;
                if (className.StartsWith("Qt", StringComparison.OrdinalIgnoreCase)) score += 80;
                if (className.Contains("QWindow", StringComparison.OrdinalIgnoreCase)) score += 60;
                if (LooksLikeWeChatWindowTitle(title)) score += 220;
                if (IsProcessMainWindowHandle(hWnd)) score += 120;

                if (TryGetWindowRectangle(hWnd, out Rectangle rect))
                {
                    int area = rect.Width * rect.Height;
                    if (area >= 700_000) score += 90;
                    else if (area >= 280_000) score += 45;
                }

                if (string.IsNullOrWhiteSpace(title)) score -= 80;
                if (hasOwner) score -= 60;

                candidates.Add(new WindowCandidate(hWnd, score));
            }
            catch
            {
            }

            return true;
        }, IntPtr.Zero);

        if (candidates.Count == 0)
            return Array.Empty<IntPtr>();

        candidates.Sort(static (left, right) => right.Score.CompareTo(left.Score));

        var handles = new IntPtr[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            handles[i] = candidates[i].Handle;

        return handles;
    }

    public static IntPtr FindBestWeChatWindow(params string[] processNames)
    {
        IReadOnlyList<IntPtr> handles = FindWeChatWindows(processNames);
        return handles.Count > 0 ? handles[0] : IntPtr.Zero;
    }

    private static void RestoreWindow(IntPtr hWnd)
    {
        ShowWindow(hWnd, SW_RESTORE);
        ShowWindow(hWnd, SW_SHOWNORMAL);
        ShowWindow(hWnd, SW_SHOW);
    }

    private static void TryPromoteWindowToForeground(IntPtr hWnd)
    {
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint targetThread = GetWindowThreadProcessId(hWnd, out _);

        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attachedToForeground = AttachThreadInput(currentThread, foregroundThread, true);

            if (targetThread != 0 &&
                targetThread != currentThread &&
                targetThread != foregroundThread)
            {
                attachedToTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            BringWindowToTop(hWnd);
            SetActiveWindow(hWnd);
            SetFocus(hWnd);
            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attachedToTarget)
                AttachThreadInput(currentThread, targetThread, false);

            if (attachedToForeground)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static bool IsMatchingProcessName(string processName, string[] processNames)
    {
        foreach (string candidate in processNames)
        {
            if (string.Equals(processName, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsProcessMainWindowHandle(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0)
                return false;

            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            process.Refresh();
            return process.MainWindowHandle == hWnd;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeWeChatWindowTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        return title.Equals("微信", StringComparison.OrdinalIgnoreCase) ||
               title.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
               title.Equals("WeChat", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("微信", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("Weixin", StringComparison.OrdinalIgnoreCase) ||
               title.Contains("WeChat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipWeChatWindowClass(string className)
    {
        if (string.IsNullOrEmpty(className))
            return false;

        return className.Contains("ime", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("msctf", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("sogou", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("chrome_systemmessagewindow", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("base_powermessagewindow", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("wxtrayiconmessagewindowclass", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("sopy", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("savebits", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct WindowCandidate(IntPtr Handle, int Score);
}
