using EventStore.Client;
using ILogger = Serilog.ILogger;

namespace EventStore.TestClient {
	/// <summary>
	/// A test client that connects using the dotnet gRPC client
	/// </summary>
	public class GrpcTestClient {
		private ClientOptions _options;
		private ILogger _log;

		/// <summary>
		/// Constructs a new <see cref="GrpcTestClient"/>
		/// </summary>
		/// <param name="options"></param>
		/// <param name="log"></param>
		public GrpcTestClient(ClientOptions options, ILogger log) {
			_options = options;
			_log = log;
		}

		/// <summary>
		/// Creates a new gRPC client
		/// </summary>
		/// <returns></returns>
		public EventStoreClient CreateGrpcClient() {
			_log.Debug("Creating gRPC client with connection string '{connectionString}'.", ConnectionString);
			return new EventStoreClient(Settings);
		}

		/// <summary>
		/// True in case username and/or password are not specified.
		/// </summary>
		public bool AreCredentialsMissing =>
			string.IsNullOrWhiteSpace(Settings.DefaultCredentials?.Username) ||
			string.IsNullOrWhiteSpace(Settings.DefaultCredentials?.Password);

		private EventStoreClientSettings Settings => EventStoreClientSettings.Create(ConnectionString);

		private string ConnectionString => string.IsNullOrWhiteSpace(_options.ConnectionString)
			? $"esdb://{_options.Host}:{_options.HttpPort}?tls={_options.UseTls}&tlsVerifyCert={_options.TlsValidateServer}"
			: _options.ConnectionString;

	}
}
