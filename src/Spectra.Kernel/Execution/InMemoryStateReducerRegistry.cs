using Spectra.Contracts.State;

namespace Spectra.Kernel.Execution;

/// <summary>
/// Default in-memory implementation of <see cref="IStateReducerRegistry"/>.
/// Thread-safe for concurrent reads; registration is expected at startup.
/// </summary>
internal class InMemoryStateReducerRegistry : IStateReducerRegistry
{
    private readonly Dictionary<string, IStateReducer> _reducers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IStateReducer reducer)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        _reducers[reducer.Key] = reducer;
    }

    public IStateReducer? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _reducers.TryGetValue(key, out var reducer) ? reducer : null;
    }
}