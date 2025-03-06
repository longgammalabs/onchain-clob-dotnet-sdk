using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.ABI.Model;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob.Events
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
        [Parameter("uint128", "quantity", 4)]
        public BigInteger Quantity { get; set; }
        [Parameter("uint72", "price", 5)]
        public BigInteger Price { get; set; }
        [Parameter("uint128", "passive_shares", 6)]
        public BigInteger PassiveShares { get; set; }
        [Parameter("uint128", "passive_fee", 7)]
        public BigInteger PassiveFee { get; set; }
        [Parameter("uint128", "aggressive_shares", 8)]
        public BigInteger AggressiveShares { get; set; }
        [Parameter("uint128", "aggressive_value", 9)]
        public BigInteger AggressiveValue { get; set; }
        [Parameter("uint128", "aggressive_fee", 10)]
        public BigInteger AggressiveFee { get; set; }
        [Parameter("bool", "market_only", 11)]
        public bool MarketOnly { get; set; }
        [Parameter("bool", "post_only", 12)]
        public bool PostOnly { get; set; }
    }
}
