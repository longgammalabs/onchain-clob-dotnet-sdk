namespace OnchainClob.Client.Configuration
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

        public bool IsNative(string tokenContractAddress)
        {
            if (!UseNative)
                return false;

            bool isTokenXNative = TokenX.ContractAddress == tokenContractAddress && TokenX.IsNative;
            bool isTokenYNative = TokenY.ContractAddress == tokenContractAddress && TokenY.IsNative;

            return isTokenXNative || isTokenYNative;
        }
    }
}
