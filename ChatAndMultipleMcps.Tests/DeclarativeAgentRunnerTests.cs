using System.Runtime.CompilerServices;

using ChatAndMultipleMcps.Declarative;

using ConsoleUtilities;
using Microsoft.Extensions.AI;
using Xunit;

namespace ChatAndMultipleMcps.Tests;

public sealed class DeclarativeAgentRunnerTests
{
    [Fact]
    public void ResolverKeepsAssignedToolsOutOfMainChat()
    {
        AIFunction chemicalTool = CreateTool("compound_lookup");
        AIFunction timeTool = CreateTool("time_now");
        DeclarativeAgentDescriptor descriptor = CreateDescriptor(["pubchem/*"]);
        McpServerRegistration[] servers =
        [
            CreateServer("pubchem", chemicalTool, "PubChem guidance"),
            CreateServer("time", timeTool, "Time guidance"),
        ];

        DeclarativeAgentToolPartition partition = DeclarativeAgentToolResolver.Resolve(
            [descriptor],
            servers);

        Assert.Equal("time_now", Assert.Single(partition.DirectTools).Function.Name);
        ResolvedDeclarativeAgent agent = Assert.Single(partition.Agents);
        Assert.Equal("compound_lookup", Assert.Single(agent.Tools).Function.Name);
        Assert.Contains("PubChem guidance", agent.McpSystemPrompt);
        Assert.DoesNotContain("PubChem guidance", partition.MainSystemPrompt);
        Assert.Contains("Time guidance", partition.MainSystemPrompt);
    }

    [Fact]
    public void ResolverRejectsUnknownSelector()
    {
        DeclarativeAgentDescriptor descriptor = CreateDescriptor(["missing/*"]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DeclarativeAgentToolResolver.Resolve(
                [descriptor],
                [CreateServer("pubchem", CreateTool("lookup"), string.Empty)]));

        Assert.Contains("missing/*", exception.Message);
    }

    [Fact]
    public void RequiredAgentToolModeForcesTheOnlySelectedAgent()
    {
        AIFunction agentTool = CreateTool("agent_chemistry");
        AIFunction directTool = CreateTool("time_now");

        ChatOptions options = ChatService.CreateChatOptions(
            [directTool, agentTool],
            requireTool: true,
            requiredFunctionName: agentTool.Name);

        RequiredChatToolMode mode = Assert.IsType<RequiredChatToolMode>(options.ToolMode);
        Assert.Equal("agent_chemistry", mode.RequiredFunctionName);
        Assert.Equal(2, options.Tools!.Count);
        Assert.Contains(options.Tools, tool => tool.Name == "time_now");
    }

    [Fact]
    public async Task RunnerExecutesPrivateToolLoopAndReturnsOnlyFinalText()
    {
        AIFunction lookup = AIFunctionFactory.Create(
            (string compound) => $"CID for {compound}: 962",
            "compound_lookup",
            "Looks up a compound");
        ResolvedDeclarativeAgent agent = new(
            CreateDescriptor(["pubchem/*"]),
            [new McpToolRegistration("pubchem", "PubChem", lookup)],
            "Use PubChem as the data source.");
        SequenceChatClient client = new(
            static (messages, options) =>
            {
                Assert.Equal("compound_lookup", Assert.Single(options.Tools!).Name);
                Assert.IsType<RequiredChatToolMode>(options.ToolMode);
                Assert.Contains("Chemistry instructions", messages.First().Text);
                Assert.Contains("Use PubChem", messages.First().Text);
                return new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent(
                        "call-1",
                        "compound_lookup",
                        new Dictionary<string, object?> { ["compound"] = "water" })]))
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                };
            },
            static (messages, options) =>
            {
                Assert.IsType<AutoChatToolMode>(options.ToolMode);
                FunctionResultContent result = Assert.IsType<FunctionResultContent>(
                    messages.Last().Contents.Single());
                Assert.Contains("962", result.Result!.ToString());
                return new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    "Water has PubChem CID 962."))
                {
                    FinishReason = ChatFinishReason.Stop,
                };
            });
        RecordingTerminal terminal = new();
        DeclarativeAgentRunner runner = new(
            client,
            agent,
            terminal,
            new VerboseState());
        AIFunction wrapper = runner.CreateFunction();

        object? result = await wrapper.InvokeAsync(new AIFunctionArguments
        {
            ["request"] = "Identify water",
        });

        Assert.Equal("Water has PubChem CID 962.", result?.ToString());
        Assert.Equal(2, client.CallCount);
        Assert.Contains(
            terminal.Lines,
            line => line.Contains("[agent:Chemistry] -> [mcp:pubchem] -> [tool:compound_lookup]"));
    }

    private static DeclarativeAgentDescriptor CreateDescriptor(IReadOnlyList<string> selectors) => new(
        "Chemistry",
        "agent_chemistry",
        "Answers chemistry questions",
        "Chemistry instructions",
        selectors,
        "chemistry.agent.md");

    private static McpServerRegistration CreateServer(
        string name,
        AIFunction tool,
        string prompt) => new(name, name, [new(name, name, tool)], prompt);

    private static AIFunction CreateTool(string name) => AIFunctionFactory.Create(
        (string input) => input,
        name,
        "Test tool");

    private sealed class SequenceChatClient(
        params Func<IReadOnlyList<ChatMessage>, ChatOptions, ChatResponse>[] responses) : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChatResponse response = responses[CallCount++](
                messages.ToArray(),
                options ?? new ChatOptions());
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingTerminal : IConsoleTerminal
    {
        public List<string> Lines { get; } = [];
        public bool IsInputRedirected => false;
        public bool IsOutputRedirected => false;
        public int CursorLeft => 0;
        public int CursorTop => 0;
        public int WindowTop => 0;
        public int WindowWidth => 120;
        public int WindowHeight => 40;
        public ConsoleColor ForegroundColor { get; set; }
        public bool CursorVisible { get; set; }
        public void Clear() { }
        public ConsoleKeyInfo ReadKey(bool intercept) => default;
        public string? ReadLine() => null;
        public void SetCursorPosition(int left, int top) { }
        public void Write(string? value) { }
        public void WriteLine() { }
        public void WriteLine(string? value) => Lines.Add(value ?? string.Empty);
    }
}
