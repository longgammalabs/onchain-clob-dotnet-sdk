namespace Hanji.Common
{
    public static class DecimalExtensions
    {
        private const int PRICE_SIGNIFICANT_DIGITS = 6;

        public static ulong ToHanjiPrice(this decimal price, int precision)
        {
            var multiplier = (decimal)Math.Pow(10, precision);
            var longPrice = (ulong)(price * multiplier);
            return longPrice.ResetLowDigits(PRICE_SIGNIFICANT_DIGITS);
        }

        public static decimal FromHanjiPrice(this decimal price, int precision)
        {
            var multiplier = (decimal)Math.Pow(10, precision);
            return price / multiplier;
        }
    }
}
