using Microsoft.Extensions.Logging;

namespace Shared.Cache.Resilience;

/// <summary>
/// In-process circuit breaker for Redis health tracking.
///
/// States:
///   Closed  — Redis is healthy; all calls proceed normally.
///   Open    — Redis is down; calls are skipped for <see cref="CooldownSeconds"/> seconds.
///   Probing — Cooldown expired; requests are allowed through to test recovery.
///             First success → Closed. Any failure → re-Open with fresh cooldown.
///
/// Thread-safe via a simple lock (state transitions are infrequent).
/// </summary>
public sealed class RedisCircuitBreaker
{
    private const int FailureThreshold = 5;
    private const int CooldownSeconds = 30;

    private readonly ILogger<RedisCircuitBreaker> _logger;
    private readonly object _lock = new();

    private bool _open;
    private bool _probePeriod;
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;

    public RedisCircuitBreaker(ILogger<RedisCircuitBreaker> logger) => _logger = logger;

    /// <summary>
    /// Returns <c>true</c> when Redis should be skipped entirely.
    /// Returns <c>false</c> when the circuit is closed or when the cooldown has expired
    /// and probe requests are being let through.
    /// </summary>
    public bool ShouldSkipRedis()
    {
        lock (_lock)
        {
            if (!_open) return false;

            if (DateTimeOffset.UtcNow - _openedAt >= TimeSpan.FromSeconds(CooldownSeconds))
            {
                _probePeriod = true;
                return false;
            }

            return true;
        }
    }

    /// <summary>Call after every successful Redis operation.</summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_open)
                _logger.LogInformation(
                    "[CircuitBreaker] Redis recovered — circuit closed.");

            _open = false;
            _probePeriod = false;
            _consecutiveFailures = 0;
        }
    }

    /// <summary>Call after every Redis exception or timeout.</summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            if (_probePeriod)
            {
                _open = true;
                _probePeriod = false;
                _openedAt = DateTimeOffset.UtcNow;
                _consecutiveFailures = 0;
                _logger.LogWarning(
                    "[CircuitBreaker] Probe failed — circuit re-opened. Retry in {Cooldown}s.",
                    CooldownSeconds);
                return;
            }

            _consecutiveFailures++;

            if (_consecutiveFailures >= FailureThreshold)
            {
                _open = true;
                _openedAt = DateTimeOffset.UtcNow;
                _consecutiveFailures = 0;
                _logger.LogWarning(
                    "[CircuitBreaker] {Threshold} consecutive Redis failures — circuit opened. " +
                    "Retry in {Cooldown}s.",
                    FailureThreshold, CooldownSeconds);
            }
        }
    }
}
