using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace OnchainClob.Abi.Lob.Events
{
    [Event("ClaimableStatusChanged")]
    public class ClaimableStatusChangedDTO : IEventDTO
    {
        public static string SignatureHash => EventExtensions
            .GetEventABI<ClaimableStatusChangedDTO>()
            .Sha3Signature;

        [Parameter("address", "owner", 1, true)]
        public string Owner { get; set; } = default!;
        [Parameter("bool", "status", 2)]
        public bool Status { get; set; }
    }
}
