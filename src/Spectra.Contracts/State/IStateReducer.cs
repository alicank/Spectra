namespace Spectra.Contracts.State;

public interface IStateReducer
{
    string Key { get; }

    object? Reduce(object? currentValue, object? incomingValue);
}