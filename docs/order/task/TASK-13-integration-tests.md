# TASK-13 — Integration Tests (E2E + Race + Idempotency)

**Team:** QA + Order · **Effort:** L (2d) · **Depends:** All previous tasks
**Branch:** `feature/order-refactor/TASK-13-integration-tests`

## Mục đích

Verify toàn bộ refactor hoạt động end-to-end qua Aspire test host: happy path, timeout, cancel, race conditions, idempotency.

## Setup

### Test project
Reuse `tests/UrbanX.Services.Catalog.IntegrationTests/` pattern hoặc tạo mới:
`tests/UrbanX.Services.Order.IntegrationTests/UrbanX.Services.Order.IntegrationTests.csproj`

- `WebApplicationFactory<Program>` cho Order.API
- Test DB: Postgres testcontainer hoặc Aspire test host
- Mock IUserContext qua headers (Trust-Gateway pattern): `X-User-Id`, `X-User-Roles`

### Tooling
- xUnit 2.9.3, Moq
- `MassTransit.TestFramework` cho saga test (`ITestHarness`)
- `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers.RabbitMq`

## Test Cases

### 1. E2E Normal Order — Happy Path

```csharp
[Fact]
public async Task PlaceOrder_HappyPath_EndsConfirmed()
{
    // Arrange: seed Catalog stub with 2 variants
    // Act
    var response = await _client.PostAsJsonAsync("/api/v1/order/orders", _validPayload);

    // Assert: 202 with ticketId
    response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    var ticket = await response.Content.ReadFromJsonAsync<TicketResponse>();
    ticket.TicketId.ShouldNotBe(Guid.Empty);

    // Poll until PENDING_PAYMENT
    var status = await PollUntilAsync(ticket.TicketId,
        s => s.Status == "PENDING_PAYMENT", maxWaitSec: 10);
    status.PaymentUrl.ShouldNotBeNull();
    status.OrderId.ShouldBe(ticket.TicketId);

    // Simulate payment callback
    await _testHarness.Bus.Publish(new PaymentSessionCompletedV1
    {
        OrderId = ticket.TicketId,
        PaymentSessionId = "test-session"
    });

    // Poll until CONFIRMED
    var finalStatus = await PollUntilAsync(ticket.TicketId,
        s => s.Status == "CONFIRMED", maxWaitSec: 10);

    // Verify DB
    var order = await _dbContext.Orders.FindAsync(ticket.TicketId);
    order.Status.ShouldBe("CONFIRMED");
    order.PaymentStatus.ShouldBe("Paid");

    // Verify Inventory deduct
    var item = await _inventoryDb.InventoryItems
        .FirstAsync(i => i.VariantId == _validPayload.Items[0].VariantId);
    item.QuantityOnHand.ShouldBe(_initialQuantity - _validPayload.Items[0].Quantity);

    // Verify OrderConfirmedV1 published
    (await _testHarness.Published.Any<OrderConfirmedV1>()).ShouldBeTrue();

    // Verify Redis pending slot decrement
    var slotCount = await _redis.GetCountAsync($"pending-orders:{userId}");
    slotCount.ShouldBe(0);
}
```

### 2. E2E Timeout

```csharp
[Fact]
public async Task PlaceOrder_PaymentTimeout_EndsCancelled()
{
    // Arrange: set OrderPayment:NormalOrderExpiryMinutes=0.05 (3s) via test config
    // Act
    var ticketId = await PlaceOrderAsync(_validPayload);

    // Wait > expiry
    await Task.Delay(TimeSpan.FromSeconds(5));

    // Verify
    var status = await GetTicketStatusAsync(ticketId);
    status.Status.ShouldBe("CANCELLED");
    status.CancelledReason.ShouldContain("Payment expired");

    var item = await GetInventoryItemAsync(_validPayload.Items[0].VariantId);
    item.QuantityReserved.ShouldBe(0);  // released
    item.QuantityOnHand.ShouldBe(_initialQuantity);  // not deducted

    (await _testHarness.Published.Any<OrderCancelledV1>()).ShouldBeTrue();
}
```

### 3. E2E Catalog Validation Fail

```csharp
[Fact]
public async Task PlaceOrder_VariantNotFound_EndsCancelled()
{
    // Arrange: variantId không tồn tại trong Catalog stub
    var payload = _validPayload with { Items = [new OrderItemDto(Guid.NewGuid(), 1, 100m)] };

    // Act
    var ticketId = await PlaceOrderAsync(payload);
    var status = await PollUntilAsync(ticketId, s => s.Status == "CANCELLED");

    // Verify
    status.CancelledReason.ShouldBe("VARIANT_VALIDATION_FAILED");

    // No Order in DB
    var order = await _dbContext.Orders.FindAsync(ticketId);
    order.ShouldBeNull();

    // Slot released
    var slot = await _redis.GetAsync($"pending-orders:{userId}");
    slot.ShouldBe("0");
}
```

### 4. E2E Catalog Circuit Breaker Open

```csharp
[Fact]
public async Task PlaceOrder_CatalogDown_CircuitBreakerOpens()
{
    // Arrange: Catalog stub trả 503 cho mọi request
    _catalogStub.Setup(...).Returns(HttpStatusCode.ServiceUnavailable);

    // Act: gửi 15 requests
    var ticketIds = await Task.WhenAll(Enumerable.Range(1, 15)
        .Select(_ => PlaceOrderAsync(_validPayload)));

    // Assert: từ request thứ ~10, CB open → fail-fast
    var statuses = await Task.WhenAll(ticketIds.Select(GetTicketStatusAsync));
    statuses.Count(s => s.Status == "CANCELLED").ShouldBeGreaterThan(0);
    statuses.Last().CancelledReason.ShouldBe("CATALOG_UNAVAILABLE");
}
```

### 5. E2E Pending Limit

```csharp
[Fact]
public async Task PlaceOrder_ExceedPendingLimit_Returns429()
{
    // PlaceOrder 1 OK
    var ticket1 = await PlaceOrderAsync(_validPayload);

    // PlaceOrder 2 — đang khi 1 chưa xong
    var response2 = await _client.PostAsJsonAsync("/api/v1/order/orders", _validPayload);
    response2.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

    var error = await response2.Content.ReadFromJsonAsync<ProblemDetails>();
    error.Title.ShouldContain("TooManyPending");
}
```

### 6. E2E Cancel sau Reserve

```csharp
[Fact]
public async Task CancelOrder_AfterReserve_PublishesReleaseEvents()
{
    var ticketId = await PlaceOrderAsync(_validPayload);
    await PollUntilAsync(ticketId, s => s.Status == "PENDING_PAYMENT");

    // Act
    var response = await _client.PostAsJsonAsync(
        $"/api/v1/order/orders/{ticketId}/cancel",
        new { reason = "User changed mind" });

    response.IsSuccessStatusCode.ShouldBeTrue();

    // Verify
    var order = await _dbContext.Orders.FindAsync(ticketId);
    order.Status.ShouldBe("CANCELLED");

    (await _testHarness.Published.Any<OrderCancelledV1>()).ShouldBeTrue();
    (await _testHarness.Published.Any<InventoryReleaseRequestedV1>()).ShouldBeTrue();
}
```

### 7. Race: PaymentExpiry vs PaymentCompleted

```csharp
[Fact]
public async Task Race_PaymentExpiryVsPaymentCompleted_ExactlyOneOutcome()
{
    // Set short expiry (1s) → reserve → near-simultaneous publish both events
    var ticketId = await PlaceOrderAsync(_validPayload);
    await PollUntilAsync(ticketId, s => s.Status == "PENDING_PAYMENT");

    var tasks = new[]
    {
        _testHarness.Bus.Publish(new PaymentSessionCompletedV1 { OrderId = ticketId, PaymentSessionId = "race" }),
        _testHarness.Bus.Publish(new PaymentExpiryTimeoutV1 { OrderId = ticketId })
    };
    await Task.WhenAll(tasks);

    await Task.Delay(2000);  // saga settle

    var order = await _dbContext.Orders.FindAsync(ticketId);
    order.Status.ShouldBeOneOf("CONFIRMED", "CANCELLED");

    // Exactly 1 final event published
    var confirmed = await _testHarness.Published.Count<OrderConfirmedV1>();
    var cancelled = await _testHarness.Published.Count<OrderCancelledV1>();
    (confirmed + cancelled).ShouldBe(1);  // exactly one
}
```

### 8. Idempotency: Duplicate PaymentCompleted

```csharp
[Fact]
public async Task PaymentCompleted_PublishedTwice_OrderConfirmedOnce()
{
    var ticketId = await PlaceOrderAsync(_validPayload);
    await PollUntilAsync(ticketId, s => s.Status == "PENDING_PAYMENT");

    // Publish 2 lần (same MessageId mặc định của MT)
    await _testHarness.Bus.Publish(new PaymentSessionCompletedV1
    {
        OrderId = ticketId, PaymentSessionId = "dup-test"
    });
    await _testHarness.Bus.Publish(new PaymentSessionCompletedV1
    {
        OrderId = ticketId, PaymentSessionId = "dup-test"
    });

    await PollUntilAsync(ticketId, s => s.Status == "CONFIRMED");

    // Inventory chỉ deduct 1 lần
    var item = await GetInventoryItemAsync(_validPayload.Items[0].VariantId);
    item.QuantityOnHand.ShouldBe(_initialQuantity - _validPayload.Items[0].Quantity);

    // OrderConfirmedV1 publish exactly 1 lần (DuplicateDetectionWindow)
    var count = await _testHarness.Published.Count<OrderConfirmedV1>();
    count.ShouldBe(1);
}
```

### 9. Idempotency: HttpIdempotency Header

```csharp
[Fact]
public async Task PostPlaceOrder_SameIdempotencyKey_ReturnsSameTicketId()
{
    var key = Guid.NewGuid().ToString();
    _client.DefaultRequestHeaders.Add("Idempotency-Key", key);

    var response1 = await _client.PostAsJsonAsync("/api/v1/order/orders", _validPayload);
    var ticket1 = await response1.Content.ReadFromJsonAsync<TicketResponse>();

    var response2 = await _client.PostAsJsonAsync("/api/v1/order/orders", _validPayload);
    var ticket2 = await response2.Content.ReadFromJsonAsync<TicketResponse>();

    ticket1.TicketId.ShouldBe(ticket2.TicketId);

    var sagaCount = await _dbContext.Set<PlaceOrderNormalSagaState>().CountAsync();
    sagaCount.ShouldBe(1);
}
```

### 10. Idempotency: ConfirmReservation Duplicate

```csharp
[Fact]
public async Task ConfirmReservation_CalledTwice_DeductsOnce()
{
    var reservationId = await SeedReservedItemAsync(quantity: 5);
    var idempKey = Guid.NewGuid().ToString();

    var cmd = new ConfirmReservationCommand(reservationId, idempKey);

    // Send 2 lần
    var r1 = await _sender.Send(cmd);
    var r2 = await _sender.Send(cmd);

    r1.IsSuccess.ShouldBeTrue();
    r2.IsSuccess.ShouldBeTrue();

    var item = await GetInventoryItemAsync(_variantId);
    item.QuantityOnHand.ShouldBe(_initialQuantity - 5);  // chỉ deduct 1 lần

    var reservation = await _inventoryDb.Reservations.FindAsync(reservationId);
    reservation.Status.ShouldBe("CONFIRMED");
}
```

### 11. Idempotency: Redis Pending Slot Underflow Protection

```csharp
[Fact]
public async Task PendingSlot_ReleaseTwice_DoesNotUnderflow()
{
    var userId = Guid.NewGuid();
    await _slotService.TryAcquireAsync(userId, default);   // slot = 1
    await _slotService.ReleaseAsync(userId, default);      // slot = 0
    await _slotService.ReleaseAsync(userId, default);      // slot = 0 (không âm)

    var value = await _redis.GetAsync($"pending-orders:{userId}");
    value.ShouldBe("0");
}
```

### 12. Idempotency: Race Saga Concurrency

```csharp
[Fact]
public async Task Saga_ConcurrentRetries_NoDuplicateSideEffect()
{
    // Saga test harness — fire events đồng thời
    // Verify: chỉ 1 instance saga, no duplicate domain transitions
    // ...
}
```

## Acceptance Criteria

- [ ] All 12 test scenarios pass
- [ ] Coverage > 80% cho saga state machines + handler async path
- [ ] No flaky tests (run 5x liên tiếp pass)
- [ ] CI pipeline pass

## Reporting

Test summary report định dạng:
| Scenario | Pass/Fail | Notes |
|---|---|---|
| Happy path | ✅ | Avg duration: 4.2s |
| Timeout | ✅ | Cancel after 5s + slot release verified |
| ... | ... | ... |

Cập nhật trong [`task/README.md`](README.md) sau khi hoàn thành.

## DoD

- [ ] All tests pass locally + CI
- [ ] PR merge
- [ ] Test summary report shared trong channel
