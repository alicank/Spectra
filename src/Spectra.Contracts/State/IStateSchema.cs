namespace Spectra.Contracts.State;

public interface IStateSchema
{
    IReadOnlyList<StateFieldDefinition> Fields { get; }
}