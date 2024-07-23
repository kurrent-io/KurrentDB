using GrpcService1.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddEndpointsApiExplorer();

builder.Services
    .AddGrpc(x => x.EnableDetailedErrors = true)
    .AddJsonTranscoding();

builder.Services
    .AddGrpcSwagger()
    .AddSwaggerGen(x => {
            x.SwaggerDoc(
                "v1",
                new OpenApiInfo {
                    Version     = "v1",
                    Title       = "Greeter API"
                }
            );

            // var filePath = Path.Combine(AppContext.BaseDirectory, "Server.xml");
            // x.IncludeXmlComments(filePath);
            // x.IncludeGrpcXmlComments(filePath, includeControllerXmlComments: true);
        }
    );

builder.Services.AddGrpc();

var app = builder.Build();

app
    .UseSwagger()
    .UseSwaggerUI(x => x.SwaggerEndpoint("/swagger/v1/swagger.json", "Greeter API v1"));

// app.UseHttpsRedirection();

app.MapGrpcService<GreeterService>();
app.MapGet("/", () =>
    "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909"
);

app.Run();