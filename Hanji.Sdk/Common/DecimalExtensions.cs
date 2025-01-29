using System.Numerics;

namespace Hanji.Common
{
    public static class DecimalExtensions
    {
        public static (BigInteger numerator, BigInteger denominator) Fraction(this decimal d)
        {
            var bits = decimal.GetBits(d);
            var numerator = (1 - ((bits[3] >> 30) & 2)) *
                unchecked(((BigInteger)(uint)bits[2] << 64) |
                          ((BigInteger)(uint)bits[1] << 32) |
                           (BigInteger)(uint)bits[0]);
            var denominator = BigInteger.Pow(10, (bits[3] >> 16) & 0xff);
            return (numerator, denominator);
        }

        public static BigInteger Multiply(this decimal a, BigInteger b)
        {
            var (numerator, denominator) = a.Fraction();
            return b * numerator / denominator;
        }
    }
}
