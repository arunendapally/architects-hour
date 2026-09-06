using System.Text.Json;

namespace D4;

/// <summary>
/// The one tool the model may call: read_file(path). It has a real side effect —
/// it reads from the local filesystem and returns the bytes as text. The model
/// decides when (and whether) to call it; we just advertise the schema and run it.
/// </summary>
public static class Tools
{
    /// <summary>The OpenAI tool schema we advertise to the model.</summary>
    public static object[] Schemas() =>
    [
        new
        {
            type = "function",
            function = new
            {
                name = "read_file",
                description = "Read and return the full contents of a local text file. " +
                              "Use this when the answer depends on a file on disk that the user referenced.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "Absolute or relative path to the file to read",
                        },
                    },
                    required = new[] { "path" },
                },
            },
        },
    ];

    /// <summary>Runs a tool call. Returns the file's text, or an error string.</summary>
    public static string Execute(ToolCall call)
    {
        if (call.Name != "read_file")
            return $"Unknown tool: {call.Name}";

        var path = ParsePath(call.Arguments);
        return File.ReadAllText(path);
    }

    private static string ParsePath(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("path").GetString()!;
    }
}
