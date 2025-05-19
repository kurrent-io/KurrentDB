using System;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Kurrent.Surge;
using Serilog;
using static System.Environment;
using static System.String;
using static System.StringComparison;

namespace KurrentDB.Surge.Testing.Containers.FluentDocker;

public abstract class TestContainer {
    protected static readonly ILogger Logger = Log.ForContext<TestContainer>();

    static TestContainer() => Ductus.FluentDocker.Services.Logging.Enabled();

    protected TestContainer(string defaultImage, string serviceName = null) {
        Ensure.NotNullOrWhiteSpace(defaultImage);

        ServiceName = GetServiceName();
        Image       = GetImage(defaultImage, ServiceName);

        return;

        string GetServiceName() {
            return serviceName ?? GetType().Name
                .Replace("TestContainer", Empty, OrdinalIgnoreCase)
                .ToLowerInvariant();
        }

        static string GetImage(string defaultImage, string serviceName) {
            var serviceTag   = serviceName.Replace("-", "").Replace("_", "").Trim().ToUpperInvariant();
            var variableName = $"TESTCONTAINER_{serviceTag}_IMAGE";
            var imageEnvVar  = GetEnvironmentVariable(variableName);
            return !IsNullOrWhiteSpace(imageEnvVar) ? imageEnvVar : defaultImage;
        }
    }

    protected string ServiceName { get; }
    protected string Image       { get; }

    public IContainerService Service { get; private set; } = null!;

    public Container Configuration => Service.GetConfiguration(true);

    protected abstract ContainerBuilder ConfigureContainer(ContainerBuilder builder);

    public async ValueTask Start() {
        try {
            Service = ConfigureContainer(
                new Builder()
                    .UseContainer()
                    .WithName(ServiceName)
                    .UseImage(Image)
                    .WithPublicEndpointResolver()
            ).Build();

            // foreach (var serviceState in Enum.GetValues<ServiceRunningState>()) {
            //     Service.AddHook(
            //         state: serviceState,
            //         hook: service => Logger.Verbose("{ServiceName} State:{ServiceState}", service.Name, serviceState),
            //         uniqueName: $"{Service.Name}-{serviceState}".ToLowerInvariant()
            //     );
            // }

            Service.Start();

            Logger.Information(
                "{ServiceName} Container started: {@ExposedPorts}",
                ServiceName, Service.GetConfiguration().Config.ExposedPorts.Keys
            );
        }
        catch (Exception ex) {
            Logger.Error(ex, "{ServiceName} Failed to start container", ServiceName);
            throw new FluentDockerException($"{ServiceName} Failed to start container", ex);
        }

        try {
            await OnStarted();
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{ServiceName} {nameof(OnStarted)} Execution error", ex);
        }
    }

    public async ValueTask Stop() {
        try {
            Service.Stop();
            Logger.Information($"{ServiceName} Container stopped");
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{ServiceName} Failed to stop container", ex);
        }

        try {
            await OnStopped();
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{ServiceName} {nameof(OnStopped)} Execution error", ex);
        }
    }

    public void ReportStatus() {
        var cfg = Service.GetConfiguration(true);

        Logger.Debug(
            "Container {ContainerName} Created On: {CreatedOn} Exposed Ports: {@ExposedPorts}",
            cfg.Name, cfg.Created, cfg.Config.ExposedPorts.Keys
        );
    }

    public virtual ValueTask DisposeAsync() {
        try {
            Service.Dispose();
        }
        catch (Exception ex) {
            throw new FluentDockerException("Failed to gracefully dispose of container service", ex);
        }

        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask OnStarted() => ValueTask.CompletedTask;
    protected virtual ValueTask OnStopped() => ValueTask.CompletedTask;
}
