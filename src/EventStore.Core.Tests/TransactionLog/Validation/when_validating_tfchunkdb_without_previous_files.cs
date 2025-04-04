using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Validation {
	[TestFixture]
	public class when_validating_tfchunkdb_without_previous_files : SpecificationWithDirectory {
		[Test]
		public void with_a_writer_checksum_of_nonzero_and_no_files_a_corrupted_database_exception_is_thrown() {
			var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 500));
			var exc = Assert.Throws<CorruptDatabaseException>(() => db.Open());
			Assert.IsInstanceOf<ChunkNotFoundException>(exc.InnerException);
			db.Dispose();
		}

		[Test]
		public void with_a_writer_checksum_of_zero_and_no_files_is_valid() {
			var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, 0));
			Assert.DoesNotThrow(() => db.Open());
			db.Dispose();
		}
	}
}
