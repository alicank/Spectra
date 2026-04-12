using Spectra.Contracts.State;
using Spectra.Contracts.Workflow;
using Spectra.Workflow;
using Xunit;

namespace Spectra.Tests.Workflow;

public class WorkflowSerializerTests
{
    [Fact]
    public void Roundtrip_BuilderToJsonAndBack_PreservesDefinition()
    {
        // Arrange
        var original = WorkflowBuilder.Create("roundtrip-test")
            .WithName("Roundtrip Workflow")
            .WithDescription("Tests JSON serialization roundtrip")
            .WithVersion(2)
            .WithMaxConcurrency(8)
            .WithDefaultTimeout(TimeSpan.FromSeconds(30))
            .WithMaxNodeIterations(50)
            .AddNode("parse", "file-reader")
            .AddNode("analyze", "llm-call", n => n
                .WithAgent("reviewer")
                .WithParameter("format", "json")
                .MapInput("prompt", "Context.UserInput")
                .MapOutput("result", "Context.Analysis"))
            .AddNode("report", "formatter", n => n.WaitForAll())
            .AddEdge("parse", "analyze")
            .AddEdge("analyze", "report", condition: "Context.HasData == true")
            .AddAgent("reviewer", "anthropic", "claude-sonnet-4-20250514", a => a
                .WithTemperature(0.3)
                .WithMaxTokens(4096)
                .WithSystemPrompt("You are a code reviewer."))
            .AddStateField<string>("Context.UserInput")
            .AddStateField("Context.Messages", typeof(List<string>), "append")
            .SetEntryNode("parse")
            .Build();

        // Act
        var json = WorkflowSerializer.Serialize(original);
        var restored = WorkflowSerializer.Deserialize(json);

        // Assert — metadata
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Description, restored.Description);
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.EntryNodeId, restored.EntryNodeId);
        Assert.Equal(original.MaxConcurrency, restored.MaxConcurrency);
        Assert.Equal(original.DefaultTimeout, restored.DefaultTimeout);
        Assert.Equal(original.MaxNodeIterations, restored.MaxNodeIterations);

        // Assert — nodes
        Assert.Equal(original.Nodes.Count, restored.Nodes.Count);
        Assert.Equal("llm-call", restored.Nodes[1].StepType);
        Assert.Equal("reviewer", restored.Nodes[1].AgentId);
        Assert.Equal("json", restored.Nodes[1].Parameters["format"]?.ToString());
        Assert.Equal("Context.UserInput", restored.Nodes[1].InputMappings["prompt"]);
        Assert.True(restored.Nodes[2].WaitForAll);

        // Assert — edges
        Assert.Equal(2, restored.Edges.Count);
        Assert.Equal("Context.HasData == true", restored.Edges[1].Condition);

        // Assert — agents
        Assert.Single(restored.Agents);
        Assert.Equal("reviewer", restored.Agents[0].Id);
        Assert.Equal("anthropic", restored.Agents[0].Provider);
        Assert.Equal(0.3, restored.Agents[0].Temperature);

        // Assert — state fields
        Assert.Equal(2, restored.StateFields.Count);
        Assert.Equal(typeof(string), restored.StateFields[0].ValueType);
        Assert.Equal(typeof(List<string>), restored.StateFields[1].ValueType);
        Assert.Equal("append", restored.StateFields[1].ReducerKey);
    }

    [Fact]
    public void Deserialize_FromRawJson_ProducesValidDefinition()
    {
        // Arrange
        var json = """
        {
          "id": "hello-world",
          "name": "Hello World",
          "version": 1,
          "entryNodeId": "greet",
          "nodes": [
            {
              "id": "greet",
              "stepType": "echo",
              "parameters": { "message": "Hello from JSON!" }
            }
          ],
          "edges": [],
          "agents": [],
          "stateFields": [],
          "maxConcurrency": 4,
          "defaultTimeout": "00:05:00",
          "maxNodeIterations": 100
        }
        """;

        // Act
        var definition = WorkflowSerializer.Deserialize(json);

        // Assert
        Assert.Equal("hello-world", definition.Id);
        Assert.Equal("Hello World", definition.Name);
        Assert.Single(definition.Nodes);
        Assert.Equal("greet", definition.EntryNodeId);
        Assert.Equal("echo", definition.Nodes[0].StepType);
        Assert.Equal("Hello from JSON!", definition.Nodes[0].Parameters["message"]?.ToString());
    }

    [Fact]
    public void TryDeserialize_WithInvalidJson_ReturnsNull()
    {
        // Act
        var result = WorkflowSerializer.TryDeserialize("not valid json {{{");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Serialize_ProducesCamelCaseJson()
    {
        // Arrange
        var definition = WorkflowBuilder.Create("case-test")
            .AddNode("step1", "echo")
            .Build();

        // Act
        var json = WorkflowSerializer.Serialize(definition);

        // Assert
        Assert.Contains("\"entryNodeId\"", json);
        Assert.Contains("\"stepType\"", json);
        Assert.Contains("\"maxConcurrency\"", json);
        Assert.DoesNotContain("\"EntryNodeId\"", json);
    }
}