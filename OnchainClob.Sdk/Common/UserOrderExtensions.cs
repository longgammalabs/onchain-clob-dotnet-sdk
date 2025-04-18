﻿using System.Numerics;
using OnchainClob.Client.Models;
using OnchainClob.Trading;

namespace OnchainClob.Common
{
    public static class UserOrderExtensions
    {
        public static Order ToOrder(this UserOrder o, string symbol, int pricePrecision)
        {
            var qty = BigInteger.Parse(o.OrigSize);
            var leaveQty = BigInteger.Parse(o.Size);
            var claimedQty = BigInteger.Parse(o.Claimed);

            var executedQty = qty - leaveQty;
            var isUnclaimed = claimedQty < executedQty;

            var side = o.Side.ToLowerInvariant() switch
            {
                "bid" => Side.Buy,
                "ask" => Side.Sell,
                _ => throw new NotSupportedException($"Not supported order side: {o.Side}")
            };

            var status = o.Status.ToLowerInvariant() switch
            {
                "open" when leaveQty == qty => OrderStatus.Placed,
                "open" when leaveQty < qty => OrderStatus.PartiallyFilled,
                "filled" => OrderStatus.Filled,
                "cancelled" when isUnclaimed => OrderStatus.Canceled,
                "cancelled" => OrderStatus.CanceledAndClaimed,
                "claimed" when claimedQty < qty => OrderStatus.PartiallyFilledAndClaimed,
                "claimed" when claimedQty == qty => OrderStatus.FilledAndClaimed,
                "rejected" => OrderStatus.Rejected,
                _ => throw new NotSupportedException($"Unknown order status")
            };

            return new Order(
                OrderId: o.OrderId,
                Price: BigInteger.Parse(o.Price),
                Qty: qty,
                LeaveQty: leaveQty,
                ClaimedQty: claimedQty,
                Side: side,
                Symbol: symbol,
                Status: status,
                Type: OrderType.Return,
                Created: DateTimeOffset.FromUnixTimeMilliseconds(o.CreatedAt),
                LastChanged: DateTimeOffset.FromUnixTimeMilliseconds(o.LastTouched),
                TxnHash: o.TxnHash);
        }
    }
}
