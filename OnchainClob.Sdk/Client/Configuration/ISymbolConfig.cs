﻿namespace OnchainClob.Client.Configuration
{
    public interface ISymbolConfig
    {
        string ContractAddress { get; }
        string Symbol { get; }
        string BinanceSymbol { get; }
        int ScallingFactorX { get; }
        int ScallingFactorY { get; }
        TokenConfig TokenX { get; }
        TokenConfig TokenY { get; }
        int PricePrecision { get; }
        bool UseNative { get; }
    }
}
