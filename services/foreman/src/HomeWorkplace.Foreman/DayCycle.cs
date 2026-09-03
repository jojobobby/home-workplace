using Microsoft.Extensions.Hosting;

namespace HomeWorkplace.Foreman;

/// <summary>
/// Drives the working day: wakes employees when their shift starts, and at shift end (with no
/// unexpired call-in override) issues a wrap-up run and puts them to sleep. Ticks on the
/// injected clock so tests can drive it with a fake time provider.
/// </summary>
public sealed class DayCycle : BackgroundService
{
    private readonly EmployeeCatalog _employees;
    private readonly RunSupervisor _supervisor;
    private readonly ForemanOptions _options;
    private readonly TimeProvider _clock;

    public DayCycle(EmployeeCatalog employees, RunSupervisor supervisor, ForemanOptions options, TimeProvider clock)
    {
        _employees = employees;
        _supervisor = supervisor;
        _options = options;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(_options.SchedulerTickSeconds), _clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
            try { await TickAsync(stoppingToken); } catch { /* a bad tick must not kill the loop */ }
        }
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        var now = _clock.GetLocalNow();
        var timeOfDay = TimeOnly.FromDateTime(now.DateTime);

        foreach (var def in _employees.Definitions)
        {
            var s = _employees.GetState(def.Id);
            var overrideActive = s.AwakeOverrideUntil is { } until && now < until;
            var desiredAwake = overrideActive ||
                EmployeeCatalog.WithinShift(timeOfDay, def.Schedule.WakeTime, def.Schedule.SleepTime);

            if (desiredAwake && s.Status == EmployeeStatus.Asleep)
            {
                _employees.Wake(def.Id, s.AwakeOverrideUntil);
                _supervisor.Pump();
            }
            else if (!desiredAwake && s.Status != EmployeeStatus.Asleep && !_supervisor.IsBusy(def.Id))
            {
                await _supervisor.WrapUpAsync(def.Id, ct);
                _employees.Sleep(def.Id);
            }
        }
    }
}
