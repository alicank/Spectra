using Spectra.Contracts.Checkpointing;
using Spectra.Extensions.Checkpointing;

namespace Spectra.Tests.Checkpointing;

public class FileCheckpointStoreTests : CheckpointStoreTestBase<FileCheckpointStore>, IDisposable
{
    private readonly string _tempDir;

    public FileCheckpointStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "spectra-tests", Guid.NewGuid().ToString());
    }

    protected override FileCheckpointStore CreateStore() => new(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}