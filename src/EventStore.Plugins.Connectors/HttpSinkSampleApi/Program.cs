using System.Text.Json;
using EventStore.Streaming.Connectors.Sinks;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.UseUrls("http://localhost:8080");

var app = builder.Build();

var jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };

app.MapPost("/sink",
        async (HttpRequest req) => {
            var receivedRecord = JsonSerializer.Serialize(await JsonSerializer.DeserializeAsync<object>(req.Body),
                jsonSerializerOptions);

            Console.WriteLine($"Received event {req.Headers[SinkHeaderKeys.SchemaSubject]}\n{receivedRecord}\n");

            return Results.Ok();
        })
    .WithName("Sink");

app.Run();