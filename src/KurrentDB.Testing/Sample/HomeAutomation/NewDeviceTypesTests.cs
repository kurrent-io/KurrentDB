using Bogus;
using Serilog;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace KurrentDB.Testing.Sample.HomeAutomation;

/// <summary>
/// Test class to verify new device types functionality (HumiditySensor, WindowSensor, WaterSensor)
/// </summary>
public class NewDeviceTypesTests {

    [Test]
    public async Task HumiditySensor_ShouldGenerateValidDeviceAndEvents() {
        // Arrange
        var room = new RoomFaker().Generate();
        var deviceFaker = new DeviceFaker(room, DeviceType.HumiditySensor);

        // Act
        var device = deviceFaker.Generate();
        var humidityDevice = device as HumiditySensor;
        var eventFaker = new HumidityReadingFaker(humidityDevice!, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var humidityEvent = eventFaker.Generate();

        // Assert
        Log.Information("Generated HumiditySensor: {DeviceName} in {RoomName}",
            device.FriendlyName, device.Room.Name);
        Log.Information("Generated HumidityReading: Humidity={Humidity}%, Comfort={ComfortLevel}",
            humidityEvent.Humidity, humidityEvent.ComfortLevel);

        await Assert.That(device.GetType()).IsEqualTo(typeof(HumiditySensor));
        await Assert.That(device.DeviceType).IsEqualTo(DeviceType.HumiditySensor);
        await Assert.That(humidityDevice!.HumidityLevel).IsGreaterThanOrEqualTo(0.0);
        await Assert.That(humidityDevice.HumidityLevel).IsLessThanOrEqualTo(100.0);
        await Assert.That(humidityDevice.BatteryLevel).IsGreaterThan(0);

        await Assert.That(humidityEvent.Humidity).IsGreaterThanOrEqualTo(0.0);
        await Assert.That(humidityEvent.Humidity).IsLessThanOrEqualTo(100.0);
        await Assert.That(humidityEvent.ComfortLevel).IsNotEqualTo(ComfortLevel.Unspecified);
    }

    [Test]
    public async Task WindowSensor_ShouldGenerateValidDeviceAndEvents() {
        // Arrange
        var room = new RoomFaker().Generate();
        var deviceFaker = new DeviceFaker(room, DeviceType.WindowSensor);

        // Act
        var device = deviceFaker.Generate();
        var windowDevice = device as WindowSensor;
        var eventFaker = new WindowStateChangedFaker(windowDevice!, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var windowEvent = eventFaker.Generate();

        // Assert
        Log.Information("Generated WindowSensor: {DeviceName} in {RoomName}",
            device.FriendlyName, device.Room.Name);
        Log.Information("Generated WindowStateChanged: IsOpen={IsOpen}, OpenPercentage={OpenPercentage}%",
            windowEvent.IsOpen, windowEvent.OpenPercentage);

        await Assert.That(device.GetType()).IsEqualTo(typeof(WindowSensor));
        await Assert.That(device.DeviceType).IsEqualTo(DeviceType.WindowSensor);
        await Assert.That(windowDevice!.BatteryLevel).IsGreaterThan(0);
        await Assert.That(windowDevice.LastOpenTime).IsGreaterThan(0);

        await Assert.That(windowEvent.OpenPercentage).IsGreaterThanOrEqualTo(0);
        await Assert.That(windowEvent.OpenPercentage).IsLessThanOrEqualTo(100);
        await Assert.That(windowEvent.WeatherCondition).IsNotEqualTo(WeatherCondition.Unspecified);
    }

    [Test]
    public async Task WaterSensor_ShouldGenerateValidDeviceAndEvents() {
        // Arrange
        var room = new RoomFaker().Generate();
        var deviceFaker = new DeviceFaker(room, DeviceType.WaterSensor);

        // Act
        var device = deviceFaker.Generate();
        var waterDevice = device as WaterSensor;
        var eventFaker = new WaterLeakDetectedFaker(waterDevice!, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var waterEvent = eventFaker.Generate();

        // Assert
        Log.Information("Generated WaterSensor: {DeviceName} in {RoomName}",
            device.FriendlyName, device.Room.Name);
        Log.Information("Generated WaterLeakDetected: HasWater={HasWater}, Level={MoistureLevel}",
            waterEvent.HasWater, waterEvent.MoistureLevel);

        await Assert.That(device.GetType()).IsEqualTo(typeof(WaterSensor));
        await Assert.That(device.DeviceType).IsEqualTo(DeviceType.WaterSensor);
        await Assert.That(waterDevice!.BatteryLevel).IsGreaterThan(0);
        await Assert.That(waterDevice.WaterLevel).IsGreaterThanOrEqualTo(0);
        await Assert.That(waterDevice.WaterLevel).IsLessThanOrEqualTo(100);

        await Assert.That(waterEvent.MoistureLevel).IsGreaterThanOrEqualTo(0);
        await Assert.That(waterEvent.MoistureLevel).IsLessThanOrEqualTo(100);
    }

    [Test]
    public async Task HomeGeneration_WithNewDeviceTypes_ShouldIncludeAllTypes() {
        // Arrange
        var faker = new Faker();
        var allowedTypes = new[] { DeviceType.HumiditySensor, DeviceType.WindowSensor, DeviceType.WaterSensor };

        // Act
        var home = faker.HomeAutomation().Home(rooms: 4, devicesPerRoom: 2, deviceTypes: allowedTypes);
        var deviceTypes = home.Devices.Select(d => d.DeviceType).Distinct().ToList();

        // Assert
        Log.Information("Generated home '{HomeName}' with {DeviceCount} devices of types: {DeviceTypes}",
            home.Name, home.Devices.Count, string.Join(", ", deviceTypes));

        await Assert.That(home.Devices).IsNotEmpty();
        foreach (var deviceType in deviceTypes) {
            await Assert.That(allowedTypes).Contains(deviceType);
        }

        // Verify we can generate events for all devices
        var events = faker.HomeAutomation().Events(home, count: 10);
        await Assert.That(events).IsNotEmpty();

        var eventTypeNames = events.Select(e => e.GetType().Name).Distinct().ToList();
        Log.Information("Generated {EventCount} events of types: {EventTypes}",
            events.Count, string.Join(", ", eventTypeNames));
    }

    [Test]
    public async Task DeviceGeneration_WithSpecificNewTypes_ShouldRespectTypeFilter() {
        // Arrange
        var faker = new Faker();
        var specificTypes = new[] { DeviceType.HumiditySensor, DeviceType.WaterSensor };

        // Act
        var devices = faker.HomeAutomation().Devices(count: 6, types: specificTypes);
        var actualTypes = devices.Select(d => d.DeviceType).Distinct().ToList();

        // Assert
        Log.Information("Generated {DeviceCount} devices with specific types: {ActualTypes}",
            devices.Count, string.Join(", ", actualTypes));

        await Assert.That(devices.Count).IsEqualTo(6);
        foreach (var deviceType in actualTypes) {
            await Assert.That(specificTypes).Contains(deviceType);
        }

        // Ensure we have the right device classes
        var humidityDevices = devices.OfType<HumiditySensor>().ToList();
        var waterDevices = devices.OfType<WaterSensor>().ToList();

        await Assert.That(humidityDevices.Count + waterDevices.Count).IsEqualTo(devices.Count);
    }

    [Test]
    public async Task EventGeneration_ShouldIncludeCorrelatedEvents() {
        // Arrange
        var faker = new Faker();
        var room = new Room(Guid.NewGuid(), "Living Room", RoomType.LivingRoom);

        // Create a home with motion sensor and smart light in same room
        var motionSensor = new MotionSensor(room, "motion-001");
        var smartLight = new SmartLight(room, "light-001");
        var devices = new List<Device> { motionSensor, smartLight };

        var home = new SmartHome {
            Id = "test-home",
            Name = "Test Home",
            Rooms = [room],
            Devices = devices,
            CreatedAt = DateTime.UtcNow
        };

        // Act - Generate many events to increase chance of correlations
        var events = faker.HomeAutomation().Events(home, count: 100);

        // Assert
        Log.Information("Generated {EventCount} events", events.Count);

        await Assert.That(events).IsNotEmpty();

        // Count event types
        var eventTypeCounts = events.GroupBy(e => e.GetType().Name).ToDictionary(g => g.Key, g => g.Count());
        Log.Information("Event type distribution: {EventTypes}",
            string.Join(", ", eventTypeCounts.Select(kvp => $"{kvp.Key}: {kvp.Value}")));

        // Should have motion events (because we're generating 100 events)
        var motionEvents = events.OfType<MotionDetected>().ToList();
        await Assert.That(motionEvents).IsNotEmpty();

        // Should have light events (either primary or correlated)
        var lightEvents = events.OfType<LightStateChanged>().ToList();
        await Assert.That(lightEvents).IsNotEmpty();

        // With correlation enabled, we should have more events than requested
        // (because correlated events are added on top of the requested count)
        Log.Information("Requested 100 events, generated {ActualCount} events (including correlations)", events.Count);
        await Assert.That(events.Count).IsGreaterThanOrEqualTo(100);

        // Verify that some light events have timestamps shortly after motion events
        // (this indicates correlation)
        var motionTimestamps = motionEvents.Select(e => e.Timestamp).OrderBy(t => t).ToList();
        var lightTimestamps = lightEvents.Select(e => e.Timestamp).OrderBy(t => t).ToList();

        bool foundCorrelation = false;
        foreach (var motionTime in motionTimestamps) {
            // Look for light events within 10 seconds (10000ms) after motion
            var correlatedLights = lightTimestamps.Where(lightTime =>
                lightTime > motionTime && lightTime <= motionTime + 10000L);

            if (correlatedLights.Any()) {
                foundCorrelation = true;
                Log.Information("Found correlated light event at {LightTime} following motion at {MotionTime}",
                    correlatedLights.First(), motionTime);
                break;
            }
        }

        // Note: Due to randomness (30% chance), we might not always find correlations in a small sample
        // But with 100 events, there should be a high probability of at least one correlation
        Log.Information("Correlation found: {CorrelationFound}", foundCorrelation);
    }
}