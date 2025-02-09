using OnchainClob.Client.Models;
using Incendium;
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

            HttpResponseMessage response;

            try
            {
                response = await _httpClient
                    .SendAsync(requestMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                return new Error(Errors.HTTP_REQUEST_ERROR, "", ex);
            }

            var responseContent = await response.Content
                .ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, responseContent);

            if (responseContent == null)
                return new Error(Errors.INVALID_RESPONSE, "Null response received");

            try
            {
                var orders = JsonSerializer.Deserialize<List<UserOrder>>(responseContent);

                if (orders == null)
                    return new Error(Errors.INVALID_RESPONSE, "Invalid response format");

                return orders;
            }
            catch (Exception ex)
            {
                return new Error(Errors.INVALID_RESPONSE, "Invalid response format", ex);
            }
        }
    }
}
