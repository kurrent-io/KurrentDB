// See https://aka.ms/new-console-template for more information

using System.Text;
using System.Text.Json;
using EventStore.Connect.Leases;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using EventStore.Streaming.Schema.Serializers.Bytes;
using EventStore.Streaming.Schema.Serializers.Json;
using EventStore.Streaming.Schema.Serializers.Protobuf;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using static NodaTime.Serialization.SystemTextJson.NodaConverters;
using Duration = NodaTime.Duration;

Console.WriteLine("Hello, World!");

ISchemaSerializer serializer = new SchemaRegistry(new InMemorySchemaRegistryClient())
    .UseBytes()
    .UseProtobuf()
    .UseJson(new SystemJsonSerializerOptions(new() { Converters = { InstantConverter, DurationConverter } }));

var settings = new Dictionary<string, string> {
    ["key1"] = "value1",
    ["key2"] = "value2"
};

var protoActivating = new ConnectorActivating {
    ConnectorId = "ConnectorId",
    Revision    = 1,
    Timestamp   = DateTimeOffset.UtcNow.ToTimestamp(),
    Settings    = { settings }
};


var temp1 = JsonSerializer.Serialize(protoActivating, SystemJsonSerializerOptions.DefaultJsonSerializerOptions);

var activatingSchema = SchemaInfo.FromMessageType<ConnectorActivating>(SchemaDefinitionType.Json);

var protoActivatingJsonBytes = await serializer.Serialize(protoActivating, activatingSchema);

var esdbJson = Encoding.UTF8.GetString(protoActivatingJsonBytes.ToArray());

var backToActivatingSchema = await serializer.Deserialize(protoActivatingJsonBytes, activatingSchema);

var protoActivatingJsonFromFormatter = JsonFormatter.Default.Format(protoActivating);

var protoActivatingJsonFromFormatterBytes = Encoding.UTF8.GetBytes(protoActivatingJsonFromFormatter);

var backToActivatingSchemaFromFormatter = await serializer.Deserialize(protoActivatingJsonBytes, activatingSchema);

ConnectorActivating message = null!;
try {
    message = ConnectorActivating.Parser.ParseFrom(protoActivatingJsonFromFormatterBytes);
}
catch (Exception e) {
    var temp = Encoding.UTF8.GetString(protoActivatingJsonFromFormatterBytes);
    message = ConnectorActivating.Parser.ParseJson(temp);
}

// var backToActivatingSchemaFromFormatterPROTO = await serializer.Deserialize(protoActivatingJsonBytes, SchemaInfo.FromMessageType<ConnectorActivating>(SchemaDefinitionType.Protobuf));


// we can only use proto to json and back if the original format is stored.
// so a timestamp will be nanos plus something else instead of a string

// if we encode using the protobuf json formatter, we can decode using the json serializer

var jsonActivatingBytes = await serializer.Serialize(
    protoActivating,
    activatingSchema
);

var jsonActivatingText = JsonSerializer.Serialize(protoActivating);

var jsonActivatingTextBytes = Encoding.UTF8.GetBytes(jsonActivatingText);

var omg =  JsonSerializer.Deserialize<ConnectorActivating>(jsonActivatingTextBytes);

var jf = new JsonFormatter(JsonFormatter.Settings.Default);

var jsonActivatingTextFromFormatter = jf.Format(protoActivating);

var jsonActivatingTextFromFormatterBytes = Encoding.UTF8.GetBytes(jsonActivatingTextFromFormatter);

var omg_for_real = JsonSerializer.Deserialize<ConnectorActivating>(jsonActivatingTextFromFormatterBytes);

var whatAboutThis = ConnectorActivating.Parser.ParseJson(Encoding.UTF8.GetString(jsonActivatingTextFromFormatterBytes));

ISchemaSerializer kebas = new ProtobufJsonSerializer();

var kebasBytes = await kebas.Serialize(protoActivating, activatingSchema);

var kebasText = Encoding.UTF8.GetString(kebasBytes.Span);

var kebasProto = await kebas.Deserialize(kebasBytes, activatingSchema);

// // so protojson is a thing? ffs...
//
// var protoActivatingAgain = await serializer.Deserialize(
//     jsonActivatingBytes,
//     activatingSchema
// );
//
// var jsonActivating = JsonSerializer.Deserialize<ConnectorActivating>(jsonActivatingBytes.Span);

// var lease = new VersionedLease {
//     ResourceId = "ResourceId",
//     AcquiredBy = "AcquiredBy",
//     AcquiredAt = NodaTime.SystemClock.Instance.GetCurrentInstant(),
//     Duration   = Duration.FromMinutes(5),
//     Version    = 0
// };
//
// var leaseSchema = SchemaInfo.FromMessageType<VersionedLease>(SchemaDefinitionType.Json);
//
// var jsonLeaseBytes = await serializer.Serialize(
//     protoActivating,
//     leaseSchema
// );
//
// var jsonLease = await serializer.Deserialize(
//     jsonActivatingBytes,
//     leaseSchema
// );

Console.WriteLine("wow");