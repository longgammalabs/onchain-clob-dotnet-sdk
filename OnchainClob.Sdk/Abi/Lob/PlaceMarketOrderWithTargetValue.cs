using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob
{
    [FunctionOutput]
    public class PlaceMarketOrderWithTargetValueOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint128", "executed_shares", 1)]
        public BigInteger ExecutedShares { get; set; }

        [Parameter("uint128", "executed_value", 2)]
        public BigInteger ExecutedValue { get; set; }

        [Parameter("uint128", "aggressive_fee", 3)]
        public BigInteger AggressiveFee { get; set; }
    }

    [Function("placeMarketOrderWithTargetValue", typeof(PlaceMarketOrderWithTargetValueOutputDTO))]
    public class PlaceMarketOrderWithTargetValue : FunctionMessage
    {
        [Parameter("bool", "isAsk", 1)]
        public bool IsAsk { get; set; }

        [Parameter("uint128", "target_token_y_value", 2)]
        public BigInteger TargetTokenYValue { get; set; }

        [Parameter("uint72", "price", 3)]
        public BigInteger Price { get; set; }

        [Parameter("uint128", "max_commission", 4)]
        public BigInteger MaxCommission { get; set; }

        [Parameter("bool", "transfer_executed_tokens", 5)]
        public bool TransferExecutedTokens { get; set; }

        [Parameter("uint256", "expires", 6)]
        public BigInteger Expires { get; set; }
    }

    [Function("placeMarketOrderWithTargetValue", typeof(PlaceMarketOrderWithTargetValueOutputDTO))]
    public class PlaceMarketOrderWithTargetValueWithPermit : FunctionMessage
    {
        [Parameter("bool", "isAsk", 1)]
        public bool IsAsk { get; set; }

        [Parameter("uint128", "target_token_y_value", 2)]
        public BigInteger TargetTokenYValue { get; set; }

        [Parameter("uint72", "price", 3)]
        public BigInteger Price { get; set; }

        [Parameter("uint128", "max_commission", 4)]
        public BigInteger MaxCommission { get; set; }

        [Parameter("uint128", "amount_to_approve", 5)]
        public BigInteger AmountToApprove { get; set; }

        [Parameter("bool", "transfer_executed_tokens", 6)]
        public bool TransferExecutedTokens { get; set; }

        [Parameter("uint256", "expires", 7)]
        public BigInteger Expires { get; set; }

        [Parameter("uint8", "v", 8)]
        public byte V { get; set; }

        [Parameter("bytes32", "r", 9)]
        public byte[] R { get; set; } = default!;

        [Parameter("bytes32", "s", 10)]
        public byte[] S { get; set; } = default!;
    }
}
