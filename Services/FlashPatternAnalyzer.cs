namespace WeChatReminder.Services;

internal sealed class FlashPatternAnalyzer
{
    public bool IsFlashing(FixedDoubleWindow values)
    {
        if (values.Count < 6)
            return false;

        double min = values[0];
        double max = values[0];

        for (int i = 1; i < values.Count; i++)
        {
            double value = values[i];
            if (value < min)
                min = value;
            if (value > max)
                max = value;
        }

        double amplitude = max - min;
        if (amplitude < 1.6)
            return false;

        double lowThreshold = min + amplitude * 0.35;
        double highThreshold = min + amplitude * 0.65;

        int lowCount = 0;
        int highCount = 0;

        for (int i = 0; i < values.Count; i++)
        {
            double value = values[i];
            if (value <= lowThreshold)
                lowCount++;
            if (value >= highThreshold)
                highCount++;
        }

        return lowCount >= 2 && highCount >= 2;
    }
}
