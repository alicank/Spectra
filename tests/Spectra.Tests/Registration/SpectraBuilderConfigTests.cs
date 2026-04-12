using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Events;
using Spectra.Contracts.Interrupts;
using Spectra.Contracts.Prompts;
using Spectra.Contracts.State;
using Spectra.Extensions.Checkpointing;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Prompts;
using Spectra.Registration;
using Xunit;

namespace Spectra.Tests.Registration;

public class SpectraBuilderConfigTests
{
    private readonly SpectraBuilder _sut = new();

    // ── Checkpoints ──────────────────────────────────────────────

    [Fact]
    public void AddCheckpoints_StoresCustomStore()
    {
        var store = new InMemoryCheckpointStore();

        var result = _sut.AddCheckpoints(store);

        Assert.Same(store, _sut.CheckpointStore);
        Assert.Same(_sut, result); // fluent chaining
    }

    [Fact]
    public void AddCheckpoints_AppliesConfigureCallback()
    {
        var store = new InMemoryCheckpointStore();

        _sut.AddCheckpoints(store, opts => opts.MaxCheckpointCount = 42);

        Assert.Equal(42, _sut.CheckpointOptions.MaxCheckpointCount);
    }

    [Fact]
    public void AddCheckpoints_SkipsCallbackWhenNull()
    {
        var store = new InMemoryCheckpointStore();
        var defaultOptions = new CheckpointOptions();

        _sut.AddCheckpoints(store, configure: null);

        Assert.Equal(defaultOptions.MaxCheckpointCount, _sut.CheckpointOptions.MaxCheckpointCount);
    }

    [Fact]
    public void AddInMemoryCheckpoints_CreatesInMemoryStore()
    {
        var result = _sut.AddInMemoryCheckpoints();

        Assert.IsType<InMemoryCheckpointStore>(_sut.CheckpointStore);
        Assert.Same(_sut, result);
    }

    [Fact]
    public void AddInMemoryCheckpoints_AppliesConfigureCallback()
    {
        _sut.AddInMemoryCheckpoints(opts => opts.MaxCheckpointCount = 10);

        Assert.Equal(10, _sut.CheckpointOptions.MaxCheckpointCount);
    }

    [Fact]
    public void AddFileCheckpoints_CreatesFileStore()
    {
        var result = _sut.AddFileCheckpoints("/tmp/checkpoints");

        Assert.IsType<FileCheckpointStore>(_sut.CheckpointStore);
        Assert.Same(_sut, result);
    }

    [Fact]
    public void AddCheckpoints_LastRegistrationWins()
    {
        var first = new InMemoryCheckpointStore();
        var second = new InMemoryCheckpointStore();

        _sut.AddCheckpoints(first);
        _sut.AddCheckpoints(second);

        Assert.Same(second, _sut.CheckpointStore);
    }

    // ── Event Sinks ──────────────────────────────────────────────

    [Fact]
    public void AddEventSink_AddsToCollection()
    {
        var sink = new ConsoleEventSink();

        var result = _sut.AddEventSink(sink);

        Assert.Single(_sut.EventSinks);
        Assert.Same(sink, _sut.EventSinks[0]);
        Assert.Same(_sut, result);
    }

    [Fact]
    public void AddEventSink_SupportsMultipleSinks()
    {
        var sink1 = new ConsoleEventSink();
        var sink2 = new ConsoleEventSink();

        _sut.AddEventSink(sink1).AddEventSink(sink2);

        Assert.Equal(2, _sut.EventSinks.Count);
    }

    [Fact]
    public void AddConsoleEvents_AddsConsoleEventSink()
    {
        var result = _sut.AddConsoleEvents();

        Assert.Single(_sut.EventSinks);
        Assert.IsType<ConsoleEventSink>(_sut.EventSinks[0]);
        Assert.Same(_sut, result);
    }

    // ── Prompts ──────────────────────────────────────────────────

    [Fact]
    public void AddPrompts_StoresCustomRegistry()
    {
        var registry = new StubPromptRegistry();

        var result = _sut.AddPrompts(registry);

        Assert.Same(registry, _sut.PromptRegistry);
        Assert.Same(_sut, result);
    }

    [Fact]
    public void AddPromptsFromDirectory_CreatesFilePromptRegistry()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"spectra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = _sut.AddPromptsFromDirectory(dir);

            Assert.IsType<FilePromptRegistry>(_sut.PromptRegistry);
            Assert.Same(_sut, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AddPrompts_LastRegistrationWins()
    {
        var first = new StubPromptRegistry();
        var second = new StubPromptRegistry();

        _sut.AddPrompts(first);
        _sut.AddPrompts(second);

        Assert.Same(second, _sut.PromptRegistry);
    }

    // ── Interrupt Handler ────────────────────────────────────────

    [Fact]
    public void AddInterruptHandler_StoresHandler()
    {
        var handler = new StubInterruptHandler();

        var result = _sut.AddInterruptHandler(handler);

        Assert.Same(handler, _sut.InterruptHandler);
        Assert.Same(_sut, result);
    }

    // ── State Reducers ───────────────────────────────────────────

    [Fact]
    public void AddStateReducers_StoresRegistry()
    {
        var registry = new StubStateReducerRegistry();

        var result = _sut.AddStateReducers(registry);

        Assert.Same(registry, _sut.StateReducerRegistry);
        Assert.Same(_sut, result);
    }

    // ── Parallelism ──────────────────────────────────────────────

    [Fact]
    public void MaxParallelism_DefaultsToFour()
    {
        Assert.Equal(4, _sut.MaxParallelism);
    }

    [Fact]
    public void ConfigureParallelism_SetsValue()
    {
        var result = _sut.ConfigureParallelism(8);

        Assert.Equal(8, _sut.MaxParallelism);
        Assert.Same(_sut, result);
    }

    // ── Defaults ─────────────────────────────────────────────────

    [Fact]
    public void Defaults_AllOptionalPropertiesAreNull()
    {
        Assert.Null(_sut.CheckpointStore);
        Assert.Null(_sut.PromptRegistry);
        Assert.Null(_sut.InterruptHandler);
        Assert.Null(_sut.StateReducerRegistry);
    }

    [Fact]
    public void Defaults_CollectionsAreEmpty()
    {
        Assert.Empty(_sut.EventSinks);
    }

    [Fact]
    public void Defaults_CheckpointOptionsIsNotNull()
    {
        Assert.NotNull(_sut.CheckpointOptions);
    }

    // ── Workflow Store ───────────────────────────────────────────

    [Fact]
    public void AddWorkflows_StoresCustomStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"spectra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var store = new JsonFileWorkflowStore(dir);

            var result = _sut.AddWorkflows(store);

            Assert.Same(store, _sut.WorkflowStore);
            Assert.Same(_sut, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AddWorkflowsFromDirectory_CreatesJsonFileWorkflowStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"spectra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = _sut.AddWorkflowsFromDirectory(dir);

            Assert.IsType<JsonFileWorkflowStore>(_sut.WorkflowStore);
            Assert.Same(_sut, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AddWorkflows_LastRegistrationWins()
    {
        var dir1 = Path.Combine(Path.GetTempPath(), $"spectra_test_{Guid.NewGuid():N}");
        var dir2 = Path.Combine(Path.GetTempPath(), $"spectra_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        try
        {
            var first = new JsonFileWorkflowStore(dir1);
            var second = new JsonFileWorkflowStore(dir2);

            _sut.AddWorkflows(first);
            _sut.AddWorkflows(second);

            Assert.Same(second, _sut.WorkflowStore);
        }
        finally
        {
            Directory.Delete(dir1, recursive: true);
            Directory.Delete(dir2, recursive: true);
        }
    }

    [Fact]
    public void Defaults_WorkflowStoreIsNull()
    {
        Assert.Null(_sut.WorkflowStore);
    }

    // ── Fluent Chaining Smoke Test ───────────────────────────────

    [Fact]
    public void FluentChaining_AllMethodsReturnBuilder()
    {
        var store = new InMemoryCheckpointStore();

        var result = _sut
            .AddCheckpoints(store)
            .AddConsoleEvents()
            .AddInterruptHandler(new StubInterruptHandler())
            .AddStateReducers(new StubStateReducerRegistry())
            .ConfigureParallelism(16);

        Assert.Same(_sut, result);
    }

    // ── Test Doubles ─────────────────────────────────────────────

    private sealed class StubPromptRegistry : IPromptRegistry
    {
        public PromptTemplate? GetPrompt(string name) => null;
        public IReadOnlyList<PromptTemplate> GetAll() => [];
        public void Register(PromptTemplate template) { }
        public void Reload() { }
    }

    private sealed class StubInterruptHandler : IInterruptHandler
    {
        public Task<InterruptResponse> HandleAsync(
            InterruptRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InterruptResponse { Status = InterruptStatus.Approved });
    }

    private sealed class StubStateReducerRegistry : IStateReducerRegistry
    {
        public IStateReducer? Get(string key) => null;

        public void Register(IStateReducer reducer) { }
    }
}