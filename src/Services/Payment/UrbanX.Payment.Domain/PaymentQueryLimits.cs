namespace UrbanX.Payment.Domain;

/// <summary>Upper bounds for repository queries (defense in depth).</summary>
public static class PaymentQueryLimits
{
    public const int SePayMemoMatchCandidatesUpperBound = 500;
}
