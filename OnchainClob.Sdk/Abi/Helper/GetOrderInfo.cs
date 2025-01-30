using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Helper
{
    [FunctionOutput]
    public class GetOrderInfoOutputDTO : IFunctionOutputDTO
    {
        [Parameter("bool", "isAsk", 1)]
        public bool IsAsk { get; set; }

        [Parameter("uint72", "price", 2)]
        public ulong Price { get; set; }

        [Parameter("uint128", "total_shares", 3)]
        public BigInteger TotalShares { get; set; }

        [Parameter("uint128", "remain_shares", 4)]
        public BigInteger RemainShares { get; set; }

        [Parameter("uint128", "payout_amount", 5)]
        public BigInteger PayoutAmount { get; set; }

        [Parameter("uint128", "total_fee", 6)]
        public BigInteger TotalFee { get; set; }

        [Parameter("uint128", "current_execution_fee", 7)]
        public BigInteger CurrentExecutionFee { get; set; }
    }

    [Function("getOrderInfo", typeof(GetOrderInfoOutputDTO))]
    public class GetOrderInfoFunction : FunctionMessage
    {
        [Parameter("address", "lob_address", 1)]
        public string LobAddress { get; set; } = default!;
        [Parameter("uint64", "order_id", 2)]
        public ulong OrderId { get; set; }
    }
}
