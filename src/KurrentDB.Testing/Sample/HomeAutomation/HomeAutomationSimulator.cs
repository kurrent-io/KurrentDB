using Bogus;

namespace KurrentDB.Testing.Sample.HomeAutomation;

/// <summary>
/// Centralized context for all simulation and fake data generation.
/// </summary>
public static class SimulatorContext {
    public static readonly Faker Faker = new();
}

/// <summary>
/// Simulates home automation IoT events for a specific home.
/// </summary>
public class HomeAutomationSimulator {
    readonly List<Device> _devices;
    readonly Home         _home;

    public HomeAutomationSimulator() {
        _home    = new HomeFaker().Generate();
        _devices = DeviceFactory.CreateDevicesForHome(_home, 12);
    }

    public HomeAutomationSimulator(Home home) {
        _home    = home;
        _devices = DeviceFactory.CreateDevicesForHome(_home, 12);
    }

    public Home                  Home    => _home;
    public IReadOnlyList<Device> Devices => _devices.AsReadOnly();

    public static HomeAutomationSimulator ForHome(string homeName) {
        var home = new HomeFaker()
            .RuleFor(h => h.Name, f => homeName)
            .Generate();

        return new HomeAutomationSimulator(home);
    }

    public List<object> Simulate(int events) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(events, nameof(events));
        return Simulate(events, null);
    }

    public List<object> Simulate(int events, long? startTime, int maxIntervalMinutes = 30) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(events, nameof(events));
        ArgumentOutOfRangeException.ThrowIfNegative(maxIntervalMinutes, nameof(maxIntervalMinutes));

        var eventList = new List<object>();
        var timestamp = startTime ?? DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        for (var i = 0; i < events; i++) {
            timestamp += SimulatorContext.Faker.Random.Number(1, maxIntervalMinutes) * 60000L;

            var device       = SimulatorContext.Faker.PickRandom(_devices);
            var primaryEvent = GenerateEventForDevice(device, timestamp);
            eventList.Add(primaryEvent);

            var correlatedEvents = GenerateCorrelatedEvents(device, timestamp);
            eventList.AddRange(correlatedEvents);
        }

        return eventList;
    }

    object GenerateEventForDevice(Device device, long timestamp) =>
        device switch {
            Thermostat thermostat       => new TemperatureReadingFaker(thermostat, timestamp).Generate(),
            MotionSensor motionSensor   => new MotionDetectedFaker(motionSensor, timestamp).Generate(),
            SmartLight smartLight       => new LightStateChangedFaker(smartLight, timestamp).Generate(),
            DoorLock doorLock           => new DoorStateChangedFaker(doorLock, timestamp).Generate(),
            SmokeDetector smokeDetector => new SmokeDetectedFaker(smokeDetector, timestamp).Generate(),
            _                           => throw new NotSupportedException($"Event generation not supported for device type: {device.GetType()}")
        };

    IEnumerable<object> GenerateCorrelatedEvents(Device device, long timestamp) {
        if (device is MotionSensor motionSensor && SimulatorContext.Faker.Random.Bool(0.3f)) {
            var lightsInSameRoom = _devices
                .OfType<SmartLight>()
                .Where(light => light.Room.RoomId == motionSensor.Room.RoomId)
                .Take(1);

            foreach (var light in lightsInSameRoom) {
                var correlatedTimestamp = timestamp + SimulatorContext.Faker.Random.Long(2000L, 10000L);
                yield return new LightStateChangedFaker(light, correlatedTimestamp).Generate();
            }
        }
    }
}
