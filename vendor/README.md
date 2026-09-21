# Clockt terminal model

The platform-independent VT parser, screen model, scrollback, Unicode support, and input encoder in `Clockt.Terminal` are copied from the user's local `D:/Workhammer/clockt/src/Clockt.Terminal` on 2026-09-22 (checkout HEAD `653538369a75d89178a312d4631eaefd2f2106d9`; local edits, if present, are included). The source namespace and comments are retained. Only its project file is adapted for standalone .NET 10 builds.

HammerOS owns the Avalonia renderer and Windows process lifecycle in `Apps/HostTerminalView.cs` and `Core/HostTerminalSession.cs`. The Win32 declarations in `Core/NativeMethods.cs` are adapted from Clockt's Windows platform project.

There is no build-time dependency on the sibling Clockt checkout. No new license is assigned to the copied Clockt source.
