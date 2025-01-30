using Nethereum.ABI.FunctionEncoding.Attributes;

namespace OnchainClob.Abi.Lob.Events
{
    [Event("PauserChanged")]
    public class PauserChangedEventDTO : IEventDTO
    {
        [Parameter("address", "new_pauser", 1, false)]
        public string NewPauser { get; set; } = default!;

        [Parameter("address", "old_pauser", 2, false)]
        public string OldPauser { get; set; } = default!;
    }
}
