# HelloWorld

The simplest possible Spectra workflow. No LLM provider, no API key — just the engine.

## What it demonstrates

- Registering Spectra with `AddSpectra()` and the .NET Generic Host
- Implementing a custom `IStep` (`EchoStep`)
- Loading a workflow from JSON with `JsonFileWorkflowStore`
- Running it with `IWorkflowRunner`
- Using `{{inputs.name}}` expressions in node parameters

## Run it

```bash
cd samples/HelloWorld
dotnet run
```

Expected output:

```
  [echo] Hello, World!
  [echo] Goodbye, World!

Errors: 0
```

## Next step

See the [Getting Started guide](../../docs/getting-started/) to build a workflow that calls an LLM.