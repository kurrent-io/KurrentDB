// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Threading.Channels;
using EventStore.AutoScavenge.Clients;
using EventStore.AutoScavenge.Converters;
using EventStore.AutoScavenge.Domain;
using EventStore.AutoScavenge.Scavengers;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Services.Transport.Http.NodeHttpClientFactory;
using EventStore.Plugins;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Subsystems;
using EventStore.POC.IO.Core;
using EventStore.POC.IO.Core.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NCrontab;
using ILogger = Serilog.ILogger;

namespace EventStore.AutoScavenge;

public class AutoScavengePlugin() : SubsystemsPlugin(name: "auto-scavenge", requiredEntitlements: ["AUTO_SCAVENGE"]), IConnectedSubsystemsPlugin {
	private static readonly ILogger Log = Serilog.Log.ForContext<AutoScavengePlugin>();
	private readonly CancellationTokenSource _cts = new();
	private AutoScavengeService? _autoScavengeService;

	private static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = {
			new EnumConverterWithDefault<AutoScavengeStatus>(),
			new EnumConverterWithDefault<AutoScavengeStatusResponse.Status>(),
			new CrontableScheduleJsonConverter(),
		},
	};

	private readonly Channel<ICommand> _commands = Channel.CreateBounded<ICommand>(
		new BoundedChannelOptions(capacity: 32) {
			SingleReader = true,
			SingleWriter = false,
		});

	private IAutoScavengeClient _dispatcher = IAutoScavengeClient.None;
	private EventStoreOptions _options = new();

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabledOption = configuration.GetValue<bool?>($"{KurrentConfigurationKeys.Prefix}:AutoScavenge:Enabled");
		var devMode = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:Dev", defaultValue: false);
		var memDb = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:MemDb", defaultValue: false);

		// enabled by default
		bool enabled = enabledOption ?? true;

		if (!enabled)
			return (false, $"To enable AutoScavenge: Do not set '{KurrentConfigurationKeys.Prefix}:AutoScavenge:Enabled' to 'false'");

		// not compatible with dev mode or memdb
		if (devMode || memDb) {
			var msg = "AutoScavenge is not compatible with " + (devMode ? "dev mode" : "mem-db");

			if (enabledOption.HasValue && enabledOption.Value) {
				// user has explicitly enabled both
				throw new Exception(msg);
			}

			// auto scavenge is not explicitly enabled, just disable it.
			return (false, msg);
		}

		return (true, "");
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		_options = configuration.GetSection(KurrentConfigurationKeys.Prefix).Get<EventStoreOptions>() ?? _options;

		services.AddSingleton(provider => {
			var nodeHttpClientFactory = provider.GetRequiredService<INodeHttpClientFactory>();
			return new HttpClientWrapper(_options, nodeHttpClientFactory);
		});
		services.AddSingleton<AutoScavengeClientDispatcher>(provider => {
			var wrapper = provider.GetRequiredService<HttpClientWrapper>();
			var internalClient = new InternalAutoScavengeClient(_commands.Writer);
			var proxyClient = new ProxyAutoScavengeClient(wrapper);
			var dispatcher = new AutoScavengeClientDispatcher(internalClient, proxyClient);
			_dispatcher = dispatcher;
			return dispatcher;
		});
		services.AddSingleton<AutoScavengeService>(provider => {
			var client = provider.GetRequiredService<IClient>();
			var wrapper = provider.GetRequiredService<HttpClientWrapper>();
			var dispatcher = provider.GetRequiredService<AutoScavengeClientDispatcher>();
			return new AutoScavengeService(
				options: _options,
				client: client,
				operationsClient: provider.GetRequiredService<IOperationsClient>(),
				commands: _commands,
				scavenger: new HttpNodeScavenger(wrapper, client),
				dispatcher,
				cts: _cts);
			});
	}

	protected override void OnLicenseException(Exception ex, Action<Exception> shutdown) {
		var msg = "AutoScavenge is not licensed, stopping.";
		Log.Information(msg);
		Disable(msg);
		PublishDiagnosticsData(
			new Dictionary<string, object?>() { ["enabled"] = Enabled },
			PluginDiagnosticsDataCollectionMode.Partial);
		_ = Stop();
	}

	public override void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration) {
		_autoScavengeService = builder.ApplicationServices.GetRequiredService<AutoScavengeService>();

		builder.UseEndpoints(endpoints => {
			var anyUserGroup = endpoints.MapGroup("/auto-scavenge");
			anyUserGroup.MapGet("/enabled", OnGetEnabled);
			anyUserGroup.MapGet("/status", (Delegate)OnGetStatus);
			anyUserGroup.RequireAuthorization();

			var opsGroup = endpoints.MapGroup("/auto-scavenge");
			opsGroup.MapPost("/resume", (Delegate)OnPostResume);
			opsGroup.MapPost("/pause", (Delegate)OnPostPause);
			opsGroup.MapPost("/configure", (Delegate)OnPostConfigure);
			opsGroup.RequireAuthorization(policy => policy.RequireRole("$admins", "$ops"));
		});
	}

	public override Task Start() {
		return _autoScavengeService?.StartAsync() ?? Task.CompletedTask;
	}

	public override async Task Stop() {
		await _cts.CancelAsync();
	}

	/// <summary>
	/// Handles the POST request to configure the auto scavenge process. It disallows anonymous users from
	/// configuring the auto scavenge process. The state machine could also deny the request if the node is no
	/// longer the cluster's leader.
	/// </summary>
	private Task<IResult> OnPostConfigure(HttpContext context) =>
		ExecuteCommand(context, Run.WithPayload<AutoScavengeConfigurationPayload, Unit>(
			(conf) => _dispatcher.Configure(conf.Schedule, _cts.Token)));

	/// <summary>
	/// Handles the GET request to check if the auto scavenge plugin is enabled.
	/// </summary>
	private IResult OnGetEnabled() =>
		Results.Json(new GetAutoScavengeEnabledResult(
			Enabled: Enabled));

	/// <summary>
	/// Handles the POST request to resume the auto scavenge process.
	/// </summary>
	private Task<IResult> OnPostResume(HttpContext context) =>
		ExecuteCommand(context, Run.WithoutPayload(() => _dispatcher.Resume(_cts.Token)));

	/// <summary>
	/// Handles the POST request to pause the auto scavenge process.
	/// </summary>
	private Task<IResult> OnPostPause(HttpContext context) =>
		ExecuteCommand(context, Run.WithoutPayload(() => _dispatcher.Pause(_cts.Token)));

	/// <summary>
	/// Handles the GET request to get the status of the auto scavenge process. That status includes but not only
	/// the current schedule, the state of the process and when next scavenge is expected.
	/// </summary>
	private Task<IResult> OnGetStatus(HttpContext context) =>
		ExecuteCommand(context, Run.WithoutPayload(() => _dispatcher.GetStatus(_cts.Token)));

	/// <summary>
	/// Common implementation to execute an auto scavenge process state machine command.
	/// </summary>
	/// <param name="runner">A command to execute on the state machine</param>
	/// <typeparam name="TParam">Type of the command's parameter</typeparam>
	/// <typeparam name="TResp">Type of the command response</typeparam>
	private async Task<IResult> ExecuteCommand<TParam, TResp>(
		HttpContext context, RunCommand<TParam, TResp> runner) {

		TParam? param = default;

		if (!Enabled)
			return Results.Problem("AutoScavenge is not enabled", statusCode: 503);

		if (runner.ParsePayload) {
			try {
				param = await context.Request.ReadFromJsonAsync<TParam>(JsonSerializerOptions);
				if (param is null)
					return Results.BadRequest("Invalid payload");
			} catch (JsonException ex) {
				return Results.BadRequest(ex.Message);
			}
		}

		Response<TResp> resp;

		try {
			resp = await runner.Run(param);
		} catch (TaskCanceledException) {
			return Results.Problem("AutoScavenge is no longer running", statusCode: 500);
		}

		return resp.Visit(
			onSuccessful: static result => Results.Json(result, JsonSerializerOptions),
			onAccepted: static () => Results.Accepted(),
			onRejected: static rejectedReason => Results.BadRequest(rejectedReason),
			onServerError: static serverError => Results.Problem(serverError, statusCode: 500));
	}

	private record RunCommand<TParam, TResp>(Func<TParam?, Task<Response<TResp>>> Run, bool ParsePayload);

	private static class Run {
		public static RunCommand<Unit, TResp> WithoutPayload<TResp>(Func<Task<Response<TResp>>> run) =>
			new((_) => run(), false);

		public static RunCommand<TParam, TResp> WithPayload<TParam, TResp>(Func<TParam, Task<Response<TResp>>> run) =>
			new((p) => run(p!), true);
	}

	/// <summary>
	/// JSON Payload for configuring the auto scavenge process.
	/// </summary>
	private class AutoScavengeConfigurationPayload {
		public required CrontabSchedule Schedule { get; init; }
	}

	record struct GetAutoScavengeEnabledResult(bool Enabled);

}
