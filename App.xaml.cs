using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WeChatReminder.Models;
using WeChatReminder.Native;
using WeChatReminder.Services;
using WeChatReminder.UI;

namespace WeChatReminder;

public partial class App : System.Windows.Application
{
    private static readonly string[] WeChatProcessNames = { "Weixin", "WeChat" };

    private TrayIconService? _trayIconService;
    private WeChatFlashMonitorService? _flashMonitorService;
    private WeChatTrayLocator? _weChatTrayLocator;
    private OpenWeChatHotkeyService? _openWeChatHotkeyService;
    private ReminderTimingCoordinator? _reminderTiming;
    private ReminderOverlayWindow? _overlayWindow;
    private System.Threading.Timer? _stateWatchTimer;

    private bool _wasWeChatForeground;
    private bool _isOverlayClosing;
    private bool _isManualReminderVisible;
    private int _isStateWatchRunning;
    private long? _lastShownSessionId;
    private DateTime? _openNowSuppressUntil;
    private long? _openNowSuppressSessionId;
    private DateTime _reminderEnabledAfter;
    private DateTime _lastOverlayClosedAt = DateTime.MinValue;
    private int _isOpeningWeChat;
    private IntPtr _cachedWeChatWindowHandle = IntPtr.Zero;
    private IntPtr _lastForegroundRootWindow = IntPtr.Zero;
    private bool _lastForegroundWasWeChat;
    private DateTime _nextWeChatWindowRefreshAt = DateTime.MinValue;
    private DateTime _preferTrayOpenPathUntil = DateTime.MinValue;

    private DateTime? SnoozeUntil => _reminderTiming?.SnoozeUntil;
    private long? SnoozeSessionId => _reminderTiming?.SnoozeSessionId;
    private bool IsReminderScheduled => _reminderTiming?.IsReminderScheduled == true;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        _openWeChatHotkeyService = new OpenWeChatHotkeyService();

        _trayIconService = new TrayIconService(
            _openWeChatHotkeyService.ShortcutText,
            AppLogger.IsDetailedEnabled);
        _trayIconService.TestReminderRequested += TrayIconService_TestReminderRequested;
        _trayIconService.ConfigureShortcutRequested += TrayIconService_ConfigureShortcutRequested;
        _trayIconService.ToggleDetailedLoggingRequested += TrayIconService_ToggleDetailedLoggingRequested;
        _trayIconService.ExitRequested += TrayIconService_ExitRequested;
        _trayIconService.ShowBalloon("微信提醒助手", "程序正在初始化，约 10 秒后开始有效检测。");
        _reminderEnabledAfter = DateTime.Now
            .Add(AppSettings.Timing.StartupReadyDelay)
            .Add(AppSettings.Timing.ReminderStartupSuppressDelay);

        _weChatTrayLocator = new WeChatTrayLocator();

        _reminderTiming = new ReminderTimingCoordinator();
        _reminderTiming.PendingReminderElapsed += ReminderTiming_PendingReminderElapsed;
        _reminderTiming.FlashEndConfirmElapsed += ReminderTiming_FlashEndConfirmElapsed;
        _reminderTiming.SnoozeElapsed += ReminderTiming_SnoozeElapsed;

        _flashMonitorService = new WeChatFlashMonitorService(_weChatTrayLocator);
        _flashMonitorService.FlashStateChanged += FlashMonitorService_FlashStateChanged;
        _flashMonitorService.ReadyStateChanged += FlashMonitorService_ReadyStateChanged;
        _flashMonitorService.Start();

        StartStateWatchTimer();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.LogError("app", "DispatcherUnhandledException", e.Exception);
        _trayIconService?.ShowBalloon("微信提醒助手", "程序遇到异常，已尝试自动恢复。");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLogger.LogError("app", "CurrentDomainUnhandledException", ex);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.LogError("app", "TaskSchedulerUnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private void FlashMonitorService_ReadyStateChanged(object? sender, bool isReady)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (isReady)
            {
                _reminderEnabledAfter = DateTime.Now.Add(AppSettings.Timing.ReminderStartupSuppressDelay);
                _trayIconService?.ShowBalloon("微信提醒助手", "初始化完成，已开始监控微信图标闪烁。");
            }
        }, DispatcherPriority.Background);
    }

    private void StartStateWatchTimer()
    {
        _stateWatchTimer = new System.Threading.Timer(
            StateWatchTimer_Tick,
            null,
            AppSettings.Timing.OverlayStateWatchInterval,
            AppSettings.Timing.OverlayStateWatchInterval);
    }

    private void StateWatchTimer_Tick(object? state)
    {
        if (_flashMonitorService == null)
            return;

        if (Interlocked.Exchange(ref _isStateWatchRunning, 1) == 1)
            return;

        try
        {
            bool isForeground = IsWeChatForeground();
            Dispatcher.BeginInvoke(() => ApplyForegroundState(isForeground), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("app", "StateWatchTimer_Tick failed.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isStateWatchRunning, 0);
        }
    }

    private void ApplyForegroundState(bool isForeground)
    {
        if (_flashMonitorService == null)
            return;

        bool isCurrentlyFlashing = _flashMonitorService.IsCurrentlyFlashing;

        if (isForeground && !_wasWeChatForeground)
        {
            CancelFlashEndConfirmTimer();
            CancelPendingReminder();
            CancelSnooze();
            if (!_isManualReminderVisible)
                _flashMonitorService.ForceEndCurrentFlashSession();
            AppLogger.LogDebug("app", "WeChat entered foreground, reset reminder state.");
        }

        if (!isForeground && _wasWeChatForeground)
        {
            CancelFlashEndConfirmTimer();
            CancelPendingReminder();
            CancelSnooze();
            if (!_isManualReminderVisible)
            {
                if (isCurrentlyFlashing)
                {
                    AppLogger.LogDebug("app", "WeChat left foreground during active flash, preserving flash session.");
                }
                else
                {
                    _flashMonitorService.ResetDetectionHistory();
                    AppLogger.LogDebug("app", "WeChat left foreground, reset detection history.");
                }
            }
        }

        _wasWeChatForeground = isForeground;

        if (!_flashMonitorService.IsReady)
            return;

        if (_overlayWindow != null &&
            !_isManualReminderVisible &&
            (isForeground || !_flashMonitorService.CanTriggerReminder))
        {
            AppLogger.LogDebug("app", "Closing overlay because reminder conditions are no longer satisfied.");
            CloseOverlay();
        }

        if (_isManualReminderVisible)
            return;

        if (!isForeground)
            TryScheduleReminderForActiveFlash();
    }

    private bool IsWeChatForeground()
    {
        return TryGetForegroundWeChatWindow() != IntPtr.Zero;
    }

    private IntPtr TryGetForegroundWeChatWindow()
    {
        try
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr foregroundRoot = NativeMethods.GetRootWindow(foreground);
            if (foregroundRoot == IntPtr.Zero)
                foregroundRoot = foreground;

            if (foregroundRoot == _lastForegroundRootWindow)
                return _lastForegroundWasWeChat ? foregroundRoot : IntPtr.Zero;

            if (_cachedWeChatWindowHandle != IntPtr.Zero &&
                !NativeMethods.IsLikelyWeChatMainWindow(_cachedWeChatWindowHandle, WeChatProcessNames))
            {
                _cachedWeChatWindowHandle = IntPtr.Zero;
                _nextWeChatWindowRefreshAt = DateTime.MinValue;
            }

            IntPtr matchedWindow = IntPtr.Zero;
            DateTime now = DateTime.Now;

            if (_cachedWeChatWindowHandle != IntPtr.Zero &&
                foregroundRoot == _cachedWeChatWindowHandle)
            {
                matchedWindow = foregroundRoot;
            }
            else if (NativeMethods.IsLikelyWeChatMainWindow(foregroundRoot, WeChatProcessNames))
            {
                matchedWindow = foregroundRoot;
            }
            else if (now >= _nextWeChatWindowRefreshAt)
            {
                _cachedWeChatWindowHandle = NativeMethods.FindBestWeChatWindow(WeChatProcessNames);
                _nextWeChatWindowRefreshAt = now.Add(AppSettings.Timing.ForegroundWindowRefreshInterval);

                if (_cachedWeChatWindowHandle != IntPtr.Zero &&
                    foregroundRoot == _cachedWeChatWindowHandle)
                {
                    matchedWindow = foregroundRoot;
                }
            }

            if (matchedWindow != IntPtr.Zero)
                RememberWeChatWindow(matchedWindow, now);

            _lastForegroundRootWindow = foregroundRoot;
            _lastForegroundWasWeChat = matchedWindow != IntPtr.Zero;
            return matchedWindow;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("app", "IsWeChatForeground failed.", ex);
            return IntPtr.Zero;
        }
    }

    private void RememberWeChatWindow(IntPtr hWnd, DateTime? now = null)
    {
        if (hWnd == IntPtr.Zero)
            return;

        DateTime effectiveNow = now ?? DateTime.Now;
        _cachedWeChatWindowHandle = hWnd;
        _nextWeChatWindowRefreshAt = effectiveNow.Add(AppSettings.Timing.ForegroundWindowRefreshInterval);
    }

    private bool ShouldPreferTrayOpenPath()
    {
        return DateTime.Now < _preferTrayOpenPathUntil;
    }

    private void MarkTrayOpenPathPreferred()
    {
        _preferTrayOpenPathUntil = DateTime.Now.Add(AppSettings.Timing.OpenWeChatPathPreferenceDuration);
        AppLogger.LogDebug("app", "Tray open path marked as preferred.");
    }

    private void ClearTrayOpenPathPreference()
    {
        _preferTrayOpenPathUntil = DateTime.MinValue;
    }

    private void TrayIconService_TestReminderRequested(object? sender, EventArgs e)
    {
        CancelPendingReminder();
        _isManualReminderVisible = true;
        ShowReminder("微信消息提醒", "检测到微信图标正在闪烁，请及时查看。");
    }

    private void TrayIconService_ConfigureShortcutRequested(object? sender, EventArgs e)
    {
        if (_openWeChatHotkeyService == null)
            return;

        var window = new HotkeySettingsWindow(_openWeChatHotkeyService.ShortcutText);
        bool? result = window.ShowDialog();
        if (result != true)
            return;

        if (!_openWeChatHotkeyService.TrySetShortcut(window.ShortcutText, out string normalized))
        {
            _trayIconService?.ShowBalloon("微信提醒助手", "快捷键格式无效，请使用类似 Ctrl+Alt+W 的格式。");
            return;
        }

        _trayIconService?.UpdateShortcutMenuText(normalized);
        _trayIconService?.ShowBalloon("微信提醒助手", $"已更新快捷键：{normalized}");
        AppLogger.LogInfo("hotkey", $"Shortcut updated to {normalized}.");
    }

    private void TrayIconService_ToggleDetailedLoggingRequested(object? sender, EventArgs e)
    {
        bool enabled = !AppLogger.IsDetailedEnabled;
        AppLogger.SetDetailedMode(enabled);
        _trayIconService?.UpdateDetailedLoggingMenuText(enabled);
        _trayIconService?.ShowBalloon("微信提醒助手", enabled ? "已开启详细日志模式。" : "已关闭详细日志模式。");
    }

    private void TrayIconService_ExitRequested(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private void FlashMonitorService_FlashStateChanged(object? sender, bool isFlashing)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_flashMonitorService == null || !_flashMonitorService.IsReady)
                return;

            if (_isManualReminderVisible)
                return;

            long currentSession = _flashMonitorService.FlashSessionId;
            bool isForeground = _wasWeChatForeground;

            if (isFlashing)
            {
                CancelFlashEndConfirmTimer();

                if (SnoozeSessionId.HasValue && SnoozeSessionId.Value != currentSession)
                    CancelSnooze();

                if (!CanAttemptReminder(currentSession, isForeground))
                    return;

                if (_lastShownSessionId != currentSession &&
                    !SnoozeUntil.HasValue &&
                    !SnoozeSessionId.HasValue &&
                    _overlayWindow == null &&
                    !IsReminderScheduled)
                {
                    ScheduleReminderForSession(currentSession);
                }

                if (SnoozeUntil.HasValue &&
                    SnoozeSessionId.HasValue &&
                    SnoozeSessionId.Value == currentSession &&
                    DateTime.Now >= SnoozeUntil.Value)
                {
                    CancelSnooze();
                    _lastShownSessionId = null;
                    ScheduleReminderForSession(currentSession);
                }

                return;
            }

            CancelPendingReminder();
            StartFlashEndConfirmTimer();

            if (_openNowSuppressSessionId.HasValue && _openNowSuppressSessionId.Value == currentSession)
            {
                _openNowSuppressSessionId = null;
                _openNowSuppressUntil = null;
            }
        }, DispatcherPriority.Background);
    }

    private bool CanAttemptReminder(long currentSessionId, bool isForeground)
    {
        if (_flashMonitorService == null)
            return false;

        if (DateTime.Now < _reminderEnabledAfter)
            return false;

        if (_openNowSuppressUntil.HasValue &&
            _openNowSuppressSessionId.HasValue &&
            _openNowSuppressSessionId.Value == currentSessionId &&
            DateTime.Now < _openNowSuppressUntil.Value)
            return false;

        if (isForeground)
            return false;

        if (!_flashMonitorService.CanTriggerReminder)
            return false;

        if (_lastShownSessionId == currentSessionId)
            return false;

        if (DateTime.Now - _lastOverlayClosedAt < AppSettings.Timing.ReminderReopenCooldown)
            return false;

        return true;
    }

    private void TryScheduleReminderForActiveFlash()
    {
        if (_flashMonitorService == null || _reminderTiming == null)
            return;

        if (_wasWeChatForeground ||
            !_flashMonitorService.IsReady ||
            !_flashMonitorService.IsCurrentlyFlashing ||
            _overlayWindow != null ||
            _isOverlayClosing)
        {
            return;
        }

        long currentSession = _flashMonitorService.FlashSessionId;
        if (_lastShownSessionId == currentSession)
            return;

        if (SnoozeSessionId.HasValue && SnoozeSessionId.Value == currentSession)
            return;

        if (_openNowSuppressUntil.HasValue &&
            _openNowSuppressSessionId.HasValue &&
            _openNowSuppressSessionId.Value == currentSession &&
            DateTime.Now < _openNowSuppressUntil.Value)
        {
            return;
        }

        TimeSpan delay = AppSettings.Timing.ReminderShowDebounce;
        DateTime now = DateTime.Now;

        if (now < _reminderEnabledAfter)
        {
            TimeSpan startupDelay = _reminderEnabledAfter - now;
            if (startupDelay > delay)
                delay = startupDelay;
        }

        TimeSpan reopenCooldownRemaining = GetReminderReopenCooldownRemaining(now);
        if (reopenCooldownRemaining > delay)
            delay = reopenCooldownRemaining;

        ScheduleReminderForSession(currentSession, delay);
    }

    private TimeSpan GetReminderReopenCooldownRemaining(DateTime now)
    {
        TimeSpan elapsedSinceClose = now - _lastOverlayClosedAt;
        if (elapsedSinceClose >= AppSettings.Timing.ReminderReopenCooldown)
            return TimeSpan.Zero;

        return AppSettings.Timing.ReminderReopenCooldown - elapsedSinceClose;
    }

    private void ScheduleReminderForSession(long sessionId)
    {
        ScheduleReminderForSession(sessionId, AppSettings.Timing.ReminderShowDebounce);
    }

    private void ScheduleReminderForSession(long sessionId, TimeSpan delay)
    {
        if (_overlayWindow != null || _isOverlayClosing || _reminderTiming == null)
            return;

        if (_reminderTiming.PendingReminderSessionId == sessionId && _reminderTiming.IsReminderScheduled)
            return;

        _reminderTiming.ScheduleReminder(sessionId, delay);
        AppLogger.LogDebug("app", $"Reminder scheduled for session {sessionId} after {delay.TotalMilliseconds:F0}ms.");
    }

    private void ReminderTiming_PendingReminderElapsed(object? sender, ReminderSessionEventArgs e)
    {
        long? sessionId = e.SessionId;

        if (_flashMonitorService == null || sessionId == null)
            return;

        if (!CanAttemptReminder(sessionId.Value, _wasWeChatForeground))
            return;

        if (_flashMonitorService.FlashSessionId != sessionId.Value)
            return;

        _lastShownSessionId = sessionId.Value;
        _isManualReminderVisible = false;
        ShowReminder("微信消息提醒", "检测到微信图标正在闪烁，请及时查看。");
    }

    private void CancelPendingReminder()
    {
        _reminderTiming?.CancelPendingReminder();
    }

    private void StartFlashEndConfirmTimer()
    {
        _reminderTiming?.StartFlashEndConfirm();
    }

    private void ReminderTiming_FlashEndConfirmElapsed(object? sender, EventArgs e)
    {
        if (_flashMonitorService == null)
            return;

        if (!_flashMonitorService.IsCurrentlyFlashing)
        {
            CancelPendingReminder();
            CancelSnooze();
            CloseOverlay();
        }
    }

    private void CancelFlashEndConfirmTimer()
    {
        _reminderTiming?.CancelFlashEndConfirm();
    }

    private void StartSnooze(TimeSpan delay)
    {
        if (_flashMonitorService == null || _reminderTiming == null)
            return;

        _reminderTiming.StartSnooze(delay, _flashMonitorService.FlashSessionId);
    }

    private void ReminderTiming_SnoozeElapsed(object? sender, EventArgs e)
    {
        if (_flashMonitorService == null)
            return;

        if (!SnoozeUntil.HasValue || !SnoozeSessionId.HasValue)
            return;

        long currentSession = _flashMonitorService.FlashSessionId;

        if (_flashMonitorService.IsCurrentlyFlashing &&
            currentSession == SnoozeSessionId.Value &&
            !_wasWeChatForeground)
        {
            CancelSnooze();
            _lastShownSessionId = null;
            ScheduleReminderForSession(currentSession);
            return;
        }

        CancelSnooze();
    }

    private void CancelSnooze()
    {
        _reminderTiming?.CancelSnooze();
    }

    private void ShowReminder(string title, string message)
    {
        if (_overlayWindow != null || _isOverlayClosing)
            return;

        try
        {
            AppLogger.LogDebug("app", $"Showing reminder overlay: {title}");

            _overlayWindow = new ReminderOverlayWindow(title, message);
            _overlayWindow.ActionSelected += OverlayWindow_ActionSelected;
            _overlayWindow.Closed += OverlayWindow_Closed;
            _overlayWindow.Show();
        }
        catch (Exception ex)
        {
            _overlayWindow = null;
            _isOverlayClosing = false;
            _isManualReminderVisible = false;
            AppLogger.LogError("app", "ShowReminder failed.", ex);
        }
    }

    private void OverlayWindow_Closed(object? sender, EventArgs e)
    {
        if (_overlayWindow != null)
        {
            _overlayWindow.ActionSelected -= OverlayWindow_ActionSelected;
            _overlayWindow.Closed -= OverlayWindow_Closed;
            _overlayWindow = null;
        }

        _isOverlayClosing = false;
        _isManualReminderVisible = false;
        _lastOverlayClosedAt = DateTime.Now;
        AppLogger.LogDebug("app", "Reminder overlay closed.");
    }

    private void OverlayWindow_ActionSelected(object? sender, ReminderAction action)
    {
        switch (action)
        {
            case ReminderAction.OpenNow:
                CancelFlashEndConfirmTimer();
                CancelPendingReminder();
                CancelSnooze();
                _openNowSuppressUntil = DateTime.Now.Add(AppSettings.Timing.OpenNowReminderSuppressDelay);
                _openNowSuppressSessionId = _flashMonitorService?.FlashSessionId;
                CloseOverlay();
                _ = OpenWeChatAsync();
                break;

            case ReminderAction.Snooze10Minutes:
                CancelFlashEndConfirmTimer();
                StartSnooze(TimeSpan.FromMinutes(10));
                _trayIconService?.ShowBalloon("微信提醒", "已设置为 10 分钟后再次提醒。");
                CloseOverlay();
                break;

            case ReminderAction.Snooze1Hour:
                CancelFlashEndConfirmTimer();
                StartSnooze(TimeSpan.FromHours(1));
                _trayIconService?.ShowBalloon("微信提醒", "已设置为 1 小时后再次提醒。");
                CloseOverlay();
                break;
        }
    }

    private async Task OpenWeChatAsync()
    {
        if (Interlocked.Exchange(ref _isOpeningWeChat, 1) == 1)
            return;

        try
        {
            bool success = await Task.Run(OpenWeChatCore);

            if (!success)
            {
                await Dispatcher.InvokeAsync(() =>
                    _trayIconService?.ShowBalloon("微信提醒", "已尝试打开微信。"));
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("app", "OpenWeChatAsync failed.", ex);
            await Dispatcher.InvokeAsync(() =>
                _trayIconService?.ShowBalloon("微信提醒", "已尝试打开微信。"));
        }
        finally
        {
            Interlocked.Exchange(ref _isOpeningWeChat, 0);
        }
    }

    private bool OpenWeChatCore()
    {
        if (_openWeChatHotkeyService == null)
            return false;

        IntPtr foregroundWeChat = TryGetForegroundWeChatWindow();
        if (foregroundWeChat != IntPtr.Zero)
        {
            ClearTrayOpenPathPreference();
            AppLogger.LogInfo("app", $"WeChat is already in foreground: {DescribeWindow(foregroundWeChat)}");
            return true;
        }

        _openWeChatHotkeyService.Execute();
        AppLogger.LogInfo("app", $"Configured WeChat shortcut executed once: {_openWeChatHotkeyService.ShortcutText}.");

        bool preferTrayOpenPath = ShouldPreferTrayOpenPath();
        TimeSpan initialHotkeyWait = preferTrayOpenPath
            ? TimeSpan.FromMilliseconds(10)
            : TimeSpan.FromMilliseconds(20);

        foregroundWeChat = WaitForForegroundWeChat(initialHotkeyWait);
        if (foregroundWeChat != IntPtr.Zero)
        {
            ClearTrayOpenPathPreference();
            AppLogger.LogInfo("app", $"WeChat reached foreground after configured shortcut: {DescribeWindow(foregroundWeChat)}");
            return true;
        }

        if (preferTrayOpenPath && TryOpenWeChatFromTrayIcon())
            return true;

        if (TryActivateWeChatWindow("immediate-post-hotkey", fastOnly: true))
            return true;

        if (TryOpenWeChatFromTrayIcon())
            return true;

        if (TryActivateWeChatWindow("post-tray-last-chance"))
        {
            MarkTrayOpenPathPreferred();
            return true;
        }

        AppLogger.LogInfo("app", $"WeChat activation failure details: {DescribeActivationState()}");
        AppLogger.LogInfo("app", "WeChat did not become foreground within 3 seconds after configured shortcut.");
        return false;
    }

    private bool TryActivateWeChatWindow(string source, bool fastOnly = false)
    {
        IntPtr hWnd = GetBestWeChatActivationCandidate();
        if (hWnd == IntPtr.Zero)
        {
            AppLogger.LogDebug("app", $"No activatable WeChat window found from {source}.");
            return false;
        }

        bool activated = fastOnly
            ? NativeMethods.TryShowAndActivateWindowFast(hWnd)
            : NativeMethods.TryShowAndActivateWindow(hWnd);

        if (!activated)
        {
            AppLogger.LogDebug("app", $"Best WeChat window activation failed from {source}: {DescribeWindow(hWnd)}");
            return false;
        }

        IntPtr foregroundWeChat = WaitForForegroundWeChat(fastOnly
            ? TimeSpan.FromMilliseconds(24)
            : TimeSpan.FromMilliseconds(60));
        if (foregroundWeChat == IntPtr.Zero)
        {
            AppLogger.LogDebug("app", $"WeChat did not enter foreground after best-window activation from {source}.");
            return false;
        }

        RememberWeChatWindow(foregroundWeChat);
        ClearTrayOpenPathPreference();
        AppLogger.LogInfo("app", $"WeChat activated from {source}: {DescribeWindow(foregroundWeChat)}");
        return true;
    }

    private bool TryOpenWeChatFromTrayIcon()
    {
        if (_weChatTrayLocator == null)
            return false;

        if (!_weChatTrayLocator.TryGetWeChatTrayIcon(out var target) || target == null)
        {
            AppLogger.LogInfo("app", "WeChat tray icon fallback skipped: tray icon not found.");
            return false;
        }

        AppLogger.LogInfo(
            "app",
            $"Clicking WeChat tray icon fallback: name={target.Name}, point={target.ClickPoint.X},{target.ClickPoint.Y}, rect={target.FullRect}.");

        _flashMonitorService?.SuppressTrayVisibilityChecks(TimeSpan.FromSeconds(3));

        if (!_weChatTrayLocator.TryActivateWeChatTrayIcon(out string activationMethod))
        {
            AppLogger.LogInfo("app", "WeChat tray icon fallback failed: activation could not be issued.");
            return false;
        }

        AppLogger.LogInfo("app", $"WeChat tray icon fallback activation method: {activationMethod}.");

        IntPtr foregroundWeChat = WaitForForegroundWeChat(TimeSpan.FromMilliseconds(50));
        if (foregroundWeChat != IntPtr.Zero)
        {
            MarkTrayOpenPathPreferred();
            AppLogger.LogInfo("app", $"WeChat reached foreground after tray icon click: {DescribeWindow(foregroundWeChat)}");
            return true;
        }

        if (TryActivateWeChatWindow("post-tray-click", fastOnly: true))
        {
            MarkTrayOpenPathPreferred();
            return true;
        }

        foregroundWeChat = WaitForForegroundWeChat(TimeSpan.FromMilliseconds(40));
        if (foregroundWeChat != IntPtr.Zero)
        {
            MarkTrayOpenPathPreferred();
            AppLogger.LogInfo("app", $"WeChat reached foreground after tray icon follow-up check: {DescribeWindow(foregroundWeChat)}");
            return true;
        }

        AppLogger.LogInfo("app", "WeChat tray icon click did not bring WeChat to foreground within 2 seconds.");
        return false;
    }

    private IntPtr WaitForForegroundWeChat(TimeSpan timeout)
    {
        IntPtr foregroundWeChat = TryGetForegroundWeChatWindow();
        if (foregroundWeChat != IntPtr.Zero)
            return foregroundWeChat;

        DateTime deadline = DateTime.Now.Add(timeout);
        while (DateTime.Now < deadline)
        {
            Thread.Sleep(AppSettings.Timing.OpenWeChatPollInterval);
            foregroundWeChat = TryGetForegroundWeChatWindow();
            if (foregroundWeChat != IntPtr.Zero)
                return foregroundWeChat;
        }

        return IntPtr.Zero;
    }

    private IntPtr GetBestWeChatActivationCandidate()
    {
        if (_cachedWeChatWindowHandle != IntPtr.Zero &&
            NativeMethods.IsLikelyWeChatMainWindow(_cachedWeChatWindowHandle, WeChatProcessNames))
        {
            return _cachedWeChatWindowHandle;
        }

        IntPtr bestWindow = NativeMethods.FindBestWeChatWindow(WeChatProcessNames);
        if (bestWindow != IntPtr.Zero &&
            NativeMethods.IsLikelyWeChatMainWindow(bestWindow, WeChatProcessNames))
        {
            return bestWindow;
        }

        return IntPtr.Zero;
    }

    private IEnumerable<IntPtr> EnumerateWeChatActivationCandidates()
    {
        var yielded = new HashSet<IntPtr>();

        if (_cachedWeChatWindowHandle != IntPtr.Zero &&
            NativeMethods.IsLikelyWeChatMainWindow(_cachedWeChatWindowHandle, WeChatProcessNames) &&
            yielded.Add(_cachedWeChatWindowHandle))
        {
            yield return _cachedWeChatWindowHandle;
        }

        IReadOnlyList<IntPtr> handles = NativeMethods.FindWeChatWindows(WeChatProcessNames);
        for (int i = 0; i < handles.Count; i++)
        {
            IntPtr hWnd = handles[i];
            if (hWnd == IntPtr.Zero ||
                !NativeMethods.IsLikelyWeChatMainWindow(hWnd, WeChatProcessNames) ||
                !yielded.Add(hWnd))
                continue;

            yield return hWnd;
        }
    }

    private static string DescribeWindow(IntPtr hWnd)
    {
        string title = NativeMethods.GetWindowTitle(hWnd);
        string className = NativeMethods.GetWindowClassName(hWnd);
        bool isVisible = NativeMethods.IsWindowVisible(hWnd);
        bool isIconic = NativeMethods.IsIconic(hWnd);
        string rectDescription = NativeMethods.TryGetWindowRectangle(hWnd, out var rect)
            ? $"rect={rect.Width}x{rect.Height}@{rect.Left},{rect.Top}"
            : "rect=<unknown>";
        return $"hWnd=0x{hWnd.ToInt64():X}, visible={isVisible}, iconic={isIconic}, class={className}, title={title}, {rectDescription}";
    }

    private string DescribeActivationState()
    {
        IntPtr foreground = NativeMethods.GetRootWindow(NativeMethods.GetForegroundWindow());
        string foregroundDescription = foreground == IntPtr.Zero
            ? "foreground=<none>"
            : $"foreground={DescribeWindow(foreground)}";

        var candidates = new List<string>();
        foreach (IntPtr hWnd in EnumerateWeChatActivationCandidates())
            candidates.Add(DescribeWindow(hWnd));

        string candidateDescription = candidates.Count == 0
            ? "candidates=<none>"
            : $"candidates=[{string.Join("; ", candidates)}]";

        string cachedDescription = _cachedWeChatWindowHandle == IntPtr.Zero
            ? "cached=<none>"
            : $"cached={DescribeWindow(_cachedWeChatWindowHandle)}";

        string trayDescription;
        if (_weChatTrayLocator != null && _weChatTrayLocator.TryGetWeChatTrayIcon(out var trayTarget) && trayTarget != null)
        {
            trayDescription = $"tray=name={trayTarget.Name}, point={trayTarget.ClickPoint.X},{trayTarget.ClickPoint.Y}, rect={trayTarget.FullRect}";
        }
        else
        {
            trayDescription = "tray=<not-found>";
        }

        return $"{foregroundDescription}, {cachedDescription}, {candidateDescription}, {trayDescription}";
    }

    private void CloseOverlay()
    {
        if (_overlayWindow == null || _isOverlayClosing)
            return;

        try
        {
            _isOverlayClosing = true;
            _overlayWindow.Close();
        }
        catch (Exception ex)
        {
            _isOverlayClosing = false;
            AppLogger.LogError("app", "CloseOverlay failed.", ex);
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;

        CancelFlashEndConfirmTimer();
        CancelPendingReminder();
        CancelSnooze();
        CloseOverlay();

        _stateWatchTimer?.Dispose();
        _stateWatchTimer = null;

        if (_flashMonitorService != null)
        {
            _flashMonitorService.FlashStateChanged -= FlashMonitorService_FlashStateChanged;
            _flashMonitorService.ReadyStateChanged -= FlashMonitorService_ReadyStateChanged;
            _flashMonitorService.Dispose();
        }

        if (_reminderTiming != null)
        {
            _reminderTiming.PendingReminderElapsed -= ReminderTiming_PendingReminderElapsed;
            _reminderTiming.FlashEndConfirmElapsed -= ReminderTiming_FlashEndConfirmElapsed;
            _reminderTiming.SnoozeElapsed -= ReminderTiming_SnoozeElapsed;
            _reminderTiming.Dispose();
            _reminderTiming = null;
        }

        if (_trayIconService != null)
        {
            _trayIconService.TestReminderRequested -= TrayIconService_TestReminderRequested;
            _trayIconService.ConfigureShortcutRequested -= TrayIconService_ConfigureShortcutRequested;
            _trayIconService.ToggleDetailedLoggingRequested -= TrayIconService_ToggleDetailedLoggingRequested;
            _trayIconService.ExitRequested -= TrayIconService_ExitRequested;
            _trayIconService.Dispose();
        }

        AppLogger.Shutdown();
    }
}
