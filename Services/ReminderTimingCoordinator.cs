using System.Windows.Threading;

namespace WeChatReminder.Services;

internal sealed class ReminderTimingCoordinator : IDisposable
{
    private readonly DispatcherTimer _pendingReminderTimer;
    private readonly DispatcherTimer _flashEndConfirmTimer;
    private readonly DispatcherTimer _snoozeTimer;

    public ReminderTimingCoordinator()
    {
        _pendingReminderTimer = CreateDispatcherTimer(PendingReminderTimer_Tick);
        _flashEndConfirmTimer = CreateDispatcherTimer(FlashEndConfirmTimer_Tick);
        _snoozeTimer = CreateDispatcherTimer(SnoozeTimer_Tick);
    }

    public event EventHandler<ReminderSessionEventArgs>? PendingReminderElapsed;
    public event EventHandler? FlashEndConfirmElapsed;
    public event EventHandler? SnoozeElapsed;

    public bool IsReminderScheduled { get; private set; }
    public long? PendingReminderSessionId { get; private set; }
    public DateTime? SnoozeUntil { get; private set; }
    public long? SnoozeSessionId { get; private set; }

    public void ScheduleReminder(long sessionId)
    {
        ScheduleReminder(sessionId, AppSettings.Timing.ReminderShowDebounce);
    }

    public void ScheduleReminder(long sessionId, TimeSpan delay)
    {
        if (PendingReminderSessionId == sessionId && IsReminderScheduled)
            return;

        CancelPendingReminder();

        PendingReminderSessionId = sessionId;
        IsReminderScheduled = true;
        RestartTimer(_pendingReminderTimer, delay);
    }

    public void CancelPendingReminder()
    {
        IsReminderScheduled = false;
        PendingReminderSessionId = null;
        _pendingReminderTimer.Stop();
    }

    public void StartFlashEndConfirm()
    {
        _flashEndConfirmTimer.Stop();
        RestartTimer(_flashEndConfirmTimer, AppSettings.Timing.FlashEndConfirmDelay);
    }

    public void CancelFlashEndConfirm()
    {
        _flashEndConfirmTimer.Stop();
    }

    public void StartSnooze(TimeSpan delay, long sessionId)
    {
        SnoozeUntil = DateTime.Now.Add(delay);
        SnoozeSessionId = sessionId;
        CancelPendingReminder();
        RestartTimer(_snoozeTimer, delay);
    }

    public void CancelSnooze()
    {
        SnoozeUntil = null;
        SnoozeSessionId = null;
        _snoozeTimer.Stop();
    }

    public void Dispose()
    {
        DisposeDispatcherTimer(_pendingReminderTimer, PendingReminderTimer_Tick);
        DisposeDispatcherTimer(_flashEndConfirmTimer, FlashEndConfirmTimer_Tick);
        DisposeDispatcherTimer(_snoozeTimer, SnoozeTimer_Tick);
    }

    private void PendingReminderTimer_Tick(object? sender, EventArgs e)
    {
        long? sessionId = PendingReminderSessionId;
        CancelPendingReminder();
        PendingReminderElapsed?.Invoke(this, new ReminderSessionEventArgs(sessionId));
    }

    private void FlashEndConfirmTimer_Tick(object? sender, EventArgs e)
    {
        _flashEndConfirmTimer.Stop();
        FlashEndConfirmElapsed?.Invoke(this, EventArgs.Empty);
    }

    private void SnoozeTimer_Tick(object? sender, EventArgs e)
    {
        _snoozeTimer.Stop();
        SnoozeElapsed?.Invoke(this, EventArgs.Empty);
    }

    private static DispatcherTimer CreateDispatcherTimer(EventHandler handler)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += handler;
        return timer;
    }

    private static void RestartTimer(DispatcherTimer timer, TimeSpan interval)
    {
        timer.Stop();
        timer.Interval = interval;
        timer.Start();
    }

    private static void DisposeDispatcherTimer(DispatcherTimer timer, EventHandler handler)
    {
        timer.Stop();
        timer.Tick -= handler;
    }
}
