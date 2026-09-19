using System.ComponentModel;
using Anthropic;
using Microsoft.Extensions.AI;

var workspaceRoot = Path.GetFullPath(
    Environment.GetEnvironmentVariable("AGENT_WORKSPACE") ?? "./workspace");
Directory.CreateDirectory(workspaceRoot);

string ResolveInWorkspace(string relativePath)
{
    var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
    if (!fullPath.StartsWith(workspaceRoot, StringComparison.Ordinal))
        throw new UnauthorizedAccessException($"'{relativePath}' escapes the workspace.");
    return fullPath;
}

bool Confirm(string prompt)
{
    Console.WriteLine($"\n  [confirm] {prompt} (y/n)");
    return Console.ReadLine()?.Trim().ToLowerInvariant() == "y";
}

[Description("Reads a UTF-8 text file from the workspace and returns its contents.")]
string ReadFile([Description("Path relative to the workspace root")] string path) =>
    File.ReadAllText(ResolveInWorkspace(path));

[Description("Lists files and subdirectories at a path inside the workspace.")]
string ListDirectory([Description("Path relative to the workspace root, or \".\" for the root")] string path) =>
    string.Join("\n", Directory.GetFileSystemEntries(ResolveInWorkspace(path)).Select(Path.GetFileName));

[Description("Writes UTF-8 text to a file in the workspace, after confirmation.")]
string WriteFile(string path, string content)
{
    if (!Confirm($"write {content.Length} chars to '{path}'?"))
        return "The user declined this write.";

    var fullPath = ResolveInWorkspace(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, content);
    return $"Wrote {content.Length} characters to {path}.";
}

// Configured using the ANTHROPIC_API_KEY environment variable
IChatClient chatClient = new AnthropicClient()
    .AsIChatClient("claude-sonnet-5")
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

ChatOptions options = new()
{
    Tools =
    [
        AIFunctionFactory.Create(ReadFile),
        AIFunctionFactory.Create(ListDirectory),
        AIFunctionFactory.Create(WriteFile),
    ],
};

var task = args.Length > 0
    ? string.Join(' ', args)
    : "Add a .gitignore for a .NET project to the workspace, then show me its contents.";

Console.WriteLine($"Workspace: {workspaceRoot}");
Console.WriteLine($"Task: {task}\n");

var response = await chatClient.GetResponseAsync(task, options);

Console.WriteLine(response);
