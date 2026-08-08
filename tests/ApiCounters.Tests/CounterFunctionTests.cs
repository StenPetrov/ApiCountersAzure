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
}
