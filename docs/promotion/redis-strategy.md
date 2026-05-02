# Promotion — Redis Strategy

## Flash Sale Slot Counter

Each flash sale item gets a Redis counter seeded on `Activate`:

```
Key:   promotion:flash:{promotionId}:item:{variantId}:slots
Value: integer (remaining slots)
TTL:   promotion EndsAt - now
```

### Claiming a Slot (Lua — atomic)

```lua
local remaining = redis.call('GET', KEYS[1])
if remaining == false then return -1 end   -- key not found (not initialized)
if tonumber(remaining) <= 0 then return 0 end  -- sold out
return redis.call('DECRBY', KEYS[1], 1)    -- claim: returns new remaining
```

Return values: `>0` = claimed (remaining count), `0` = sold out, `-1` = key not in Redis (promotion not active/expired).

### Re-seeding on Activate

`ActivatePromotionCommandHandler` calls `ICacheService.SetAsync(key, totalSlots - reservedSlots, ttl, ct)` for each flash sale item. This is idempotent — re-activating after a pause re-seeds from current `ReservedSlots`.

## Distributed Lock

`[DistributedLock]` attribute is NOT used on `RedeemPromotionCommand` because slot claiming is already atomic via Lua. The Lua script is the concurrency primitive.
