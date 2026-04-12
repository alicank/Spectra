using Spectra.Contracts.State;
using Spectra.Kernel.Evaluation;
using Xunit;

namespace Spectra.Tests.Evaluation;

public class ConditionEvaluatorTests
{
    private readonly SimpleConditionEvaluator _evaluator = new();

    [Fact]
    public void EvaluatesEquality()
    {
        var state = new WorkflowState();
        state.Context["status"] = "success";

        var result = _evaluator.Evaluate("Context.status == success", state);
        Assert.True(result.Satisfied);

        result = _evaluator.Evaluate("Context.status == failed", state);
        Assert.False(result.Satisfied);
    }

    [Fact]
    public void EvaluatesInequality()
    {
        var state = new WorkflowState();
        state.Context["status"] = "success";

        var result = _evaluator.Evaluate("Context.status != failed", state);
        Assert.True(result.Satisfied);
    }

    [Fact]
    public void EvaluatesNumericComparisons()
    {
        var state = new WorkflowState();
        state.Context["count"] = 5;

        Assert.True(_evaluator.Evaluate("Context.count > 3", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.count >= 5", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.count < 10", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.count <= 5", state).Satisfied);
        Assert.False(_evaluator.Evaluate("Context.count > 5", state).Satisfied);
    }

    [Fact]
    public void EvaluatesBooleans()
    {
        var state = new WorkflowState();
        state.Context["hasMore"] = true;
        state.Context["isDone"] = false;

        Assert.True(_evaluator.Evaluate("Context.hasMore == true", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.isDone == false", state).Satisfied);
        Assert.False(_evaluator.Evaluate("Context.hasMore == false", state).Satisfied);
    }

    [Fact]
    public void EvaluatesTruthyValues()
    {
        var state = new WorkflowState();
        state.Context["flag"] = true;
        state.Context["empty"] = "";
        state.Context["zero"] = 0;

        Assert.True(_evaluator.Evaluate("Context.flag", state).Satisfied);
        Assert.False(_evaluator.Evaluate("Context.empty", state).Satisfied);
        Assert.False(_evaluator.Evaluate("Context.zero", state).Satisfied);
        Assert.False(_evaluator.Evaluate("Context.nonexistent", state).Satisfied);
    }

    [Fact]
    public void EvaluatesNestedPaths()
    {
        var state = new WorkflowState();
        state.Context["result"] = new Dictionary<string, object?>
        {
            ["fileCount"] = 10,
            ["status"] = "done"
        };

        Assert.True(_evaluator.Evaluate("Context.result.fileCount > 5", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.result.status == done", state).Satisfied);
    }

    [Fact]
    public void EvaluatesInputs()
    {
        var state = new WorkflowState();
        state.Inputs["mode"] = "full";

        Assert.True(_evaluator.Evaluate("Inputs.mode == full", state).Satisfied);
        Assert.False(_evaluator.Evaluate("Inputs.mode == partial", state).Satisfied);
    }

    [Fact]
    public void HandlesNullValues()
    {
        var state = new WorkflowState();
        state.Context["value"] = null;

        Assert.True(_evaluator.Evaluate("Context.value == null", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.nonexistent == null", state).Satisfied);
    }

    [Fact]
    public void EvaluatesStringOperations()
    {
        var state = new WorkflowState();
        state.Context["filename"] = "document.pdf";

        Assert.True(_evaluator.Evaluate("Context.filename endswith .pdf", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.filename startswith document", state).Satisfied);
        Assert.True(_evaluator.Evaluate("Context.filename contains ment", state).Satisfied);
    }
}