using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;
using Spectra.Kernel.Execution;
using Xunit;

namespace Spectra.Tests.Execution;

public class StateMapperTests
{
    [Fact]
    public void ResolveInputs_StartsWithParameters_ThenOverridesWithMappings()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["RepoPath"] = "/tmp/repo"
            }
        };

        var node = new NodeDefinition
        {
            Id = "scan",
            StepType = "TestStep",
            Parameters = new()
            {
                ["path"] = "/default/path",
                ["mode"] = "full"
            },
            InputMappings = new()
            {
                ["path"] = "Inputs.RepoPath"
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, state);

        // Assert
        Assert.Equal("/tmp/repo", inputs["path"]);
        Assert.Equal("full", inputs["mode"]);
    }

    [Fact]
    public void ResolveInputs_RendersTemplateStrings_FromState()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["RepoPath"] = "/tmp/repo"
            },
            Context = new()
            {
                ["scan"] = new Dictionary<string, object?>
                {
                    ["count"] = 3
                }
            }
        };

        var node = new NodeDefinition
        {
            Id = "render",
            StepType = "TestStep",
            Parameters = new()
            {
                ["message"] = "Repo={{Inputs.RepoPath}}, Count={{Context.scan.count}}"
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, state);

        // Assert
        Assert.Equal("Repo=/tmp/repo, Count=3", inputs["message"]);
    }

    [Fact]
    public void ResolveInputs_ExactTemplate_ReturnsRawObject_NotString()
    {
        // Arrange
        var mapper = new StateMapper();
        var files = new List<string> { "a.cs", "b.cs" };

        var state = new WorkflowState
        {
            Context = new()
            {
                ["scan"] = new Dictionary<string, object?>
                {
                    ["files"] = files
                }
            }
        };

        var node = new NodeDefinition
        {
            Id = "consumer",
            StepType = "TestStep",
            Parameters = new()
            {
                ["files"] = "{{Context.scan.files}}"
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, state);

        // Assert
        var resolved = Assert.IsAssignableFrom<List<string>>(inputs["files"]);
        Assert.Equal(2, resolved.Count);
        Assert.Equal("a.cs", resolved[0]);
        Assert.Equal("b.cs", resolved[1]);
    }

    [Fact]
    public void ResolveInputs_DoesNotTreat_JsxLikeDoubleCurly_AsTemplate()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState();

        var jsxLike = "{{ opacity: 0, y: -10 }}";
        var node = new NodeDefinition
        {
            Id = "jsx",
            StepType = "TestStep",
            Parameters = new()
            {
                ["content"] = jsxLike
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, state);

        // Assert
        Assert.Equal(jsxLike, inputs["content"]);
    }

    [Fact]
    public void ResolveInputs_DoesNotBreak_CssStyleDoubleCurlySyntax()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState();

        var cssLike = "style={{ width: \"100%\" }}";
        var node = new NodeDefinition
        {
            Id = "css",
            StepType = "TestStep",
            Parameters = new()
            {
                ["content"] = cssLike
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, state);

        // Assert
        Assert.Equal(cssLike, inputs["content"]);
    }

    [Fact]
    public void ResolveInputs_OnlyResolvesKnownRoots()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState
        {
            Inputs = new()
            {
                ["RepoPath"] = "/tmp/repo"
            }
        };

        var node = new NodeDefinition
        {
            Id = "unknown-root",
            StepType = "TestStep",
            Parameters = new()
            {
                ["content"] = "{{Unknown.Root}} and {{Inputs.RepoPath}}"
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, state);

        // Assert
        Assert.Equal("{{Unknown.Root}} and /tmp/repo", inputs["content"]);
    }

    [Fact]
    public void GetValueFromPath_ReturnsNestedContextValue()
    {
        // Arrange
        var state = new WorkflowState
        {
            Context = new()
            {
                ["scan"] = new Dictionary<string, object?>
                {
                    ["summary"] = new Dictionary<string, object?>
                    {
                        ["count"] = 42
                    }
                }
            }
        };

        // Act
        var value = StateMapper.GetValueFromPath(state, "Context.scan.summary.count");

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void GetValueFromPath_ReturnsNull_ForMissingPath()
    {
        // Arrange
        var state = new WorkflowState();

        // Act
        var value = StateMapper.GetValueFromPath(state, "Context.missing.value");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void SetValueAtPath_CreatesIntermediateDictionaries()
    {
        // Arrange
        var state = new WorkflowState();

        // Act
        StateMapper.SetValueAtPath(state, "Context.scan.files", new List<string> { "a.cs" });

        // Assert
        var scan = Assert.IsType<Dictionary<string, object?>>(state.Context["scan"]);
        var files = Assert.IsType<List<string>>(scan["files"]);
        Assert.Single(files);
        Assert.Equal("a.cs", files[0]);
    }

    [Fact]
    public void ApplyOutputs_UsesOutputMappings()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState();

        var node = new NodeDefinition
        {
            Id = "writer",
            StepType = "TestStep",
            OutputMappings = new()
            {
                ["result"] = "Context.Processed.Value",
                ["artifact"] = "Artifacts.Output.File"
            }
        };

        var outputs = new Dictionary<string, object?>
        {
            ["result"] = 123,
            ["artifact"] = "report.json"
        };

        // Act
        mapper.ApplyOutputs(node, state, outputs);

        // Assert
        var processed = Assert.IsType<Dictionary<string, object?>>(state.Context["Processed"]);
        Assert.Equal(123, processed["Value"]);

        var output = Assert.IsType<Dictionary<string, object?>>(state.Artifacts["Output"]);
        Assert.Equal("report.json", output["File"]);
    }

    [Fact]
    public void ApplyOutputs_WithoutMappings_StoresOutputsUnderNodeId()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState();

        var node = new NodeDefinition
        {
            Id = "read",
            StepType = "ReadFile"
        };

        var outputs = new Dictionary<string, object?>
        {
            ["content"] = "hello",
            ["lineCount"] = 1
        };

        // Act
        mapper.ApplyOutputs(node, state, outputs);

        // Assert
        var stored = Assert.IsType<Dictionary<string, object?>>(state.Context["read"]);
        Assert.Equal("hello", stored["content"]);
        Assert.Equal(1, stored["lineCount"]);
    }

    [Fact]
    public void ResolveInputs_NormalizesJsonElement_ObjectArrayAndPrimitiveValues()
    {
        // Arrange
        var mapper = new StateMapper();
        var json = """
        {
          "config": {
            "enabled": true,
            "threshold": 5,
            "items": ["a", "b"]
          }
        }
        """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var configElement = doc.RootElement.GetProperty("config");

        var node = new NodeDefinition
        {
            Id = "normalize",
            StepType = "TestStep",
            Parameters = new()
            {
                ["config"] = configElement
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, new WorkflowState());

        // Assert
        var config = Assert.IsType<Dictionary<string, object?>>(inputs["config"]);
        var threshold = Assert.IsAssignableFrom<IConvertible>(config["threshold"]);
        Assert.Equal(true, config["enabled"]);
        Assert.Equal(5, Convert.ToInt32(threshold));

        var items = Assert.IsType<List<object?>>(config["items"]);
        Assert.Equal(2, items.Count);
        Assert.Equal("a", items[0]);
        Assert.Equal("b", items[1]);
    }

    // ── Nodes namespace tests ──

    [Fact]
    public void GetValueFromPath_Nodes_ResolvesNodeOutputField()
    {
        // Arrange
        var state = new WorkflowState
        {
            Nodes = new()
            {
                ["flat-render"] = new Dictionary<string, object?>
                {
                    ["tree"] = "rendered-tree-value"
                }
            }
        };

        // Act
        var value = StateMapper.GetValueFromPath(state, "nodes.flat-render.tree");

        // Assert
        Assert.Equal("rendered-tree-value", value);
    }

    [Fact]
    public void ResolveInputs_NodesTemplate_ResolvesFromNodesState()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState
        {
            Nodes = new()
            {
                ["someNode"] = new Dictionary<string, object?>
                {
                    ["someField"] = "hello-from-node"
                }
            }
        };

        var node = new NodeDefinition
        {
            Id = "consumer",
            StepType = "TestStep",
            Parameters = new()
            {
                ["data"] = "{{nodes.someNode.someField}}"
            }
        };

        // Act
        var inputs = mapper.ResolveInputs(node, state);

        // Assert
        Assert.Equal("hello-from-node", inputs["data"]);
    }

    [Fact]
    public void ApplyOutputs_WithMappings_DoesNotWriteToContextUnderNodeId()
    {
        // Arrange
        var mapper = new StateMapper();
        var state = new WorkflowState();

        var node = new NodeDefinition
        {
            Id = "mapped-node",
            StepType = "TestStep",
            OutputMappings = new()
            {
                ["result"] = "Context.myResult"
            }
        };

        var outputs = new Dictionary<string, object?>
        {
            ["result"] = "value"
        };

        // Act
        mapper.ApplyOutputs(node, state, outputs);

        // Assert — outputs go to mapped location, not under node id in Context
        Assert.False(state.Context.ContainsKey("mapped-node"));
    }

    [Fact]
    public void SetValueAtPath_Nodes_WritesToNodesDictionary()
    {
        // Arrange
        var state = new WorkflowState();

        // Act
        StateMapper.SetValueAtPath(state, "Nodes.myNode.output", "test-value");

        // Assert
        var myNode = Assert.IsType<Dictionary<string, object?>>(state.Nodes["myNode"]);
        Assert.Equal("test-value", myNode["output"]);
    }
}