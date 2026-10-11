using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DTech.OmniDebugger.SourceGenerator
{
	internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
	{
		public DiagnosticDescriptor Descriptor { get; }
		public LocationInfo Location { get; }
		public EquatableArray<string> Arguments { get; }

		public DiagnosticInfo(DiagnosticDescriptor descriptor, Location location, params string[] arguments)
		{
			Descriptor = descriptor;
			Location = LocationInfo.From(location);
			Arguments = new EquatableArray<string>(arguments.ToImmutableArray());
		}

		public Diagnostic ToDiagnostic() =>
			Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.Cast<object>().ToArray());

		public bool Equals(DiagnosticInfo other) =>
			other != null && Descriptor.Id == other.Descriptor.Id && Equals(Location, other.Location) &&
			Arguments.Equals(other.Arguments);

		public override bool Equals(object obj) => Equals(obj as DiagnosticInfo);

		public override int GetHashCode() => Descriptor.Id.GetHashCode() ^ (Location?.GetHashCode() ?? 0);
	}
}
