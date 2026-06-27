using Machina.Core.Styling;
using Machina.Standard.Text;

namespace Machina.Standard.Components;

public sealed record StandardTextBlockMetadata(
    MachinaTextSpec Text,
    ColorToken Foreground,
    ColorToken? LinkForeground);
