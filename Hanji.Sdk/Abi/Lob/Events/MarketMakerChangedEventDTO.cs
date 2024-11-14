using Nethereum.ABI.FunctionEncoding.Attributes;

namespace Hanji.Abi.Lob.Events
{
    [Event("MarketMakerChanged")]
    public class MarketMakerChangedEventDTO : IEventDTO
    {
        [Parameter("address", "new_marketmaker", 1, false)]
        public string NewMarketMaker { get; set; } = default!;

        [Parameter("address", "old_marketmaker", 2, false)]
        public string OldMarketMaker { get; set; } = default!;
    }
}
