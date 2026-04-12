namespace Spectra.Contracts.Validation;

/// <summary>
/// Result of input validation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; init; }
    public string? Message { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Fail(string message, params string[] errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Message = message,
            Errors = errors.ToList()
        };
    }

    /// <summary>
    /// Creates a result that is valid but contains warnings.
    /// </summary>
    public static ValidationResult SuccessWithWarnings(List<string> warnings)
    {
        return new ValidationResult
        {
            IsValid = true,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Creates a result with both errors and warnings.
    /// </summary>
    public static ValidationResult FailWithDetails(List<string> errors, List<string>? warnings = null)
    {
        return new ValidationResult
        {
            IsValid = false,
            Message = errors.Count > 0 ? errors[0] : "Validation failed",
            Errors = errors,
            Warnings = warnings ?? []
        };
    }
}