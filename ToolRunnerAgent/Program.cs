using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Helpers.Beta;
using Anthropic.Models.Beta.Messages;

var workspaceRoot = Path.GetFullPath(
    Environment.GetEnvironmentVariable("AGENT_WORKSPACE") ?? "./workspace");
Directory.CreateDirectory(workspaceRoot);

var allowedPrograms = new HashSet<string> { "dotnet", "git", "ls" };

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

var readFileTool = new BetaRunnableTool
{
    Name = "read_file",
    Definition = new BetaTool
    {
        Name = "read_file",
        Description = "Reads a UTF-8 text file from the workspace and returns its contents.",
        InputSchema = new InputSchema
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement(
                    new { type = "string", description = "Path relative to the workspace root" }),
            },
            Required = ["path"],
        },
    },
    Run = (toolUse, _) =>
    {
        var path = toolUse.Input["path"].GetString()!;
        var content = File.ReadAllText(ResolveInWorkspace(path));
        return Task.FromResult<BetaToolResultBlockParamContent>(content);
    },
};

var listDirectoryTool = new BetaRunnableTool
{
    Name = "list_directory",
    Definition = new BetaTool
    {
        Name = "list_directory",
        Description = "Lists files and subdirectories at a path inside the workspace.",
        InputSchema = new InputSchema
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement(
                    new { type = "string", description = "Path relative to the workspace root, or \".\" for the root" }),
            },
            Required = ["path"],
        },
    },
    Run = (toolUse, _) =>
    {
        var path = toolUse.Input["path"].GetString()!;
        var entries = Directory.GetFileSystemEntries(ResolveInWorkspace(path))
            .Select(Path.GetFileName);
        return Task.FromResult<BetaToolResultBlockParamContent>(string.Join("\n", entries));
    },
};

var writeFileTool = new BetaRunnableTool
{
    Name = "write_file",
    Definition = new BetaTool
    {
        Name = "write_file",
        Description = "Writes UTF-8 text to a file in the workspace, creating directories as needed.",
        InputSchema = new InputSchema
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                ["content"] = JsonSerializer.SerializeToElement(new { type = "string" }),
            },
            Required = ["path", "content"],
        },
    },
    Run = async (toolUse, ct) =>
    {
        var path = toolUse.Input["path"].GetString()!;
        var content = toolUse.Input["content"].GetString()!;
        var fullPath = ResolveInWorkspace(path);

        if (!Confirm($"write {content.Length} chars to '{path}'?"))
            return "The user declined this write.";

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, ct);
        return $"Wrote {content.Length} characters to {path}.";
    },
};

var runCommandTool = new BetaRunnableTool
{
    Name = "run_command",
    Definition = new BetaTool
    {
        Name = "run_command",
        Description = "Runs an allowlisted shell command inside the workspace and returns its output.",
        InputSchema = new InputSchema
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["command"] = JsonSerializer.SerializeToElement(new { type = "string" }),
            },
            Required = ["command"],
        },
    },
    Run = async (toolUse, ct) =>
    {
        var command = toolUse.Input["command"].GetString()!;
        var program = command.Split(' ', 2)[0];
        if (!allowedPrograms.Contains(program))
            return $"Blocked: '{program}' is not on the allowlist.";

        if (!Confirm($"run '{command}' in {workspaceRoot}?"))
            return "The user declined this command.";

        var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
        {
            ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c", command },
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return $"{stdout}\n{stderr}".Trim();
    },
};

// Configured using the ANTHROPIC_API_KEY environment variable
var client = new AnthropicClient();

var task = args.Length > 0
    ? string.Join(' ', args)
    : "Add a .gitignore for a .NET project to the workspace, then run `git status`.";

Console.WriteLine($"Workspace: {workspaceRoot}");
Console.WriteLine($"Task: {task}\n");

var runner = client.Beta.Messages.ToolRunner(
    new MessageCreateParams
    {
        Model = Anthropic.Models.Messages.Model.ClaudeSonnet5,
        MaxTokens = 4096,
        Messages = [new() { Role = Role.User, Content = task }],
    },
    [readFileTool, listDirectoryTool, writeFileTool, runCommandTool]
);

var turnIndex = 0;
await foreach (var message in runner)
{
    turnIndex++;
    Console.WriteLine($"\n--- Turn {turnIndex} (stop_reason: {message.StopReason}) ---");

    foreach (var block in message.Content)
    {
        if (block.TryPickText(out var text))
            Console.WriteLine(text.Text);
        else if (block.TryPickToolUse(out var toolUse))
            Console.WriteLine($"  -> calling tool '{toolUse.Name}' with input: {JsonSerializer.Serialize(toolUse.Input)}");
    }
}

Console.WriteLine("\n=== Done ===");
