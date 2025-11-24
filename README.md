# LLMStarter

This repositories contains very simple Console applications showing how to chat with a Large Language Model with the use of tools.

The non-streaming and streaming version of the APIs are split in different sets of projects. The first uses the API getting back the answer from the LLM all at once. The Streaming version continuously updates the console while retrieving the tokens.

The streaming API is primarily intended to update the UI while the non-streaming is useful for Agents.

- Minichat. Non-streaming version of the Azure OpenAI completions. 
- MinichatEx. Non-streaming version of the Microsoft.Extensions.AI completions.
- MiniStreamingChat. Streaming version of the Azure OpenAI completions.
- MiniStreamingChatEx. Streaming version of the Microsoft.Extensions.AI completions.
- Similarities. A simple example of the Azure OpenAI embedding.

In the source code there are comments explaining how to set-up the configurations and secrets.

The code using Microsoft.Extensions.AI can be easily modified to access Ollama or other compliant offline or online providers.

## MCP Support

Initially the solution included the `ChatAndMCP` which is now obsolete and replaced by the `ChatAndMultipleMcps` project.

For some time, the old `ChatAndMCP` will still be part of the repository but excluded from the solution. Later on, it will be removed from the repository as well.

The project `McpClientUtilities` is a generic library providing utilities and helpers to simplify the development of applications making use of in-process and out-of-process MCP servers loaded from JSON configuration files. Refer to the README.md in the project file for more details.



 

