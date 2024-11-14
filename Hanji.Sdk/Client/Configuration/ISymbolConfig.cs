namespace Hanji.Client.Configuration
{
    public interface ISymbolConfig
    {
        string ContractAddress { get; init; }
        string Symbol { get; init; }
        int ScallingFactorX { get; init; }
        int ScallingFactorY { get; init; }
        int PricePrecision { get; init; }
        string NetworkId { get; init; }
    }
}
