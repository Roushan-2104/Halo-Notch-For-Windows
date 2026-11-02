using System.Windows.Threading;
using Windows.System.Power;

namespace Halo.SystemInfo;

internal sealed class BatteryService
{
    readonly Dispatcher _ui;
    bool _wasPluggedIn;
    int _lastWarned = 101;

    public BatteryService(Dispatcher ui) => _ui = ui;

    public event Action? Changed;
    public event Action<int>? PluggedIn;
    public event Action<int>? Low;

    public bool HasBattery => PowerManager.BatteryStatus != BatteryStatus.NotPresent;
    public int Percent => PowerManager.RemainingChargePercent;
    public bool IsPluggedIn => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
    public bool IsCharging => PowerManager.BatteryStatus == BatteryStatus.Charging || (HasBattery && IsPluggedIn && Percent >= 100);

    public void Start()
    {
        _wasPluggedIn = IsPluggedIn;
        PowerManager.PowerSupplyStatusChanged += (_, _) => _ui.BeginInvoke(OnSupplyChanged);
        PowerManager.RemainingChargePercentChanged += (_, _) => _ui.BeginInvoke(OnPercentChanged);
        PowerManager.BatteryStatusChanged += (_, _) => _ui.BeginInvoke(() => Changed?.Invoke());
    }

    void OnSupplyChanged()
    {
        bool plugged = IsPluggedIn;
        if (plugged && !_wasPluggedIn && HasBattery) PluggedIn?.Invoke(Percent);
        if (plugged) _lastWarned = 101;
        _wasPluggedIn = plugged;
        Changed?.Invoke();
    }

    void OnPercentChanged()
    {
        int p = Percent;
        if (HasBattery && !IsPluggedIn)
        {
            foreach (var threshold in new[] { 20, 10, 5 })
            {
                if (p <= threshold && _lastWarned > threshold)
                {
                    _lastWarned = threshold;
                    Low?.Invoke(p);
                    break;
                }
            }
        }
        Changed?.Invoke();
    }
}
