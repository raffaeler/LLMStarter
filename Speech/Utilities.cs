using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Speech;

internal static class Utilities
{
    /// <summary>
    /// Read a secret from a JSON formatted dictionary file
    /// and set it as an environment variable.
    /// </summary>
    /// <param name="pathname">The pathname of the JSON file.</param>
    /// <param name="dictKey">The name of the dictionary key inside the JSON.</param>
    /// <param name="env_name">The name of the environment variable to set.</param>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="Exception"></exception>
    public static void SetSecretWithKey(string pathname, string dictKey, string env_name)
    {
        if (!File.Exists(pathname))
        {
            // if the env variable is already set, do nothing
            if (Environment.GetEnvironmentVariable(env_name) != null)
                return;

            throw new FileNotFoundException($"The secret file {pathname} does not exist");
        }

        var json = File.ReadAllText(pathname);
        var content = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        content ??= new Dictionary<string, string>();
        content.Remove("_notes");

        if (!content.TryGetValue(dictKey, out var secret))
            throw new Exception($"The key {dictKey} was not found in the secret file {pathname}");

        Environment.SetEnvironmentVariable(env_name, secret);
    }

    public static string GetEnv(string env_name)
        => Environment.GetEnvironmentVariable(env_name)
            ?? throw new Exception($"{env_name} not found");

}
