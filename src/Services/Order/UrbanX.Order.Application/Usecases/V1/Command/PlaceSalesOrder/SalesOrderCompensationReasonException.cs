using Shared.Kernel.Primitives;

namespace UrbanX.Order.Application.Usecases.V1.Command.PlaceSalesOrder;

/// <summary>Carries error code for compensation outbox <c>Reason</c> when the handler returned a failed result.</summary>
internal sealed class SalesOrderCompensationReasonException : Exception
{
    public string ReasonCode { get; }

    public SalesOrderCompensationReasonException(Error error)
        : base(error.Message) =>
        ReasonCode = string.IsNullOrWhiteSpace(error.Code) ? "ORDER_UNKNOWN" : error.Code;
}
