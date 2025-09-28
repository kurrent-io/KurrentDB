using Bogus;

namespace KurrentDB.Testing.Sample.HomeAutomation;

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

public class HumidityReadingFaker : Faker<HumidityReading> {
    public HumidityReadingFaker(HumiditySensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.Humidity, f => {
                    var change = f.Random.Double(-5.0, 5.0);
                    device.UpdateHumidity(device.HumidityLevel + change);

                    if (f.Random.Bool(0.1f)) device.DepleteBattery();

                    return Math.Round(device.HumidityLevel, 1);
                }
            )
            .RuleFor(e => e.ComfortLevel, f => {
                var humidity = device.HumidityLevel;
                return humidity switch {
                    < 30 or > 70 => ComfortLevel.Low,
                    >= 40 and <= 60 => ComfortLevel.Optimal,
                    _ => ComfortLevel.High
                };
            })
            .RuleFor(e => e.Timestamp, timestamp);
    }
}

public class WindowStateChangedFaker : Faker<WindowStateChanged> {
    public WindowStateChangedFaker(WindowSensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.IsOpen, f => {
                    if (f.Random.Bool(0.1f)) device.ToggleWindow();
                    if (f.Random.Bool(0.05f)) device.DepleteBattery();

                    return device.IsOpen;
                }
            )
            .RuleFor(e => e.OpenPercentage, f => device.IsOpen ? f.Random.Number(10, 100) : 0)
            .RuleFor(e => e.WeatherCondition, f => f.Random.Enum<WeatherCondition>())
            .RuleFor(e => e.Timestamp, timestamp);
    }
}

public class WaterLeakDetectedFaker : Faker<WaterLeakDetected> {
    public WaterLeakDetectedFaker(WaterSensor device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(device.BatteryLevel))
            .RuleFor(
                e => e.HasWater, f => {
                    // Water detection is rare but serious
                    var hasWater = f.Random.Bool(0.002f); // 0.2% chance
                    if (hasWater) {
                        var waterLevel = f.Random.Number(1, 100);
                        device.UpdateWaterLevel(waterLevel);
                    } else {
                        device.DetectWater(false);
                    }

                    if (f.Random.Bool(0.1f)) device.DepleteBattery();

                    return device.WaterDetected;
                }
            )
            .RuleFor(e => e.MoistureLevel, f => device.WaterLevel)
            .RuleFor(e => e.AlertSent, f => device.WaterDetected && f.Random.Bool(0.9f)) // Alert sent 90% of time when water detected
            .RuleFor(e => e.Timestamp, timestamp);
    }
}

public class RoomFaker : Faker<Room> {
    public RoomFaker() {
        CustomInstantiator(f => {
                var roomType = f.PickRandom<RoomType>();
                var roomName = GenerateRoomName(f, roomType);
                return new Room(Guid.NewGuid(), roomName, roomType);
            }
        );
    }

    static string GenerateRoomName(Faker faker, RoomType roomType) =>
        roomType switch {
            RoomType.LivingRoom => faker.PickRandom(
                "Living Room", "Family Room", "Great Room",
                "Lounge", "Front Room"
            ),
            RoomType.Kitchen => faker.PickRandom(
                "Kitchen", "Main Kitchen", "Galley Kitchen",
                "Chef's Kitchen"
            ),
            RoomType.Bedroom => faker.PickRandom(
                "Master Bedroom", "Guest Bedroom", "Kids' Room",
                "Bedroom", "Second Bedroom", "Third Bedroom",
                "Children's Room"
            ),
            RoomType.Bathroom => faker.PickRandom(
                "Main Bathroom", "Master Bathroom", "Guest Bathroom",
                "Half Bath", "Powder Room", "Full Bath", "Ensuite"
            ),
            RoomType.Office => faker.PickRandom(
                "Office", "Study", "Home Office",
                "Den", "Library", "Work Room"
            ),
            RoomType.Garage => faker.PickRandom(
                "Garage", "Two-Car Garage", "Single Garage",
                "Carport", "Workshop"
            ),
            RoomType.Hallway => faker.PickRandom(
                "Hallway", "Main Hall", "Corridor",
                "Foyer", "Entry Hall"
            ),
            RoomType.DiningRoom => faker.PickRandom(
                "Dining Room", "Formal Dining", "Breakfast Nook",
                "Eating Area"
            ),
            RoomType.Basement => faker.PickRandom(
                "Basement", "Lower Level", "Finished Basement",
                "Rec Room", "Wine Cellar"
            ),
            RoomType.Entrance => faker.PickRandom(
                "Entrance", "Front Entry", "Foyer",
                "Vestibule", "Mudroom"
            ),
            RoomType.Utility => faker.PickRandom(
                "Utility Room", "Laundry Room", "Storage Room",
                "Pantry", "Closet"
            ),
            _ => "Room"
        };
}

public class DeviceFaker : Faker<Device> {
    public DeviceFaker(Room room, DeviceType? deviceType = null, string? homeId = null) {
        CustomInstantiator(f => {
                var actualDeviceType = deviceType ?? SelectWeightedDeviceType(f, room.Type);
                var deviceId         = GenerateDeviceUrn(homeId ?? f.Random.AlphaNumeric(6).ToLower(), actualDeviceType, f);

                return actualDeviceType switch {
                    DeviceType.Thermostat     => new Thermostat(room, deviceId),
                    DeviceType.MotionSensor   => new MotionSensor(room, deviceId),
                    DeviceType.SmartLight     => new SmartLight(room, deviceId),
                    DeviceType.DoorLock       => new DoorLock(room, deviceId),
                    DeviceType.SmokeDetector  => new SmokeDetector(room, deviceId),
                    DeviceType.HumiditySensor => new HumiditySensor(room, deviceId),
                    DeviceType.WindowSensor   => new WindowSensor(room, deviceId),
                    DeviceType.WaterSensor    => new WaterSensor(room, deviceId),
                    _                         => new Thermostat(room, deviceId)
                };
            }
        );
    }

    static string GenerateDeviceUrn(string homeId, DeviceType deviceType, Faker faker) {
        var deviceTypeName = deviceType.ToString().ToLower();
        var uniqueId       = faker.Internet.Mac().Replace(":", "").ToLower();
        return $"urn:iot:{homeId}:{deviceTypeName}:{uniqueId}";
    }

    static DeviceType SelectWeightedDeviceType(Faker faker, RoomType roomType) {
        var weightedDevices = GetSuitableDeviceTypes(roomType);

        // Calculate total weight
        var totalWeight = weightedDevices.Sum(wd => wd.Weight);
        var randomValue = faker.Random.Float(0f, totalWeight);

        // Select device based on weight
        var runningWeight = 0f;
        foreach (var weightedDevice in weightedDevices) {
            runningWeight += weightedDevice.Weight;
            if (randomValue <= runningWeight) return weightedDevice.DeviceType;
        }

        // Fallback (should not happen)
        return weightedDevices.First().DeviceType;
    }

    static WeightedDevice[] GetSuitableDeviceTypes(RoomType roomType) =>
        roomType switch {
            RoomType.Kitchen => [
                new(DeviceType.MotionSensor, 0.8f),  // Very common
                new(DeviceType.SmartLight, 0.7f),    // Very common
                new(DeviceType.SmokeDetector, 0.6f), // Important for safety
                new(DeviceType.WaterSensor, 0.4f),   // Leak detection
                new(DeviceType.Thermostat, 0.3f)     // Less common in kitchen
            ],
            RoomType.LivingRoom => [
                new(DeviceType.MotionSensor, 0.9f), // Most common
                new(DeviceType.SmartLight, 0.8f),   // Very common
                new(DeviceType.Thermostat, 0.5f),   // Climate control
                new(DeviceType.WindowSensor, 0.3f)  // Security/automation
            ],
            RoomType.Bedroom => [
                new(DeviceType.SmartLight, 0.7f),   // Common
                new(DeviceType.Thermostat, 0.6f),   // Climate comfort
                new(DeviceType.WindowSensor, 0.4f), // Security/automation
                new(DeviceType.MotionSensor, 0.3f)  // Less intrusive
            ],
            RoomType.Bathroom => [
                new(DeviceType.MotionSensor, 0.8f),   // Automation
                new(DeviceType.SmartLight, 0.7f),     // Common
                new(DeviceType.HumiditySensor, 0.6f), // Moisture monitoring
                new(DeviceType.WaterSensor, 0.5f),    // Leak detection
                new(DeviceType.Thermostat, 0.2f)      // Less common
            ],
            RoomType.Office => [
                new(DeviceType.MotionSensor, 0.8f), // Occupancy
                new(DeviceType.SmartLight, 0.7f),   // Task lighting
                new(DeviceType.Thermostat, 0.5f),   // Comfort
                new(DeviceType.WindowSensor, 0.3f)  // Security
            ],
            RoomType.Garage => [
                new(DeviceType.MotionSensor, 0.9f),  // Security/automation
                new(DeviceType.DoorLock, 0.7f),      // Security
                new(DeviceType.SmokeDetector, 0.4f), // Fire safety
                new(DeviceType.WaterSensor, 0.3f)    // Flood detection
            ],
            RoomType.Entrance => [
                new(DeviceType.DoorLock, 0.9f),     // Security priority
                new(DeviceType.MotionSensor, 0.8f), // Security/automation
                new(DeviceType.SmartLight, 0.4f)    // Welcome lighting
            ],
            RoomType.Hallway => [
                new(DeviceType.MotionSensor, 0.9f), // Automation
                new(DeviceType.SmartLight, 0.8f)    // Path lighting
            ],
            RoomType.DiningRoom => [
                new(DeviceType.SmartLight, 0.8f),   // Ambiance
                new(DeviceType.MotionSensor, 0.6f), // Automation
                new(DeviceType.Thermostat, 0.4f)    // Comfort
            ],
            RoomType.Basement => [
                new(DeviceType.MotionSensor, 0.7f),   // Security/automation
                new(DeviceType.HumiditySensor, 0.6f), // Moisture monitoring
                new(DeviceType.WaterSensor, 0.5f),    // Flood detection
                new(DeviceType.SmokeDetector, 0.4f),  // Fire safety
                new(DeviceType.SmartLight, 0.3f)      // Basic lighting
            ],
            RoomType.Utility => [
                new(DeviceType.WaterSensor, 0.8f),    // Leak detection priority
                new(DeviceType.HumiditySensor, 0.6f), // Environmental monitoring
                new(DeviceType.MotionSensor, 0.5f),   // Basic automation
                new(DeviceType.SmartLight, 0.4f)      // Basic lighting
            ],
            _ => [
                new(DeviceType.MotionSensor, 0.5f), // Generic fallback
                new(DeviceType.SmartLight, 0.4f)    // Generic fallback
            ]
        };

    record WeightedDevice(DeviceType DeviceType, float Weight);
}

public class HomeDeviceCollectionFaker : Faker<List<Device>> {
    public HomeDeviceCollectionFaker(SmartHome home, int deviceCount) {
        CustomInstantiator(f => {
                var devices             = new List<Device>();
                var usedRoomDevicePairs = new HashSet<string>();
                var attempts            = 0;
                var maxAttempts         = deviceCount * 3; // Prevent infinite loops

                while (devices.Count < deviceCount && attempts < maxAttempts) {
                    attempts++;
                    var room        = f.PickRandom(home.Rooms);
                    var deviceFaker = new DeviceFaker(room, homeId: home.Id);
                    var device      = deviceFaker.Generate();

                    var roomDeviceKey = $"{room.RoomId}_{device.DeviceType}";

                    // Allow some duplicates once we have enough variety
                    var allowDuplicate = usedRoomDevicePairs.Count >= deviceCount / 2;

                    if (!usedRoomDevicePairs.Contains(roomDeviceKey) || allowDuplicate) {
                        devices.Add(device);
                        usedRoomDevicePairs.Add(roomDeviceKey);
                    }
                }

                return devices;
            }
        );
    }
}

public class HomeFaker : Faker<SmartHome> {
    public HomeFaker() {
        CustomInstantiator(f => {
                // Generate home properties
                var homeId = f.Random.AlphaNumeric(6).ToLower();
                var homeName = f.Random.ArrayElement(
                    [
                        $"{f.Name.LastName()} Family Home",
                        $"{f.Name.LastName()} Residence",
                        $"{f.Address.StreetName()} House",
                        $"{f.Address.StreetName()} Villa",
                        $"{f.Address.City()} Apartment {f.Random.Number(1, 50)}",
                        $"{f.Company.CompanyName()} Executive Home"
                    ]
                );

                var createdAt = f.Date.Between(DateTime.UtcNow.AddYears(-10), DateTime.UtcNow.AddMonths(-1));

                // Generate rooms
                var roomFaker = new RoomFaker();
                var baseRooms = new List<Room> {
                    new(Guid.NewGuid(), "Living Room", RoomType.LivingRoom),
                    new(Guid.NewGuid(), "Kitchen", RoomType.Kitchen),
                    new(Guid.NewGuid(), "Master Bedroom", RoomType.Bedroom)
                };

                var additionalRooms = roomFaker.Generate(f.Random.Number(2, 5));
                baseRooms.AddRange(additionalRooms);

                // Create temporary home to pass to device faker
                var tempHome = new SmartHome {
                    Id        = homeId,
                    Name      = homeName,
                    Rooms     = baseRooms,
                    Devices   = [], // Empty for now
                    CreatedAt = createdAt
                };

                // Generate devices for this home
                var deviceCollectionFaker = new HomeDeviceCollectionFaker(tempHome, 12);
                var devices               = deviceCollectionFaker.Generate();

                // Return complete home with devices
                return new SmartHome {
                    Id        = homeId,
                    Name      = homeName,
                    Rooms     = baseRooms,
                    Devices   = devices,
                    CreatedAt = createdAt
                };
            }
        );
    }
}
