using GrpcServiceAOT.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateSlimBuilder(args);

// Add services to the container.
builder.Services.AddGrpc().AddJsonTranscoding();
builder.Services
    .AddGrpcHealthChecks()
    .AddCheck("liveness", () => HealthCheckResult.Healthy());

// builder.Host.ConfigureWebHost(x => x.UseKestrelHttpsConfiguration());

var app = builder.Build();
// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();
app.MapGrpcHealthChecksService();

app.Run();