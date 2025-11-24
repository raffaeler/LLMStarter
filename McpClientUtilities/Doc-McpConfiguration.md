



### ClaudeAI

```json
{
  "mcpServers": {
    "everything": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-everything"
      ]
    }
  }
}
```

### VSCode

The "mcp" property exists only because the server section is inside the vscode configuration, therefore it can be ignored.

```
"mcp": 

    {
    "servers": {
        "my-mcp-server-37844c0e": {
            "type": "stdio",
            "command": "raf",             // <== mandatory
            "args": []
        },
        "my-mcp-server-d1b5e64b": {
            "type": "stdio",
            "command": "raf2",            // <== mandatory
            "args": []
        },
        "my-mcp-server-9f3f21a1": {
            "url": "https://iamraf.net"   // <== mandatory
        }
    }
}
```


### Custom extension for in-process

The type "inproc" is only defined in this project.
It allows loading the MCP server as a .NET class in the same process of the MCP client making it as fast as a local tool would be.
The transport used for this type is the `StreamClientTransport`.

```json
{
  "mcpServers": {
    "my-mcp": {
      "type": "inproc",
      "class": "MyNamespace.MyMcpServerClass, MyMcpServerAssembly"
    }
}
```