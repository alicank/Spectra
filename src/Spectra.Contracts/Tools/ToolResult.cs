namespace Spectra.Contracts.Tools;

public class ToolResult
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }

    public static ToolResult Ok(string content, object? data = null)
        => new() { Success = true, Content = content, Data = data };

    public static ToolResult Fail(string error)
        => new() { Success = false, Error = error };
}