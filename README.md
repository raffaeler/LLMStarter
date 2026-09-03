# LLMStarter

This repositories contains very simple Console applications showing how to chat with a Large Language Model with the use of tools.

The non-streaming and streaming version of the APIs are split in different sets of projects. The first uses the API getting back the answer from the LLM all at once. The Streaming version continuously updates the console while retrieving the tokens.

The streaming API is primarily intended to update the UI while the non-streaming is useful for Agents.

- Minichat. Non-streaming version of the Azure OpenAI completions. 
- MinichatEx. Non-streaming version of the Microsoft.Extensions.AI completions.
- MiniStreamingChat. Streaming version of the Azure OpenAI completions.
- MiniStreamingChatEx. Streaming version of the Microsoft.Extensions.AI completions.
- Similarities. A simple example of the Azure OpenAI embedding.
- Speech. A Windows console demonstration of live Azure Realtime microphone
  transcription and MEAI text-to-speech playback.

In the source code there are comments explaining how to set-up the configurations and secrets.

The code using Microsoft.Extensions.AI can be easily modified to access Ollama or other compliant offline or online providers.

## Speech setup

`Speech` reads the Azure API key through the same `llmstarter.json` secret-file
mechanism used by the other Azure samples. The key is placed into
`AZURE_SECRET_KEY`; do not add the key to launch settings or source control.

Deploy a realtime transcription-capable model and `gpt-4o-mini-tts` (or
equivalent models), then configure these non-secret values. Model settings must
use the **deployment names** created in Azure, not the base-model names:

- `AZURE_ENDPOINT` (the Azure OpenAI resource root, for example
  `https://my-resource.openai.azure.com`)
- `AZURE_REALTIME_MODEL_NAME`
- `AZURE_TTS_MODEL_NAME`
- `AZURE_TTS_VOICE` (optional; defaults to `alloy`)
- `AZURE_TRANSCRIPTION_LANGUAGE` (optional ISO-639-1 language hint)
- `AZURE_TRANSCRIPTION_DELAY` (optional; defaults to `medium`)

The application continuously captures 24 kHz, 16-bit, mono microphone PCM
audio and displays partial and final transcript events. Type text at the prompt
to synthesize and play it; enter `exit` or press Ctrl+C to stop.

## MCP Support

Initially the solution included the `ChatAndMCP` which is now obsolete and replaced by the `ChatAndMultipleMcps` project.

For some time, the old `ChatAndMCP` will still be part of the repository but excluded from the solution. Later on, it will be removed from the repository as well.

The project `McpClientUtilities` is a generic library providing utilities and helpers to simplify the development of applications making use of in-process and out-of-process MCP servers loaded from JSON configuration files. Refer to the README.md in the project file for more details.



 
