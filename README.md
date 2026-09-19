# There's No Official Claude Agent SDK for C#. Here's What to Use Instead

Companion sample for the blog post [There's No Official Claude Agent SDK for C#. Here's What to Use Instead](https://adrianbailador.github.io/blog/71-claude-code-agent-csharp/).

Two runnable console apps, both building the same small coding agent — one that can
read files, list a directory, write files, and run a shell command against a scoped
workspace — using the two paths that actually exist in C# today.

| Project | Approach |
|---|---|
| `ToolRunnerAgent` | The official `Anthropic` SDK's `client.Beta.Messages.ToolRunner` — the agent loop as an `IAsyncEnumerable`, one turn at a time |
| `MeaiAgent` | The same loop through `Microsoft.Extensions.AI`'s `UseFunctionInvocation()`, tools defined as plain C# methods |

## Requirements

- .NET 10 SDK
- An [Anthropic API key](https://platform.claude.com/settings/keys)

```bash
export ANTHROPIC_API_KEY=sk-ant-...
```

## Running it

```bash
dotnet run --project ToolRunnerAgent
dotnet run --project MeaiAgent
```

Both default to: *"Add a `.gitignore` for a .NET project to the workspace, then run
`git status`"* (or *"...then show me its contents"* for the MEAI version, which
doesn't wire up `run_command`). Pass your own task as arguments instead:

```bash
dotnet run --project ToolRunnerAgent -- List everything in the workspace, then read any file you find.
```

Each app creates a `workspace/` directory (or `$AGENT_WORKSPACE`, if set) next to the
executable and scopes every file operation to it — the agent cannot read or write
outside of it, `write_file` always asks for a `y/n` confirmation before it touches
disk, and `run_command` (in `ToolRunnerAgent` only) checks the command's program
name against an allowlist (`dotnet`, `git`, `ls`) before it even reaches the
confirmation prompt.

## What each project demonstrates

**`ToolRunnerAgent/Program.cs`**
- `BetaRunnableTool` + `BetaTool` — defining a tool as hand-written JSON Schema plus
  a `Run` delegate
- `client.Beta.Messages.ToolRunner(...)` driving the full tool-use loop
- Iterating the loop turn by turn via `await foreach`, printing `stop_reason` and
  every tool call as it happens

**`MeaiAgent/Program.cs`**
- The same four tools as plain C# methods with `[Description]` attributes
- `AIFunctionFactory.Create` building the tool schema by reflection instead of by hand
- `AnthropicClient().AsIChatClient(...)` + `UseFunctionInvocation()` running the same
  loop, returning only the final response

## Notes / things worth knowing before you reuse this

- **This is a demo, not a sandbox.** The workspace scoping and allowlist stop the
  obvious mistakes, but `run_command` still executes real shell commands on your
  machine once you type `y`. Don't point `AGENT_WORKSPACE` at anything you care about,
  and don't widen `allowedPrograms` without thinking about what you're allowing.
- **`MeaiAgent` doesn't wire up `run_command`** on purpose — command execution behind
  an automatic function-invocation loop, with no per-call visibility before it runs,
  is a worse default than `ToolRunnerAgent`'s turn-by-turn loop where you see the
  `tool_use` block before deciding whether the confirmation prompt should say yes.
- Built and compiled against `Anthropic` 12.49.0 and `Microsoft.Extensions.AI` 10.10.0
  (whatever `dotnet add package` resolves to today may be newer). Both projects build
  clean; running them end-to-end requires a real API key, which this repo doesn't ship.

## License

MIT — do whatever you want with it.
