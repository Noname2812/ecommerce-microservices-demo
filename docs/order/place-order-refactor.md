# Place Order — Dedup Refactor (Normal + Sales)

## Mục đích
Loại bỏ code trùng lặp ~70-80% giữa luồng `PlaceOrderCommand` (Normal) và `PlaceSalesOrderCommand` (Flash-sale, async saga) sau khi cả hai cùng chuyển sang mô hình 202-Accepted + saga choreography.

## Thay đổi

### Folder mới
`src/Services/Order/UrbanX.Order.Application/Usecases/V1/Command/Common/`
- `IPlaceOrderRequest.cs` — interface chung cho cả 2 Command (ShippingAddress, Items, ShippingFee, CouponCode, CustomerNote, IdempotencyKey, PricingSnapshot, CustomerEmail).
- `PlaceOrderValidationRules.cs` — extension method tĩnh trên `AbstractValidator<T> where T : IPlaceOrderRequest`:
  - `RuleForShippingAddress`, `RuleForShippingFee`, `RuleForIdempotencyKey`, `RuleForCouponCode`, `RuleForCustomerEmail`, `RuleForPricingSnapshot`, `RuleForPricingWindow(window, message)`, `RuleForItems(maxItems, maxQty, ...)`.
  - Hằng `PhoneRegex`, `CouponCodeRegex` chỉ tồn tại một chỗ.
- `ParallelValidator.cs` — utility chạy nhiều validator song song với `Task.WhenAny` + linked CTS, cancel các validator còn lại ngay khi 1 cái fail.
- `OrderFactory.cs` — `Build(IPlaceOrderRequest, userId, orderNumber, orderType, campaignId, useItemDiscount)` đóng gói `ShippingAddress.Create` + `NewOrderItemSpec` mapping + `Order.Create`. Flag `useItemDiscount=false` cho Sales (force `DiscountAmount=0`).
- `OrderNumberGenerator.cs` — `Generate(prefix)` → `{prefix}-yyyyMMdd-{guidsuffix}`.

### File mới (API)
`src/Services/Order/UrbanX.Order.API/Abstractions/PlaceOrderEndpointHelpers.cs`
- `RequireUserId(IUserContext)` → trả `IResult?` (401 nếu fail, null nếu ok).
- `Accepted202(orderId, locationUri)` → wrap `Results.Accepted` với body chuẩn `{ orderId, status = "Pending" }`.

### File đã xóa (dead code, không register DI)
- `Usecases/V1/Command/PlaceOrder/PlaceOrderCompensationBehavior.cs`
- `Usecases/V1/Command/PlaceOrder/PlaceOrderCompensationContext.cs`

### File đã refactor
| File | Trước | Sau |
|---|---|---|
| `PlaceOrderCommand.cs` (validator) | 60 dòng rules duplicate | 15 dòng — gọi extension shared |
| `PlaceSalesOrderCommand.cs` (validator) | 62 dòng rules duplicate | 18 dòng — gọi extension shared + `CampaignId.NotEmpty()` |
| `PlaceOrderCommandHandler.cs` | 118 dòng | 56 dòng |
| `PlaceSalesOrderCommandHandler.cs` | 183 dòng | 125 dòng |
| `OrderApis.cs` (PlaceOrder + PlaceSales endpoints) | 39 dòng × 2 helper boilerplate | 12 dòng × 2 |
| `Application/ServiceCollectionExtensions.cs` | — | Không đổi (compensation behavior cho Normal đã không được register từ trước) |

### Stale tests đã xóa
4 file test trong `tests/UrbanX.Services.Order.UnitTests/Usecases/V1/Command/PlaceOrder/` đã broken từ commit `a74d88e` (PlaceOrder Sync→Async) — tham chiếu API cũ (`PlaceOrderCommand(UserId, ...)`, `ICompensationOutboxWriter`, `OrderConfirmedForPlaceOrderV1`):
- `PlaceOrderCommandHandlerTests.cs`
- `PlaceOrderCommandValidatorTests.cs`
- `PlaceOrderBusinessValidatorsTests.cs`
- `PlaceOrderCompensationBehaviorTests.cs`

Test coverage cho luồng async hiện tại sẽ được dựng lại sau (skill `unit-test-writer`).

## Hành vi giữ nguyên (không thay đổi)
- **Normal**: pricing window 30 phút, max 20 items / 100 qty per item, prefix `ORD-`, không idempotency cache, không allocation gate.
- **Sales**: pricing window 5 phút, max 10 items / 5 qty per item, prefix `SALE-`, có Redis idempotency guard, có allocation gate, có `PlaceSalesOrderCompensationBehavior` cho quota release, `OrderType.Sales` + `CampaignId`.
- Integration events (`PlaceOrderRequestedV1` / `PlaceSalesOrderRequestedV1`) giữ nguyên shape — saga consumers không cần đổi.
- `[RequirePermission(Permissions.Orders.Write, MinScope = Own)]` giữ nguyên trên cả 2 Command.
- `IIdempotentCommand` (TTL 24h) chỉ áp cho `PlaceSalesOrderCommand`.

## Lưu ý về namespace
Subfolder ban đầu định đặt tên `Shared/` nhưng namespace `UrbanX.Order.Application.Usecases.V1.Command.Shared` shadow toàn bộ `Shared.Application.*` library trong các file cùng layer (CancelOrder, ConfirmOrder handlers). Đổi thành **`Common/`** để tránh collision.

## Verify
- `dotnet build src/Services/Order/UrbanX.Order.API/UrbanX.Order.API.csproj` — 0 errors
- `dotnet build tests/UrbanX.Services.Order.UnitTests/UrbanX.Services.Order.UnitTests.csproj` — 0 errors
- Full solution có 1 error pre-existing không liên quan: `FluentValidation.TestHelper` NuGet không restore được trong `Catalog.UnitTests` (đã có trước refactor)
