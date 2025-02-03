using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Vault
{
    [Function("placeOrder", typeof(PlaceOrderOutputDTO))]
    public class PlaceOrder : FunctionMessage
    {
        [Parameter("uint8", "lobId", 1)]
        public byte LobId { get; set; }

        [Parameter("bool", "isAsk", 2)]
        public bool IsAsk { get; set; }

        [Parameter("uint128", "quantity", 3)]
        public BigInteger Quantity { get; set; }

        [Parameter("uint72", "price", 4)]
        public BigInteger Price { get; set; }

        [Parameter("uint128", "maxCommission", 5)]
        public BigInteger MaxCommission { get; set; }

        [Parameter("bool", "marketOnly", 6)]
        public bool MarketOnly { get; set; }

        [Parameter("bool", "postOnly", 7)]
        public bool PostOnly { get; set; }

        [Parameter("uint256", "expires", 8)]
        public BigInteger Expires { get; set; }

        [Parameter("bytes[]", "priceUpdateData", 9)]
        public byte[][] PriceUpdateData { get; set; } = default!;
    }

    [FunctionOutput]
    public class PlaceOrderOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint64", "orderId", 1)]
        public ulong OrderId { get; set; }
    }
}
