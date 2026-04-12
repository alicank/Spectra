using Spectra.Contracts.Memory;

namespace Spectra.Tests.Memory;

public class InMemoryMemoryStoreTests : MemoryStoreTestBase<InMemoryMemoryStore>
{
    protected override InMemoryMemoryStore CreateStore() => new();
}