using Incendium;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Hex.HexTypes;
using OnchainClob.Common;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Rpc.Models;
using Revelium.Evm.Rpc.Parameters;
using System.Numerics;

namespace OnchainClob.Services
{
    public class FeePerGasEventArgs
    {
        public BigInteger BaseFeePerGas { get; set; }
        public BigInteger MaxPriorityFeePerGas { get; set; }
    }

    public class GasStationOptions
    {
        public int UpdateIntervalMs { get; init; } = 10000;
    }

    public class GasStation(
        GasStationOptions options,
        RpcClient rpc,
        ILogger<GasStation>? logger = null) : IHostedService
    {
        public event EventHandler<FeePerGasEventArgs>? OnFeePerGasUpdated;

        private readonly RpcClient _rpc = rpc;
        private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(options.UpdateIntervalMs);
        private readonly ILogger<GasStation>? _logger = logger;
        private readonly object _startStopLock = new();
        private readonly object _lock = new();
        private BigInteger? _baseFeePerGas;
        private BigInteger? _maxPriorityFeePerGas;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_startStopLock)
            {
                if (_isRunning)
                    return Task.CompletedTask;

                _cts = new CancellationTokenSource();
                _isRunning = true;

                _ = Task.Run(async () => await DoWorkAsync(_cts.Token), _cts.Token);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            lock (_startStopLock)
            {
                if (!_isRunning)
                    return Task.CompletedTask;

                _cts?.Cancel();
                _cts = null;
                _isRunning = false;
            }

            return Task.CompletedTask;
        }

        public Result<BigInteger> GetBaseFeePerGas()
        {
            lock (_lock)
            {
                return _baseFeePerGas != null
                    ? Result<BigInteger>.Success(_baseFeePerGas.Value)
                    : Result<BigInteger>.Failure(new Error("BaseFeePerGas not set"));
            }
        }

        public Result<BigInteger> GetMaxPriorityFeePerGas()
        {
            lock (_lock)
            {
                return _maxPriorityFeePerGas != null
                    ? Result<BigInteger>.Success(_maxPriorityFeePerGas.Value)
                    : Result<BigInteger>.Failure(new Error("MaxPriorityFeePerGas not set"));
            }
        }

        private async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await UpdateAsync(cancellationToken);
                    await Task.Delay(_updateInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating gas prices");
            }
        }

        private async Task UpdateAsync(CancellationToken cancellationToken = default)
        {
            var (((maxPriorityFeePerGas, feeError), (block, blockError)), error) =
                await _rpc.SendBatchAsync<BigInteger, LightBlock>(
                    _rpc.CreateMaxPriorityFeePerGasRequest(),
                    _rpc.CreateBlockByNumberRequest(BlockNumber.Pending, includeTransactions: false),
                    cancellationToken);

            if (error != null)
            {
                _logger?.LogError(error, "Error getting gas prices");
                return;
            }

            if (feeError != null)
            {
                _logger?.LogError(feeError, "Error getting max priority fee per gas");
                return;
            }

            if (blockError != null)
            {
                _logger?.LogError(blockError, "Error getting block");
                return;
            }

            if (block == null)
            {
                _logger?.LogError("Block is null");
                return;
            }

            var baseFeePerGas = new HexBigInteger(block.BaseFeePerGas).Value;

            lock (_lock)
            {
                _baseFeePerGas = baseFeePerGas;
                _maxPriorityFeePerGas = maxPriorityFeePerGas;
            }

            OnFeePerGasUpdated?.Invoke(this, new FeePerGasEventArgs
            {
                BaseFeePerGas = baseFeePerGas,
                MaxPriorityFeePerGas = maxPriorityFeePerGas
            });
        }
    }
}
