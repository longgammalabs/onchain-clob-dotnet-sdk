using System.Numerics;

namespace OnchainClob.Client.Parameters
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
}
