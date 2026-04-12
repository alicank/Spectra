namespace Spectra.Contracts.Interrupts;

public sealed record InterruptResponse
{
    public required InterruptStatus Status { get; init; }

    public bool Approved => Status == InterruptStatus.Approved;
    public bool Rejected => Status == InterruptStatus.Rejected;
    public bool TimedOut => Status == InterruptStatus.TimedOut;
    public bool Cancelled => Status == InterruptStatus.Cancelled;

    public string? RespondedBy { get; init; }
    public string? Comment { get; init; }
    public object? Payload { get; init; }

    public DateTimeOffset RespondedAt { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();

    public static InterruptResponse ApprovedResponse(
        object? payload = null,
        string? respondedBy = null,
        string? comment = null) =>
        new()
        {
            Status = InterruptStatus.Approved,
            Payload = payload,
            RespondedBy = respondedBy,
            Comment = comment
        };

    public static InterruptResponse RejectedResponse(
        string? respondedBy = null,
        string? comment = null,
        object? payload = null) =>
        new()
        {
            Status = InterruptStatus.Rejected,
            Payload = payload,
            RespondedBy = respondedBy,
            Comment = comment
        };

    public static InterruptResponse TimedOutResponse(string? comment = null) =>
        new()
        {
            Status = InterruptStatus.TimedOut,
            Comment = comment
        };

    public static InterruptResponse CancelledResponse(string? comment = null) =>
        new()
        {
            Status = InterruptStatus.Cancelled,
            Comment = comment
        };
}