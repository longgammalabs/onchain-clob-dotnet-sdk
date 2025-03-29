using Microsoft.Extensions.Hosting;
using OnchainClob.Client.Events;

namespace OnchainClob.Client.Abstract
{
    public interface IOnchainClobWsClient : IHostedService
    {
        bool IsConnected { get; }
        WsClientStatus StateStatus { get; }

        event EventHandler? Connected;
        event EventHandler? Disconnected;
        event EventHandler<WsClientStatus>? StateStatusChanged;
        event EventHandler<UserOrdersEventArgs>? UserOrdersUpdated;

        void SubscribeUserOrdersChannel(string userAddress, string marketId);
    }
}