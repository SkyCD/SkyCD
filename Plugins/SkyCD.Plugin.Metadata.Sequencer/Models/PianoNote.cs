namespace SkyCD.Plugin.Metadata.Sequencer.Models;

internal readonly record struct PianoNote(double Start, double End, int Pitch, int Channel);
