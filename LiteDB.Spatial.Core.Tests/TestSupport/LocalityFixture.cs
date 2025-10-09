using System;
using System.Text.Json.Serialization;

namespace LiteDB.Spatial.Core.Tests.TestSupport;

public sealed class LocalityFixture
{
    [JsonPropertyName("dimensions")]
    public int Dimensions { get; set; }

    [JsonPropertyName("gridSize")]
    public int GridSize { get; set; }

    [JsonPropertyName("precisionBits")]
    public int PrecisionBits { get; set; }

    [JsonPropertyName("neighborCount")]
    public int NeighborCount { get; set; }

    [JsonPropertyName("expectedOverlap")]
    public double ExpectedOverlap { get; set; }

    [JsonPropertyName("averageWindowSpan")]
    public double AverageWindowSpan { get; set; }

    [JsonPropertyName("mortonCodes")]
    public ulong[] MortonCodes { get; set; } = Array.Empty<ulong>();
}
