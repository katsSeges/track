namespace Track.Core.Live;

public sealed record OllamaStatus(bool Running, string? Model);

/// <summary>Reads the loaded-model status from the opt-in Ollama loopback endpoint.</summary>
public interface IOllamaStatusProbe
{
    OllamaStatus Probe();
}
