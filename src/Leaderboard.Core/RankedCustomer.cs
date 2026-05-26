namespace Leaderboard.Core;

/// <summary>Single leaderboard row returned by the API (1-based rank).</summary>
public sealed record RankedCustomer(long CustomerId, decimal Score, int Rank);
