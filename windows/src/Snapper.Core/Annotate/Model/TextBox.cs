using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

/// <param name="Origin">Top-left of the text layout box, not its baseline.</param>
/// <param name="MeasuredSize">Authoritative layout size in image pixels, excluding the contrasting
/// outline. The UI must remeasure and update it in the same edit whenever text or stroke width
/// changes. The model never measures text.</param>
public sealed record TextBox(PointD Origin, string Text, SizeD MeasuredSize);
