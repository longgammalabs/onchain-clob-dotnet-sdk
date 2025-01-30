using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.LobFactory
{
    [Function("createOnchainCLOB", "address")]
    public class CreateOnchainCLOB : FunctionMessage
    {
        [Parameter("address", "tokenXAddress", 1)]
        public string TokenXAddress { get; set; } = default!;

        [Parameter("address", "tokenYAddress", 2)]
        public string TokenYAddress { get; set; } = default!;

        [Parameter("bool", "supports_native_eth", 3)]
        public bool SupportsNativeEth { get; set; }

        [Parameter("bool", "is_token_x_weth", 4)]
        public bool IsTokenXWeth { get; set; }

        [Parameter("uint256", "scaling_token_x", 5)]
        public BigInteger ScalingTokenX { get; set; }

        [Parameter("uint256", "scaling_token_y", 6)]
        public BigInteger ScalingTokenY { get; set; }

        [Parameter("address", "administrator", 7)]
        public string Administrator { get; set; } = default!;

        [Parameter("address", "marketmaker", 8)]
        public string Marketmaker { get; set; } = default!;

        [Parameter("address", "pauser", 9)]
        public string Pauser { get; set; } = default!;

        [Parameter("bool", "should_invoke_on_trade", 10)]
        public bool ShouldInvokeOnTrade { get; set; }

        [Parameter("uint64", "admin_commission_rate", 11)]
        public ulong AdminCommissionRate { get; set; }

        [Parameter("uint64", "total_aggressive_commission_rate", 12)]
        public ulong TotalAggressiveCommissionRate { get; set; }

        [Parameter("uint64", "total_passive_commission_rate", 13)]
        public ulong TotalPassiveCommissionRate { get; set; }

        [Parameter("uint64", "passive_order_payout", 14)]
        public ulong PassiveOrderPayout { get; set; }
    }
}
