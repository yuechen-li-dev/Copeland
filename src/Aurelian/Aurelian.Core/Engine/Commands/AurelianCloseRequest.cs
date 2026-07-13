namespace Aurelian.Core.Engine.Commands;

/// <summary>
/// A backend-owned request to end the host session. It is a command/request,
/// not a frontend object or a platform close callback.
/// </summary>
public sealed record AurelianCloseRequest;
