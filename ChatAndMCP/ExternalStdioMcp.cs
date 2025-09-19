using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatAndMCP;

public class ExternalStdioMcp
{
    public bool IsEnabled { get; set; } = true;
    public required string Name { get; set; }
    public required string Type { get; set; } = "stdio";
    public required string Command { get; set; }
    public IEnumerable<string> Arguments { get; set; } = [];
}
