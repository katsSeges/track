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
        Assert.Equal(["--command", "workbench.action.chat.open"], result.Target.Arguments);
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
