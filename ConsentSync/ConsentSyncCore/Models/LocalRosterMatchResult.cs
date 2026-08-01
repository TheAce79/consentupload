namespace ConsentSyncCore.Models
{
    public class LocalRosterMatchResult
    {
        public bool Matched { get; init; }
        public string ClientId { get; init; } = string.Empty;
        public string MatchMethod { get; init; } = string.Empty;
        public string Suggestion { get; init; } = string.Empty;
        public double NameScore { get; init; }
    }
}
