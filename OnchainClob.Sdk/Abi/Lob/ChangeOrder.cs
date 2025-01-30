using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob
{
    [FunctionOutput]
    public class ChangeOrderOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint64", null, 1)]
        public ulong OrderId { get; set; }
    }

    [Function("changeOrder", typeof(ChangeOrderOutputDTO))]
    public class ChangeOrder : FunctionMessage
    {
        [Parameter("uint64", "old_order_id", 1)]
        public ulong OldOrderId { get; set; }
        [Parameter("uint128", "new_quantity", 2)]
        public BigInteger NewQuantity { get; set; }
        [Parameter("uint72", "new_price", 3)]
        public BigInteger NewPrice { get; set; }
        [Parameter("uint128", "max_commission", 4)]
        public BigInteger MaxCommission { get; set; }
        [Parameter("bool", "post_only", 5)]
        public bool PostOnly { get; set; }
        [Parameter("bool", "transfer_tokens", 6)]
        public bool TransferTokens { get; set; }
        [Parameter("uint256", "expires", 7)]
        public BigInteger Expires { get; set; }
    }
}
