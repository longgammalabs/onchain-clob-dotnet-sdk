using OnchainClob.Client.Configuration;
using OnchainClob.Trading.Requests;

namespace OnchainClob.Common
{
    public static class ITraderRequestExtensions
    {
        public static IEnumerable<(ulong?, IEnumerable<ITraderRequest>)> SplitIntoSeveralBatches(
            this IEnumerable<ITraderRequest> requests,
            GasLimits? gasLimits)
        {
            var requestsList = requests.ToList();

            if (gasLimits == null)
            {
                yield return (null, requests);
                yield break;
            }

            var totalGasLimit = 0ul;
            var batch = new List<ITraderRequest>();

            foreach (var request in requestsList)
            {
                var gasLimit = request switch
                {
                    PlaceOrderRequest => gasLimits.PlaceOrder,
                    ClaimOrderRequest => gasLimits.ClaimOrder,
                    ChangeOrderRequest => gasLimits.ChangeOrder,
                    _ => 0ul
                };

                if (totalGasLimit + gasLimit > gasLimits.MaxPerTransaction)
                {
                    yield return (totalGasLimit, batch.ToList());

                    totalGasLimit = gasLimit;
                    batch.Clear();
                    batch.Add(request);
                }
                else
                {
                    totalGasLimit += gasLimit;
                    batch.Add(request);
                }
            }

            yield return (totalGasLimit, batch.ToList());
        }
    }
}
