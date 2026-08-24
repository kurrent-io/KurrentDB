namespace Kurrent.Kontext.Data;

public sealed class KontextIndexConstants {
    public const int VectorsDimension = 384;

    /// <summary>
    /// Tokens longer than this are dropped silently; the engine default is 40. 'null' is rejected
    /// by the DDL, so a large ceiling is the only way to say unlimited.
    /// </summary>
    public const int MaxTokenLength = 1_048_576; // 1 MiB
}