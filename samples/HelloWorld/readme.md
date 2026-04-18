You're right. I overdesigned it and wrote docs, not a sample README.

For a sample project README, the job is:
show what it is, why it exists, how to run it, and what tiny concept it proves.

Here’s a developer-friendly version:

````markdown
# HelloWorld

The smallest possible Spectra sample.

This project shows that a Spectra workflow can run without any LLM provider or API key. It uses a custom `echo` step, loads the workflow from JSON, and executes two nodes in sequence.

## What this sample demonstrates

- registering Spectra with the .NET Generic Host
- adding a custom `IStep` implementation
- loading a workflow from `./workflows`
- passing runtime input with `WorkflowState`
- using `{{inputs.name}}` in workflow parameters

## Project structure

```text
HelloWorld/
├─ Program.cs
└─ workflows/
   └─ hello-world.json
````

* `Program.cs` wires up Spectra, registers `EchoStep`, loads the workflow, and runs it
* `workflows/hello-world.json` defines a two-node workflow: `greet -> farewell`

## How it works

The JSON workflow uses `stepType: "echo"` for both nodes.

The custom `EchoStep` reads the `message` parameter, prints it, and returns success.

This input:

```csharp
state.Inputs["name"] = "World";
```

is used here:

```json
"message": "Hello, {{inputs.name}}!"
```

So the workflow prints:

* `Hello, World!`
* `Goodbye, World!`

## Run it

```bash
cd samples/HelloWorld
dotnet run
```

Expected output:

```text
  [echo] Hello, World!
  [echo] Goodbye, World!

Errors: 0
```

## Why this sample exists

This is the Spectra hello world.

It proves the core engine works with:

* a workflow graph from JSON
* a custom step implementation
* simple runtime inputs

No model provider needed.


```

For this sample family, I should keep the README style much tighter and more “developer scanning GitHub” than “product documentation.”
```
