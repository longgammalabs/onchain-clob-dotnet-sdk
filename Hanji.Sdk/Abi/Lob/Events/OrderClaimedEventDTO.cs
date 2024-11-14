using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace Hanji.Abi.Lob.Events
{
    [Event("OrderClaimed")]
    public class OrderClaimedEventDTO : IEventDTO
    {
        public static string SignatureHash => EventExtensions
            .GetEventABI<OrderClaimedEventDTO>()
            .Sha3Signature;

        [Parameter("uint64", "order_id", 1)]
        public ulong OrderId { get; set; }
        [Parameter("uint128", "order_shares_remaining", 2)]
        public BigInteger OrderSharesRemaining { get; set; }
        [Parameter("uint128", "token_x_sent", 3)]
        public BigInteger TokenXSent { get; set; }
        [Parameter("uint128", "token_y_sent", 4)]
        public BigInteger TokenYSent { get; set; }
        [Parameter("uint128", "passive_payout", 5)]
        public BigInteger PassivePayout { get; set; }
        [Parameter("bool", "only_claim", 6)]
        public bool OnlyClaim { get; set; }
    }
}
