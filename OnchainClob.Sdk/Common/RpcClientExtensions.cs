using Incendium;
using Microsoft.Extensions.Logging;
using Revelium.Evm.Common;
using Revelium.Evm.Crypto.Abstract;
using Revelium.Evm.Rpc;
using Revelium.Evm.Transactions;
using Revelium.Evm.Transactions.Abstract;
using NonceManager = OnchainClob.Services.NonceManager;

namespace OnchainClob.Common
{
    public static class RpcClientExtensions
    {
        public const int TX_SEND_ERROR = 1;
        public const int TX_VERIFY_ERROR = 2;
        public const int INVALID_RESPONSE = 2;

        public static async Task<Result<string>> SignAndSendTransactionAsync(
           this RpcClient rpc,
           TransactionRequestBase tx,
           ISigner signer,
           NonceManager nonceManager,
           bool estimateGas = true,
           uint? estimateGasReserveInPercent = 0,
           ILogger? logger = null,
           CancellationToken ct = default)
        {
            using var nonceLock = await nonceManager.LockAsync(tx.From, ct);

            var (nonce, nonceError) = await nonceLock.GetNonceAsync(ct);

            if (nonceError != null)
                return nonceError;

            tx.Nonce = nonce;

            logger?.LogDebug("Transaction nonce is {@nonce}", tx.Nonce.ToString());

            if (estimateGas)
            {
                var (estimatedGas, estimateGasError) = tx switch
                {
                    Transaction1559Request eip1559Tx => await rpc.EstimateGasAsync(eip1559Tx),
                    TransactionLegacyRequest legacyTx => await rpc.EstimateGasAsync(legacyTx),
                    _ => throw new NotImplementedException(),
                };

                if (estimateGasError != null)
                {
                    nonceLock.Reset(tx.Nonce);
                    return estimateGasError;
                }

                tx.GasLimit = estimatedGas;

                if (estimateGasReserveInPercent != null && estimateGasReserveInPercent >= 0)
                    tx.GasLimit += tx.GasLimit / 100 * estimateGasReserveInPercent.Value;
            }

            signer.Sign(tx);

            if (!tx.Verify())
            {
                nonceLock.Reset(tx.Nonce);
                return new Error(TX_VERIFY_ERROR, "Can't verify transaction");
            }

            var (txId, txSendError) = await rpc.SendTransactionAsync(tx, ct);

            if (txSendError != null)
            {
                nonceLock.Reset(tx.Nonce);
                return new Error(TX_SEND_ERROR, "Transaction sending error", txSendError);
            }

            return txId;
        }
    }
}
