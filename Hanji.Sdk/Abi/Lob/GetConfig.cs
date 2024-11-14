using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace Hanji.Abi.Lob
{
    [FunctionOutput]
    public class GetConfigOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint256", "_scaling_factor_token_x", 1)]
        public BigInteger ScalingFactorTokenX { get; set; }

        [Parameter("uint256", "_scaling_factor_token_y", 2)]
        public BigInteger ScalingFactorTokenY { get; set; }

        [Parameter("address", "_token_x", 3)]
        public string TokenX { get; set; } = default!;

        [Parameter("address", "_token_y", 4)]
        public string TokenY { get; set; } = default!;

        [Parameter("bool", "_supports_native_eth", 5)]
        public bool SupportsNativeEth { get; set; }

        [Parameter("bool", "_is_token_x_weth", 6)]
        public bool IsTokenXWeth { get; set; }

        [Parameter("address", "_ask_trie", 7)]
        public string AskTrie { get; set; } = default!;

        [Parameter("address", "_bid_trie", 8)]
        public string BidTrie { get; set; } = default!;

        [Parameter("uint64", "_admin_commission_rate", 9)]
        public ulong AdminCommissionRate { get; set; }

        [Parameter("uint64", "_total_aggressive_commission_rate", 10)]
        public ulong TotalAggressiveCommissionRate { get; set; }

        [Parameter("uint64", "_total_passive_commission_rate", 11)]
        public ulong TotalPassiveCommissionRate { get; set; }

        [Parameter("uint64", "_passive_order_payout_rate", 12)]
        public ulong PassiveOrderPayoutRate { get; set; }

        [Parameter("bool", "_should_invoke_on_trade", 13)]
        public bool ShouldInvokeOnTrade { get; set; }
    }

    [Function("getConfig", typeof(GetConfigOutputDTO))]
    public class GetConfig : FunctionMessage
    {
    }
}
