namespace Clockt.Terminal;

/// <summary>DECSCUSR values.</summary>
public enum CursorStyle : byte { Default = 0, BlinkBlock = 1, SteadyBlock = 2, BlinkUnderline = 3, SteadyUnderline = 4, BlinkBar = 5, SteadyBar = 6 }

/// <summary>OSC 133 shell-integration marks.</summary>
public enum PromptMarkKind : byte { PromptStart = (byte)'A', CommandStart = (byte)'B', CommandExecuted = (byte)'C', CommandFinished = (byte)'D' }

/// <summary>OSC 9;4 (ConEmu) progress states.</summary>
public enum ProgressKind : byte { Hidden = 0, Normal = 1, Error = 2, Indeterminate = 3, Warning = 4 }

public readonly record struct ProgressState(ProgressKind Kind, int Percent);
