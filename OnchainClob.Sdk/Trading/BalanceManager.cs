﻿using Incendium;
using Nethereum.Contracts;
using OnchainClob.Abi.Lob;
using OnchainClob.Client.Configuration;
using OnchainClob.Common;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Rpc.Parameters;
using System.Collections.Concurrent;
using System.Numerics;
using Error = Incendium.Error;

namespace OnchainClob.Trading
{
    public readonly struct Balances {
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

    public class BalanceManager
    {
        private readonly RpcClient _rpc;
        private readonly string _fromAddress;
        private readonly string _vaultAddress;

        private readonly ConcurrentDictionary<string, BigInteger> _tokenBalances = [];
        private readonly ConcurrentDictionary<string, BigInteger> _lobBalancesTokenX = [];
        private readonly ConcurrentDictionary<string, BigInteger> _lobBalancesTokenY = [];
        private readonly ConcurrentDictionary<string, ISymbolConfig> _symbolConfigs = [];
        private readonly object _nativeBalanceSync = new();
        private BigInteger _nativeBalance;

        public BalanceManager(
            RpcClient rpc,
            string fromAddress,
            string vaultAddress,
            IEnumerable<ISymbolConfig> symbols)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _fromAddress = fromAddress ?? throw new ArgumentNullException(nameof(fromAddress));
            _vaultAddress = vaultAddress ?? throw new ArgumentNullException(nameof(vaultAddress));

            foreach (var symbolConfig in symbols)
                _symbolConfigs[symbolConfig.ContractAddress.ToLowerInvariant()] = symbolConfig;
        }

        public void AddSymbol(ISymbolConfig symbolConfig)
        {
            _symbolConfigs[symbolConfig.ContractAddress.ToLowerInvariant()] = symbolConfig;
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

            lock (_nativeBalanceSync)
            {
                _nativeBalance = balance;
            }

            return balance;
        }

        public async Task<Result<BigInteger>> GetTokenBalanceAsync(
            string tokenContractAddress,
            bool forceUpdate = true,
            CancellationToken cancellationToken = default)
        {
            if (!forceUpdate)
            {
                return _tokenBalances.TryGetValue(tokenContractAddress.ToLowerInvariant(), out var balance)
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

            _tokenBalances[tokenContractAddress.ToLowerInvariant()] = tokenBalance;
            return tokenBalance;
        }

        public async Task<Result<(BigInteger tokenX, BigInteger tokenY)>> GetLobBalancesAsync(
            ISymbolConfig symbolConfig,
            bool forceUpdate = true,
            CancellationToken cancellationToken = default)
        {
            var contractAddress = symbolConfig.ContractAddress.ToLowerInvariant();

            if (!forceUpdate)
            {
                return (
                    _lobBalancesTokenX.TryGetValue(contractAddress, out var balanceX)
                        ? balanceX
                        : BigInteger.Zero,
                    _lobBalancesTokenY.TryGetValue(contractAddress, out var balanceY)
                        ? balanceY
                        : BigInteger.Zero
                );
            }

            var getTraderBalance = new GetTraderBalance()
            {
                Address = _vaultAddress
            };

            var (hexResult, error) = await _rpc.CallAsync<string>(
                to: contractAddress,
                from: _fromAddress,
                input: getTraderBalance.CreateTransactionInput(contractAddress).Data,
                cancellationToken: cancellationToken);

            if (error != null)
                return error;

            var traderBalance = new GetTraderBalanceOutput().DecodeOutput(hexResult);

            var newBalanceTokenX = traderBalance.TokenX * BigInteger.Pow(10, symbolConfig.ScallingFactorX);
            var newBalanceTokenY = traderBalance.TokenY * BigInteger.Pow(10, symbolConfig.ScallingFactorY);

            _lobBalancesTokenX[contractAddress] = newBalanceTokenX;
            _lobBalancesTokenY[contractAddress] = newBalanceTokenY;

            return (newBalanceTokenX, newBalanceTokenY);
        }

        public async Task<Result<Balances>> GetAvailableBalancesAsync(
            string lobContractAddress,
            string? tokenContractAddress = null,
            bool forceUpdate = true,
            CancellationToken cancellationToken = default)
        {
            if (!_symbolConfigs.TryGetValue(lobContractAddress.ToLowerInvariant(), out var symbolConfig))
            {
                return new Error($"Symbol config not found for contract address {lobContractAddress}");
            }

            if (!forceUpdate)
            {
                var currentNativeBalance = BigInteger.Zero;

                lock (_nativeBalanceSync)
                {
                    currentNativeBalance = _nativeBalance;
                }

                return new Balances
                {
                    NativeBalance = currentNativeBalance,
                    TokenBalanceX = GetCachedTokenBalance(symbolConfig.TokenX.ContractAddress),
                    TokenBalanceY = GetCachedTokenBalance(symbolConfig.TokenY.ContractAddress),
                    LobBalanceX = _lobBalancesTokenX.TryGetValue(lobContractAddress.ToLowerInvariant(), out var balanceX)
                        ? balanceX
                        : BigInteger.Zero,
                    LobBalanceY = _lobBalancesTokenY.TryGetValue(lobContractAddress.ToLowerInvariant(), out var balanceY)
                        ? balanceY
                        : BigInteger.Zero,
                };
            }

            // TODO: Use RPC batching to get all balances in one call

            var (nativeBalance, nativeBalanceError) = await GetNativeBalanceAsync(
                forceUpdate,
                cancellationToken);

            if (nativeBalanceError != null)
                return nativeBalanceError;

            var tokenBalanceX = BigInteger.Zero;
            Error? tokenBalanceError;

            if (tokenContractAddress == null ||
                (tokenContractAddress != null && tokenContractAddress.Equals(symbolConfig.TokenX.ContractAddress, StringComparison.OrdinalIgnoreCase)))
            {
                (tokenBalanceX, tokenBalanceError) = await GetTokenBalanceAsync(
                    symbolConfig.TokenX.ContractAddress,
                    forceUpdate,
                    cancellationToken);

                if (tokenBalanceError != null)
                    return tokenBalanceError;
            }

            var tokenBalanceY = BigInteger.Zero;

            if (tokenContractAddress == null ||
                (tokenContractAddress != null && tokenContractAddress.Equals(symbolConfig.TokenY.ContractAddress, StringComparison.OrdinalIgnoreCase)))
            {
                (tokenBalanceY, tokenBalanceError) = await GetTokenBalanceAsync(
                    symbolConfig.TokenY.ContractAddress,
                    forceUpdate,
                    cancellationToken);

                if (tokenBalanceError != null)
                    return tokenBalanceError;
            }

            var (lobBalances, lobBalanceError) = await GetLobBalancesAsync(
                symbolConfig,
                forceUpdate,
                cancellationToken);

            if (lobBalanceError != null)
                return lobBalanceError;

            return new Balances
            {
                NativeBalance = nativeBalance,
                TokenBalanceX = tokenBalanceX,
                TokenBalanceY = tokenBalanceY,
                LobBalanceX = lobBalances.tokenX,
                LobBalanceY = lobBalances.tokenY
            };
        }

        private BigInteger GetCachedTokenBalance(string tokenContractAddress)
        {
            return _tokenBalances.TryGetValue(tokenContractAddress.ToLowerInvariant(), out var balance)
                ? balance
                : BigInteger.Zero;
        }
    }
}
