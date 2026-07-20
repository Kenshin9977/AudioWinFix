using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>A default-device slot: one (data-flow, role) pair, e.g. (Render, Communications).</summary>
public readonly record struct EndpointKey(DataFlow Flow, Role Role);
