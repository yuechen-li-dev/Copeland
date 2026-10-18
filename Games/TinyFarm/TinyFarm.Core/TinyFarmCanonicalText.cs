namespace TinyFarm.Core;

/// <summary>
/// Text that is hashed (semantic state hashes, canonical TSON snapshots) must not depend on the platform.
/// <see cref="System.Text.StringBuilder.AppendLine()"/> writes <see cref="Environment.NewLine"/>, which is "\r\n" on
/// Windows and "\n" elsewhere, so the same state hashed differently on Linux. Canonical writers append this fixed
/// terminator instead. It is "\r\n" so that every hash recorded on Windows before this fix stays valid.
/// </summary>
public static class TinyFarmCanonicalText
{
    public const string NewLine = "\r\n";
}
