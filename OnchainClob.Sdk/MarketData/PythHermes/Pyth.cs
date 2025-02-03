using System.Numerics;
using Incendium;

public class Pyth(
    string[] priceFeedIds,
    long priceValidityPeriodSeconds,
    PythHermesApi pythHermesApi,
    BigInteger priceUpdateFeePerFeed)
{
    private readonly PythHermesApi _pythHermesApi = pythHermesApi;
    private long _lastPriceUpdateTime;

    public string[] PriceFeedIds { get; } = priceFeedIds;
    public long PriceValidityPeriodSeconds { get; } = priceValidityPeriodSeconds;
    public BigInteger PriceUpdateFeePerFeed { get; } = priceUpdateFeePerFeed;
    public BigInteger PriceUpdateFee { get; } = priceUpdateFeePerFeed * priceFeedIds.Length;

    public async Task<NullableResult<byte[][]>> GetPriceUpdateDataAsync()
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (currentTime - _lastPriceUpdateTime > PriceValidityPeriodSeconds)
        {
            var (priceUpdateData, error) = await _pythHermesApi.GetPriceUpdateDataAsync(PriceFeedIds);

            if (error != null)
                return error;

            _lastPriceUpdateTime = currentTime;
            return priceUpdateData;
        }

        // price update data is still valid
        return NullableResult<byte[][]>.Success(null);
    }
}
