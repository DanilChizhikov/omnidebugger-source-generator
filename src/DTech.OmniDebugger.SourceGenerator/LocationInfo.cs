using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DTech.OmniDebugger.SourceGenerator
{
	internal sealed class LocationInfo : IEquatable<LocationInfo>
	{
		public string Path { get; }
		public TextSpan Span { get; }
		public LinePositionSpan Lines { get; }

		private LocationInfo(string path, TextSpan span, LinePositionSpan lines)
		{
			Path = path;
			Span = span;
			Lines = lines;
		}

		public static LocationInfo From(Location location) =>
			location == null || location.SourceTree == null
				? null
				: new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

		public Location ToLocation() => Location.Create(Path, Span, Lines);

		public bool Equals(LocationInfo other) =>
			other != null && Path == other.Path && Span.Equals(other.Span);

		public override bool Equals(object obj) => Equals(obj as LocationInfo);

		public override int GetHashCode() => (Path?.GetHashCode() ?? 0) ^ Span.GetHashCode();
	}
}
