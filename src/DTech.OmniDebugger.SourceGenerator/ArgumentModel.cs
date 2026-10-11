using System;

namespace DTech.OmniDebugger.SourceGenerator
{
	internal sealed class ArgumentModel : IEquatable<ArgumentModel>
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public bool IsOptional { get; set; }
		public string DefaultValue { get; set; }
		public string Range { get; set; }
		public string Options { get; set; }

		public bool Equals(ArgumentModel other) =>
			other != null && Name == other.Name && Type == other.Type && IsOptional == other.IsOptional &&
			DefaultValue == other.DefaultValue && Range == other.Range && Options == other.Options;

		public override bool Equals(object obj) => Equals(obj as ArgumentModel);

		public override int GetHashCode() => (Name + "|" + Type).GetHashCode();
	}
}
