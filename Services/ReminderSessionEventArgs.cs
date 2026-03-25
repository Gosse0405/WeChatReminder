namespace WeChatReminder.Services;

internal sealed class ReminderSessionEventArgs : EventArgs
{
    public ReminderSessionEventArgs(long? sessionId)
    {
        SessionId = sessionId;
    }

    public long? SessionId { get; }
}
