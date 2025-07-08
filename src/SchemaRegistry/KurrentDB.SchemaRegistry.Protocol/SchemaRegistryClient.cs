// #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
//
// using Grpc.Core;
// using Grpc.Net.Client;
// using static KurrentDB.SchemaRegistry.Protocol.SchemaRegistryCommandService;
// using static KurrentDB.SchemaRegistry.Protocol.SchemaRegistryQueryService;
//
// namespace KurrentDB.SchemaRegistry.Protocol;
//
// public class SchemaRegistryClient : ISchemaRegistryClient {
//     static readonly GrpcChannelOptions DefaultChannelOptions = new GrpcChannelOptions {
//         Credentials = ChannelCredentials.Insecure
//     };
//
//     public SchemaRegistryClient(ChannelBase channel) {
//         CommandServiceClient = new(channel);
//         QueryServiceClient   = new(channel);
//     }
//
//     public SchemaRegistryClient(CallInvoker callInvoker) {
//         CommandServiceClient = new(callInvoker);
//         QueryServiceClient   = new(callInvoker);
//     }
//
//     public SchemaRegistryClient(string address, GrpcChannelOptions? options = null) : this(
//         GrpcChannel.ForAddress(address, options ?? DefaultChannelOptions)
//     ) { }
//
//     SchemaRegistryCommandServiceClient CommandServiceClient { get; }
//     SchemaRegistryQueryServiceClient   QueryServiceClient   { get; }
//
//     public async Task<RegisterSchemaResponse> Register(RegisterSchema request, CancellationToken cancellationToken) {
//         using var call = CommandServiceClient.RegisterAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
//
//     public async Task<RegisterOrUpdateSchemaResponse> RegisterOrUpdate(RegisterOrUpdateSchema request, CancellationToken cancellationToken) {
//         using var call = CommandServiceClient.RegisterOrUpdateAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
//
//     public async Task<DestroySchemaResponse> Destroy(DestroySchema request, CancellationToken cancellationToken) {
//         using var call = CommandServiceClient.DestroyAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
//
//     public async Task<GetSchemaResponse> Get(GetSchema request, CancellationToken cancellationToken) {
//         using var call = QueryServiceClient.GetAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
//
//     public async Task<ListSchemasResponse> List(ListSchemas request, CancellationToken cancellationToken) {
//         using var call = QueryServiceClient.ListAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
//
//     public async Task<GetSchemaVersionsResponse> GetVersions(GetSchemaVersions request, CancellationToken cancellationToken) {
//         using var call = QueryServiceClient.GetVersionsAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
//
//     public async Task<ValidateSchemaResponse> Validate(ValidateSchema request, CancellationToken cancellationToken) {
//         using var call = CommandServiceClient.ValidateAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
//
//     public async Task<BulkWriteSchemaResponse> BulkWrite(BulkWriteSchema request, CancellationToken cancellationToken) {
//         using var call = CommandServiceClient.BulkWriteAsync(request, cancellationToken: cancellationToken);
//         return await call.ConfigureAwait(false);
//     }
// }