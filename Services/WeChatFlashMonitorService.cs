using WeChatReminder.Models;

namespace WeChatReminder.Services;

public class WeChatFlashMonitorService : IDisposable
{
    private readonly WeChatTrayLocator _locator;
    private readonly FlashPatternAnalyzer _analyzer = new();
    private readonly object _stateLock = new();
    private readonly FixedDoubleWindow _history = new(AppSettings.Detection.FlashHistorySize);
    private readonly FixedDoubleWindow _baselineHistory = new(AppSettings.Detection.BaselineHistorySize);

    private System.Threading.Timer? _timer;
    private int _isChecking;

    private DateTime _startupTime = DateTime.Now;
    private DateTime _nextLocateAttemptAt = DateTime.MinValue;
    private WeChatTrayIconTarget? _cachedTarget;

    private bool _isCurrentlyFlashing;
    private long _flashSessionId;

    private int _flashHitCount;
    private int _notFlashCount;

    private bool _isReady;

    private DateTime _occlusionCooldownUntil = DateTime.MinValue;
    private DateTime _trayVisibilityCheckSuppressedUntil = DateTime.MinValue;
    private bool _inOcclusionMode;
    private bool _disposed;

    public event EventHandler<bool>? FlashStateChanged;
    public event EventHandler<bool>? ReadyStateChanged;

    public bool IsCurrentlyFlashing
    {
        get
        {
            lock (_stateLock)
            {
                return _isCurrentlyFlashing;
            }
        }
    }

    public long FlashSessionId
    {
        get
        {
            lock (_stateLock)
            {
                return _flashSessionId;
            }
        }
    }

    public bool IsReady
    {
        get
        {
            lock (_stateLock)
            {
                return _isReady;
            }
        }
    }

    public bool CanTriggerReminder
    {
        get
        {
            lock (_stateLock)
            {
                return _isReady &&
                       _isCurrentlyFlashing &&
                       !_inOcclusionMode &&
                       _cachedTarget != null;
            }
        }
    }

    public WeChatFlashMonitorService(WeChatTrayLocator locator)
    {
        _locator = locator;
    }

    public void Start()
    {
        lock (_stateLock)
        {
            _startupTime = DateTime.Now;
            _nextLocateAttemptAt = DateTime.MinValue;
            _disposed = false;
        }

        AppLogger.LogInfo("flash-monitor", "Service started.");
        _timer = new System.Threading.Timer(
            Check,
            null,
            AppSettings.Timing.FlashCheckDueTime,
            AppSettings.Timing.FlashCheckPeriod);
    }

    public void ForceEndCurrentFlashSession()
    {
        bool flashChanged;

        lock (_stateLock)
        {
            ResetDetectionStateCore(resetBaseline: true);
            EnterOcclusionCooldownCore("ForceEndCurrentFlashSession called", 1.2);
            flashChanged = SetFlashStateCore(false);
        }

        if (flashChanged)
            FlashStateChanged?.Invoke(this, false);
    }

    public void ResetDetectionHistory()
    {
        lock (_stateLock)
        {
            ResetDetectionStateCore(resetBaseline: true);
            EnterOcclusionCooldownCore("ResetDetectionHistory called", 1.2);
        }
    }

    public void SuppressTrayVisibilityChecks(TimeSpan duration)
    {
        lock (_stateLock)
        {
            DateTime until = DateTime.Now.Add(duration);
            if (until > _trayVisibilityCheckSuppressedUntil)
                _trayVisibilityCheckSuppressedUntil = until;
        }

        AppLogger.LogInfo("flash-monitor", $"Tray visibility checks suppressed for {duration.TotalMilliseconds:F0}ms.");
    }

    private void Check(object? state)
    {
        if (Interlocked.Exchange(ref _isChecking, 1) == 1)
            return;

        bool? readyChanged = null;
        bool? flashChanged = null;

        try
        {
            lock (_stateLock)
            {
                DateTime now = DateTime.Now;

                if (_disposed)
                    return;

                if (!_isReady && now - _startupTime >= AppSettings.Timing.StartupReadyDelay)
                {
                    _isReady = true;
                    readyChanged = true;
                }

                bool needLocate = now >= _nextLocateAttemptAt;

                if (needLocate)
                {
                    var oldRect = _cachedTarget?.SampleRect;
                    bool located = _locator.TryGetWeChatTrayIcon(out var newTarget);
                    _nextLocateAttemptAt = now.Add(AppSettings.Timing.TrayRelocateInterval);

                    if (!located || newTarget == null)
                    {
                        _cachedTarget = null;
                        ResetDetectionStateCore(resetBaseline: true);
                        flashChanged = SetFlashStateCore(false);
                        AppLogger.LogInfo("flash-monitor", "Locate failed: target not found.");
                        return;
                    }

                    _cachedTarget = newTarget;

                    if (!oldRect.HasValue || oldRect.Value != _cachedTarget.SampleRect)
                    {
                        ResetDetectionStateCore(resetBaseline: true);
                        flashChanged = SetFlashStateCore(false);
                        AppLogger.LogInfo("flash-monitor", $"Sample rect changed to {_cachedTarget.SampleRect}.");
                    }
                }

                if (_cachedTarget == null)
                {
                    ResetDetectionStateCore(resetBaseline: true);
                    flashChanged = SetFlashStateCore(false);
                    return;
                }

                bool visibilityCheckSuppressed = now < _trayVisibilityCheckSuppressedUntil;
                if (!visibilityCheckSuppressed && !_locator.IsTrayIconActuallyVisible(_cachedTarget))
                {
                    EnterOcclusionCooldownCore("Tray icon is occluded by visibility check", 2.2);
                    flashChanged = SetFlashStateCore(false);
                    ResetDetectionStateCore(resetBaseline: true);
                    return;
                }

                double brightness = ScreenCaptureHelper.CaptureAverageBrightness(_cachedTarget.SampleRect);
                double baseline = _baselineHistory.Count > 0 ? _baselineHistory.Sum / _baselineHistory.Count : brightness;
                double diffFromBaseline = Math.Abs(brightness - baseline);
                AppLogger.LogDebug(
                    "flash-monitor",
                    $"brightness={brightness:F2}, baseline={baseline:F2}, diff={diffFromBaseline:F2}, history={_history.Count}, baselineHistory={_baselineHistory.Count}");

                if (_baselineHistory.Count >= AppSettings.Detection.BaselineWarmupCount &&
                    !_isCurrentlyFlashing &&
                    diffFromBaseline >= AppSettings.Detection.BrightnessJumpThreshold)
                {
                    EnterOcclusionCooldownCore(
                        $"Brightness jump too large: current={brightness:F2}, baseline={baseline:F2}, diff={diffFromBaseline:F2}",
                        2.5);
                    flashChanged = SetFlashStateCore(false);
                    ResetDetectionStateCore(resetBaseline: true);
                    return;
                }

                if (_inOcclusionMode)
                {
                    if (diffFromBaseline <= AppSettings.Detection.OcclusionRecoveryThreshold)
                    {
                        _inOcclusionMode = false;
                        _occlusionCooldownUntil = DateTime.MinValue;
                        ResetDetectionStateCore(resetBaseline: true);
                        AppLogger.LogInfo("flash-monitor", "Occlusion cooldown exited by recovery.");
                    }
                    else if (DateTime.Now < _occlusionCooldownUntil)
                    {
                        flashChanged = SetFlashStateCore(false);
                        ResetDetectionStateCore(resetBaseline: true);
                        return;
                    }
                    else
                    {
                        _inOcclusionMode = false;
                        ResetDetectionStateCore(resetBaseline: true);
                        AppLogger.LogInfo("flash-monitor", "Occlusion cooldown timed out.");
                    }
                }

                AddBaselineCore(brightness);

                _history.Add(brightness);
                bool flashing = _analyzer.IsFlashing(_history);
                AppLogger.LogDebug(
                    "flash-monitor",
                    $"analyzer={flashing}, hitCount={_flashHitCount}, notFlashCount={_notFlashCount}, occlusion={_inOcclusionMode}, ready={_isReady}");

                if (!_isReady)
                    return;

                if (flashing)
                {
                    _flashHitCount++;
                    _notFlashCount = 0;
                }
                else
                {
                    _flashHitCount = 0;
                    _notFlashCount++;
                }

                if (!_isCurrentlyFlashing && _flashHitCount >= AppSettings.Detection.FlashHitThreshold)
                {
                    flashChanged = SetFlashStateCore(true);
                }

                if (_isCurrentlyFlashing && _notFlashCount >= AppSettings.Detection.NotFlashThreshold)
                {
                    flashChanged = SetFlashStateCore(false);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("flash-monitor", "Check failed.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }

        if (readyChanged == true)
        {
            AppLogger.LogInfo("flash-monitor", "Ready state changed: TRUE.");
            ReadyStateChanged?.Invoke(this, true);
        }

        if (flashChanged.HasValue)
        {
            FlashStateChanged?.Invoke(this, flashChanged.Value);
        }
    }

    private void AddBaselineCore(double brightness)
    {
        if (_isCurrentlyFlashing)
            return;

        _baselineHistory.Add(brightness);
    }

    private void EnterOcclusionCooldownCore(string reason, double seconds)
    {
        _inOcclusionMode = true;
        _occlusionCooldownUntil = DateTime.Now.AddSeconds(seconds);
        AppLogger.LogInfo("flash-monitor", $"Enter occlusion cooldown: {reason}");
    }

    private bool SetFlashStateCore(bool value)
    {
        if (_isCurrentlyFlashing == value)
            return false;

        _isCurrentlyFlashing = value;
        _flashHitCount = 0;
        _notFlashCount = 0;

        if (value)
        {
            _flashSessionId++;
            AppLogger.LogInfo("flash-monitor", $"Flash state changed: TRUE, Session={_flashSessionId}");
        }
        else
        {
            AppLogger.LogInfo("flash-monitor", $"Flash state changed: FALSE, Session={_flashSessionId}");
        }

        return true;
    }

    private void ResetDetectionStateCore(bool resetBaseline)
    {
        _history.Clear();
        _flashHitCount = 0;
        _notFlashCount = 0;

        if (resetBaseline)
            _baselineHistory.Clear();
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _disposed = true;
        }

        _timer?.Dispose();
        _timer = null;
    }
}
