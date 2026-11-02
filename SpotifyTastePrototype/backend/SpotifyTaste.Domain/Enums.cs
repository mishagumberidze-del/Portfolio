namespace SpotifyTaste.Domain;

public enum ImportBatchStatus { Pending, Running, Completed, CompletedWithErrors, Failed }
public enum EnrichmentStatus { Pending, Completed, NotFound, RetryLater, Failed }
public enum RecommendationListType { NotSaved, NeverHeard }
public enum RecommendationBand { HighConfidence, Discovery, Wildcard }

