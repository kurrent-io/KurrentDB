namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Resolves dataset paths and table names purely by convention from the store's database file path:
/// the file's directory holds one <c>{collection}.lance</c> dataset per collection, and the table
/// name IS the collection name.
/// </summary>
sealed class LanceDatasetResolver {
    /// <summary>The default Lance attachment alias for qualified table references.</summary>
    public const string DefaultStorageAlias = "ldb";

    public LanceDatasetResolver(string databasePath) {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);

        DatabasePath      = Path.GetFullPath(databasePath);
        DatabaseDirectory = Path.GetDirectoryName(DatabasePath)
                         ?? throw new ArgumentException("The database path must point to a file, not a filesystem root.", nameof(databasePath));
    }

    /// <summary>The resolved absolute path of the DuckDB database file.</summary>
    public string DatabasePath { get; }

    /// <summary>The database file's directory — doubles as the Lance namespace holding every collection's dataset.</summary>
    public string DatabaseDirectory { get; }

    /// <summary>The validated table name for a collection — the collection name itself.</summary>
    public string GetTableName(string collectionName) =>
        DuckDBNameValidator.GetValidatedTableName(collectionName);

    /// <summary>The qualified table name (<c>ldb.main.{collection}</c>) for DML and the search table functions.</summary>
    public string GetQualifiedTableName(string collectionName) =>
        $"{DefaultStorageAlias}.main.{GetTableName(collectionName)}";

    /// <summary>The dataset's filesystem path (<c>{dir}/{collection}.lance</c>) — index DDL cannot address the qualified name.</summary>
    public string GetDatasetPath(string collectionName) =>
        Path.Combine(DatabaseDirectory, $"{GetTableName(collectionName)}.lance");
}

static class DuckDBNameValidator {
    // Stricter than Lance's own naming rule (which also allows hyphens and periods): the name is used as an
    // unquoted SQL identifier and inside search-table-function URI strings ('alias.main.table'), where '.' is
    // the catalog separator and '-' would parse as an operator.
    const string LanceNameRule = "^[A-Za-z0-9_]+$ (letters, digits, and underscores only)";

    public static string GetValidatedTableName(string collectionName) {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);

        return collectionName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
            ? throw new ArgumentException($"The collection name '{collectionName}' is not a valid Lance table name. It must match {LanceNameRule}.", nameof(collectionName))
            : collectionName;
    }
}
