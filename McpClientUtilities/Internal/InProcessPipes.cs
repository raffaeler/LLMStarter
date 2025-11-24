using System;
using System.Collections.Generic;
using System.Text;

namespace McpClientUtilities.Internal;

internal class InProcessPipes
{
    public System.IO.Pipelines.Pipe ClientToServer { get; } = new();
    public System.IO.Pipelines.Pipe ServerToClient { get; } = new();
}
