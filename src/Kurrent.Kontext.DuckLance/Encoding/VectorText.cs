namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// What to embed, and which vector COLUMN the result is for — one entry per vector column, returned
/// by <see cref="RecordCodec{TRecord}.GetVectorTexts"/>. The column is the storage column name: the
/// vector↔column association is written in code at both ends, never implied by ordering.
/// </summary>
public readonly record struct VectorText(string Column, string Text);
