using Spectra.Contracts.Events;
using Spectra.Contracts.Execution;
using Spectra.Contracts.State;
using Spectra.Contracts.Steps;
using Spectra.Contracts.Streaming;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class SubgraphStepTests
{
    private static WorkflowDefinition CreateChildWorkflow(string id = "child-wf") =>
        new() { Id = id, Nodes = [new NodeDefinition { Id = "c1", StepType = "noop" }] };

    private static WorkflowDefinition CreateParentWorkflow(
        SubgraphDefinition subgraph) =>
        new()
        {
            Id = "parent-wf",
            Nodes = [new NodeDefinition { Id = "sg-node", StepType = "subgraph", SubgraphId = subgraph.Id }],
            Subgraphs = [subgraph]
        };

    private static StepContext CreateContext(
        WorkflowState state,
        WorkflowDefinition workflow,
        Dictionary<string, object?>? inputs = null)
    {
        return new StepContext
        {
            RunId = state.RunId,
            WorkflowId = workflow.Id,
            NodeId = "sg-node",
            State = state,
            CancellationToken = CancellationToken.None,
            Inputs = inputs ?? new() { ["__subgraphId"] = "sub1" },
            WorkflowDefinition = workflow
        };
    }

    // ── Fakes ──

    private class FakeWorkflowRunner : IWorkflowRunner
    {
        public WorkflowState? ReceivedState { get; private set; }
        public WorkflowDefinition? ReceivedWorkflow { get; private set; }
        public RunContext? ReceivedRunContext { get; private set; }
        public Func<WorkflowState, WorkflowState>? OnRun { get; set; }

        public Task<WorkflowState> RunAsync(
            WorkflowDefinition workflow,
            WorkflowState? initialState = null,
            CancellationToken cancellationToken = default)
        {
            return RunCore(workflow, initialState, null);
        }

        public Task<WorkflowState> RunAsync(
            WorkflowDefinition workflow,
            WorkflowState? initialState,
            RunContext runContext,
            CancellationToken cancellationToken = default)
        {
            return RunCore(workflow, initialState, runContext);
        }

        private Task<WorkflowState> RunCore(
            WorkflowDefinition workflow,
            WorkflowState? initialState,
            RunContext? runContext)
        {
            ReceivedWorkflow = workflow;
            ReceivedState = initialState;
            ReceivedRunContext = runContext;

            var result = OnRun != null
                ? OnRun(initialState ?? new WorkflowState())
                : initialState ?? new WorkflowState();

            return Task.FromResult(result);
        }

        public Task<WorkflowState> ResumeAsync(WorkflowDefinition w, string r, CancellationToken c = default) =>
            throw new NotImplementedException();
        public Task<WorkflowState> ResumeFromCheckpointAsync(WorkflowDefinition w, string r, int i, CancellationToken c = default) =>
            throw new NotImplementedException();
        public Task<WorkflowState> ResumeWithResponseAsync(WorkflowDefinition w, string r, Contracts.Interrupts.InterruptResponse ir, CancellationToken c = default) =>
            throw new NotImplementedException();
        public Task<WorkflowState> ForkAndRunAsync(WorkflowDefinition w, string s, int i, string? n = null, WorkflowState? so = null, CancellationToken c = default) =>
            throw new NotImplementedException();
        public IAsyncEnumerable<WorkflowEvent> StreamAsync(
            WorkflowDefinition workflow,
            StreamMode mode = StreamMode.Tokens,
            WorkflowState? initialState = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<WorkflowEvent> StreamAsync(WorkflowDefinition workflow, StreamMode mode, WorkflowState? initialState,
            RunContext runContext, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<WorkflowState> SendMessageAsync(WorkflowDefinition workflow, string runId, string userMessage,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    // ── Tests ──

    [Fact]
    public async Task Executes_child_workflow_with_isolated_state()
    {
        var runner = new FakeWorkflowRunner
        {
            OnRun = childState =>
            {
                childState.Artifacts["result"] = "done";
                return childState;
            }
        };

        var subgraph = new SubgraphDefinition
        {
            Id = "sub1",
            Workflow = CreateChildWorkflow(),
            OutputMappings = { ["Artifacts.result"] = "Context.subResult" }
        };

        var parent = CreateParentWorkflow(subgraph);
        var parentState = new WorkflowState();
        parentState.Context["parentOnly"] = "should-not-leak";

        var step = new SubgraphStep(runner);
        var ctx = CreateContext(parentState, parent);
        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        // Child should not have seen parent's Context
        Assert.False(runner.ReceivedState!.Context.ContainsKey("parentOnly"));
        // Output mapping should be in outputs
        Assert.Equal("done", result.Outputs["Context.subResult"]);
    }

    [Fact]
    public async Task Input_mappings_copy_parent_to_child()
    {
        var runner = new FakeWorkflowRunner();

        var subgraph = new SubgraphDefinition
        {
            Id = "sub1",
            Workflow = CreateChildWorkflow(),
            InputMappings = { ["Context.documents"] = "items" }
        };

        var parent = CreateParentWorkflow(subgraph);
        var parentState = new WorkflowState();
        parentState.Context["documents"] = new List<string> { "a.txt", "b.txt" };

        var step = new SubgraphStep(runner);
        var ctx = CreateContext(parentState, parent);
        await step.ExecuteAsync(ctx);

        var childInputs = runner.ReceivedState!.Inputs;
        Assert.True(childInputs.ContainsKey("items"));
        Assert.IsType<List<string>>(childInputs["items"]);
    }

    [Fact]
    public async Task Output_mappings_copy_child_to_parent_outputs()
    {
        var runner = new FakeWorkflowRunner
        {
            OnRun = childState =>
            {
                childState.Artifacts["summary"] = "all good";
                childState.Context["detail"] = 42;
                return childState;
            }
        };

        var subgraph = new SubgraphDefinition
        {
            Id = "sub1",
            Workflow = CreateChildWorkflow(),
            OutputMappings =
            {
                ["Artifacts.summary"] = "Context.childSummary",
                ["Context.detail"] = "Context.childDetail"
            }
        };

        var parent = CreateParentWorkflow(subgraph);
        var step = new SubgraphStep(runner);
        var ctx = CreateContext(new WorkflowState(), parent);
        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Succeeded, result.Status);
        Assert.Equal("all good", result.Outputs["Context.childSummary"]);
        Assert.Equal(42, result.Outputs["Context.childDetail"]);
    }

    [Fact]
    public async Task No_output_mappings_exposes_child_context_and_artifacts()
    {
        var runner = new FakeWorkflowRunner
        {
            OnRun = childState =>
            {
                childState.Context["foo"] = "bar";
                childState.Artifacts["file"] = "output.txt";
                return childState;
            }
        };

        var subgraph = new SubgraphDefinition
        {
            Id = "sub1",
            Workflow = CreateChildWorkflow()
            // No OutputMappings
        };

        var parent = CreateParentWorkflow(subgraph);
        var step = new SubgraphStep(runner);
        var ctx = CreateContext(new WorkflowState(), parent);
        var result = await step.ExecuteAsync(ctx);

        Assert.True(result.Outputs.ContainsKey("childContext"));
        Assert.True(result.Outputs.ContainsKey("childArtifacts"));
    }

    [Fact]
    public async Task Child_errors_produce_failed_result()
    {
        var runner = new FakeWorkflowRunner
        {
            OnRun = childState =>
            {
                childState.Errors.Add("child broke");
                return childState;
            }
        };

        var subgraph = new SubgraphDefinition
        {
            Id = "sub1",
            Workflow = CreateChildWorkflow()
        };

        var parent = CreateParentWorkflow(subgraph);
        var step = new SubgraphStep(runner);
        var ctx = CreateContext(new WorkflowState(), parent);
        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("child broke", result.ErrorMessage);
    }

    [Fact]
    public async Task Missing_subgraph_id_fails_gracefully()
    {
        var runner = new FakeWorkflowRunner();
        var parent = new WorkflowDefinition
        {
            Id = "p",
            Nodes = [new NodeDefinition { Id = "n1", StepType = "subgraph" }]
        };

        var step = new SubgraphStep(runner);
        var ctx = CreateContext(new WorkflowState(), parent,
            inputs: new Dictionary<string, object?>()); // no __subgraphId

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("__subgraphId", result.ErrorMessage);
    }

    [Fact]
    public async Task Missing_workflow_definition_fails_gracefully()
    {
        var runner = new FakeWorkflowRunner();
        var step = new SubgraphStep(runner);

        var ctx = new StepContext
        {
            RunId = "r1",
            WorkflowId = "w1",
            NodeId = "n1",
            State = new WorkflowState(),
            CancellationToken = CancellationToken.None,
            Inputs = new() { ["__subgraphId"] = "sub1" },
            WorkflowDefinition = null
        };

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("WorkflowDefinition", result.ErrorMessage);
    }

    [Fact]
    public async Task Unknown_subgraph_id_fails_gracefully()
    {
        var runner = new FakeWorkflowRunner();
        var parent = new WorkflowDefinition
        {
            Id = "p",
            Nodes = [new NodeDefinition { Id = "n1", StepType = "subgraph" }],
            Subgraphs = [] // empty
        };

        var step = new SubgraphStep(runner);
        var ctx = CreateContext(new WorkflowState(), parent,
            inputs: new() { ["__subgraphId"] = "nonexistent" });

        var result = await step.ExecuteAsync(ctx);

        Assert.Equal(StepStatus.Failed, result.Status);
        Assert.Contains("nonexistent", result.ErrorMessage);
    }

    [Fact]
    public async Task Child_state_is_isolated_from_parent()
    {
        var runner = new FakeWorkflowRunner
        {
            OnRun = childState =>
            {
                // Child mutates its own state
                childState.Context["childKey"] = "childValue";
                return childState;
            }
        };

        var subgraph = new SubgraphDefinition
        {
            Id = "sub1",
            Workflow = CreateChildWorkflow()
        };

        var parent = CreateParentWorkflow(subgraph);
        var parentState = new WorkflowState();
        parentState.Context["parentKey"] = "parentValue";

        var step = new SubgraphStep(runner);
        var ctx = CreateContext(parentState, parent);
        await step.ExecuteAsync(ctx);

        // Parent state should be untouched by child mutations
        Assert.False(parentState.Context.ContainsKey("childKey"));
        Assert.Equal("parentValue", parentState.Context["parentKey"]);
        // Child should not have parent's context
        Assert.False(runner.ReceivedState!.Context.ContainsKey("parentKey"));
    }

    // ── RunContext propagation ──

    [Fact]
    public async Task RunContext_is_propagated_to_child_workflow()
    {
        var runner = new FakeWorkflowRunner();

        var subgraph = new SubgraphDefinition
        {
            Id = "sub1",
            Workflow = CreateChildWorkflow()
        };

        var parent = CreateParentWorkflow(subgraph);
        var runContext = new RunContext
        {
            TenantId = "acme",
            UserId = "user-1",
            Roles = ["admin"]
        };

        var step = new SubgraphStep(runner);
        var ctx = CreateContext(new WorkflowState(), parent);
        ctx = new StepContext
        {
            RunId = ctx.RunId,
            WorkflowId = ctx.WorkflowId,
            NodeId = ctx.NodeId,
            State = ctx.State,
            CancellationToken = ctx.CancellationToken,
            Inputs = ctx.Inputs,
            WorkflowDefinition = ctx.WorkflowDefinition,
            RunContext = runContext
        };

        await step.ExecuteAsync(ctx);

        Assert.NotNull(runner.ReceivedRunContext);
        Assert.Equal("acme", runner.ReceivedRunContext.TenantId);
        Assert.Equal("user-1", runner.ReceivedRunContext.UserId);
        Assert.Contains("admin", runner.ReceivedRunContext.Roles);
    }
}