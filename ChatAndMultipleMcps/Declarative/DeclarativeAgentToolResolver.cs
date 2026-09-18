namespace ChatAndMultipleMcps.Declarative;

/// <summary>
/// Resolves the tool names declared in Markdown before any chat starts. This is
/// the boundary that keeps agent-owned MCP schemas out of the main chat.
/// </summary>
internal static class DeclarativeAgentToolResolver
{
    public static DeclarativeAgentToolPartition Resolve(
        IReadOnlyList<DeclarativeAgentDescriptor> agents,
        IReadOnlyList<McpServerRegistration> servers)
    {
        List<ResolvedDeclarativeAgent> resolvedAgents = [];
        HashSet<McpToolRegistration> assignedTools = [];

        foreach (DeclarativeAgentDescriptor agent in agents)
        {
            List<McpToolRegistration> tools = [];
            foreach (string selector in agent.ToolSelectors)
            {
                McpToolRegistration[] matches = servers
                    .SelectMany(static server => server.Tools)
                    .Where(tool => Matches(selector, tool))
                    .ToArray();
                if (matches.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Agent '{agent.Name}' in '{agent.SourcePath}' references unavailable tool selector '{selector}'.");
                }

                tools.AddRange(matches);
            }

            McpToolRegistration[] distinctTools = tools.Distinct().ToArray();
            EnsureUniqueFunctionNames(distinctTools, $"agent '{agent.Name}'");
            assignedTools.UnionWith(distinctTools);

            HashSet<string> serverNames = distinctTools
                .Select(static tool => tool.ServerName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string mcpPrompt = JoinPrompts(servers
                .Where(server => serverNames.Contains(server.Name))
                .Select(static server => server.SystemPrompt));
            resolvedAgents.Add(new(agent, distinctTools, mcpPrompt));
        }

        McpToolRegistration[] directTools = servers
            .SelectMany(static server => server.Tools)
            .Where(tool => !assignedTools.Contains(tool))
            .ToArray();
        EnsureUniqueFunctionNames(directTools, "the main chat");

        HashSet<string> directServerNames = directTools
            .Select(static tool => tool.ServerName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string mainPrompt = JoinPrompts(servers
            .Where(server => server.Tools.Count == 0 || directServerNames.Contains(server.Name))
            .Select(static server => server.SystemPrompt));

        HashSet<string> mainNames = directTools
            .Select(static tool => tool.Function.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedDeclarativeAgent agent in resolvedAgents)
        {
            if (!mainNames.Add(agent.Descriptor.FunctionName))
            {
                throw new InvalidOperationException(
                    $"Agent function '{agent.Descriptor.FunctionName}' conflicts with another main-chat tool.");
            }
        }

        return new(directTools, resolvedAgents, mainPrompt);
    }

    private static bool Matches(string selector, McpToolRegistration tool)
    {
        if (selector == "*")
        {
            return true;
        }

        int separator = selector.IndexOf('/');
        if (separator <= 0 || separator == selector.Length - 1)
        {
            return false;
        }

        string serverName = selector[..separator];
        string toolName = selector[(separator + 1)..];
        return string.Equals(serverName, tool.ServerName, StringComparison.OrdinalIgnoreCase)
            && (toolName == "*" || string.Equals(
                toolName, tool.Function.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureUniqueFunctionNames(
        IEnumerable<McpToolRegistration> tools,
        string context)
    {
        string? duplicate = tools
            .GroupBy(static tool => tool.Function.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Tool name '{duplicate}' is provided more than once in {context}.");
        }
    }

    private static string JoinPrompts(IEnumerable<string> prompts) => string.Join(
        Environment.NewLine + Environment.NewLine,
        prompts.Where(static prompt => !string.IsNullOrWhiteSpace(prompt)));
}
