using Spectra.Contracts.Checkpointing;
using Spectra.Extensions.Checkpointing;

namespace Spectra.Tests.Checkpointing;

public class InMemoryCheckpointStoreTests : CheckpointStoreTestBase<InMemoryCheckpointStore>
{
    protected override InMemoryCheckpointStore CreateStore() => new();
}