using Microsoft.Extensions.AI;

namespace ChatAndMultipleMcps;

internal sealed record McpToolRegistration(
    string ServerName,
    string ServerDisplayName,
    AIFunction Function);
