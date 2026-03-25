namespace WeChatReminder.Services;

public static class AppSettings
{
    public static class Timing
    {
        public static readonly TimeSpan StartupReadyDelay = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan ReminderStartupSuppressDelay = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan FlashCheckDueTime = TimeSpan.FromMilliseconds(1200);
        public static readonly TimeSpan FlashCheckPeriod = TimeSpan.FromMilliseconds(180);
        public static readonly TimeSpan TrayRelocateInterval = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan OverlayStateWatchInterval = TimeSpan.FromMilliseconds(250);
        public static readonly TimeSpan FlashEndConfirmDelay = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan ForegroundWindowRefreshInterval = TimeSpan.FromSeconds(1.2);
        public static readonly TimeSpan OpenNowReminderSuppressDelay = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan ReminderShowDebounce = TimeSpan.FromMilliseconds(800);
        public static readonly TimeSpan ReminderReopenCooldown = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan OpenWeChatPollInterval = TimeSpan.FromMilliseconds(10);
        public static readonly TimeSpan OpenWeChatWindowFallbackDelay = TimeSpan.FromMilliseconds(160);
        public static readonly TimeSpan OpenWeChatWindowFallbackRetryInterval = TimeSpan.FromMilliseconds(120);
        public static readonly TimeSpan OpenWeChatTrayFallbackDelay = TimeSpan.FromMilliseconds(320);
        public static readonly TimeSpan OpenWeChatTrayPreferredDelay = TimeSpan.FromMilliseconds(220);
        public static readonly TimeSpan OpenWeChatPathPreferenceDuration = TimeSpan.FromMinutes(15);
    }

    public static class Detection
    {
        public const int FlashHistorySize = 20;
        public const int BaselineHistorySize = 18;
        public const int BaselineWarmupCount = 8;
        public const double BrightnessJumpThreshold = 30;
        public const double OcclusionRecoveryThreshold = 6;
        public const int FlashHitThreshold = 3;
        public const int NotFlashThreshold = 10;
    }

    public static class Logging
    {
        public const long MaxLogFileBytes = 1_048_576;
        public const int MaxArchiveFiles = 3;
        public static readonly TimeSpan RepeatCollapseWindow = TimeSpan.FromSeconds(10);
    }
}
