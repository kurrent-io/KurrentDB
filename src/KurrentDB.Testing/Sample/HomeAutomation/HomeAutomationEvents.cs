namespace KurrentDB.Testing.Sample.HomeAutomation;

public record DeviceInfo {
    public required string     Id               { get; init; }
    public required string     Name             { get; init; }
    public required Guid       RoomId           { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }
    public required int?       BatteryLevel     { get; init; }
}

public record TemperatureReading {
    public          Guid            EventId     { get; init; }
    public required DeviceInfo      Device      { get; init; }
    public required double          Temperature { get; init; }
    public required TemperatureUnit Unit        { get; init; }
    public required long            Timestamp   { get; init; }
}

public record MotionDetected {
    public required Guid       EventId    { get; init; }
    public required DeviceInfo Device     { get; init; }
    public required double     Confidence { get; init; }
    public required int        ZoneId     { get; init; }
    public required long       Timestamp  { get; init; }
}

public record DoorStateChanged {
    public required Guid       EventId    { get; init; }
    public required DeviceInfo Device     { get; init; }
    public required bool       IsOpen     { get; init; }
    public required LockStatus LockStatus { get; init; }
    public required string     UserId     { get; init; }
    public required long       Timestamp  { get; init; }
}

public record WindowStateChanged {
    public required Guid             EventId          { get; init; }
    public required DeviceInfo       Device           { get; init; }
    public required bool             IsOpen           { get; init; }
    public required int              OpenPercentage   { get; init; }
    public required WeatherCondition WeatherCondition { get; init; }
    public required long             Timestamp        { get; init; }
}

public record SmokeDetected {
    public required Guid        EventId     { get; init; }
    public required DeviceInfo  Device      { get; init; }
    public required bool        HasSmoke    { get; init; }
    public required int         SmokeLevel  { get; init; }
    public required AlarmStatus AlarmStatus { get; init; }
    public required long        Timestamp   { get; init; }
}

public record WaterLeakDetected {
    public required Guid       EventId       { get; init; }
    public required DeviceInfo Device        { get; init; }
    public required bool       HasWater      { get; init; }
    public required int        MoistureLevel { get; init; }
    public required bool       AlertSent     { get; init; }
    public required long       Timestamp     { get; init; }
}

public record LightStateChanged {
    public required Guid       EventId          { get; init; }
    public required DeviceInfo Device           { get; init; }
    public required bool       IsOn             { get; init; }
    public required int        Brightness       { get; init; }
    public required string     Color            { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}

public record HumidityReading {
    public required Guid         EventId      { get; init; }
    public required DeviceInfo   Device       { get; init; }
    public required double       Humidity     { get; init; }
    public required ComfortLevel ComfortLevel { get; init; }
    public required long         Timestamp    { get; init; }
}
