using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace OnchainClob.Abi.Pyth
{
    [Event("PriceFeedUpdate")]
    public class PriceFeedUpdateEventDTO : IEventDTO
    {
        public static string SignatureHash => EventExtensions
            .GetEventABI<PriceFeedUpdateEventDTO>()
            .Sha3Signature;

        [Parameter("bytes32", "id", 1, true)]
        public byte[] Id { get; set; } = default!;

        [Parameter("uint64", "publishTime", 2, false)]
        public ulong PublishTime { get; set; }

        [Parameter("int64", "price", 3, false)]
        public long Price { get; set; }

        [Parameter("uint64", "conf", 4, false)]
        public ulong Conf { get; set; }
    }
}
