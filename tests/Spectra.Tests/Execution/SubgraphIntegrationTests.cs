using Spectra.Contracts.Evaluation;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Evaluation;
using Spectra.Kernel.Execution;
using Spectra.Workflow;
using Xunit;

namespace Spectra.Tests.Execution;

/// <summary>
/// Integration tests that run subgraph nodes through the full WorkflowRunner pipeline,
/// verifying end-to-end wiring: builder → definition → runner → SubgraphStep → child runner → state.
/// </summary>
public class SubgraphIntegrationTests
{
    /// <summary>
    /// A trivial step that copies inputs to outputs, used as a leaf node in child workflows.
    /// </summary>
    private class EchoStep : IStep
    {
        public string StepType => "echo";

        public Task<StepResult> ExecuteAsync(StepContext context)
        {
            var outputs = new Dictionary<string, object?>();
            foreach (var (key, value) in context.Inputs)
            {
                if (!key.StartsWith("__"))
                    outputs[key] = value;
            }

            // Also write to state so output mappings can resolve
            foreach (var (key, value) in outputs)
                context.State.Context[key] = value;

            return Task.FromResult(StepResult.Success(outputs));
        }
    }

    /// <summary>
    /// A step that writes a fixed value into the child state's Artifacts section.
    /// </summary>
    private class ProduceArtifactStep : IStep
    {
        public string StepType => "produce-artifact";

        public Task<StepResult> ExecuteAsync(StepContext context)
        {
            var key = context.Inputs.TryGetValue("artifactKey", out var k) ? k as string ?? "result" : "result";
            var val = context.Inputs.TryGetValue("artifactValue", out var v) ? v : "produced";
            context.State.Artifacts[key] = val;

            return Task.FromResult(StepResult.Success(new Dictionary<string, object?>
            {
                [key] = val
            }));
        }
    }

    private class NoopStep : IStep
    {
        public string StepType => "noop";
        public Task<StepResult> ExecuteAsync(StepContext context) => Task.FromResult(StepResult.Success());
    }

    private static InMemoryStepRegistry BuildRegistry(IWorkflowRunner runner)
    {
        var registry = new InMemoryStepRegistry();
        registry.Register(new NoopStep());
        registry.Register(new EchoStep());
        registry.Register(new ProduceArtifactStep());
        registry.Register(new SubgraphStep(runner));
        return registry;
    }

    [Fact]
    public async Task Subgraph_node_runs_child_workflow_end_to_end()
    {
        // Arrange: child workflow with a single produce-artifact node
        var child = WorkflowBuilder.Create("child-wf")
            .AddNode("produce", "produce-artifact", n => n
                .WithParameter("artifactKey", "summary")
                .WithParameter("artifactValue", "child-done"))
            .Build();

        // Parent workflow: start → subgraph-node
        var parent = WorkflowBuilder.Create("parent-wf")
            .AddNode("start", "noop")
            .AddSubgraph("sub1", child, sg => sg
                .MapOutput("Artifacts.summary", "Context.childSummary"))
            .AddSubgraphNode("sg-node", "sub1")
            .AddEdge("start", "sg-node")
            .Build();

        // The runner needs to be shared so SubgraphStep can invoke the same runner
        WorkflowRunner? runner = null;
        var stateMapper = new StateMapper();
        var conditionEvaluator = new SimpleConditionEvaluator();

        // We need a lazy approach since runner references registry and registry references runner
        IStepRegistry? lazyRegistry = null;
        runner = new WorkflowRunner(
            new DeferredStepRegistry(() => lazyRegistry!),
            stateMapper,
            conditionEvaluator);

        lazyRegistry = BuildRegistry(runner);

        // Act
        var result = await runner.RunAsync(parent);

        // Assert
        Assert.Empty(result.Errors);
        Assert.True(result.Context.ContainsKey("childSummary"),
            "Output mapping should copy child Artifacts.summary → parent Context.childSummary");
        Assert.Equal("child-done", result.Context["childSummary"]);
    }

    [Fact]
    public async Task Subgraph_input_mappings_flow_parent_data_to_child()
    {
        var child = WorkflowBuilder.Create("child-wf")
            .AddNode("echo", "echo")
            .Build();

        var parent = WorkflowBuilder.Create("parent-wf")
            .AddNode("setup", "noop")
            .AddSubgraph("sub1", child, sg => sg
                .MapInput("Context.query", "searchTerm"))
            .AddSubgraphNode("sg-node", "sub1")
            .AddEdge("setup", "sg-node")
            .Build();

        WorkflowRunner? runner = null;
        var stateMapper = new StateMapper();
        IStepRegistry? lazyRegistry = null;
        runner = new WorkflowRunner(
            new DeferredStepRegistry(() => lazyRegistry!),
            stateMapper,
            new SimpleConditionEvaluator());
        lazyRegistry = BuildRegistry(runner);

        var initialState = new WorkflowState();
        initialState.Context["query"] = "hello spectra";

        // Act
        var result = await runner.RunAsync(parent, initialState);

        // Assert
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Subgraph_child_errors_propagate_to_parent()
    {
        // Child workflow references a step type that doesn't exist → runner will fail
        var child = WorkflowBuilder.Create("child-wf")
            .AddNode("bad-node", "nonexistent-step")
            .Build();

        var parent = WorkflowBuilder.Create("parent-wf")
            .AddSubgraph("sub1", child)
            .AddSubgraphNode("sg-node", "sub1")
            .Build();

        WorkflowRunner? runner = null;
        var stateMapper = new StateMapper();
        IStepRegistry? lazyRegistry = null;
        runner = new WorkflowRunner(
            new DeferredStepRegistry(() => lazyRegistry!),
            stateMapper,
            new SimpleConditionEvaluator());
        lazyRegistry = BuildRegistry(runner);

        var result = await runner.RunAsync(parent);

        // The parent workflow should report failure from the subgraph
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Wraps a step registry with deferred initialization to break the
    /// circular dependency: runner → registry → SubgraphStep → runner.
    /// </summary>
    private class DeferredStepRegistry : IStepRegistry
    {
        private readonly Func<IStepRegistry> _factory;
        public DeferredStepRegistry(Func<IStepRegistry> factory) => _factory = factory;
        public IStep? GetStep(string stepType) => _factory().GetStep(stepType);
        public void Register(IStep step) => _factory().Register(step);
    }

    /// <summary>
    /// Minimal in-memory step registry for tests.
    /// </summary>
    private class InMemoryStepRegistry : IStepRegistry
    {
        private readonly Dictionary<string, IStep> _steps = new(StringComparer.OrdinalIgnoreCase);
        public void Register(IStep step) => _steps[step.StepType] = step;
        public IStep? GetStep(string stepType) => _steps.TryGetValue(stepType, out var s) ? s : null;
    }
}