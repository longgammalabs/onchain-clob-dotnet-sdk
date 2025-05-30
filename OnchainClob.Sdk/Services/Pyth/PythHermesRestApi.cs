using Incendium;
using Revelium.Evm.Common;
using System.Text.Json;

namespace OnchainClob.Services.Pyth
{
    public class PythHermesRestApi(string baseUrl = PythHermesRestApi.BASE_URL, HttpClient? httpClient = null)
    {
        public const string BASE_URL = "https://hermes.pyth.network/";

        private readonly HttpClient _httpClient = httpClient ?? new HttpClient();
        private readonly string _baseUrl = baseUrl;

        public async Task<Result<byte[][]>> GetPriceUpdateDataAsync(
            IEnumerable<string> priceFeedIds,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_httpClient.Timeout);

                var idsQuery = string.Join("&", priceFeedIds.Select(id => $"ids[]={id}").ToList());

                using var response = await _httpClient.GetAsync(
                    Url.Combine(_baseUrl, $"v2/updates/price/latest?{idsQuery}"),
                    cts.Token);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, response.ReasonPhrase);

                var content = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(content);
                var data = json.RootElement
                    .GetProperty("binary")
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(e => Hex.FromString(e.GetString()!))
                    .ToArray();

                return data;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return new Error("Pyth Hermes API request timed out");
            }
            catch (Exception ex)
            {
                return new Error("Pyth Hermes API unknown error", ex);
            }
        }
    }
}
