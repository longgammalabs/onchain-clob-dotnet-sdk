using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using OnchainClob.Abi.Vault;
using OnchainClob.Client.Abstract;
using OnchainClob.Client.Parameters;
using Revelium.Evm.Abi.Erc20;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Transactions;
using Revelium.Evm.Transactions.Abstract;
using System.Numerics;

namespace OnchainClob.Client.Vault
{
    public class PlaceOrderParams : TransactionParams
    {
        public byte LobId { get; init; }
        public bool IsAsk { get; init; }
        public BigInteger Quantity { get; init; }
        public BigInteger Price { get; init; }
        public BigInteger MaxCommission { get; init; }
        public bool MarketOnly { get; init; }
        public bool PostOnly { get; init; }
        public BigInteger Expires { get; init; }
        public byte[][] PriceUpdateData { get; init; } = default!;
    }

    public class ClaimOrderParams : TransactionParams
    {
        public byte LobId { get; init; }
        public ulong OrderId { get; init; }
        public bool OnlyClaim { get; init; }
        public BigInteger Expires { get; init; }
    }

    public class BatchChangeOrderParams : TransactionParams
    {
        public string LpManagerAddress { get; init; } = default!;
        public byte LobId { get; init; }
        public List<ulong> OrderIds { get; init; } = default!;
        public List<BigInteger> Quantities { get; init; } = default!;
        public List<BigInteger> Prices { get; init; } = default!;
        public BigInteger MaxCommissionPerOrder { get; init; }
        public bool PostOnly { get; init; }
        public BigInteger Expires { get; init; }
        public byte[][] PriceUpdateData { get; init; } = default!;
    }

    public class Vault(IAsyncExecutor executor)
    {
        public IAsyncExecutor Executor { get; } = executor ?? throw new ArgumentNullException(nameof(executor));

        public Task ApproveAsync(
            ApproveParams @params,
            CancellationToken cancellationToken = default)
        {
            var approve = new Approve
            {
                Spender = @params.Spender,
                Value = @params.Amount,

                FromAddress = Executor.Signer.GetAddress(),
                AmountToSend = @params.Value,
                GasPrice = @params.GasPrice,
                Gas = @params.GasLimit,
                MaxFeePerGas = @params.MaxFeePerGas,
                MaxPriorityFeePerGas = @params.MaxPriorityFeePerGas,
                Nonce = @params.Nonce,
                TransactionType = @params.TransactionType
            };

            var txInput = approve.CreateTransactionInput(@params.ContractAddress, @params.ChainId ?? 0);

            var requestParams = new TransactionRequestParams
            {
                RequestId = @params.RequestId,
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public Task PlaceOrderAsync(
            PlaceOrderParams @params,
            CancellationToken cancellationToken = default)
        {
            var placeOrder = new PlaceOrder
            {
                LobId = @params.LobId,
                IsAsk = @params.IsAsk,
                Quantity = @params.Quantity,
                Price = @params.Price,
                MaxCommission = @params.MaxCommission,
                MarketOnly = @params.MarketOnly,
                PostOnly = @params.PostOnly,
                Expires = @params.Expires,
                PriceUpdateData = @params.PriceUpdateData,

                FromAddress = Executor.Signer.GetAddress(),
                AmountToSend = @params.Value,
                GasPrice = @params.GasPrice,
                Gas = @params.GasLimit,
                MaxFeePerGas = @params.MaxFeePerGas,
                MaxPriorityFeePerGas = @params.MaxPriorityFeePerGas,
                Nonce = @params.Nonce,
                TransactionType = @params.TransactionType
            };

            var txInput = placeOrder.CreateTransactionInput(@params.ContractAddress, @params.ChainId ?? 0);

            var requestParams = new TransactionRequestParams
            {
                RequestId = @params.RequestId,
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public Task ClaimOrderAsync(
            ClaimOrderParams @params,
            CancellationToken cancellationToken = default)
        {
            var claimOrder = new ClaimOrder
            {
                LobId = @params.LobId,
                OrderId = @params.OrderId,
                OnlyClaim = @params.OnlyClaim,
                Expires = @params.Expires,

                FromAddress = Executor.Signer.GetAddress(),
                GasPrice = @params.GasPrice,
                Gas = @params.GasLimit,
                MaxFeePerGas = @params.MaxFeePerGas,
                MaxPriorityFeePerGas = @params.MaxPriorityFeePerGas,
                Nonce = @params.Nonce,
                TransactionType = @params.TransactionType
            };

            var txInput = claimOrder.CreateTransactionInput(@params.ContractAddress, @params.ChainId ?? 0);

            var requestParams = new TransactionRequestParams
            {
                RequestId = @params.RequestId,
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public Task BatchChangeOrderAsync(
            BatchChangeOrderParams @params,
            CancellationToken cancellationToken = default)
        {
            var batchChangeOrder = new BatchChangeOrder
            {
                LpManagerAddress = @params.LpManagerAddress,
                LobId = @params.LobId,
                OrderIds = [.. @params.OrderIds],
                Quantities = [.. @params.Quantities],
                Prices = [.. @params.Prices],
                MaxCommissionPerOrder = @params.MaxCommissionPerOrder,
                PostOnly = @params.PostOnly,
                Expires = @params.Expires,
                PriceUpdateData = @params.PriceUpdateData,

                FromAddress = Executor.Signer.GetAddress(),
                AmountToSend = @params.Value,
                GasPrice = @params.GasPrice,
                Gas = @params.GasLimit,
                MaxFeePerGas = @params.MaxFeePerGas,
                MaxPriorityFeePerGas = @params.MaxPriorityFeePerGas,
                Nonce = @params.Nonce,
                TransactionType = @params.TransactionType
            };

            var txInput = batchChangeOrder.CreateTransactionInput(@params.ContractAddress, @params.ChainId ?? 0);

            var requestParams = new TransactionRequestParams
            {
                RequestId = @params.RequestId,
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        private TransactionRequestBase TxInputToTxRequest(TransactionInput transactionInput)
        {
            var transactionType = transactionInput.Type?.Value ?? 0;

            return (int)transactionType switch
            {
                0 => new TransactionLegacyRequest(transactionInput),
                2 => new Transaction1559Request(transactionInput),
                _ => throw new NotImplementedException()
            };
        }
    }
}
