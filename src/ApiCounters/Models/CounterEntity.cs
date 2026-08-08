using Azure;
using Azure.Data.Tables;

namespace ApiCounters.Models;

/// <summary>
/// Represents a counter event record stored in Azure Table Storage.
/// </summary>
public class CounterEntity : ITableEntity
{
    /// <summary>PartitionKey is the counter name.</summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>RowKey is the key-id (GUID) from the request header.</summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>Serialized counter dimensions as "dim1:val1,dim2:val2".</summary>
    public string Dimensions { get; set; } = string.Empty;

    /// <summary>UTC time the record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
