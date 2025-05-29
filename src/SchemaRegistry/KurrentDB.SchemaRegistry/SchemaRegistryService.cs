using System.Collections.Concurrent;
using Eventuous;
using FluentValidation;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.SchemaRegistry.Infrastructure.Grpc;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;
using static KurrentDB.Protocol.Registry.V2.SchemaRegistryService;

namespace KurrentDB.SchemaRegistry;

public class SchemaRegistryService : SchemaRegistryServiceBase {
    public SchemaRegistryService(SchemaApplication commands, SchemaQueries queries, GrpcRequestValidator requestValidator, CheckAccess checkAccess, ILogger<SchemaRegistryService> logger) {
        Commands         = commands;
        Queries          = queries;
        RequestValidator = requestValidator;
        CheckAccess      = checkAccess;
        Logger           = logger;
    }

    SchemaApplication    Commands         { get; }
    SchemaQueries        Queries          { get; }
    GrpcRequestValidator RequestValidator { get; }
    CheckAccess          CheckAccess      { get; }
    ILogger              Logger           { get; }

    #region . Schemas .

    #region . Commands .

    public override Task<CreateSchemaResponse> CreateSchema(CreateSchemaRequest request, ServerCallContext context) =>
        Execute(request, context, async (req, ct) => {
            var result = await Commands.Handle(req, ct);

            return await result.Match(
                async ok => {
                    await Queries.WaitUntilCaughtUp(ok.StreamPosition, ct);
                    var evt = ok.Changes.GetSingleEvent<SchemaCreated>();
                    return new CreateSchemaResponse {
                        SchemaVersionId = evt.SchemaVersionId,
                        VersionNumber   = evt.VersionNumber
                    };
                },
                ko => throw ko.Exception ?? new(ko.ErrorMessage)
            );
        });

    public override Task<UpdateSchemaResponse> UpdateSchema(UpdateSchemaRequest request, ServerCallContext context)  =>
        Execute(request, context, async (req, ct) => {
            var result = await Commands.Handle(req, ct);

            return result.Match(
	            ok => new UpdateSchemaResponse(),
	            ko => throw ko.Exception ?? new(ko.ErrorMessage)
            );
        });

    public override Task<DeleteSchemaResponse> DeleteSchema(DeleteSchemaRequest request, ServerCallContext context) =>
        Execute(request, context, async (req, ct) => {
            var result = await Commands.Handle(req, ct);

            return result.Match(
                 ok => new DeleteSchemaResponse(),
                ko => throw ko.Exception ?? new(ko.ErrorMessage)
            );
        });

    public override Task<RegisterSchemaVersionResponse> RegisterSchemaVersion(RegisterSchemaVersionRequest request, ServerCallContext context) =>
        Execute(request, context, async (req, ct) => {
            var result = await Commands.Handle(req, ct);

            return await result.Match(
                async ok => {
                    await Queries.WaitUntilCaughtUp(ok.StreamPosition, ct);
                    var evt = ok.Changes.GetSingleEvent<SchemaVersionRegistered>();
                    return new RegisterSchemaVersionResponse {
                        SchemaVersionId = evt.SchemaVersionId,
                        VersionNumber   = evt.VersionNumber
                    };
                },
                ko => throw ko.Exception ?? new(ko.ErrorMessage)
            );
        });

    public override Task<DeleteSchemaVersionsResponse> DeleteSchemaVersions(DeleteSchemaVersionsRequest request, ServerCallContext context) =>
        Execute(request, context, async (req, ct) => {
            var result = await Commands.Handle(req, ct);

            return result.Match(
                ok => new DeleteSchemaVersionsResponse(),
                ko => throw ko.Exception ?? new(ko.ErrorMessage)
            );
        });

    public override async Task<BulkRegisterSchemasResponse> BulkRegisterSchemas(BulkRegisterSchemasRequest cmd, ServerCallContext ctx) {
        // interesting, we can optimize this by requesting sequence of ids from duck
        // but if it does not work we loose them... still not sure about this...
        // need to pay attention to the parallel execution cause it will call the
        // get next id function multiple times...

        // ATTENTION!!! XD
        // thinking out of the box here!! but we could use duck db with an appender
        // to generate the ids, and then read from it to actually register the schemas.

        // its true that with guids we have no issues, but yeah a numeric id
        // is sooo much better...

        var start = TimeProvider.System.GetTimestamp();

        var responses = new ConcurrentBag<CreateSchemaResponse>();

        if (!cmd.KeepOrder) {
            await Parallel.ForEachAsync(
                cmd.Requests,
                ctx.CancellationToken,
                (request, _) => ProcessBulkRegistration(request, ctx, cmd.StopOnError)
            );
        }
        else {
            foreach (var request in cmd.Requests)
                await ProcessBulkRegistration(request, ctx, cmd.StopOnError);
        }

        var elapsed = TimeProvider.System.GetElapsedTime(start);

        return new BulkRegisterSchemasResponse {
            Duration  = elapsed.ToDuration(),
            Responses = { responses }
        };

        async ValueTask ProcessBulkRegistration(CreateSchemaRequest request, ServerCallContext serverCallContext, bool stopOnError) {
            try {
                responses.Add(await CreateSchema(request, serverCallContext));
            }
            catch (RpcException rex) when (rex.StatusCode == StatusCode.AlreadyExists && !stopOnError) {
                // no worries
            }
        }
    }

    #endregion

    #region . Queries .

    public override Task<GetSchemaResponse> GetSchema(GetSchemaRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.GetSchema);

    public override Task<ListSchemasResponse> ListSchemas(ListSchemasRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.ListSchemas);

    public override Task<LookupSchemaNameResponse> LookupSchemaName(LookupSchemaNameRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.LookupSchemaName);

    public override Task<GetSchemaVersionResponse> GetSchemaVersion(GetSchemaVersionRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.GetSchemaVersion);

    public override Task<GetSchemaVersionByIdResponse> GetSchemaVersionById(GetSchemaVersionByIdRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.GetSchemaVersionById);

    public override Task<ListSchemaVersionsResponse> ListSchemaVersions(ListSchemaVersionsRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.ListSchemaVersions);

    public override Task<ListRegisteredSchemasResponse> ListRegisteredSchemas(ListRegisteredSchemasRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.ListRegisteredSchemas);

    public override Task<CheckSchemaCompatibilityResponse> CheckSchemaCompatibility(CheckSchemaCompatibilityRequest request, ServerCallContext context) =>
        Execute(request, context, Queries.CheckSchemaCompatibility);

    #endregion

    #endregion

    async Task<TResponse> Execute<TRequest, TResponse>(TRequest request, ServerCallContext context, HandleRequest<TRequest, TResponse> handle) where TRequest : class, IMessage {
        if (!await CheckAccess(context))
            throw RpcExceptions.PermissionDenied();

        var validationResult = RequestValidator.Validate(request);
        if (!validationResult.IsValid)
            throw RpcExceptions.InvalidArgument(validationResult);

        var traceId = context.GetHttpContext().TraceIdentifier;

        try {
            var response = await handle(request, context.CancellationToken);
            return response;
        }
        catch (Exception error) {
            var rpcEx = error switch {
                RpcException rex                        => rex, // Pass through gRPC errors from queries
                ValidationException ex                  => RpcExceptions.InvalidArgument(ex.Errors),
                DomainExceptions.EntityAlreadyExists ex => RpcExceptions.AlreadyExists(ex),
                DomainExceptions.EntityDeleted ex       => RpcExceptions.NotFound(ex),
                DomainExceptions.EntityNotFound ex      => RpcExceptions.NotFound(ex),
                DomainException ex                      => RpcExceptions.FailedPrecondition(ex),
                StreamAccessDeniedError ex              => RpcExceptions.PermissionDenied(ex),
                StreamNotFoundError ex                  => RpcExceptions.NotFound(ex),
                StreamDeletedError ex                   => RpcExceptions.FailedPrecondition(ex),
                ExpectedStreamRevisionError ex          => RpcExceptions.FailedPrecondition(ex),
                InvalidOperationException ex            => RpcExceptions.InvalidArgument(ex),
                NotImplementedException ex              => RpcExceptions.FailedPrecondition(ex),
                _                                       => RpcExceptions.Internal(error)
            };

            if (rpcEx.StatusCode == StatusCode.Internal)
                Logger.LogError(error, "{TraceIdentifier} {CommandType} failed", traceId, request.GetType().Name);
            else
                Logger.LogError("{TraceIdentifier} {CommandType} failed: {ErrorMessage}", traceId, request.GetType().Name, error.Message);

            throw rpcEx;
        }
    }

    delegate Task<TResponse> HandleRequest<in TRequest, TResponse>(TRequest request, CancellationToken cancellationToken) where TRequest : class, IMessage;
}
