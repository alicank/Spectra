namespace Spectra.Contracts.Prompts;

public interface IPromptRegistry
{
    PromptTemplate? GetPrompt(string promptId);
    IReadOnlyList<PromptTemplate> GetAll();
    void Register(PromptTemplate prompt);
    void Reload();
}