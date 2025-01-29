namespace Hanji.Client.Configuration
{
    public interface ISymbolConfig
    {
        string ContractAddress { get; }
        string Symbol { get; }
        int ScallingFactorX { get; }
        int ScallingFactorY { get; }
        TokenConfig TokenX { get; }
        TokenConfig TokenY { get; }
        int PricePrecision => TokenY.Decimals - ScallingFactorY - (TokenX.Decimals - ScallingFactorX);
        bool UseNative { get; }
    }
}
