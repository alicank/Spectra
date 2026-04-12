using Spectra.Contracts.State;

namespace Spectra.Contracts.Evaluation;

public interface IConditionEvaluator
{
    ConditionResult Evaluate(string expression, WorkflowState state);
}