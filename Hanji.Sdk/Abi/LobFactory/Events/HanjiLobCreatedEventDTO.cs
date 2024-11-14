using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;

namespace Hanji.Abi.LobFactory.Events
{
    [Event("HanjiLOBCreated")]
    public class HanjiLobCreatedEventDTO
    {
        [Parameter("address", "creator", 1, true)]
        public string Creator { get; set; } = default!;

        [Parameter("address", "hanjiLOB", 2)]
        public string HanjiLOB { get; set; } = default!;

        [Parameter("address", "tokenXAddress", 3)]
        public string TokenXAddress { get; set; } = default!;

        [Parameter("address", "tokenYAddress", 4)]
        public string TokenYAddress { get; set; } = default!;

        [Parameter("bool", "supports_native_eth", 5)]
        public bool SupportsNativeEth { get; set; }

        [Parameter("uint256", "scaling_token_x", 6)]
        public BigInteger ScalingTokenX { get; set; }

        [Parameter("uint256", "scaling_token_y", 7)]
        public BigInteger ScalingTokenY { get; set; }

        [Parameter("address", "administrator", 8)]
        public string Administrator { get; set; } = default!;

        [Parameter("address", "marketmaker", 9)]
        public string Marketmaker { get; set; } = default!;

        [Parameter("address", "pauser", 10)]
        public string Pauser { get; set; } = default!;

        [Parameter("bool", "should_invoke_on_trade", 11)]
        public bool ShouldInvokeOnTrade { get; set; }

        [Parameter("uint64", "admin_commission_rate", 12)]
        public ulong AdminCommissionRate { get; set; }

        [Parameter("uint64", "total_aggressive_commission_rate", 13)]
        public ulong TotalAggressiveCommissionRate { get; set; }

        [Parameter("uint64", "total_passive_commission_rate", 14)]
        public ulong TotalPassiveCommissionRate { get; set; }

        [Parameter("uint64", "passive_order_payout", 15)]
        public ulong PassiveOrderPayout { get; set; }
    }
}
