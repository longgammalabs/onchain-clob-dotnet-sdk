﻿using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using OnchainClob.Abi.Lob;
using OnchainClob.Client.Abstract;
using OnchainClob.Client.Parameters;
using Revelium.Evm.Abi.Erc20;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Transactions;
using Revelium.Evm.Transactions.Abstract;
using System.Numerics;

namespace OnchainClob.Client.Lob
{
    public class DepositTokensParams : TransactionParams
    {
        public BigInteger TokenXAmount { get; init; }
        public BigInteger TokenYAmount { get; init; }
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

    public class Lob(IAsyncExecutor executor)
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

            var txInput = approve.CreateTransactionInput(
                @params.ContractAddress,
                @params.ChainId ?? 0);

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

        public Task DepositTokensAsync(
            DepositTokensParams @params,
            CancellationToken cancellationToken = default)
        {
            var depositTokens = new DepositTokens
            {
                TokenXAmount = @params.TokenXAmount,
                TokenYAmount = @params.TokenYAmount,

                FromAddress = Executor.Signer.GetAddress(),
                AmountToSend = @params.Value,
                GasPrice = @params.GasPrice,
                Gas = @params.GasLimit,
                MaxFeePerGas = @params.MaxFeePerGas,
                MaxPriorityFeePerGas = @params.MaxPriorityFeePerGas,
                Nonce = @params.Nonce,
                TransactionType = @params.TransactionType
            };

            var txInput = depositTokens.CreateTransactionInput(
                @params.ContractAddress,
                @params.ChainId ?? 0);

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

            var txInput = placeOrder.CreateTransactionInput(
                @params.ContractAddress,
                @params.ChainId ?? 0);

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

            var txInput = claimOrder.CreateTransactionInput(
                @params.ContractAddress,
                @params.ChainId ?? 0);

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

        public Task ChangeOrderAsync(
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

            var txInput = changeOrder.CreateTransactionInput(
                @params.ContractAddress,
                @params.ChainId ?? 0);

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

        public Task BatchClaimOrderAsync(
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

            var txInput = batchClaim.CreateTransactionInput(
                @params.ContractAddress,
                @params.ChainId ?? 0);

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

            var txInput = batchChangeOrder.CreateTransactionInput(
                @params.ContractAddress,
                @params.ChainId ?? 0);

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
