using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.ABI.Model;
using Nethereum.Contracts;
using System.Numerics;

namespace Hanji.Abi.Lob.Events
{
    [Event("OrderPlaced")]
    public class OrderPlacedEventDTO : IEventDTO
    {
        public static string SignatureHash => EventExtensions
            .GetEventABI<OrderPlacedEventDTO>()
            .Sha3Signature;

        [Parameter("address", "owner", 1, true)]
        public string Owner { get; set; } = default!;
        [Parameter("uint64", "order_id", 2)]
        public ulong OrderId { get; set; }
        [Parameter("bool", "isAsk", 3, true)]
        public bool IsAsk { get; set; }
        [Parameter("uint72", "price", 4)]
        public BigInteger Price { get; set; }
        [Parameter("uint128", "passive_shares", 5)]
        public BigInteger PassiveShares { get; set; }
        [Parameter("uint128", "passive_fee", 6)]
        public BigInteger PassiveFee { get; set; }
        [Parameter("uint128", "aggressive_shares", 7)]
        public BigInteger AggressiveShares { get; set; }
        [Parameter("uint128", "aggressive_value", 8)]
        public BigInteger AggressiveValue { get; set; }
        [Parameter("uint128", "aggressive_fee", 9)]
        public BigInteger AggressiveFee { get; set; }
        [Parameter("bool", "market_only", 10)]
        public bool MarketOnly { get; set; }
        [Parameter("bool", "post_only", 11)]
        public bool PostOnly { get; set; }
    }
}
