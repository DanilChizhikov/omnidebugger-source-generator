using System;

namespace DTech.OmniDebugger.SourceGenerator
{
	internal sealed class CommandModel : IEquatable<CommandModel>
	{
		public string TypeName { get; set; }
		public string TypeId { get; set; }
		public string MemberName { get; set; }
		public MemberKind Kind { get; set; }
		public string Name { get; set; }
		public string GroupPath { get; set; }
		public int Order { get; set; }
		public string Description { get; set; }
		public string IconKey { get; set; }
		public EquatableArray<string> Tags { get; set; }
		public EquatableArray<ArgumentModel> Arguments { get; set; }
		public DiagnosticInfo Error { get; set; }

		public bool Equals(CommandModel other) =>
			other != null &&
			TypeName == other.TypeName &&
			MemberName == other.MemberName &&
			Kind == other.Kind &&
			Name == other.Name &&
			GroupPath == other.GroupPath &&
			Order == other.Order &&
			Description == other.Description &&
			IconKey == other.IconKey &&
			Tags.Equals(other.Tags) &&
			Arguments.Equals(other.Arguments) &&
			Equals(Error, other.Error);

		public override bool Equals(object obj) => Equals(obj as CommandModel);

		public override int GetHashCode() => (TypeName + "." + MemberName).GetHashCode();
	}
}
