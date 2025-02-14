using Incendium;
using OnchainClob.Client.Models;
using Revelium.Evm.Common;
using System.Text.Json;

namespace OnchainClob.Client
{
    public class RestApi(string url, HttpClient? httpClient = null)
    {
        private readonly string _url = url ?? throw new ArgumentNullException(nameof(url));
        private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

        public async Task<Result<List<UserOrder>>> GetActiveOrdersAsync(
            string userAddress,
            string marketId,
            bool includeFilled = true,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
        {

            try
            {
                var requestMessage = new HttpRequestMessage
                {
                    RequestUri = new Uri(Url.Combine(_url, $"/orders?" +
                        $"user={userAddress}&" +
                        $"status=open&" +
                        (includeFilled ? "status=filled&" : "") +
                        $"market={marketId}&" +
                        $"limit={limit}")),
                    Method = HttpMethod.Get
                };

                var response = await _httpClient
                    .SendAsync(requestMessage, cancellationToken);

                var responseContent = await response.Content
                    .ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, responseContent);

                if (responseContent == null)
                    return new Error(Errors.INVALID_RESPONSE, "Null active orders response received");

                var orders = JsonSerializer.Deserialize<List<UserOrder>>(responseContent);

                if (orders == null)
                    return new Error(Errors.INVALID_RESPONSE, "Invalid active orders response format");

                return orders;
            }
            catch (HttpRequestException ex)
            {
                return new Error(Errors.HTTP_REQUEST_ERROR, "Active orders request error", ex);
            }
            catch (Exception ex)
            {
                return new Error(Errors.INVALID_RESPONSE, "Invalid active orders response format", ex);
            }
        }

        public async Task<Result<List<Market>>> GetMarketsAsync(CancellationToken cancellationToken = default)
        {

            try
            {
                var requestMessage = new HttpRequestMessage
                {
                    RequestUri = new Uri(Url.Combine(_url, $"/markets")),
                    Method = HttpMethod.Get
                };

                var response = await _httpClient
                    .SendAsync(requestMessage, cancellationToken);

                var responseContent = await response.Content
                    .ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, responseContent);

                if (responseContent == null)
                    return new Error(Errors.INVALID_RESPONSE, "Null markets response received");

                var markets = JsonSerializer.Deserialize<List<Market>>(responseContent);

                if (markets == null)
                    return new Error(Errors.INVALID_RESPONSE, "Invalid markets response format");

                return markets;
            }
            catch (HttpRequestException ex)
            {
                return new Error(Errors.HTTP_REQUEST_ERROR, "Markets request error", ex);
            }
            catch (Exception ex)
            {
                return new Error(Errors.INVALID_RESPONSE, "Invalid markets response format", ex);
            }
        }
    }
}
