using Microsoft.CodeAnalysis;

namespace DTech.OmniDebugger.SourceGenerator
{
	internal static class Diagnostics
	{
		private const string Category = "OmniDebugger";

		public static readonly DiagnosticDescriptor StaticMember = Error(
			"OMNI001",
			"Debug command is static",
			"'{0}' is static; a debug command runs against the instance handed to Commands.Register");

		public static readonly DiagnosticDescriptor InaccessibleMember = Error(
			"OMNI002",
			"Debug command is not accessible",
			"'{0}' has to be public or internal to be a debug command");

		public static readonly DiagnosticDescriptor NonVoidMethod = Error(
			"OMNI003",
			"Debug command returns a value",
			"'{0}' returns {1}; a debug command method has to return void. Use a property to report a value.");

		public static readonly DiagnosticDescriptor GenericMethod = Error(
			"OMNI004",
			"Debug command is generic",
			"'{0}' is generic; debug command methods cannot have type parameters");

		public static readonly DiagnosticDescriptor ByRefParameter = Error(
			"OMNI005",
			"Debug command has an unsupported parameter",
			"Parameter '{1}' of '{0}' is passed by reference or is a pointer, which a debug command cannot take");

		public static readonly DiagnosticDescriptor NoGetter = Error(
			"OMNI006",
			"Debug command property cannot be read",
			"'{0}' needs a public or internal getter to be a debug command");

		public static readonly DiagnosticDescriptor Indexer = Error(
			"OMNI007",
			"Debug command is an indexer",
			"'{0}' is an indexer; only methods and plain properties can be debug commands");

		public static readonly DiagnosticDescriptor InaccessibleType = Error(
			"OMNI008",
			"Type declaring a debug command is not accessible",
			"'{0}' declares debug commands, so it and every type it is nested in have to be public or internal");

		public static readonly DiagnosticDescriptor GenericType = Error(
			"OMNI009",
			"Type declaring a debug command is generic",
			"'{0}' is generic or nested in a generic type; debug commands cannot be declared there");

		public static readonly DiagnosticDescriptor NotAClass = Error(
			"OMNI010",
			"Type declaring a debug command is not a class",
			"'{0}' is not a class; declare debug commands on a class (a registered struct would be a boxed copy)");

		public static readonly DiagnosticDescriptor InvalidRange = Error(
			"OMNI011",
			"Invalid debug range",
			"The range of '{0}' is invalid: {1}");

		public static readonly DiagnosticDescriptor EmptyGroup = Error(
			"OMNI012",
			"Debug command has no group",
			"'{0}' needs a group path, such as [DebugCommand(\"Economy/Coins\")]");

		public static readonly DiagnosticDescriptor InvalidName = Error(
			"OMNI013",
			"Invalid debug command name",
			"The name of '{0}' cannot be blank or contain '/'; nest it through the group path instead");

		public static readonly DiagnosticDescriptor OptionsNotFound = Error(
			"OMNI014",
			"Debug options member not found",
			"The options of '{0}' come from '{1}', which is not a member of its type or a base type");

		public static readonly DiagnosticDescriptor OptionsUnusable = Error(
			"OMNI015",
			"Debug options member cannot be read",
			"The options of '{0}' come from '{1}', which has to be a public or internal field, a property with such a getter, or a method without parameters");

		public static readonly DiagnosticDescriptor OptionsMismatch = Error(
			"OMNI016",
			"Debug options do not fit the argument",
			"The options of '{0}' come from '{1}', which {2}");

		private static DiagnosticDescriptor Error(string id, string title, string message) =>
			new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
	}
}
