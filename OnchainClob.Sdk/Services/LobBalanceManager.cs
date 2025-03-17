using Incendium;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using OnchainClob.Abi.Lob;
using OnchainClob.Client.Configuration;
using OnchainClob.Common;
using Revelium.Evm.Abi.Erc20;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Rpc.Parameters;
using System.Collections.Concurrent;
using System.Numerics;

namespace OnchainClob.Services
{
    public readonly struct Balances
    {
        public BigInteger NativeBalance { get; init; }
        public BigInteger TokenBalanceX { get; init; }
        public BigInteger TokenBalanceY { get; init; }
        public BigInteger LobBalanceX { get; init; }
        public BigInteger LobBalanceY { get; init; }

        public BigInteger GetTokenBalanceBySide(Side side) =>
            side == Side.Sell ? TokenBalanceX : TokenBalanceY;

        public BigInteger GetLobBalanceBySide(Side side) =>
            side == Side.Sell ? LobBalanceX : LobBalanceY;
    }

    public class LobBalanceManagerOptions
    {
        public string FromAddress { get; init; } = default!;
        public string VaultAddress { get; init; } = default!;
    }

    public class LobBalanceManager(
        LobBalanceManagerOptions options,
        RpcClient rpc)
    {
        private readonly RpcClient _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        private readonly string _fromAddress = options.FromAddress ?? throw new ArgumentNullException(nameof(options.FromAddress));
        private readonly string _vaultAddress = options.VaultAddress ?? throw new ArgumentNullException(nameof(options.VaultAddress));

        private readonly ConcurrentDictionary<string, BigInteger> _tokenBalances = [];
        private readonly ConcurrentDictionary<string, BigInteger> _lobBalancesTokenX = [];
        private readonly ConcurrentDictionary<string, BigInteger> _lobBalancesTokenY = [];
        private readonly object _nativeBalanceSync = new();
        private BigInteger _nativeBalance;

        public BigInteger GetNativeBalance()
        {
            lock (_nativeBalanceSync)
            {
                return _nativeBalance;
            }
        }

        public BigInteger GetTokenBalance(string tokenContractAddress)
        {
            return GetCachedBalance(tokenContractAddress.ToLowerInvariant(), _tokenBalances);
        }

        public (BigInteger tokenX, BigInteger tokenY) GetLobBalances(ISymbolConfig symbolConfig)
        {
            var contractAddress = symbolConfig.ContractAddress.ToLowerInvariant();

            return (
                GetCachedBalance(contractAddress, _lobBalancesTokenX),
                GetCachedBalance(contractAddress, _lobBalancesTokenY)
            );
        }

        public Balances GetAvailableBalances(ISymbolConfig symbolConfig)
        {
            var nativeBalance = BigInteger.Zero;

            lock (_nativeBalanceSync)
            {
                nativeBalance = _nativeBalance;
            }

            var contractAddress = symbolConfig.ContractAddress.ToLowerInvariant();

            return new Balances
            {
                NativeBalance = nativeBalance,
                TokenBalanceX = GetCachedBalance(
                    symbolConfig.TokenX.ContractAddress.ToLowerInvariant(),
                    _tokenBalances),
                TokenBalanceY = GetCachedBalance(
                    symbolConfig.TokenY.ContractAddress.ToLowerInvariant(),
                    _tokenBalances),
                LobBalanceX = GetCachedBalance(contractAddress, _lobBalancesTokenX),
                LobBalanceY = GetCachedBalance(contractAddress, _lobBalancesTokenY)
            };
        }

        public async Task<Result<BigInteger>> UpdateNativeBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            var (balance, error) = await _rpc.GetBalanceAsync(
                _fromAddress,
                BlockNumber.Pending,
                cancellationToken);

            if (error != null)
                return error;

            lock (_nativeBalanceSync)
            {
                _nativeBalance = balance;
            }

            return balance;
        }

        public async Task<Result<Balances>> UpdateBalancesAsync(
            ISymbolConfig symbolConfig,
            CancellationToken cancellationToken = default)
        {
            var vaultAddress = _fromAddress != _vaultAddress ? _vaultAddress : _fromAddress;

            var requests = new List<RpcRequest>
            {
                // get native balance
                _rpc.CreateBalanceRequest(_fromAddress, BlockNumber.Pending) with { Id = 1 },

                // get token X balance
                _rpc.CreateCallRequest(
                    to: symbolConfig.TokenX.ContractAddress,
                    from: _fromAddress,
                    input: new BalanceOf { Account = vaultAddress }
                        .CreateTransactionInput(symbolConfig.TokenX.ContractAddress)
                        .Data,
                    block: BlockNumber.Pending) with { Id = 2 },

                // get token Y balance
                _rpc.CreateCallRequest(
                    to: symbolConfig.TokenY.ContractAddress,
                    from: _fromAddress,
                    input: new BalanceOf { Account = vaultAddress }
                        .CreateTransactionInput(symbolConfig.TokenY.ContractAddress)
                        .Data,
                    block: BlockNumber.Pending) with { Id = 3 },

                // get lob balances
                _rpc.CreateCallRequest(
                    to: symbolConfig.ContractAddress.ToLowerInvariant(),
                    from: _fromAddress,
                    input: new GetTraderBalance{ Address = _vaultAddress }
                        .CreateTransactionInput(symbolConfig.ContractAddress)
                        .Data,
                    block: BlockNumber.Pending) with { Id = 4 }
            };

            var (results, error) = await _rpc.SendBatchAsync<string>(requests, cancellationToken);

            if (error != null)
                return error;

            var (hexNativeBalance, nativeBalanceError) = results[0];

            if (nativeBalanceError != null)
                return nativeBalanceError;

            var (hexTokenBalanceX, tokenBalanceXError) = results[1];

            if (tokenBalanceXError != null)
                return tokenBalanceXError;

            var (hexTokenBalanceY, tokenBalanceYError) = results[2];

            if (tokenBalanceYError != null)
                return tokenBalanceYError;

            var (hexLobBalances, lobBalancesError) = results[3];

            if (lobBalancesError != null)
                return lobBalancesError;

            var nativeBalance = new HexBigInteger(hexNativeBalance).Value;
            var tokenBalanceX = new HexBigInteger(hexTokenBalanceX).Value;
            var tokenBalanceY = new HexBigInteger(hexTokenBalanceY).Value;
            var traderBalance = new GetTraderBalanceOutput().DecodeOutput(hexLobBalances);
            var lobBalanceTokenX = traderBalance.TokenX * BigInteger.Pow(10, symbolConfig.ScallingFactorX);
            var lobBalanceTokenY = traderBalance.TokenY * BigInteger.Pow(10, symbolConfig.ScallingFactorY);

            lock (_nativeBalanceSync)
            {
                _nativeBalance = nativeBalance;
            }

            _tokenBalances[symbolConfig.TokenX.ContractAddress.ToLowerInvariant()] = tokenBalanceX;
            _tokenBalances[symbolConfig.TokenY.ContractAddress.ToLowerInvariant()] = tokenBalanceY;
            _lobBalancesTokenX[symbolConfig.ContractAddress.ToLowerInvariant()] = lobBalanceTokenX;
            _lobBalancesTokenY[symbolConfig.ContractAddress.ToLowerInvariant()] = lobBalanceTokenY;

            return new Balances
            {
                NativeBalance = nativeBalance,
                TokenBalanceX = tokenBalanceX,
                TokenBalanceY = tokenBalanceY,
                LobBalanceX = lobBalanceTokenX,
                LobBalanceY = lobBalanceTokenY,
            };
        }

        private BigInteger GetCachedBalance(
            string tokenContractAddress,
            ConcurrentDictionary<string, BigInteger> cache)
        {
            return cache.TryGetValue(tokenContractAddress, out var balance)
                ? balance
                : BigInteger.Zero;
        }
    }
}
