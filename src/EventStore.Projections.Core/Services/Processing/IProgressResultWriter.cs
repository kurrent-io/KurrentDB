namespace EventStore.Projections.Core.Services.Processing {
	public interface IProgressResultWriter {
		void WriteProgress(float progress);
	}
}
