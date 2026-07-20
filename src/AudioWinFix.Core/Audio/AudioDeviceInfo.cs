using NAudio.CoreAudioApi;

namespace AudioWinFix.Core.Audio;

/// <summary>An active audio endpoint, with whether it is currently the default / default-communication device for its flow.</summary>
public sealed record AudioDeviceInfo(string Id, string Name, DataFlow Flow, bool IsDefault, bool IsDefaultComm);
