using System.Numerics;

namespace Hanji.Common
{
    public static class BigIntegerExtensions
    {
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

        public static long ResetLowDigits(this long value, int significantDigits) =>
            (long)new BigInteger(value).ResetLowDigits(significantDigits);

        public static ulong ResetLowDigits(this ulong value, int significantDigits) =>
            (ulong)new BigInteger(value).ResetLowDigits(significantDigits);
    }
}
