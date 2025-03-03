namespace OnchainClob.Client
{
    public class WsClientOptions
    {
        public int? ReconnectionTimeoutInSec { get; init; }
        public int? ErrorReconnectionTimeoutInSec { get; init; }
    }
}
