# ChatAndMultipleMcps

## About this project

`ChatAndMultipleMcps` is an interactive .NET console chat application built on
`Microsoft.Extensions.AI` and the Model Context Protocol (MCP). It demonstrates
how one chat client can use tools from several MCP servers, while also showing
how to keep specialist tools behind declarative agents.

The application deliberately keeps the orchestration code visible. It does not
hide the conversation/tool loop behind a higher-level agent framework. The
important pieces are:

- a main streaming chat loop;
- multiple MCP clients, including an in-process MCP server and externally
  configured MCP servers;
- saved Markdown prompts;
- Markdown-defined declarative agents;
- MCP sampling, elicitation, and client-roots callbacks; and
- switchable console logging and diagnostic output.

The executable project targets .NET 10. The solution also contains the shared
`McpClientUtilities` project, which loads MCP server configurations and creates
the client proxies used here.

## The services

There are two meanings of “service” in this project:

1. .NET dependency-injection services registered in `Program.cs`.
2. MCP servers, which expose tools and prompts through the MCP protocol.

### .NET services registered by the host

`Program.Main` creates a generic host and registers the following important
services:

- keyed `IChatClient` named `main`, used by the interactive chat and by
  declarative agents;
- keyed `IChatClient` named `SummarySamplingClient`, used when the Summary MCP
  server asks the client to sample a model response;
- `McpProxyFactoryService`, which loads MCP configurations and starts one proxy
  per configured server;
- `VerboseState`, shared by logging, command handling, and tool error display;
- `IConsoleTerminal` and `ConsoleLineEditor`, used by the interactive UI;
- `IPromptCatalog`, implemented by `MarkdownPromptCatalog`;
- `IDeclarativeAgentCatalog`, implemented by
  `MarkdownDeclarativeAgentCatalog`; and
- the hosted `ChatService`, which owns startup, tool discovery, and the chat
  loop.

The current client selection is in `Program.cs`: Azure is assigned to `main`,
and DeepSeek is assigned to `SummarySamplingClient`. An OpenAI client is also
constructed as an example, but is not currently selected for either role.

### MCP servers in this sample

The local MCP server is registered in the `AddMcpServer()` chain in
`Program.cs`:

```csharp
builder.Services
    .AddMcpServer()
    .WithPipesStreamServerTransport()
    .WithPrompts(PromptTemplatesMcpServer.CreatePrompts(promptCatalog))
    .WithTools<LocalFilesMcpServer>()
    .WithTools<ClientRootsMcpServer>()
    .WithTools<AskUserMcpServer>()
    .WithPrompts<AskUserMcpServer>()
    .WithTools<SummaryMcpServer>()
    .WithPrompts<SummaryMcpServer>()
    .WithTools<TimeMcpServer>()
    .WithPrompts<TimeMcpServer>();
```

The in-process server contains these capabilities:

| Server/component | MCP capabilities | Purpose |
| --- | --- | --- |
| `LocalFilesMcpServer` | `localFiles_getFilenames`, `localFiles_getDocument` | Lists and reads files below the configured `RootFolder`. |
| `ClientRootsMcpServer` | `clientRoots_getRoots` | Asks the MCP client for its configured roots. |
| `AskUserMcpServer` | `askuser_askquestion` and `askuser_system` | Lets the model ask the user for clarification through MCP elicitation. |
| `SummaryMcpServer` | `summary_createSummary` and `summary_prompt` | Creates summaries through MCP sampling and exposes the corresponding prompt. |
| `TimeMcpServer` | `time_now` and `time_system` | Returns local date/time/time-zone information and provides usage guidance. |
| `PromptTemplatesMcpServer` | one MCP prompt for each saved prompt | Makes the `.prompt.md` catalog available as MCP prompts. |

The local server is not started as a separate process. The MCP SDK server uses
pipe streams, and the client side communicates with it through the
`InProcessPipes` registered by `WithPipesStreamServerTransport()`.

### External MCP server configurations

`appsettings.json` points `McpProxyFactoryService` at the `McpServers` folder:

```json
{
  "mcpConfigurationDirectory": "../../../McpServers"
}
```

The shared `McpClientUtilities` library scans the top level of that directory
for `*.json` files. Each file may contain either a top-level `servers` object or
a top-level `mcpServers` object. Each entry is interpreted as one of:

- stdio transport, using `command`, `args`, and optional `env`;
- HTTP transport, using the MCP HTTP transport fields; or
- the library's custom in-process transport format.

The sample configurations currently recognized by the loader are:

- `playwright-mcp.json`: starts Playwright MCP through `npx`;
- `pubchem-local.json`: starts the local PubChem MCP server through `uvx`.

`inprocess.json` and `perplexityai.json` use `X-mcpServers` rather than
`mcpServers`. They are therefore intentionally ignored by the current loader;
the `X-` prefix acts as a convenient switch for these sample configurations.
The in-process C# servers are registered directly in `Program.cs`, so they do
not need entries in `inprocess.json`.

MCP servers provide the tools and prompts; the main application decides which
of those tools are visible to the main model and which belong to a declarative
agent. MCP system prompts are also used as guidance, but only for servers
whose tools are visible in the corresponding chat context.

## Manually serving the loop

The application manually drives both the MCP tool loop and the model streaming
loop. The startup sequence is:

1. `Program.Main` loads provider secrets into environment variables, creates the
   main and sampling `IChatClient` instances, and builds the generic host.
2. The host registers the in-process MCP server over pipe streams and registers
   `ChatService` as a hosted service.
3. `ChatService.ExecuteAsync` calls
   `McpProxyFactoryService.StartAll(...)`. The factory reads all active JSON MCP
   configurations, inserts a special `InProcessMcpServer` configuration, and
   starts the proxies in parallel.
4. For every started proxy, `ChatService` calls `ListToolsAsync()` when the
   server advertises tools. Each returned `McpClientTool` is wrapped in an
   `McpToolRegistration` containing the configuration name, server display name,
   and `AIFunction`.
5. When a server advertises prompts, the app calls `ListPromptsAsync()` and
   invokes prompts whose names end in `system`. The text messages returned by
   those prompts are joined into that server's MCP system prompt.
6. `DeclarativeAgentToolResolver` partitions the discovered tools. Tools
   assigned to an agent are removed from the main chat. Unassigned tools remain
   direct main-chat tools, and each resolved agent becomes one wrapper
   `AIFunction`.
7. The resolver also builds the main system prompt from the system prompts of
   servers that contribute direct tools. If a direct tool name contains
   `browse`, the app adds a short browser-use instruction.
8. `ChatLoop` maintains a `List<ChatMessage>` conversation. It reads a user
   message, handles slash commands, expands saved prompts, and builds a
   `ChatOptions` object containing the currently available direct tools and any
   selected agent wrapper.
9. The main `IChatClient` is called with
   `GetStreamingResponseAsync`. `StreamingManager` consumes each
   `ChatResponseUpdate`, writes text tokens to the terminal, records usage, and
   collects function calls.
10. If the model requested tools, the assistant message containing those calls
    is added to the conversation. `ProcessToolRequest` invokes each matching
    `AIFunction`, catches ordinary tool failures, prints the routing and result,
    and appends a `FunctionResultContent` message.
11. Because the last operation was a tool call, the loop goes back to the model
    without asking the user for another message. This continues until the model
    returns ordinary text, a refusal, or a length/other finish reason.

The direct MCP path looks like this:

```text
main model -> AIFunction -> MCP client proxy -> MCP server tool
            <- function result <- MCP response <--------------
```

There is no hidden tool dispatcher in the application. `ProcessToolRequest`
performs the invocation explicitly, which makes the message sequence and the
place where tool results re-enter the conversation easy to inspect.

### Tool visibility and delegation

By default, the main model receives all direct tools. Agent-owned tools are not
sent to the main model as schemas; the main model only sees an agent wrapper if
that agent is selected. A selected agent is required on the first model turn of
a new user request. If exactly one agent is selected, the options require that
specific agent function; otherwise any tool may be required.

After an agent wrapper returns, its final text is added to the main conversation
as the agent tool result. The main model can then continue reasoning or call a
direct tool.

## Declarative prompts

Saved prompts live in the repository-level `.prompts` directory. The location
is configured by `Prompts:Directory` in `appsettings.json` and defaults to
`./.prompts`.

Each file must be named `*.prompt.md` and contain YAML front matter followed by
the Markdown prompt body:

```markdown
---
description: chemical data for aspirin
---
Give me the chemical data for aspirin, including the CAS number.
```

The filename without `.prompt.md` is the prompt name. The description is
required, the body must not be empty, and prompt names must be unique
case-insensitively.

`MarkdownPromptCatalog` locates the repository root, resolves the configured
directory, validates that it remains inside the repository, reads the files at
startup, and stores both an ordered list and a case-insensitive name lookup.
Prompts are not re-read during the chat session.

Prompts can be used in three ways:

- `/prompt <name>` displays the prompt choices and submits the selected body;
- typing the prompt name directly preserves the older shorthand behavior; and
- `PromptTemplatesMcpServer` exposes every catalog entry as an MCP prompt, so
  another MCP client can request the same text through the protocol.

The `/system` command is separate: it shows, replaces, or clears the assembled
system prompt for the current chat. It does not edit a `.prompt.md` file.

## Declarative agents

Declarative agents live in the repository-level `.agents` directory. The
location is configured by `DeclarativeAgents:Directory` and defaults to
`./.agents`.

Each `*.agent.md` file contains YAML front matter and a Markdown instruction
body:

```markdown
---
name: Chemical Ingredients Expert
description: Investigates chemical ingredients using PubChem.
tools:
  - pubchem/*
---
You are an expert in chemical ingredients and chemical compounds.
Use PubChem to ground factual claims.
```

`name` is optional; when omitted, the filename supplies the name. `description`
and a non-empty instruction body are required. `tools` may be a YAML sequence
or a comma-separated string. Supported selectors are:

- `*`: every discovered MCP tool;
- `server/*`: every tool from the named MCP configuration; and
- `server/tool_name`: one particular tool.

The catalog loads and validates all agents before chat begins. It creates a
function name by converting the agent name to a lowercase identifier prefixed
with `agent_`; for example, `Chemical Ingredients Expert` becomes
`agent_chemical_ingredients_expert`.

### How an agent fits into the main loop

At startup, `DeclarativeAgentToolResolver` resolves each selector against the
discovered MCP tools. For every agent it stores:

- the Markdown instructions;
- the selected MCP tool functions; and
- the MCP system prompts belonging to the selected tools' servers.

Those tools are removed from the main direct-tool set. The agent itself is
registered as a single wrapper function. `/agent <name>` selects one agent for
delegation, selecting another agent switches to it, and selecting the same
agent again disables it.

The main model therefore sees a small interface such as
`agent_chemical_ingredients_expert`, rather than all of the PubChem tool
schemas. This is the main purpose of the partitioning: specialist tool schemas
and specialist MCP guidance stay inside the specialist conversation.

### How an agent calls tools

`DeclarativeAgentRunner` starts a fresh, stateless private conversation for
each invocation:

1. The agent Markdown instructions and its resolved MCP system prompt become a
   system message.
2. The delegated request becomes the user message.
3. The same `main` `IChatClient` is called synchronously with only the agent's
   assigned tool functions.
4. On the first model turn, tool use is required when the agent has tools. Later
   turns use automatic tool selection.
5. Every requested tool is invoked directly through its `AIFunction`, and its
   result is appended to the private conversation.
6. The loop stops when the agent produces text and returns only that final text
   to the main chat.

An agent may perform at most ten model turns. An agent with no assigned tools
uses `ChatToolMode.None` and can answer directly from its instructions. Tool
errors are returned to the private model as text; exception details are also
printed when verbose mode is enabled.

## Elicitation services

MCP elicitation is the request/response path used when an MCP server needs a
human answer. `McpClientApp.GetMcpClientOptions()` installs
`ElicitationHandlerQA` in the client handlers.

The `askuser_askquestion` tool demonstrates the complete path:

1. The main model calls the AskUser MCP tool with a question.
2. `AskUserMcpServer` calls `server.ElicitAsync` with a schema containing a
   required string field named `answer`.
3. The client-side `ElicitationHandlerQA` prints the request and reads one line
   from the console.
4. The handler returns an accepted `ElicitResult` whose `answer` value is a
   JSON string.
5. The server validates the action and answer, then returns the text to the
   model.

Decline, cancel, unknown actions, missing content, null text, and empty text
are converted into error strings by `AskUserMcpServer`.

This project also demonstrates MCP client roots. `RootsHandler` returns the
sample root `my://url` / `some-name`, and `clientRoots_getRoots` asks the client
for those roots through a second MCP round trip. In a real application,
`RootsHandler` would return user- or workspace-specific locations.

## Other services and cross-cutting pieces

The other notable components are:

- **Sampling:** `SummaryMcpServer` calls `server.SampleAsync`. The MCP client
  forwards that request through `_samplingClient.CreateSamplingHandler()`,
  currently backed by the keyed `SummarySamplingClient`.
- **Local files:** `LocalFilesMcpServer` reads the configured
  `LocalFilesMcpServer:RootFolder`. The sample value in `appsettings.json` is a
  machine-specific path and usually needs to be changed.
- **Time guidance:** `time_system` is an MCP prompt telling the model to use
  `time_now` for current information.
- **Prompt guidance:** MCP prompts whose names end in `system` are collected
  during startup and contribute to the main or agent system prompt according to
  tool ownership.
- **Streaming:** `StreamingManager` separates assistant text, function calls,
  usage content, refusal text, and finish reasons. It deliberately does not
  render images in the console.
- **Command menu:** `ChatCommandMenu` supplies completion for `/system`,
  `/prompt`, `/agent`, `/verbose`, `/new`, and `/quit`.
- **Conversation reset:** `/new` clears the current conversation but does not
  reload MCP servers, prompts, or agents.
- **Host lifetime:** when the chat loop exits, `ChatService` requests host
  shutdown; MCP proxies are disposed by `McpProxyFactoryService`.

## How the logs are created

Logging has two layers: normal application status/routing output and
`Microsoft.Extensions.Logging` output.

### Verbose off

Verbose logging starts disabled. `Program.Main` clears the default providers,
adds the console logger, and installs this filter:

```csharp
builder.Logging.AddFilter((_, _) => verboseState.Enabled);
```

With verbose off:

- normal startup messages still appear, such as MCP load times and tool counts;
- tool routing and results still appear, for example
  `[main] -> [mcp:...] -> [tool:...]`;
- token usage is printed after streamed responses;
- ordinary `ILogger` messages from MCP servers and the MCP infrastructure are
  filtered out; and
- caught tool failures show a short failure result, but not the full exception.

### Verbose on

`/verbose on` sets the shared `VerboseState.Enabled` flag to `true`.
`/verbose off` sets it back to `false`.

With verbose on, the console logger is allowed to emit all configured log
levels (the console provider sends Trace-and-above logs to standard error), so
server messages such as tool entry logs become visible. When a tool throws,
the application also writes `exception.ToString()` to the terminal. The
existing routing lines remain visible in both modes.

### Debug and JSON diagnostics

`Debug.WriteLine` calls in `ChatService`, `StreamingManager`, and `Diag` are
debugger diagnostics, not controlled by `/verbose`. The `Diag` extension methods
can append raw serialized responses to `log_sync.json` or `log_async.json`, but
the active chat path does not call those dump methods by default. They are
available when deeper provider-response inspection is needed.

## Running and extending the sample

Before running, configure the provider environment variables used by
`Program.cs`:

- `AZURE_ENDPOINT`, `AZURE_SECRET_KEY`, `AZURE_MODEL_NAME`;
- `DEEPSEEK_ENDPOINT`, `DEEPSEEK_SECRET_KEY`, `DEEPSEEK_MODEL_NAME`; and
- any variables required by an enabled external MCP server, such as Playwright
  or PubChem.

The program can also populate provider secrets from the JSON dictionary at the
hard-coded path in `Program.cs`. That path is specific to the original
developer machine; for a portable setup, use environment variables or change
the secret-file location. Do not commit API keys or MCP extension tokens.

Typical development commands are:

```powershell
dotnet build .\ChatAndMultipleMcps\ChatAndMultipleMcps.csproj
dotnet test .\ChatAndMultipleMcps.Tests\ChatAndMultipleMcps.Tests.csproj
dotnet run --project .\ChatAndMultipleMcps\ChatAndMultipleMcps.csproj
```

When adding a new local MCP service, register its tools/prompts in the
`AddMcpServer()` chain. When adding an external MCP service, add a JSON file in
the configured MCP directory with a top-level `servers` or `mcpServers` object.
When adding a saved prompt or agent, add a validated `.prompt.md` or
`.agent.md` file under the configured repository-relative directory. If the
new tool belongs only to an agent, reference it from that agent's `tools`
selectors so it is kept out of the main chat schema.

