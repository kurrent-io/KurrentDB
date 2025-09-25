using Bogus;

namespace KurrentDB.Testing.Sample.HomeAutomation;

public class RoomFaker : Faker<Room> {
    public RoomFaker() {
        RuleFor(r => r.RoomId, f => Guid.NewGuid())
            .RuleFor(r => r.Name, f => f.PickRandom(GetRoomNames()))
            .RuleFor(r => r.Type, (f, r) => GetRoomType(r.Name));
    }

    static string[] GetRoomNames() => [
        "Living Room", "Kitchen", "Master Bedroom", "Office", "Guest Bedroom",
        "Dining Room", "Basement", "Garage", "Main Bathroom", "Hallway"
    ];

    static RoomType GetRoomType(string roomName) =>
        roomName switch {
            "Living Room"                       => RoomType.LivingRoom,
            "Kitchen"                           => RoomType.Kitchen,
            "Master Bedroom" or "Guest Bedroom" => RoomType.Bedroom,
            "Main Bathroom"                     => RoomType.Bathroom,
            "Office"                            => RoomType.Office,
            "Garage"                            => RoomType.Garage,
            "Hallway"                           => RoomType.Hallway,
            "Dining Room"                       => RoomType.DiningRoom,
            "Basement"                          => RoomType.Basement,
            _                                   => RoomType.Undefined
        };

    static string GetRoomName(RoomType room) =>
        room switch {
            RoomType.LivingRoom => "Living Room",
            RoomType.Kitchen    => "Kitchen",
            RoomType.Bedroom    => "Bedroom",
            RoomType.Bathroom   => "Bathroom",
            RoomType.Office     => "Office",
            RoomType.Garage     => "Garage",
            RoomType.Hallway    => "Hallway",
            RoomType.DiningRoom => "Dining Room",
            RoomType.Basement   => "Basement",
            _                   => "Undefined"
        };
}

public class HomeFaker : Faker<Home> {
    public HomeFaker() {
        RuleFor(h => h.Id, f => f.Random.AlphaNumeric(6).ToLower())
            .RuleFor(
                h => h.Name, f => f.Random.ArrayElement(
                    [
                        $"{f.Name.LastName()} Family Home",
                        $"{f.Name.LastName()} Residence",
                        $"{f.Address.StreetName()} House",
                        $"{f.Address.StreetName()} Villa",
                        $"{f.Address.City()} Apartment {f.Random.Number(1, 50)}",
                        $"{f.Company.CompanyName()} Executive Home"
                    ]
                )
            )
            .RuleFor(h => h.CreatedAt, f => f.Date.Between(DateTime.UtcNow.AddYears(-10), DateTime.UtcNow.AddMonths(-1)))
            .RuleFor(
                h => h.Rooms, f => {
                    var baseRooms = new List<Room> {
                        new(Guid.NewGuid(), "Living Room", RoomType.LivingRoom),
                        new(Guid.NewGuid(), "Kitchen", RoomType.Kitchen),
                        new(Guid.NewGuid(), "Master Bedroom", RoomType.Bedroom)
                    };

                    var optionalRooms = new[] {
                        ("Office", RoomType.Office),
                        ("Guest Bedroom", RoomType.Bedroom),
                        ("Dining Room", RoomType.DiningRoom),
                        ("Basement", RoomType.Basement),
                        ("Garage", RoomType.Garage),
                        ("Main Bathroom", RoomType.Bathroom),
                        ("Hallway", RoomType.Hallway)
                    };

                    var additionalCount = f.Random.Number(2, 5);
                    var selectedRooms   = f.Random.ListItems(optionalRooms, additionalCount);

                    foreach (var (name, type) in selectedRooms)
                        baseRooms.Add(new Room(Guid.NewGuid(), name, type));

                    return baseRooms;
                }
            );
    }
}

public static class DeviceFactory {
    public static List<Device> CreateDevicesForHome(Home home, int count) {
        var devices             = new List<Device>();
        var usedRoomDevicePairs = new HashSet<string>();

        for (var i = 0; i < count; i++) {
            var room                = SimulatorContext.Faker.PickRandom(home.Rooms);
            var suitableDeviceTypes = GetSuitableDeviceTypes(room.Type);
            var deviceType          = SimulatorContext.Faker.PickRandom(suitableDeviceTypes);
            var deviceId            = GenerateDeviceUrn(home.Id, deviceType);

            Device device = deviceType switch {
                DeviceType.Thermostat    => new Thermostat(room, deviceId),
                DeviceType.MotionSensor  => new MotionSensor(room, deviceId),
                DeviceType.SmartLight    => new SmartLight(room, deviceId),
                DeviceType.DoorLock      => new DoorLock(room, deviceId),
                DeviceType.SmokeDetector => new SmokeDetector(room, deviceId),
                _                        => new Thermostat(room, deviceId)
            };

            var roomDeviceKey = $"{room.RoomId}_{device.DeviceType}";
            if (!usedRoomDevicePairs.Contains(roomDeviceKey) || usedRoomDevicePairs.Count < count / 2) {
                devices.Add(device);
                usedRoomDevicePairs.Add(roomDeviceKey);
            }
            else {
                i--;
            }
        }

        return devices;
    }

    static string GenerateDeviceUrn(string homeId, DeviceType deviceType) {
        var deviceTypeName = deviceType.ToString().ToLower();
        var uniqueId       = SimulatorContext.Faker.Internet.Mac().Replace(":", "").ToLower();
        return $"urn:iot:{homeId}:{deviceTypeName}:{uniqueId}";
    }

    static DeviceType[] GetSuitableDeviceTypes(RoomType roomType) =>
        roomType switch {
            RoomType.Kitchen    => [DeviceType.Thermostat, DeviceType.MotionSensor, DeviceType.SmartLight, DeviceType.SmokeDetector],
            RoomType.LivingRoom => [DeviceType.Thermostat, DeviceType.MotionSensor, DeviceType.SmartLight],
            RoomType.Bedroom    => [DeviceType.Thermostat, DeviceType.MotionSensor, DeviceType.SmartLight],
            RoomType.Bathroom   => [DeviceType.Thermostat, DeviceType.MotionSensor, DeviceType.SmartLight],
            RoomType.Office     => [DeviceType.Thermostat, DeviceType.MotionSensor, DeviceType.SmartLight],
            RoomType.Garage     => [DeviceType.MotionSensor, DeviceType.DoorLock, DeviceType.SmokeDetector],
            RoomType.Entrance   => [DeviceType.DoorLock, DeviceType.MotionSensor],
            RoomType.Hallway    => [DeviceType.MotionSensor, DeviceType.SmartLight],
            _                   => [DeviceType.MotionSensor, DeviceType.SmartLight]
        };
}

public class TemperatureReadingFaker : Faker<TemperatureReading> {
    public TemperatureReadingFaker(Thermostat device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.Temperature, f => {
                    var change = f.Random.Double(-0.5, 0.5);
                    device.UpdateTemperature(device.CurrentTemperature + change);

                    if (f.Random.Bool(0.1f)) device.DepleteBattery();

                    return Math.Round(device.CurrentTemperature, 1);
                }
            )
            .RuleFor(e => e.Unit, TemperatureUnit.Celsius)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}

public class MotionDetectedFaker : Faker<MotionDetected> {
    public MotionDetectedFaker(MotionSensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(
                e => e.Confidence, f => {
                    var timeSinceLastMotion    = timestamp - device.LastMotionTime;
                    var minutesSinceLastMotion = timeSinceLastMotion / 60000.0;

                    device.RecordMotion(timestamp);

                    return Math.Round(
                        minutesSinceLastMotion switch {
                            < 5  => f.Random.Double(40, 70),
                            > 60 => f.Random.Double(80, 100),
                            _    => f.Random.Double(60, 90)
                        }, 1
                    );
                }
            )
            .RuleFor(e => e.ZoneId, f => f.Random.Number(1, 5))
            .RuleFor(e => e.Timestamp, timestamp);
    }
}

public class LightStateChangedFaker : Faker<LightStateChanged> {
    public LightStateChangedFaker(SmartLight device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(
                e => e.IsOn, f => {
                    if (f.Random.Bool(0.15f)) device.ToggleLight();
                    if (device.IsOn && f.Random.Bool(0.1f))
                        device.SetBrightness(device.Brightness + f.Random.Number(-20, 20));

                    return device.IsOn;
                }
            )
            .RuleFor(e => e.Brightness, f => device.Brightness)
            .RuleFor(e => e.Color, f => f.Internet.Color())
            .RuleFor(e => e.PowerConsumption, f => device.IsOn ? Math.Round(device.Brightness * 0.8 + f.Random.Double(0, 20), 2) : 0.0)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}

public class DoorStateChangedFaker : Faker<DoorStateChanged> {
    public DoorStateChangedFaker(DoorLock device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo())
            .RuleFor(
                e => e.IsOpen, f => {
                    if (f.Random.Bool(0.05f)) device.ToggleDoor();
                    if (!device.IsOpen && f.Random.Bool(0.2f))
                        device.SetLockStatus(f.Random.Bool(0.8f) ? LockStatus.Locked : LockStatus.Unlocked);

                    return device.IsOpen;
                }
            )
            .RuleFor(e => e.LockStatus, f => device.LockStatus)
            .RuleFor(e => e.UserId, f => f.Internet.UserName())
            .RuleFor(e => e.Timestamp, timestamp);
    }
}

public class SmokeDetectedFaker : Faker<SmokeDetected> {
    public SmokeDetectedFaker(SmokeDetector device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.HasSmoke, f => {
                    if (f.Random.Bool(0.05f)) device.DepleteBattery();
                    return f.Random.Bool(0.005f);
                }
            )
            .RuleFor(e => e.SmokeLevel, f => f.Random.Number(0, 20))
            .RuleFor(e => e.AlarmStatus, f => f.Random.Bool(0.005f) ? AlarmStatus.Active : AlarmStatus.Normal)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
