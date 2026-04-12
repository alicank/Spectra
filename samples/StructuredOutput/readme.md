# StructuredOutput

Extracts structured contact information from unstructured text using `StructuredOutputStep` with a JSON schema. The schema guides the LLM via the prompt, and validation happens client-side (Pydantic-style) — no provider-specific structured output API required.

## What it demonstrates

- `StructuredOutputStep` (`stepType: "structured_output"`) — LLM completion with client-side JSON parsing
- `jsonSchema` parameter — injected into the prompt as guidance, not pushed to the provider API
- Flat dictionary outputs — extracted fields are directly accessible (no `parsedResponse` wrapper)
- `ExtractJson` — handles LLM responses wrapped in markdown code blocks
- `NormalizeJsonElement` — converts `JsonElement` to plain CLR types (no leaks to downstream steps)
- `AddAnthropic` — uses Claude as the LLM provider

## Prerequisites

```bash
# bash
export ANTHROPIC_API_KEY="your-key"

# PowerShell
$env:ANTHROPIC_API_KEY="your-key"
```

## Run it

```bash
cd samples/StructuredOutput
dotnet run
```

## Expected output

```
Extracted contact:
  name      : Marie Dupont
  company   : Electrosoft Tech Company
  email     : marie.dupont@electrosoft
  phone     : +33 4 78 00 12 34
  role      : Technical Director
```

## How it works

Unlike provider-level structured output (which varies by provider and model), Spectra's `StructuredOutputStep` takes a Pydantic-style approach:

1. Sets `outputMode: "json"` — universally supported across all providers
2. Injects the schema into the system prompt so the LLM knows the expected shape
3. Extracts JSON from the response (handles ```` ```json ``` ```` wrapping)
4. Parses and normalizes to plain CLR types — downstream steps get `Dictionary<string, object?>`, not `JsonElement`

This means it works with **any provider** — Anthropic, OpenRouter, OpenAI, Ollama, Gemini — without requiring model-specific structured output support.

## When to use StructuredOutputStep

Use it when you need the LLM response as typed data — entity extraction, classification, form filling, or any task where downstream code consumes the output programmatically.

For free-text responses, use `PromptStep` instead (see the `PromptBasic` sample).