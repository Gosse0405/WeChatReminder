namespace WeChatReminder.Services;

internal sealed class FixedDoubleWindow
{
    private readonly double[] _values;
    private int _start;
    private int _count;
    private double _sum;

    public FixedDoubleWindow(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _values = new double[capacity];
    }

    public int Count => _count;

    public double Sum => _sum;

    public double this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _values[GetPhysicalIndex(index)];
        }
    }

    public void Add(double value)
    {
        if (_count < _values.Length)
        {
            _values[GetPhysicalIndex(_count)] = value;
            _count++;
            _sum += value;
            return;
        }

        _sum -= _values[_start];
        _values[_start] = value;
        _sum += value;
        _start = (_start + 1) % _values.Length;
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
        _sum = 0;
    }

    private int GetPhysicalIndex(int index)
    {
        return (_start + index) % _values.Length;
    }
}
