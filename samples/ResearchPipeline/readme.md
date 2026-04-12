# ResearchPipeline — The Final Boss

A production-realistic Compliance Incident Analysis Pipeline that combines nearly every Spectra feature into a single end-to-end workflow. This is the capstone sample — the proof that all the pieces compose cleanly.

## The Scenario

A financial services firm's automated surveillance system detects suspicious trading activity on desk FX-7. The pipeline:

1. **Classifies** the incident type using a prompt node
2. **Investigates** using an agent with custom tools (incident search + transaction query)
3. **Stores findings** in memory for cross-node access
4. **Fans out** to two parallel analysis agents (risk assessment + regulatory impact)
5. **Merges** results using a `WaitForAll` barrier
6. **Pauses** for human compliance officer review (declarative interrupt)
7. **Remediates** via a subgraph — an isolated child workflow where a code agent reads and modifies a CSV of flagged transactions
8. **Generates** a structured final compliance report

## The Graph

```
┌───────────┐    ┌──────────────┐    ┌────────────────┐
│  classify  │──▶│  investigate  │──▶│  store-findings │
│  (prompt)  │   │  (agent+tools)│   │  (memory_store) │
└───────────┘    └──────────────┘    └────────┬───────┘
                                              │
                              ┌───────────────┼───────────────┐
                              │                               │
                    ┌─────────▼──────────┐         ┌─────────▼───────────┐
                    │  risk-assessment   │         │  regulatory-impact  │
                    │  (prompt)          │         │  (prompt)           │
                    └─────────┬──────────┘         └─────────┬───────────┘
                              │                               │
                              └───────────┬───────────────────┘
                                          │
                              ┌───────────▼───────────┐
                              │  merge-analyses       │
                              │  (WaitForAll)         │
                              └───────────┬───────────┘
                                          │
                              ┌───────────▼───────────┐
                              │  human-review         │
                              │  ⏸ InterruptBefore    │
                              └───────────┬───────────┘
                                          │
                              ┌───────────▼───────────┐
                              │  remediation          │
                              │  (subgraph)           │
                              │  ┌───────────────────┐│
                              │  │ flag-transactions  ││
                              │  │ (agent+file tools) ││
                              │  └───────────────────┘│
                              └───────────┬───────────┘
                                          │
                              ┌───────────▼───────────┐
                              │  generate-report      │
                              │  (prompt)             │
                              └───────────────────────┘
```

## Features Demonstrated

| Feature | Where | Sample # |
|---------|-------|----------|
| JSON workflow definition | `workflows/*.json` | 07 |
| PromptStep (single LLM call) | `classify`, `risk-assessment`, `regulatory-impact`, `generate-report` | 08 |
| AgentStep (tool loop) | `investigate` | 11 |
| Custom tools (ITool) | `SearchIncidentsTool`, `QueryTransactionsTool` | 11 |
| MemoryStoreStep | `store-findings` | 13 |
| Parallel fan-out | `store-findings` → `risk-assessment` + `regulatory-impact` | 03 |
| WaitForAll merge | `merge-analyses` | 03 |
| Declarative InterruptBefore | `human-review` | 06 |
| ResumeWithResponseAsync | Phase 2 resume after approval | 06 |
| SubgraphStep (child workflow) | `remediation` → `csv-remediation` | — |
| File read/write tools | `ReadFileTool`, `WriteFileTool` (Anthropic-style coding) | — |
| FileCheckpointStore | Checkpoint at every node | 05 |
| Console event streaming | Full observability via `AddConsoleEvents` | 01 |
| Fallback providers | Primary → fallback on failure | 14 |
| Agent definitions in JSON | 6 agents with distinct system prompts | 07 |

## Prerequisites

```bash
# bash
export OPENROUTER_API_KEY="your-key"

# PowerShell
$env:OPENROUTER_API_KEY="your-key"
```

## Run it

```bash
cd samples/ResearchPipeline
dotnet run
```

## What to look for

**Phase 1 — Classification through Analysis:**
- `classify` outputs a single category (e.g., `TRADE_SURVEILLANCE`)
- `investigate` calls `search_incidents` and `query_transactions` tools autonomously
- `store-findings` persists investigation results in memory
- `risk-assessment` and `regulatory-impact` run in parallel (check `ParallelBatchStartedEvent`)
- `merge-analyses` waits for both branches (`WaitForAll: true`)

**Interrupt — Human Review:**
- `StepInterruptedEvent` with `IsDeclarative: true` pauses the pipeline
- Checkpoint status shows `Interrupted` with `PendingInterrupt`
- `ResumeWithResponseAsync` with approval clears the interrupt

**Phase 2 — Remediation and Report:**
- `remediation` subgraph executes an isolated child workflow
- Code agent reads `./data/flagged-transactions.csv` via `read_file` tool
- Code agent writes modified CSV with `RiskFlag=FLAGGED` for Meridian Capital transactions
- `generate-report` synthesizes everything into a structured compliance report

**Post-run verification:**
- Check `./data/flagged-transactions.csv` — Meridian Capital rows should have `FLAGGED` in the `RiskFlag` column
- Total errors should be 0

