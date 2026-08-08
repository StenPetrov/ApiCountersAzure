using Azure.Data.Tables;
using ApiCounters.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ApiCounters.Functions;

public class CounterFunction
{
    private const string TableName = "counters";
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<CounterFunction> _logger;

    public CounterFunction(TableServiceClient tableServiceClient, ILogger<CounterFunction> logger)
    {
        _tableServiceClient = tableServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// POST /counter/{counterName}
    /// Headers: key-id=&lt;guid&gt;, counter-dimensions=&lt;dim1&gt;:&lt;val1&gt;,&lt;dim2&gt;:&lt;val2&gt;
    /// Stores the counter event in Azure Table Storage.
    /// </summary>
    [Function("CounterPost")]
    public async Task<IActionResult> PostCounter(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "counter/{counterName}")] HttpRequest req,
        string counterName)
    {
        var keyId = req.Headers["key-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(keyId) || !Guid.TryParse(keyId, out var parsedKeyId))
        {
            return new BadRequestObjectResult("Header 'key-id' must be a valid GUID.");
        }
        // Use the canonical GUID string for all storage/logging to prevent log-forging.
        keyId = parsedKeyId.ToString();

        if (string.IsNullOrWhiteSpace(counterName))
        {
            return new BadRequestObjectResult("Counter name must not be empty.");
        }

        var rawDimensions = req.Headers["counter-dimensions"].FirstOrDefault() ?? string.Empty;
        var parsedDimensions = ParseDimensions(rawDimensions);

        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();

        var entity = new CounterEntity
        {
            PartitionKey = counterName,
            RowKey = keyId,
            Dimensions = SerializeDimensions(parsedDimensions),
            CreatedAt = DateTime.UtcNow
        };

        await tableClient.UpsertEntityAsync(entity);
        _logger.LogInformation("Stored counter '{CounterName}' with key-id '{KeyId}'.", counterName, keyId);

        return new OkObjectResult(new { counterName, keyId, dimensions = parsedDimensions });
    }

    /// <summary>
    /// GET /counter/{counterName}
    /// Returns the count of distinct key-id entries for the given counter name.
    /// </summary>
    [Function("CounterGet")]
    public async Task<IActionResult> GetCounter(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "counter/{counterName}")] HttpRequest req,
        string counterName)
    {
        if (string.IsNullOrWhiteSpace(counterName))
        {
            return new BadRequestObjectResult("Counter name must not be empty.");
        }

        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();

        var count = 0;
        await foreach (var _ in tableClient.QueryAsync<CounterEntity>(
            filter: $"PartitionKey eq '{EscapeODataString(counterName)}'",
            select: new[] { "PartitionKey" }))
        {
            count++;
        }

        _logger.LogInformation("Counter '{CounterName}' has value {Count}.", counterName, count);
        return new OkObjectResult(new { counterName, count });
    }

    /// <summary>
    /// Parses "dim1:val1,dim2:val2" or "dim1:val1,dim2=val2" into a dictionary.
    /// Both ':' and '=' are accepted as key/value separators within each dimension pair.
    /// </summary>
    internal static Dictionary<string, string> ParseDimensions(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOfAny(new[] { ':', '=' });
            if (separatorIndex > 0)
            {
                var key = pair[..separatorIndex].Trim();
                var value = pair[(separatorIndex + 1)..].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = value;
                }
            }
        }

        return result;
    }

    internal static string SerializeDimensions(Dictionary<string, string> dimensions)
        => string.Join(",", dimensions.Select(kv => $"{kv.Key}:{kv.Value}"));

    private static string EscapeODataString(string value)
        => value.Replace("'", "''");
}
