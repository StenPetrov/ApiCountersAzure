using System.Security.Cryptography;
using System.Text;
using Azure;
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "counter/{counterName}")] HttpRequest req,
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
            return new BadRequestObjectResult("counterName must not be empty.");
        }

        var rawDimensions = req.Headers["counter-dimensions"].FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawDimensions))
        {
            return new BadRequestObjectResult("Header 'counter-dimensions' must not be empty.");
        }

        int counterAppend = 1;
        var counterAppendValue = req.Headers["counter-value-append"].FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(counterAppendValue) || !int.TryParse(counterAppendValue, out counterAppend))
        {
            counterAppend = 1;
        }
        else if (counterAppend < 1)
        {
            return new BadRequestObjectResult("Header 'counter-value-append' must be a positive integer.");
        }

        double trackedMax = 0.0;
        var trackedMaxHdr = req.Headers["tracked-max"].FirstOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(trackedMaxHdr))
        {
            trackedMax = double.TryParse(trackedMaxHdr, out var parsedMax) ? parsedMax : 0.0;
            if (double.IsNaN(trackedMax) || double.IsInfinity(trackedMax) || trackedMax < 0.0)
            {
                return new BadRequestObjectResult("Header 'tracked-max' must be a non-negative number.");
            }
        }

        var parsedDimensions = ParseDimensions(rawDimensions);

        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();

        var dimensions = SerializeDimensions(parsedDimensions);
        var entity = new CounterEntity
        {
            PartitionKey = counterName,
            RowKey = BuildRowKey(keyId, dimensions),
            Dimensions = dimensions,
            CounterValue = 1,
            CreatedAt = DateTime.UtcNow
        };

        var count = await IncrementCounterAsync(tableClient, entity, counterAppend, trackedMax, req.HttpContext.RequestAborted);
        return new OkObjectResult(new { counterName, keyId, dimensions = parsedDimensions, count });
    }

    /// <summary>
    /// GET /counter/{counterName}
    /// Returns the count of distinct key-id entries for the given counter name.
    /// </summary>
    [Function("CounterGet")]
    public async Task<IActionResult> GetCounter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "counter/{counterName}")] HttpRequest req,
        string counterName)
    {
        if (string.IsNullOrWhiteSpace(counterName))
        {
            return new BadRequestObjectResult("counterName must not be empty.");
        }

        var keyId = req.Headers["key-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(keyId) || !Guid.TryParse(keyId, out var parsedKeyId))
        {
            keyId = null; // key-id is optional for GET, but if provided, must be a valid GUID
            parsedKeyId = Guid.Empty;
        }

        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();

        long count = 0;
        long value = 0;
        var rowKeyPrefix = keyId != null ? $"{parsedKeyId}_" : null;
        double trackedMax = 0.0;
        await foreach (var entity in tableClient.QueryAsync<CounterEntity>(
            filter: $"PartitionKey eq '{EscapeODataString(counterName)}'"
                + (rowKeyPrefix != null
                    ? $" and RowKey ge '{rowKeyPrefix}' and RowKey lt '{rowKeyPrefix}g'"
                    : string.Empty),
            select: new[]
                {
                    "PartitionKey",
                    "RowKey",
                    nameof(CounterEntity.CounterValue),
                    nameof(CounterEntity.TrackedMax) 
                }))
        {
            value += entity.CounterValue;
            trackedMax = Math.Max(trackedMax, entity.TrackedMax);
            count++;
        }

        _logger.LogInformation("Counter '{CounterName}' has value {Value} across {Count} entries.", counterName, value, count);
        return new OkObjectResult(new { counterName, value, count, trackedMax });
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
        => string.Join(",", dimensions
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}:{kv.Value}"));

    internal static string BuildRowKey(string keyId, string dimensions)
    {
#pragma warning disable CA5351 // Required as a deterministic identifier, not for security.
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(dimensions));
#pragma warning restore CA5351
        return $"{keyId}_{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static async Task<long> IncrementCounterAsync(
        TableClient tableClient,
        CounterEntity newEntity,
        int addCountValue,
        double trackedMax,
        CancellationToken cancellationToken)
    {
        try
        {
            await tableClient.AddEntityAsync(newEntity, cancellationToken);
            return newEntity.CounterValue;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status409Conflict)
        {
            // Another request already created this counter row; update it below.
        }

        const int maxAttempts = 20;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await tableClient.GetEntityAsync<CounterEntity>(
                newEntity.PartitionKey,
                newEntity.RowKey,
                cancellationToken: cancellationToken);
            var entity = response.Value;
            entity.CounterValue = Math.Max(entity.CounterValue, addCountValue) + addCountValue;
            entity.TrackedMax = Math.Max(entity.TrackedMax, trackedMax);

            try
            {
                await tableClient.UpdateEntityAsync(
                    entity,
                    entity.ETag,
                    TableUpdateMode.Merge,
                    cancellationToken);

                return entity.CounterValue;
            }
            catch (RequestFailedException exception)
                when (exception.Status == StatusCodes.Status412PreconditionFailed && attempt < maxAttempts)
            {
                // Reload the latest value and retry after a concurrent update.
            }
        }

        throw new InvalidOperationException("Could not increment the counter after repeated concurrent updates.");
    }

    private static string EscapeODataString(string value)
        => value.Replace("'", "''");
}
