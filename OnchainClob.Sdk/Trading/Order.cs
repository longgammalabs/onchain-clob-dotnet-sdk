using OnchainClob.Common;
using System.Numerics;

namespace OnchainClob.Trading
{
    public enum OrderStatus
    {
        Pending,
        Mempooled,
        Placed,
        PartiallyFilled,
        PartiallyFilledAndClaimed,
        Filled,
        FilledAndClaimed,
        Canceled,
        CanceledAndClaimed,
        Rejected
    }

    public enum OrderType
    {
        Return,
        FillOrKill,
        ImmediateOrCancel
    }

    public record Order(
        string OrderId,
        BigInteger Price,
        BigInteger Qty,
        BigInteger LeaveQty,
        BigInteger ClaimedQty,
        Side Side,
        string Symbol,
        OrderStatus Status,
        OrderType Type,
        DateTimeOffset Created,
        DateTimeOffset LastChanged,
        string? TxnHash)
    {
        public bool IsUnconfirmed => Status == OrderStatus.Pending || Status == OrderStatus.Mempooled;
        public bool IsActive => Status == OrderStatus.Placed || Status == OrderStatus.PartiallyFilled;
        public bool IsUnclaimed => ClaimedQty < Qty - LeaveQty && Status != OrderStatus.Rejected;
    }
}
