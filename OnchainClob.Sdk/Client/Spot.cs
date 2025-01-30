using OnchainClob.Abi.Lob;
using OnchainClob.Trading.Abstract;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Revelium.Evm.Abi.Erc20;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Transactions;
using Revelium.Evm.Transactions.Abstract;
using System.Numerics;

namespace OnchainClob.Client
{
    public class TransactionParams
    {
        public string ContractAddress { get; init; } = default!;
        public BigInteger Value { get; init; }
        public BigInteger? GasPrice { get; init; }
        public BigInteger? GasLimit { get; init; }
        public BigInteger? MaxFeePerGas { get; init; }
        public BigInteger? MaxPriorityFeePerGas { get; init; }
        public BigInteger? Nonce { get; init; }
        public byte? TransactionType { get; init; }
        public BigInteger? ChainId { get; init; }
        public bool EstimateGas { get; init; }
        public uint? EstimateGasReserveInPercent { get; init; }
    }

    public class ApproveParams : TransactionParams
    {
        public string Spender { get; init; } = default!;
        public BigInteger Amount { get; init; }
    }

    public class PlaceOrderParams : TransactionParams
    {
        public bool IsAsk { get; init; }
        public BigInteger Quantity { get; init; }
        public BigInteger Price { get; init; }
        public BigInteger MaxCommission { get; init; }
        public bool MarketOnly { get; init; }
        public bool PostOnly { get; init; }
        public bool TransferExecutedTokens { get; init; }
        public BigInteger Expires { get; init; }
    }

    public class ClaimOrderParams : TransactionParams
    {
        public ulong OrderId { get; init; }
        public bool OnlyClaim { get; init; }
        public bool TransferTokens { get; init; }
        public BigInteger Expires { get; init; }
    }

    public class ChangeOrderParams : TransactionParams
    {
        public ulong OldOrderId { get; init; }
        public BigInteger NewQuantity { get; init; }
        public BigInteger NewPrice { get; init; }
        public BigInteger MaxCommission { get; init; }
        public bool PostOnly { get; init; }
        public bool TransferTokens { get; init; }
        public BigInteger Expires { get; init; }
    }

    public class BatchClaimParams : TransactionParams
    {
        public List<string> Addresses { get; init; } = default!;
        public List<ulong> OrderIds { get; init; } = default!;
        public bool OnlyClaim { get; init; }
        public BigInteger Expires { get; init; }
    }

    public class BatchChangeOrderParams : TransactionParams
    {
        public List<ulong> OrderIds { get; init; } = default!;
        public List<BigInteger> Quantities { get; init; } = default!;
        public List<BigInteger> Prices { get; init; } = default!;
        public BigInteger MaxCommissionPerOrder { get; init; }
        public bool PostOnly { get; init; }
        public bool TransferTokens { get; init; }
        public BigInteger Expires { get; init; }
    }

    public class Spot(IExecutor executor)
    {
        public IExecutor Executor { get; } = executor ?? throw new ArgumentNullException(nameof(executor));

        public async Task<string> ApproveAsync(
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
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return await Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public async Task<string> PlaceOrderAsync(
            PlaceOrderParams @params,
            CancellationToken cancellationToken = default)
        {
            var placeOrder = new PlaceOrder
            {
                IsAsk = @params.IsAsk,
                Quantity = @params.Quantity,
                Price = @params.Price,
                MaxCommission = @params.MaxCommission,
                MarketOnly = @params.MarketOnly,
                PostOnly = @params.PostOnly,
                TransferExecutedTokens = @params.TransferExecutedTokens,
                Expires = @params.Expires,

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
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return await Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public async Task<string> ClaimOrderAsync(
            ClaimOrderParams @params,
            CancellationToken cancellationToken = default)
        {
            var claimOrder = new ClaimOrder
            {
                OrderId = @params.OrderId,
                OnlyClaim = @params.OnlyClaim,
                TransferTokens = @params.TransferTokens,
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
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return await Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public async Task<string> ChangeOrderAsync(
            ChangeOrderParams @params,
            CancellationToken cancellationToken = default)
        {
            var changeOrder = new ChangeOrder
            {
                OldOrderId = @params.OldOrderId,
                NewQuantity = @params.NewQuantity,
                NewPrice = @params.NewPrice,
                MaxCommission = @params.MaxCommission,
                PostOnly = @params.PostOnly,
                TransferTokens = @params.TransferTokens,
                Expires = @params.Expires,

                FromAddress = Executor.Signer.GetAddress(),
                AmountToSend = @params.Value,
                GasPrice = @params.GasPrice,
                Gas = @params.GasLimit,
                MaxFeePerGas = @params.MaxFeePerGas,
                MaxPriorityFeePerGas = @params.MaxPriorityFeePerGas,
                Nonce = @params.Nonce,
                TransactionType = @params.TransactionType
            };

            var txInput = changeOrder.CreateTransactionInput(@params.ContractAddress, @params.ChainId ?? 0);

            var requestParams = new TransactionRequestParams
            {
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return await Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public async Task<string> BatchClaimOrderAsync(
            BatchClaimParams @params,
            CancellationToken cancellationToken = default)
        {
            var batchClaim = new BatchClaim
            {
                Addresses = @params.Addresses,
                OrderIds = @params.OrderIds,
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

            var txInput = batchClaim.CreateTransactionInput(@params.ContractAddress, @params.ChainId ?? 0);

            var requestParams = new TransactionRequestParams
            {
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return await Executor.ExecuteAsync(
                requestParams,
                cancellationToken);
        }

        public async Task<string> BatchChangeOrderAsync(
            BatchChangeOrderParams @params,
            CancellationToken cancellationToken = default)
        {
            var batchChangeOrder = new BatchChangeOrder
            {
                OrderIds = @params.OrderIds,
                Quantities = @params.Quantities,
                Prices = @params.Prices,
                MaxCommissionPerOrder = @params.MaxCommissionPerOrder,
                PostOnly = @params.PostOnly,
                TransferTokens = @params.TransferTokens,
                Expires = @params.Expires,

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
                Tx = TxInputToTxRequest(txInput),
                EstimateGas = @params.EstimateGas,
                EstimateGasReserveInPercent = @params.EstimateGasReserveInPercent,
            };

            return await Executor.ExecuteAsync(
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
