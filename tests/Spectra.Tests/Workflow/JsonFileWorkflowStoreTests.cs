using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;
using Xunit;

namespace Spectra.Tests.Workflow;

public class JsonFileWorkflowStoreTests
{
    private readonly string _tempDir;

    public JsonFileWorkflowStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"spectra-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private void WriteWorkflow(string name, string json)
        => File.WriteAllText(Path.Combine(_tempDir, $"{name}.workflow.json"), json);

    [Fact]
    public void Get_ExistingWorkflow_ReturnsDefinition()
    {
        // Arrange
        var json = """
        {
          "id": "test-wf",
          "name": "Test",
          "entryNodeId": "step1",
          "nodes": [{ "id": "step1", "stepType": "echo" }],
          "edges": []
        }
        """;
        WriteWorkflow("test-wf", json);
        var store = new JsonFileWorkflowStore(_tempDir);

        // Act
        var result = store.Get("test-wf");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-wf", result!.Id);
        Assert.Single(result.Nodes);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        var store = new JsonFileWorkflowStore(_tempDir);
        Assert.Null(store.Get("does-not-exist"));
    }

    [Fact]
    public void List_ReturnsAllValidWorkflows()
    {
        // Arrange
        WriteWorkflow("wf-a", """{ "id": "a", "nodes": [{ "id": "s1", "stepType": "echo" }], "edges": [] }""");
        WriteWorkflow("wf-b", """{ "id": "b", "nodes": [{ "id": "s1", "stepType": "echo" }], "edges": [] }""");
        var store = new JsonFileWorkflowStore(_tempDir);

        // Act
        var all = store.List();

        // Assert
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void List_SkipsInvalidJsonFiles()
    {
        // Arrange
        WriteWorkflow("good", """{ "id": "good", "nodes": [{ "id": "s1", "stepType": "echo" }], "edges": [] }""");
        WriteWorkflow("bad", "not json at all {{{");
        var store = new JsonFileWorkflowStore(_tempDir);

        // Act
        var all = store.List();

        // Assert
        Assert.Single(all);
        Assert.Equal("good", all[0].Id);
    }

    [Fact]
    public void Constructor_ThrowsOnMissingDirectory()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => new JsonFileWorkflowStore("/nonexistent/path/xyz"));
    }
}

public class TypeNameJsonConverterTests
{
    [Theory]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(bool), "bool")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(List<string>), "list<string>")]
    [InlineData(typeof(Dictionary<string, object>), "dictionary<string,object>")]
    public void Roundtrip_StateFieldWithType_PreservesType(Type expectedType, string jsonTypeName)
    {
        // Arrange
        var json = $$"""
        {
          "path": "Context.Test",
          "valueType": "{{jsonTypeName}}"
        }
        """;

        // Act — deserialize
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var field = System.Text.Json.JsonSerializer.Deserialize<StateFieldDefinition>(json, options);

        // Assert
        Assert.NotNull(field);
        Assert.Equal(expectedType, field!.ValueType);

        // Act — serialize back
        var serialized = System.Text.Json.JsonSerializer.Serialize(field, options);
        Assert.Contains($"\"{jsonTypeName}\"", serialized);
    }

    [Fact]
    public void Deserialize_UnknownType_Throws()
    {
        var json = """{ "path": "X", "valueType": "widget<foo>" }""";
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        Assert.Throws<System.Text.Json.JsonException>(
            () => System.Text.Json.JsonSerializer.Deserialize<StateFieldDefinition>(json, options));
    }
}