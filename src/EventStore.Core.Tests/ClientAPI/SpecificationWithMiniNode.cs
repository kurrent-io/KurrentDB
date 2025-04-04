using System;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI {
	public abstract class SpecificationWithMiniNode<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
		private readonly int _chunkSize;
		protected MiniNode<TLogFormat, TStreamId> _node;
		protected IEventStoreConnection _conn;
		protected virtual TimeSpan Timeout { get; } = TimeSpan.FromMinutes(1);

		protected virtual Task Given() => Task.CompletedTask;

		protected abstract Task When();

		protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
			return TestConnection.Create(node.TcpEndPoint, TcpType.Ssl);
		}

		protected async Task CloseConnectionAndWait(IEventStoreConnection conn) {
			TaskCompletionSource closed = new TaskCompletionSource();
			conn.Closed += (_, _) => closed.SetResult();
			conn.Close();
			await closed.Task.WithTimeout(Timeout);
		}

		protected SpecificationWithMiniNode() : this(chunkSize: 1024 * 1024) { }

		protected SpecificationWithMiniNode(int chunkSize) {
			_chunkSize = chunkSize;
		}

		[OneTimeSetUp]
		public override async Task TestFixtureSetUp() {

			MiniNodeLogging.Setup();

			try {
				await base.TestFixtureSetUp();
			} catch (Exception ex) {
				throw new Exception("TestFixtureSetUp Failed", ex);
			}

			try {
				_node = new MiniNode<TLogFormat, TStreamId>(PathName, chunkSize: _chunkSize);
				await _node.Start();
				_conn = BuildConnection(_node);
				await _conn.ConnectAsync();
			} catch (Exception ex) {
				MiniNodeLogging.WriteLogs();
				throw new Exception("MiniNodeSetUp Failed", ex);
			}

			try {
				await Given().WithTimeout(Timeout);
			} catch (Exception ex) {
				MiniNodeLogging.WriteLogs();
				throw new Exception("Given Failed", ex);
			}

			try {
				await When().WithTimeout(Timeout);
			} catch (Exception ex) {
				MiniNodeLogging.WriteLogs();
				throw new Exception("When Failed", ex);
			}
		}

		[OneTimeTearDown]
		public override async Task TestFixtureTearDown() {
			_conn?.Close();
			await _node.Shutdown();
			await base.TestFixtureTearDown();

			MiniNodeLogging.Clear();
		}
	}
}
