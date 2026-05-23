using Shared.Kernel.Primitives;

namespace UrbanX.Payment.Domain.Errors;

public static class PaymentErrors
{
    public static readonly Error PaymentNotFound = new("Payment.NotFound", "Không tìm thấy payment.");
    public static readonly Error PaymentAlreadyExists = new("Payment.AlreadyExists", "Payment đã tồn tại cho đơn hàng này.");
    public static readonly Error InvalidStatusTransition = new("Payment.InvalidStatusTransition", "Trạng thái payment không hợp lệ cho thao tác này.");
    public static readonly Error ProviderNotFound = new("Payment.ProviderNotFound", "Không tìm thấy payment provider.");
    public static readonly Error RefundNotFound = new("Refund.NotFound", "Không tìm thấy refund.");
    public static readonly Error PaymentNotCompleted = new("Refund.PaymentNotCompleted", "Chỉ có thể hoàn tiền khi payment đã hoàn thành.");
    public static readonly Error UnsupportedPaymentMethod = new("Payment.UnsupportedMethod", "Phương thức thanh toán không được hỗ trợ.");
    public static readonly Error InvalidWebhookSignature = new("Payment.InvalidWebhookSignature", "Chữ ký webhook không hợp lệ.");
    public static readonly Error MomoGatewayFailed = new("Payment.MomoGatewayFailed", "MoMo gateway trả về lỗi khi tạo session.");
    public static readonly Error RefundFailed = new("Payment.RefundFailed", "Provider từ chối yêu cầu hoàn tiền.");
}
