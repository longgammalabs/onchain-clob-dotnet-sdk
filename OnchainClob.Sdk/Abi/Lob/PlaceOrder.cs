using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.ABI.Model;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob
{
    [Function("placeOrder", typeof(PlaceOrderOutputDTO))]
    public class PlaceOrder : FunctionMessage
    {
        [Parameter("bool", "isAsk", 1)]
        public bool IsAsk { get; set; }
        [Parameter("uint128", "quantity", 2)]
        public BigInteger Quantity { get; set; }
        [Parameter("uint72", "price", 3)]
        public BigInteger Price { get; set; }
        [Parameter("uint128", "max_commission", 4)]
        public BigInteger MaxCommission { get; set; }
        [Parameter("bool", "market_only", 5)]
        public bool MarketOnly { get; set; }
        [Parameter("bool", "post_only", 6)]
        public bool PostOnly { get; set; }
        [Parameter("bool", "transfer_executed_tokens", 7)]
        public bool TransferExecutedTokens { get; set; }
        [Parameter("uint256", "expires", 8)]
        public BigInteger Expires { get; set; }
    }

    [Function("placeOrder", typeof(PlaceOrderOutputDTO))]
    public class PlaceOrderWithPermit : FunctionMessage
    {
        [Parameter("bool", "isAsk", 1)]
        public bool IsAsk { get; set; }

        [Parameter("uint128", "quantity", 2)]
        public BigInteger Quantity { get; set; }

        [Parameter("uint72", "price", 3)]
        public BigInteger Price { get; set; }

        [Parameter("uint128", "max_commission", 4)]
        public BigInteger MaxCommission { get; set; }

        [Parameter("uint128", "amount_to_approve", 5)]
        public BigInteger AmountToApprove { get; set; }

        [Parameter("bool", "market_only", 6)]
        public bool MarketOnly { get; set; }

        [Parameter("bool", "post_only", 7)]
        public bool PostOnly { get; set; }

        [Parameter("bool", "transfer_executed_tokens", 8)]
        public bool TransferExecutedTokens { get; set; }

        [Parameter("uint256", "expires", 9)]
        public BigInteger Expires { get; set; }

        [Parameter("uint8", "v", 10)]
        public byte V { get; set; }

        [Parameter("bytes32", "r", 11)]
        public byte[] R { get; set; } = default!;

        [Parameter("bytes32", "s", 12)]
        public byte[] S { get; set; } = default!;
    }

    [FunctionOutput]
    public class PlaceOrderOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint64", "order_id", 1)]
        public ulong OrderId { get; set; }
        [Parameter("uint128", "executed_shares", 2)]
        public BigInteger ExecutedShares { get; set; }
        [Parameter("uint128", "executed_value", 3)]
        public BigInteger ExecutedValue { get; set; }
        [Parameter("uint128", "aggressive_fee", 4)]
        public BigInteger AggressiveFee { get; set; }
    }
}
