using EventStore.Plugins.Subsystems;

namespace EventStore.Plugins.Telemetry.RealPlugins;

using System.Diagnostics.Metrics;

public interface IMeteredSubsystemsPlugin : ISubsystemsPlugin {
    public string MeterName { get; }
}

public class Plugin0 : IMeteredSubsystemsPlugin {
    
    public Plugin0(IMeterFactory meterFactory) {
        Name = "Plugin0";
        Version = "1.0.0";
        CommandLineName = "plugin-zero";
        //Meter = new($"{Name}-Meter", Version);
        
        Meter = meterFactory.Create(new MeterOptions(Name)
        {
            Version = Version
        });
        
        TimedCounter = Meter.CreateCounter<long>("plugin_0_timed_counter");
        
        RequestsCounter = Meter.CreateCounter<long>("plugin_0_http_requests");
        
        _ = Task.Run(async () => {
            TimedCounter.Add(1);
            await Task.Delay(2000);
        });
    }
    
    Meter Meter { get; } 
    Counter<long> TimedCounter { get; }
    Counter<long> RequestsCounter { get; }
    
    public string Name { get; }
    public string Version { get; }
    public string CommandLineName { get; }
    public string MeterName => Meter.Name;

    public void CountRequest() => 
        RequestsCounter.Add(1);

    public IReadOnlyList<ISubsystem> GetSubsystems() => [];
}