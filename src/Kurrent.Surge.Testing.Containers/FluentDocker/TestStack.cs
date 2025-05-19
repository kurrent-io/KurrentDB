using System;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;
using Serilog;

namespace KurrentDB.Surge.Testing.Containers.FluentDocker;

public abstract class TestStack {
    static readonly ILogger Logger = Log.ForContext<TestStack>();

    static TestStack() => Ductus.FluentDocker.Services.Logging.Enabled();

    protected TestStack(string serviceName = null) =>
        ServiceName = serviceName ?? GetType().Name.Replace("TestStack", string.Empty).ToLowerInvariant();

    string ServiceName { get; }

    protected ICompositeService Service { get; set; } = default!;

    protected abstract ICompositeService CreateService();

    public async ValueTask Start() {
        try {
            Service = CreateService();

            // foreach (var serviceState in Enum.GetValues<ServiceRunningState>()) {
            //     Service.AddHook(
            //         state: serviceState,
            //         hook: service => Logger.Verbose("{ServiceName} Service {ServiceState}", service.Name, serviceState),
            //         uniqueName: $"{Service.Name}-{serviceState}".ToLowerInvariant()
            //     );
            // }

            Service.Start();

            Logger.Information("{ServiceName} Container stack started", ServiceName);
        }
        catch (Exception ex) {
            Logger.Error(ex, "{ServiceName} Failed to start container stack", ServiceName);
            throw new FluentDockerException($"{ServiceName} Failed to start container stack", ex);
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
            Logger.Information($"{ServiceName} Container stack stopped");
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{ServiceName} Failed to stop container stack", ex);
        }

        try {
            await OnStopped();
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{ServiceName} {nameof(OnStopped)} Execution error", ex);
        }
    }

    public void ReportStatus() {
        foreach (var container in Service.Containers) {
            var cfg = container.GetConfiguration(true);

            Logger.Information(
                "{ServiceName} Container {ContainerName} Created On: {CreatedOn} Exposed Ports: {@ExposedPorts}",
                Service.Name, container.Name, cfg.Created, cfg.Config.ExposedPorts.Keys
            );
        }
    }

    public virtual ValueTask DisposeAsync() {
        try {
            Service.Dispose();
        }
        catch (Exception ex) {
            throw new FluentDockerException("Failed to gracefully dispose of container stack", ex);
        }

        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask OnStarted() => ValueTask.CompletedTask;
    protected virtual ValueTask OnStopped() => ValueTask.CompletedTask;
}
