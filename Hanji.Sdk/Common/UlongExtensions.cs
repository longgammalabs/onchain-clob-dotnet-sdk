namespace Hanji.Common
{
    public static class UlongExtensions
    {
        public static decimal FromHanjiPrice(this ulong price, int precision)
        {
            var multiplier = (decimal)Math.Pow(10, precision);
            return price / multiplier;
        }
    }
}
