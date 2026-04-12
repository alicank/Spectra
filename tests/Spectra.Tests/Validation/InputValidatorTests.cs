using Spectra.Contracts.State;
using Spectra.Kernel.Validation;
using Xunit;

namespace Spectra.Tests.Validation;

public class InputValidatorTests
{
    [Fact]
    public void ValidateRequired_ReturnsSuccess_WhenAllKeysExist()
    {
        // Arrange
        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["repoPath"] = "/tmp/repo",
                ["mode"] = "full"
            }
        };

        // Act
        var result = InputValidator.ValidateRequired(state, "repoPath", "mode");

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.Message);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateRequired_ReturnsFailure_WhenKeysAreMissing()
    {
        // Arrange
        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["repoPath"] = "/tmp/repo",
                ["mode"] = null
            }
        };

        // Act
        var result = InputValidator.ValidateRequired(state, "repoPath", "mode", "outputDir");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Missing required inputs: mode, outputDir", result.Message);
        Assert.Contains("mode", result.Errors);
        Assert.Contains("outputDir", result.Errors);
    }

    [Fact]
    public void ValidateRepoPath_ReturnsFailure_WhenMissing()
    {
        // Arrange
        var state = new WorkflowState();

        // Act
        var result = InputValidator.ValidateRepoPath(state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("repoPath is required", result.Message);
    }

    [Fact]
    public void ValidateRepoPath_ReturnsFailure_WhenNotString()
    {
        // Arrange
        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["repoPath"] = 123
            }
        };

        // Act
        var result = InputValidator.ValidateRepoPath(state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("repoPath must be a non-empty string", result.Message);
    }

    [Fact]
    public void ValidateRepoPath_ReturnsFailure_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var missingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["repoPath"] = missingDir
            }
        };

        // Act
        var result = InputValidator.ValidateRepoPath(state);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Repository directory not found:", result.Message);
    }

    [Fact]
    public void ValidateRepoPath_NormalizesToAbsolutePath_WhenValid()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), tempDir);

            var state = new WorkflowState
            {
                Inputs = new()
                {
                    ["repoPath"] = relativePath
                }
            };

            // Act
            var result = InputValidator.ValidateRepoPath(state);

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(Path.GetFullPath(relativePath), state.Inputs["repoPath"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ValidateDirectoryPath_ReturnsFailure_WhenMissing()
    {
        // Arrange
        var state = new WorkflowState();

        // Act
        var result = InputValidator.ValidateDirectoryPath(state, "outputDir");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("outputDir is required", result.Message);
    }

    [Fact]
    public void ValidateDirectoryPath_ReturnsFailure_WhenNotString()
    {
        // Arrange
        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["outputDir"] = false
            }
        };

        // Act
        var result = InputValidator.ValidateDirectoryPath(state, "outputDir");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("outputDir must be a non-empty string", result.Message);
    }

    [Fact]
    public void ValidateDirectoryPath_ReturnsFailure_WhenMustExist_AndDirectoryMissing()
    {
        // Arrange
        var missingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["outputDir"] = missingDir
            }
        };

        // Act
        var result = InputValidator.ValidateDirectoryPath(state, "outputDir", mustExist: true);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Directory not found:", result.Message);
    }

    [Fact]
    public void ValidateDirectoryPath_AllowsMissingDirectory_WhenMustExistFalse()
    {
        // Arrange
        var missingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["outputDir"] = missingDir
            }
        };

        // Act
        var result = InputValidator.ValidateDirectoryPath(state, "outputDir", mustExist: false);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(missingDir), state.Inputs["outputDir"]);
    }

    [Fact]
    public void ValidateDirectoryPath_NormalizesAbsolutePath_WhenValid()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), tempDir);

            var state = new WorkflowState
            {
                Inputs = new()
                {
                    ["inputDir"] = relativePath
                }
            };

            // Act
            var result = InputValidator.ValidateDirectoryPath(state, "inputDir");

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(Path.GetFullPath(relativePath), state.Inputs["inputDir"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}