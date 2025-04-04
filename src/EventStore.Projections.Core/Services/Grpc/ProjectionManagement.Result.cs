using System.Text.Json;
using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc {
	internal partial class ProjectionManagement {
		private static readonly Operation ResultOperation = new Operation(Operations.Projections.Result);
		private static readonly Operation StateOperation = new Operation(Operations.Projections.State);
		public override async Task<ResultResp> Result(ResultReq request, ServerCallContext context) {

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, ResultOperation, context.CancellationToken)
				.ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}

			var resultSource = new TaskCompletionSource<Value>();
			var options = request.Options;

			var name = options.Name;
			var partition = options.Partition ?? string.Empty;

			var envelope = new CallbackEnvelope(OnMessage);

			_queue.Publish(new ProjectionManagementMessage.Command.GetResult(envelope, name, partition));

			return new ResultResp {
				Result = await resultSource.Task.ConfigureAwait(false)
			};

			void OnMessage(Message message) {
				if (!(message is ProjectionManagementMessage.ProjectionResult result)) {
					resultSource.TrySetException(UnknownMessage<ProjectionManagementMessage.ProjectionResult>(message));
					return;
				}

				if (string.IsNullOrEmpty(result.Result)) {
					resultSource.TrySetResult(new Value {
						StructValue = new Struct()
					});
					return;
				}
				var document = JsonDocument.Parse(result.Result);

				resultSource.TrySetResult(GetProtoValue(document.RootElement));
			}
		}

		public override async Task<StateResp> State(StateReq request, ServerCallContext context) {

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user, StateOperation, context.CancellationToken)
				.ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}
			var resultSource = new TaskCompletionSource<Value>();

			var options = request.Options;

			var name = options.Name;
			var partition = options.Partition ?? string.Empty;

			var envelope = new CallbackEnvelope(OnMessage);

			_queue.Publish(new ProjectionManagementMessage.Command.GetState(envelope, name, partition));

			return new StateResp {
				State = await resultSource.Task.ConfigureAwait(false)
			};

			void OnMessage(Message message) {
				if (!(message is ProjectionManagementMessage.ProjectionState result)) {
					resultSource.TrySetException(UnknownMessage<ProjectionManagementMessage.ProjectionState>(message));
					return;
				}

				if (string.IsNullOrEmpty(result.State)) {
					resultSource.TrySetResult(new Value {
						StructValue = new Struct()
					});
					return;
				}
				var document = JsonDocument.Parse(result.State);
				resultSource.TrySetResult(GetProtoValue(document.RootElement));
			}
		}
	}
}
