namespace Spectra.Contracts.State;

public interface IStateReducerRegistry
{
    void Register(IStateReducer reducer);
    IStateReducer? Get(string key);
}