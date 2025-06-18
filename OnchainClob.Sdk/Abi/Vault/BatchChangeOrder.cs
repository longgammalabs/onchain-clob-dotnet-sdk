using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Vault
{
    [Function("batchChangeOrder", typeof(BatchChangeOrderOutputDTO))]
    public class BatchChangeOrder : FunctionMessage
    {
        [Parameter("address", "lpManagerAddress", 1)]
        public string LpManagerAddress { get; set; } = default!;

        [Parameter("uint8", "lobId", 2)]
        public byte LobId { get; set; }

        [Parameter("uint64[]", "orderIds", 3)]
        public ulong[] OrderIds { get; set; } = default!;

        [Parameter("uint128[]", "quantities", 4)]
        public BigInteger[] Quantities { get; set; } = default!;

        [Parameter("uint72[]", "prices", 5)]
        public BigInteger[] Prices { get; set; } = default!;

        [Parameter("uint128", "maxCommissionPerOrder", 6)]
        public BigInteger MaxCommissionPerOrder { get; set; }

        [Parameter("bool", "postOnly", 7)]
        public bool PostOnly { get; set; }

        [Parameter("uint256", "expires", 8)]
        public BigInteger Expires { get; set; }

        [Parameter("bytes[]", "priceUpdateData", 9)]
        public byte[][] PriceUpdateData { get; set; } = default!;
    }

    [FunctionOutput]
    public class BatchChangeOrderOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint64[]", "newOrderIds", 1)]
        public List<ulong> NewOrderIds { get; set; } = default!;
    }
}
