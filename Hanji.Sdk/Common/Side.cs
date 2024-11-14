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
    }
}
