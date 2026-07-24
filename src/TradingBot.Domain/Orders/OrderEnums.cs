namespace TradingBot.Domain.Orders;

public enum OrderSide
{
    Buy = 1,
    Sell = 2
}

public enum OrderType
{
    Market = 1,
    Limit = 2
}

public enum OrderStatus
{
    Draft = 1,
    RiskApproved = 2,
    Submitting = 3,
    Open = 4,
    PartiallyFilled = 5,
    CancelPending = 6,
    Filled = 7,
    Cancelled = 8,
    Rejected = 9,
    Unknown = 10
}
