using Track.Core;
using Track.Core.Display;

namespace Track.Core.Tests.Display;

public sealed class ChatTargetResolverTests
{
    [Fact]
    public void Options_include_auto_then_provider_specific_targets()
    {
        var options = ChatTargetResolver.OptionsFor(Tool.Copilot);

        Assert.Equal("", options[0].Id);
        Assert.Equal(["copilot-app", "vscode-chat", "copilot-cli"], options.Skip(1).Select(option => option.Id));
    }

    [Fact]
    public void Auto_uses_the_first_detected_target_in_catalog_order()
    {
        var result = ChatTargetResolver.Resolve(Tool.Copilot, null, new HashSet<string> { "copilot-cli", "vscode-chat" });

        Assert.True(result.Found);
        Assert.Equal("vscode-chat", result.Target!.TargetId);
        // VS Code has no command-line flag that opens its chat: it is just opened.
        Assert.Empty(result.Target.Arguments);
        Assert.Equal(ChatTargetKind.App, result.Target.Kind);
    }

    private static readonly ChatSearchFolders Folders = new(
        @"C:\Users\u\.local\bin;relative\bin; ;\\server\share\bin;C:\Program Files\nodejs",
        @"C:\Users\u\AppData\Local",
        @"C:\Program Files",
        @"C:\Users\u\AppData\Roaming");

    [Fact]
    public void Desktop_apps_are_looked_for_only_in_their_install_folders_never_on_path()
    {
        // claude.exe on PATH is the CLI; Windows names ignore case, so Claude.exe there would be the same file.
        var app = ChatTargetResolver.TargetsFor(Tool.Claude).Single(t => t.Id == "claude-desktop");

        Assert.Equal(
            [
                @"C:\Users\u\AppData\Local\AnthropicClaude\Claude.exe",
                @"C:\Program Files\AnthropicClaude\Claude.exe",
                @"C:\Users\u\AppData\Local\Programs\Claude\Claude.exe",
                @"C:\Program Files\Programs\Claude\Claude.exe",
            ],
            ChatTargetResolver.CandidatePaths(app, Folders));
        Assert.Contains(@"C:\Program Files\Microsoft VS Code\Code.exe",
            ChatTargetResolver.CandidatePaths(ChatTargetResolver.TargetsFor(Tool.Cline)[0], Folders));
    }

    [Fact]
    public void Command_line_tools_are_looked_for_in_local_absolute_path_folders_then_npm()
    {
        var cli = ChatTargetResolver.TargetsFor(Tool.Claude).Single(t => t.Id == "claude-cli");

        Assert.Equal(ChatTargetKind.CommandLine, cli.Kind);
        Assert.Equal(
            [
                @"C:\Users\u\.local\bin\claude.exe",
                @"C:\Program Files\nodejs\claude.exe",
                @"C:\Users\u\AppData\Roaming\npm\claude.exe",
            ],
            ChatTargetResolver.CandidatePaths(cli, Folders));
        Assert.Equal(
            [@"C:\Users\u\AppData\Roaming\npm\claude.exe"],
            ChatTargetResolver.CandidatePaths(cli, Folders with { PathVariable = null }));
    }

    [Fact]
    public void Override_uses_only_the_selected_detected_target()
    {
        var result = ChatTargetResolver.Resolve(
            Tool.Copilot,
            "copilot-cli",
            new HashSet<string> { "copilot-app", "copilot-cli" });

        Assert.True(result.Found);
        Assert.Equal("copilot-cli", result.Target!.TargetId);
    }

    [Fact]
    public void Missing_override_does_not_fall_back_to_another_target()
    {
        var result = ChatTargetResolver.Resolve(Tool.Copilot, "copilot-app", new HashSet<string> { "copilot-cli" });

        Assert.False(result.Found);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_override_returns_a_repair_message()
    {
        var result = ChatTargetResolver.Resolve(Tool.Copilot, "arbitrary", new HashSet<string> { "copilot-cli" });

        Assert.False(result.Found);
        Assert.Contains("unsupported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auto_without_a_detected_target_returns_a_setup_message()
    {
        var result = ChatTargetResolver.Resolve(Tool.Gemini, null, new HashSet<string>());

        Assert.False(result.Found);
        Assert.Contains("No compatible", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ollama_model_is_one_argument_and_is_not_shell_interpolated()
    {
        const string model = "qwen; echo unsafe";
        var result = ChatTargetResolver.Resolve(Tool.Ollama, null, new HashSet<string> { "ollama-cli" }, model);

        Assert.True(result.Found);
        Assert.Equal(["run", model], result.Target!.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Ollama_does_not_append_an_empty_model(string? model)
    {
        var result = ChatTargetResolver.Resolve(Tool.Ollama, null, new HashSet<string> { "ollama-cli" }, model);

        Assert.True(result.Found);
        Assert.Equal(["run"], result.Target!.Arguments);
    }

    [Fact]
    public void Non_ollama_targets_do_not_receive_the_last_model_as_an_argument()
    {
        var result = ChatTargetResolver.Resolve(Tool.Gemini, null, new HashSet<string> { "gemini-cli" }, "logged-model");

        Assert.True(result.Found);
        Assert.Empty(result.Target!.Arguments);
    }

    [Fact]
    public void Unknown_provider_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatTargetResolver.OptionsFor((Tool)999));
    }
}
