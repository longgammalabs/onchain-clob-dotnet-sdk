using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Helper
{
    [FunctionOutput]
    public class AssembleOrderbookFromOrdersDTO : IFunctionOutputDTO
    {
        [Parameter("uint72[]", "array_prices", 1)]
        public List<BigInteger> Prices { get; set; } = default!;

        [Parameter("uint128[]", "array_shares", 2)]
        public List<BigInteger> Quantities { get; set; } = default!;
    }

    [Function("assembleOrderbookFromOrders", typeof(AssembleOrderbookFromOrdersDTO))]
    public class AssembleOrderbookFromOrders : FunctionMessage
    {
        [Parameter("address", "lob_address", 1)]
        public string LobAddress { get; set; } = default!;

        [Parameter("bool", "isAsk", 2)]
        public bool IsAsk { get; set; }

        [Parameter("uint24", "max_price_levels", 3)]
        public uint MaxPriceLevels { get; set; }

        public static string GetData(string contractAddress, string lobAddress, bool isAsk, uint maxPriceLevels)
        {
            var message = new AssembleOrderbookFromOrders
            {
                LobAddress = lobAddress,
                IsAsk = isAsk,
                MaxPriceLevels = maxPriceLevels
            };

            return message.CreateTransactionInput(contractAddress).Data;
        }
    }
}
