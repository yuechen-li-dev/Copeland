namespace TinyFarm.Oblivion;

public sealed record TinyFarmPresentationInspection(
    int Width,
    int Height,
    float WorldPixelsPerMetre,
    bool HudVisible,
    bool InspectorVisible,
    double TreeScale,
    double FarmhouseScale,
    string Sampling,
    string Detail,
    string WorldFrame,
    string HudFrame);
