using System.Runtime.InteropServices;

namespace ynoez.Services;

/// <summary>
/// Requests 1 ms timer precision only for the ynoez process. It is never a
/// global HPET/BCD change and is always paired with timeEndPeriod.
/// </summary>
public static class TimerResolutionService
{
    private static readonly object Sync = new();
    private static bool _enabled;
    private const uint OneMillisecond = 1;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    public static bool IsEnabled
    {
        get { lock (Sync) return _enabled; }
    }

    public static void Enable()
    {
        lock (Sync)
        {
            if (_enabled) return;
            var result = TimeBeginPeriod(OneMillisecond);
            if (result != 0)
                throw new InvalidOperationException("O Windows/driver não aceitou a solicitação de temporizador de 1 ms (código " + result + ").");
            _enabled = true;
            AppLog.Write("Solicitação de temporizador 1 ms ativada apenas para o processo ynoez.");
        }
    }

    public static void Disable()
    {
        lock (Sync)
        {
            if (!_enabled) return;
            var result = TimeEndPeriod(OneMillisecond);
            _enabled = false;
            if (result != 0)
                AppLog.Write("timeEndPeriod retornou código " + result + ".");
            else
                AppLog.Write("Solicitação de temporizador 1 ms removida do processo ynoez.");
        }
    }
}
