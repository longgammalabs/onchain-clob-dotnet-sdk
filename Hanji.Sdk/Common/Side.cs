namespace Hanji.Common
{
    public enum Side
    {
        Buy,
        Sell
    }

    public static class SideExtensions
    {
        public static Side OppositeSide(this Side side) => side switch
        {
            Side.Buy => Side.Sell,
            Side.Sell => Side.Buy,
            _ => throw new NotImplementedException(),
        };

        public static Side GetSideFromOrderId(this ulong orderId) =>
            (orderId & 0x1) == 0x1 ? Side.Sell : Side.Buy;
    }
}
