namespace Spectra.Contracts.Tools;

public interface IToolRegistry
{
    void Register(ITool tool);
    ITool? Get(string name);
    IReadOnlyList<ITool> GetAll();
    IReadOnlyList<ToolDefinition> GetDefinitions(IEnumerable<string>? filter = null);
}