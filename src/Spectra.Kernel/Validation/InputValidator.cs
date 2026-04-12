using Spectra.Contracts.State;
using Spectra.Contracts.Validation;

namespace Spectra.Kernel.Validation;

/// <summary>
/// Validates workflow inputs before execution.
/// </summary>
public static class InputValidator
{
    /// <summary>
    /// Validates that required input keys exist in state.
    /// </summary>
    public static ValidationResult ValidateRequired(WorkflowState state, params string[] requiredKeys)
    {
        var missing = requiredKeys
            .Where(k => !state.Inputs.ContainsKey(k) || state.Inputs[k] is null)
            .ToList();

        if (missing.Any())
        {
            return ValidationResult.Fail(
                $"Missing required inputs: {string.Join(", ", missing)}",
                missing.ToArray());
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates and normalizes repoPath input.
    /// </summary>
    public static ValidationResult ValidateRepoPath(WorkflowState state)
    {
        if (!state.Inputs.TryGetValue("repoPath", out var value))
        {
            return ValidationResult.Fail("repoPath is required");
        }

        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return ValidationResult.Fail("repoPath must be a non-empty string");
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (!Directory.Exists(fullPath))
            {
                return ValidationResult.Fail($"Repository directory not found: {fullPath}");
            }

            state.Inputs["repoPath"] = fullPath;
            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            return ValidationResult.Fail($"Invalid repoPath: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a directory path input.
    /// </summary>
    public static ValidationResult ValidateDirectoryPath(
        WorkflowState state,
        string inputKey,
        bool mustExist = true)
    {
        if (!state.Inputs.TryGetValue(inputKey, out var value))
        {
            return ValidationResult.Fail($"{inputKey} is required");
        }

        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return ValidationResult.Fail($"{inputKey} must be a non-empty string");
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (mustExist && !Directory.Exists(fullPath))
            {
                return ValidationResult.Fail($"Directory not found: {fullPath}");
            }

            state.Inputs[inputKey] = fullPath;
            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            return ValidationResult.Fail($"Invalid {inputKey}: {ex.Message}");
        }
    }
}