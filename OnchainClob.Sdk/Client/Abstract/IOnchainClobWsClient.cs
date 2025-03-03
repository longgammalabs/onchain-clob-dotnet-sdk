using OnchainClob.Client.Events;

namespace OnchainClob.Client.Abstract
{
    public interface IOnchainClobWsClient
    {
        bool IsConnected { get; }
        WsClientStatus StateStatus { get; }

        event EventHandler? Connected;
        event EventHandler? Disconnected;
        event EventHandler<WsClientStatus>? StateStatusChanged;
        event EventHandler<UserOrdersEventArgs>? UserOrdersUpdated;

        Task StartAsync();
        Task StopAsync();
        void SubscribeUserOrdersChannel(string userAddress, string marketId);
    }
}