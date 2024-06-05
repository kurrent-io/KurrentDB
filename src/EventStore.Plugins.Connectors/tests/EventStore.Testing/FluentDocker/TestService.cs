using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;
using Serilog;
using static Serilog.Core.Constants;

namespace EventStore.Testing.FluentDocker;

[PublicAPI]
public interface ITestService : IAsyncDisposable {
    Task Start();
    Task Stop();
    Task Restart(TimeSpan delay);
    Task Restart() => Restart(TimeSpan.Zero);
    void ReportStatus();
}

public abstract class TestService<TService, TBuilder> : ITestService where TService : IService where TBuilder : BaseBuilder<TService> {
    ILogger Logger { get; }

    protected TestService() => Logger = Log.ForContext(SourceContextPropertyName, GetType().Name);

    protected TService Service { get; private set; } = default!;

    public virtual async Task Start() {
        Logger.Information("container service starting");

        var builder = await ConfigureAsync();

        //TODO SS: add retry policy here
        Service = builder.Build();

        try {
            Service.Start();
            Logger.Information("container service started");
        }
        catch (Exception ex) {
            throw new FluentDockerException("Failed to start container service", ex);
        }

        try {
            await OnServiceStarted();
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{nameof(OnServiceStarted)} execution error", ex);
        }
    }

    public virtual async Task Stop() {
        try {
            await OnServiceStopping();
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{nameof(OnServiceStopping)} execution error", ex);
        }

        try {
            Service.Stop();
            Logger.Information("container service stopping...");
        }
        catch (Exception ex) {
            throw new FluentDockerException("Failed to stop container service", ex);
        }

        try {
            await OnServiceStopped();
        }
        catch (Exception ex) {
            throw new FluentDockerException($"{nameof(OnServiceStopped)} execution error", ex);
        }
    }

    public virtual async Task Restart(TimeSpan delay) {
        try {
            try {
                Service.Stop();
                Logger.Information("container service stopped");
            }
            catch (Exception ex) {
                throw new FluentDockerException("Failed to stop container service", ex);
            }

            await Task.Delay(delay);

            Logger.Information("container service starting...");

            try {
                Service.Start();
            }
            catch (Exception ex) {
                throw new FluentDockerException("Failed to start container service", ex);
            }

            try {
                await OnServiceStarted();
                Logger.Information("container service started");
            }
            catch (Exception ex) {
                throw new FluentDockerException($"{nameof(OnServiceStarted)} execution error", ex);
            }
        }
        catch (Exception ex) {
            throw new FluentDockerException("Failed to restart container service", ex);
        }
    }

    public void ReportStatus() {
        if (Service is IContainerService containerService)
            ReportContainerStatus(containerService);

        if (Service is ICompositeService compose) {
            foreach (var container in compose.Containers)
                ReportContainerStatus(container);
        }

        return;

        void ReportContainerStatus(IContainerService service) {
            var cfg = service.GetConfiguration(true);
            Logger.Information("container {Name} {State} ports: {Ports}", service.Name, service.State, cfg.Config.ExposedPorts.Keys);
        }
    }

    public virtual ValueTask DisposeAsync() {
        try {
            Service.Dispose();
        }
        catch (Exception ex) {
            throw new FluentDockerException("Failed to dispose of container service", ex);
        }

        return ValueTask.CompletedTask;
    }

    protected abstract Task<TBuilder> ConfigureAsync();

    protected virtual Task OnServiceStarted()  => Task.CompletedTask;
    protected virtual Task OnServiceStopping() => Task.CompletedTask;
    protected virtual Task OnServiceStopped()  => Task.CompletedTask;
}