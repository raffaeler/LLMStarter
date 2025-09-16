using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ChatAndMCP.Server.LocalFiles;

internal class LocalFilesMcpServer : IMyMcpServer
{
    private readonly LocalFilesMcpServerConfiguration _localFilesMcpServerConfiguration;
    private readonly ILogger<LocalFilesMcpServer> _logger;

    public LocalFilesMcpServer(
        ILogger<LocalFilesMcpServer> logger,
        IOptions<LocalFilesMcpServerConfiguration> localFilesMcpServerConfiguration)
    {
        _localFilesMcpServerConfiguration = localFilesMcpServerConfiguration.Value;

        ServerInfo = new()
        {
            Name = "Local Files MCP Server",
            Title = "Allows to read files from a local folder",
            Version = "1.0.0",
        };

        Capabilities = new()
        {
            Tools = new ToolsCapability()
            {
                ToolCollection =
                [
                    McpServerTool.Create(GetFilenames, new() { /* ... */}),
                    McpServerTool.Create(GetDocument, new() { /* ... */}),
                ],
            }

        };
        _logger = logger;
    }

    public Implementation ServerInfo { get; }
    public ServerCapabilities Capabilities { get; }


    [McpServerTool(Name = "localFiles_getFilenames")]
    [Description("Get the list of filenames on the local disk. This must be called first, in order to obtain the list of the names of the files.")]
    [return: Description("The list of file names.")]
    public Task<string[]> GetFilenames()
    {
        _logger.LogInformation(nameof(GetFilenames));
        DirectoryInfo di = new(_localFilesMcpServerConfiguration.RootFolder);
        var files = di.GetFiles();
        if (files == null) return Task.FromResult(Array.Empty<string>());
        return Task.FromResult(files.Select(f => f.Name).ToArray());
    }


    [McpServerTool(Name = "localFiles_getDocument")]
    [Description("Get the content of a file, given its filename")]
    [return: Description("The content of the document")]
    public async Task<string> GetDocument(
        [Description("The name of the file, including the extension")]
        string filename)
    {
        _logger.LogInformation($"{nameof(GetDocument)}: {filename}");
        var fullpath = Path.Combine(_localFilesMcpServerConfiguration.RootFolder, filename);

        if (!File.Exists(fullpath))
        {
            throw new Exception($"The file {filename} does not exist");
        }

        var content = await File.ReadAllTextAsync(fullpath);
        return content;
    }

}


