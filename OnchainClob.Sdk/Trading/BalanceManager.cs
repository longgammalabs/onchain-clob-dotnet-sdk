using Incendium;
using Nethereum.Contracts;
using OnchainClob.Abi.Lob;
using OnchainClob.Client.Configuration;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Rpc.Parameters;
using System.Numerics;
using Error = Incendium.Error;

namespace OnchainClob.Trading
{
    public readonly struct Balance {
        public BigInteger NativeBalance { get; init; }
        public BigInteger TokenBalance { get; init; }
        public BigInteger LobBalance { get; init; }
        public bool IsNative { get; init; }
    }

    public class BalanceManager
    {
        private readonly RpcClient _rpc;
        private readonly string _fromAddress;
        private readonly string _vaultAddress;
        private BigInteger _nativeBalance;
        private readonly Dictionary<string, BigInteger> _tokenBalances = [];
        private readonly Dictionary<string, BigInteger> _lobBalancesTokenX = [];
        private readonly Dictionary<string, BigInteger> _lobBalancesTokenY = [];
        private readonly Dictionary<string, ISymbolConfig> _symbolConfigs = [];

        public BalanceManager(RpcClient rpc, string fromAddress, string vaultAddress)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _fromAddress = fromAddress ?? throw new ArgumentNullException(nameof(fromAddress));
            _vaultAddress = vaultAddress ?? throw new ArgumentNullException(nameof(vaultAddress));
        }

        public void AddSymbolConfig(ISymbolConfig symbolConfig)
        {
            _symbolConfigs[symbolConfig.ContractAddress] = symbolConfig;
        }

        public async Task<Result<BigInteger>> GetNativeBalanceAsync(
            bool forceUpdate = true,
            CancellationToken cancellationToken = default)
        {
            if (!forceUpdate)
                return _nativeBalance;

            var (balance, balanceError) = await _rpc.GetBalanceAsync(
                _fromAddress,
                BlockNumber.Pending,
                cancellationToken);

            if (balanceError != null)
                return balanceError;

            _nativeBalance = balance;
            return balance;
        }

        public async Task<Result<BigInteger>> GetTokenBalanceAsync(
            string tokenContractAddress,
            bool forceUpdate = true,
            CancellationToken cancellationToken = default)
        {
            if (!forceUpdate)
            {
                return _tokenBalances.TryGetValue(tokenContractAddress, out var balance)
                    ? balance
                    : BigInteger.Zero;
            }

            var vaultAddress = _fromAddress != _vaultAddress ? _vaultAddress : _fromAddress;

            var (tokenBalance, tokenBalanceError) = await _rpc.GetErc20TokenBalanceAsync(
                tokenContractAddress,
                vaultAddress,
                BlockNumber.Pending,
                cancellationToken);

            if (tokenBalanceError != null)
                return tokenBalanceError;

            _tokenBalances[tokenContractAddress] = tokenBalance;
            return tokenBalance;
        }

        public async Task<Result<(BigInteger tokenX, BigInteger tokenY)>> GetLobBalancesAsync(
            ISymbolConfig symbolConfig,
            bool forceUpdate = true,
            CancellationToken cancellationToken = default)
        {
            if (!forceUpdate)
            {
                return (
                    _lobBalancesTokenX.TryGetValue(symbolConfig.ContractAddress, out var balanceX)
                        ? balanceX
                        : BigInteger.Zero,
                    _lobBalancesTokenY.TryGetValue(symbolConfig.ContractAddress, out var balanceY)
                        ? balanceY
                        : BigInteger.Zero
                );
            }

            var getTraderBalance = new GetTraderBalance()
            {
                Address = _vaultAddress
            };

            var (hexResult, error) = await _rpc.CallAsync<string>(
                to: symbolConfig.ContractAddress,
                from: _fromAddress,
                input: getTraderBalance.CreateTransactionInput(symbolConfig.ContractAddress).Data,
                cancellationToken: cancellationToken);

            if (error != null)
                return error;

            var traderBalance = new GetTraderBalanceOutput().DecodeOutput(hexResult);

            _lobBalancesTokenX[symbolConfig.ContractAddress] = 
                traderBalance.TokenX * BigInteger.Pow(10, symbolConfig.ScallingFactorX);
            _lobBalancesTokenY[symbolConfig.ContractAddress] = 
                traderBalance.TokenY * BigInteger.Pow(10, symbolConfig.ScallingFactorY);

            return (
                _lobBalancesTokenX[symbolConfig.ContractAddress],
                _lobBalancesTokenY[symbolConfig.ContractAddress]
            );
        }

        public async Task<Result<Balance>> GetAvailableBalanceAsync(
            string tokenContractAddress,
            string lobContractAddress,
            bool forceUpdate = true,
            CancellationToken cancellationToken = default)
        {
            if (!_symbolConfigs.TryGetValue(lobContractAddress, out var symbolConfig)) {
                return new Error($"Symbol config not found for contract address {lobContractAddress}");
            }

            if (!forceUpdate)
            {
                var cachedTokenBalance = GetCachedTokenBalance(tokenContractAddress);

                var cachedLobBalance = symbolConfig.TokenX.ContractAddress == tokenContractAddress
                    ? _lobBalancesTokenX[lobContractAddress]
                    : _lobBalancesTokenY[lobContractAddress];

                return new Balance
                {
                    NativeBalance = _nativeBalance,
                    TokenBalance = cachedTokenBalance,
                    LobBalance = cachedLobBalance,
                    IsNative = symbolConfig.IsNative(tokenContractAddress)
                };
            }

            var (nativeBalance, nativeBalanceError) = await GetNativeBalanceAsync(
                forceUpdate,
                cancellationToken);

            if (nativeBalanceError != null)
                return nativeBalanceError;

            var (tokenBalance, tokenBalanceError) = await GetTokenBalanceAsync(
                tokenContractAddress,
                forceUpdate,
                cancellationToken);

            if (tokenBalanceError != null)
                return tokenBalanceError;

            var (lobBalances, lobBalanceError) = await GetLobBalancesAsync(
                symbolConfig,
                forceUpdate,
                cancellationToken);

            if (lobBalanceError != null)
                return lobBalanceError;

            var lobBalance = symbolConfig.TokenX.ContractAddress == tokenContractAddress
                ? lobBalances.tokenX
                : lobBalances.tokenY;

            return new Balance
            {
                NativeBalance = nativeBalance,
                TokenBalance = tokenBalance,
                LobBalance = lobBalance,
                IsNative = symbolConfig.IsNative(tokenContractAddress)
            };
        }

        private BigInteger GetCachedTokenBalance(string tokenContractAddress)
        {
            return _tokenBalances.TryGetValue(tokenContractAddress, out var balance)
                ? balance
                : BigInteger.Zero;
        }
    }
}
