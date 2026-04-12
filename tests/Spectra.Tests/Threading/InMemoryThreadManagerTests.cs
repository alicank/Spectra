using Spectra.Contracts.Checkpointing;
using Spectra.Contracts.Threading;
using Spectra.Extensions.Checkpointing;
using Spectra.Kernel.Threading;

namespace Spectra.Tests.Threading;

public class InMemoryThreadManagerTests : ThreadManagerTestBase
{
    private readonly InMemoryCheckpointStore _checkpointStore = new();

    protected override IThreadManager CreateManager() =>
        new InMemoryThreadManager(_checkpointStore);

    protected override ICheckpointStore CreateCheckpointStore() =>
        _checkpointStore;
}