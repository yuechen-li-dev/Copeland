namespace Machina.Presentation.Input;

/// <summary>
/// Immutable frontend intent emitted across a frontend/backend boundary.
/// Concrete integrations translate it into a consumer-owned backend request.
/// </summary>
public abstract record MachinaFrontendMessage;

/// <summary>
/// The only currently evidenced cross-system frontend request: ask the host to
/// close. This is distinct from a raw platform close notification.
/// </summary>
public sealed record MachinaFrontendCloseRequested : MachinaFrontendMessage;
