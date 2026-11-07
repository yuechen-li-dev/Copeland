namespace Marionette.Skyrim.App;

/// <summary>Immutable view of the existing serial world host; presentation owns no wire connection or gameplay state.</summary>
public sealed record SkyrimHostPresentation(SkyrimStateResult State, BodyControlObservation? Body,
    EmbodimentState? Roster, bool Switching);
