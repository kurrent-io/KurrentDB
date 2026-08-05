using Kurrent.Surge.Consumers;
using Kurrent.Surge.DuckDB.Projectors;

namespace Kurrent.Kontext.Modules.Memory.Data;

public abstract class KontextProjection : DuckDBProjection {
    public abstract ConsumeFilter Filter { get; }
}