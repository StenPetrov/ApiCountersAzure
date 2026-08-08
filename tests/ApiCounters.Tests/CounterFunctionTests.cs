using ApiCounters.Functions;
using Xunit;

namespace ApiCounters.Tests;

public class CounterFunctionTests
{
    [Fact]
    public void ParseDimensions_EmptyString_ReturnsEmpty()
    {
        var result = CounterFunction.ParseDimensions(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseDimensions_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(CounterFunction.ParseDimensions("   "));
    }

    [Fact]
    public void ParseDimensions_ColonSeparated_ParsesCorrectly()
    {
        var result = CounterFunction.ParseDimensions("region:us-east-1,env:prod");
        Assert.Equal(2, result.Count);
        Assert.Equal("us-east-1", result["region"]);
        Assert.Equal("prod", result["env"]);
    }

    [Fact]
    public void ParseDimensions_EqualsSeparated_ParsesCorrectly()
    {
        var result = CounterFunction.ParseDimensions("region=us-west-2,env=staging");
        Assert.Equal(2, result.Count);
        Assert.Equal("us-west-2", result["region"]);
        Assert.Equal("staging", result["env"]);
    }

    [Fact]
    public void ParseDimensions_MixedSeparators_ParsesCorrectly()
    {
        var result = CounterFunction.ParseDimensions("region:us-east-1,env=prod");
        Assert.Equal("us-east-1", result["region"]);
        Assert.Equal("prod", result["env"]);
    }

    [Fact]
    public void ParseDimensions_TrimsWhitespace()
    {
        var result = CounterFunction.ParseDimensions(" region : us-east-1 , env : prod ");
        Assert.Equal("us-east-1", result["region"]);
        Assert.Equal("prod", result["env"]);
    }

    [Fact]
    public void ParseDimensions_IsCaseInsensitive()
    {
        var result = CounterFunction.ParseDimensions("Region:us-east-1");
        Assert.True(result.ContainsKey("region"));
        Assert.True(result.ContainsKey("REGION"));
    }

    [Fact]
    public void ParseDimensions_SkipsMalformedPairs()
    {
        // A pair with no separator should be skipped
        var result = CounterFunction.ParseDimensions("nodimension,region:us-east-1");
        Assert.Single(result);
        Assert.Equal("us-east-1", result["region"]);
    }

    [Fact]
    public void SerializeDimensions_RoundTrips()
    {
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["region"] = "us-east-1",
            ["env"] = "prod"
        };

        var serialized = CounterFunction.SerializeDimensions(input);
        var reparsed = CounterFunction.ParseDimensions(serialized);

        Assert.Equal("us-east-1", reparsed["region"]);
        Assert.Equal("prod", reparsed["env"]);
    }

    [Fact]
    public void SerializeDimensions_EmptyDictionary_ReturnsEmpty()
    {
        var result = CounterFunction.SerializeDimensions(new Dictionary<string, string>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SerializeDimensions_DifferentInputOrder_IsCanonical()
    {
        var first = CounterFunction.ParseDimensions("region:us-east-1,env:prod");
        var second = CounterFunction.ParseDimensions("env:prod,region:us-east-1");

        Assert.Equal(
            CounterFunction.SerializeDimensions(first),
            CounterFunction.SerializeDimensions(second));
    }

    [Fact]
    public void BuildRowKey_IncludesKeyIdAndExpectedHash()
    {
        const string keyId = "8b0d8f64-e1a6-447f-a967-52cdbb2c79e9";

        var rowKey = CounterFunction.BuildRowKey(keyId, "env:prod");

        Assert.StartsWith($"{keyId}_", rowKey);
        Assert.Matches($"^{keyId}_[0-9a-f]{{32}}$", rowKey);
    }

    [Fact]
    public void BuildRowKey_DifferentDimensions_ProducesDifferentRows()
    {
        const string keyId = "8b0d8f64-e1a6-447f-a967-52cdbb2c79e9";

        var first = CounterFunction.BuildRowKey(keyId, "env:prod");
        var second = CounterFunction.BuildRowKey(keyId, "env:test");

        Assert.NotEqual(first, second);
    }
}
