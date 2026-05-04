# Changelog — Shared.Contract

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

- **Place order** integration contracts (interfaces only) under `Shared.Contract.Messaging.PlaceOrder`:
  - `IPlaceOrderIntegrationContract` (`string EventId` for consumer idempotency)
  - `IOrderCreated.cs` — `IOrderCreated`, `IOrderCreatedItem`
  - `IInventoryEvents.cs` — `IInventoryReserved`, `IInventoryReservedItem`, `IInventoryReserveFailed`, `IInventoryReleaseRequested`
  - `ICouponEvents.cs` — `ICouponClaimed`, `ICouponClaimFailed`, `ICouponReleaseRequested`
  - `IOrderConfirmed.cs` — `IOrderConfirmed`
