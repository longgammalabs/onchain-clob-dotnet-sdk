using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob
{
    [FunctionOutput]
    public class BatchChangeOrderOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint64[]", "new_order_ids", 1)]
        public List<ulong> NewOrderIds { get; set; } = default!;
    }

    [Function("batchChangeOrder", typeof(BatchChangeOrderOutputDTO))]
    public class BatchChangeOrder : FunctionMessage
    {
        [Parameter("uint64[]", "order_ids", 1)]
        public List<ulong> OrderIds { get; set; } = default!;

        [Parameter("uint128[]", "quantities", 2)]
        public List<BigInteger> Quantities { get; set; } = default!;

        [Parameter("uint72[]", "prices", 3)]
        public List<BigInteger> Prices { get; set; } = default!;

        [Parameter("uint128", "max_commission_per_order", 4)]
        public BigInteger MaxCommissionPerOrder { get; set; }

        [Parameter("bool", "post_only", 5)]
        public bool PostOnly { get; set; }

        [Parameter("bool", "transfer_tokens", 6)]
        public bool TransferTokens { get; set; }

        [Parameter("uint256", "expires", 7)]
        public BigInteger Expires { get; set; }
    }
}
