using Revelium.Evm.Common;
using System.Numerics;

namespace OnchainClob.Common
{
    public static class PriceHelper
    {
        private const int PRICE_SIGNIFICANT_DIGITS = 6;

        public static BigInteger ResetLowDigits(this BigInteger value, int significantDigits)
        {
            int priceDigits = (int)BigInteger.Log10(value) + 1;

            if (priceDigits > significantDigits)
            {
                var extraDigitsMult = BigInteger.Pow(10, priceDigits - significantDigits);
                return value / extraDigitsMult * extraDigitsMult;
            }

            return value;
        }

        public static BigInteger ToNormalizePrice(this decimal price, int precision, out decimal rest)
        {
            var priceMultiplier = BigInteger.Pow(10, precision);
            var priceBigInt = price.Multiply(priceMultiplier);
            var result = priceBigInt.ResetLowDigits(PRICE_SIGNIFICANT_DIGITS);

            var restBigInt = priceBigInt - result;
            rest = restBigInt.Divide(priceMultiplier);

            return result;
        }

        public static decimal FromNormalizePrice(this BigInteger price, int precision)
        {
            var multiplier = BigInteger.Pow(10, precision);
            return price.Divide(multiplier);
        }
    }
}
