using Spectra.Contracts.Memory;

namespace Spectra.Tests.Memory;

public class FileMemoryStoreTests : MemoryStoreTestBase<FileMemoryStore>, IDisposable
{
    private readonly string _tempDir;

    public FileMemoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "spectra-memory-tests", Guid.NewGuid().ToString());
    }

    protected override FileMemoryStore CreateStore() => new(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}