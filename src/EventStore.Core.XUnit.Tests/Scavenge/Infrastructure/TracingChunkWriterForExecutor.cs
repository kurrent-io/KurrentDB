using System.IO;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge {
	public class TracingChunkWriterForExecutor<TStreamId, TRecord> :
		IChunkWriterForExecutor<TStreamId, TRecord> {

		private readonly IChunkWriterForExecutor<TStreamId, TRecord> _wrapped;
		private readonly Tracer _tracer;

		public TracingChunkWriterForExecutor(
			IChunkWriterForExecutor<TStreamId, TRecord> wrapped,
			Tracer tracer) {

			_wrapped = wrapped;
			_tracer = tracer;
		}

		public string FileName => _wrapped.FileName;

		public void WriteRecord(RecordForExecutor<TStreamId, TRecord> record) {
			_wrapped.WriteRecord(record);
		}

		public void Complete(out string newFileName, out long newFileSize) {
			_wrapped.Complete(out newFileName, out newFileSize);
			_tracer.Trace($"Switched in {Path.GetFileName(newFileName)}");
		}

		public void Abort(bool deleteImmediately) {
			_wrapped.Abort(deleteImmediately);
		}
	}
}
