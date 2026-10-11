using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DTech.OmniDebugger.SourceGenerator
{
	/// <summary>
	/// Turns every <c>[DebugCommand]</c> member into code that builds its <c>DebugCommand</c> from an instance,
	/// and registers that code with <c>CommandBinders</c> as the domain loads. Nothing is left for reflection,
	/// and the direct calls keep the members from being stripped by the IL2CPP linker.
	/// </summary>
	[Generator(LanguageNames.CSharp)]
	public sealed class CommandGenerator : IIncrementalGenerator
	{
		private const string Namespace = "DTech.OmniDebugger";
		private const string CommandAttribute = Namespace + ".DebugCommandAttribute";
		private const string TagsAttribute = Namespace + ".DebugTagsAttribute";
		private const string IconAttribute = Namespace + ".DebugIconAttribute";
		private const string RangeAttribute = Namespace + ".DebugRangeAttribute";
		private const string OptionsAttribute = Namespace + ".DebugOptionsAttribute";
		private const string OptionsType = "global::System.Func<global::System.Collections.IEnumerable>";
		private const int DefaultOrder = 1000;

		private static readonly string _version = typeof(CommandGenerator).Assembly.GetName().Version.ToString(3);

		private static readonly SymbolDisplayFormat _typeFormat = SymbolDisplayFormat.FullyQualifiedFormat
			.WithMiscellaneousOptions(
				SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
				SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			IncrementalValuesProvider<CommandModel> commands = context.SyntaxProvider.ForAttributeWithMetadataName(
				CommandAttribute,
				static (node, _) => node is MethodDeclarationSyntax || node is BasePropertyDeclarationSyntax,
				static (attributeContext, token) => Read(attributeContext, token));

			IncrementalValueProvider<string> assembly =
				context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? "Assembly");

			context.RegisterSourceOutput(commands.Collect().Combine(assembly), static (output, source) =>
				Emit(output, source.Left, source.Right));
		}

		private static CommandModel Read(GeneratorAttributeSyntaxContext context, CancellationToken token)
		{
			ISymbol member = context.TargetSymbol;
			INamedTypeSymbol type = member.ContainingType;
			AttributeData attribute = context.Attributes[0];
			Location location = member.Locations.FirstOrDefault();
			string display = type.Name + "." + member.Name;

			CommandModel model = new CommandModel
			{
				TypeName = type.ToDisplayString(_typeFormat),
				TypeId = MakeIdentifier(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)),
				MemberName = Escape(member.Name),
			};

			model.Error = CheckType(type, location) ?? CheckMember(member, display, location);
			if (model.Error != null)
			{
				return model;
			}

			string groupPath = attribute.ConstructorArguments.Length > 0
				? NormalizePath(attribute.ConstructorArguments[0].Value as string)
				: string.Empty;

			if (groupPath.Length == 0)
			{
				model.Error = new DiagnosticInfo(Diagnostics.EmptyGroup, location, display);
				return model;
			}

			string name = member.Name;
			int order = DefaultOrder;

			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				switch (argument.Key)
				{
					case "Name" when argument.Value.Value is string named:
						name = named;
						break;

					case "Order" when argument.Value.Value is int value:
						order = value;
						break;

					case "Description" when argument.Value.Value is string description:
						model.Description = string.IsNullOrWhiteSpace(description) ? null : description;
						break;
				}
			}

			if (string.IsNullOrWhiteSpace(name) || name.IndexOf('/') >= 0)
			{
				model.Error = new DiagnosticInfo(Diagnostics.InvalidName, location, display);
				return model;
			}

			model.Name = name.Trim();
			model.GroupPath = groupPath;
			model.Order = order;
			model.Tags = new EquatableArray<string>(ReadTags(member));
			model.IconKey = ReadIcon(member);

			CSharpCompilation compilation = (CSharpCompilation)context.SemanticModel.Compilation;

			token.ThrowIfCancellationRequested();

			if (member is IMethodSymbol method)
			{
				model.Kind = MemberKind.Method;
				ImmutableArray<ArgumentModel>.Builder arguments = ImmutableArray.CreateBuilder<ArgumentModel>();

				foreach (IParameterSymbol parameter in method.Parameters)
				{
					string range = ReadRange(parameter, display, parameter.Locations.FirstOrDefault() ?? location, out DiagnosticInfo rangeError);
					if (rangeError != null)
					{
						model.Error = rangeError;
						return model;
					}

					string options = ReadOptions(parameter, type, parameter.Type, compilation, display,
						parameter.Locations.FirstOrDefault() ?? location, out DiagnosticInfo optionsError);
					if (optionsError != null)
					{
						model.Error = optionsError;
						return model;
					}

					string typeName = FormatType(parameter.Type);

					arguments.Add(new ArgumentModel
					{
						Name = parameter.Name,
						Type = typeName,
						IsOptional = parameter.IsOptional,
						DefaultValue = parameter.IsOptional ? FormatDefault(parameter, typeName) : null,
						Range = range,
						Options = options,
					});
				}

				model.Arguments = new EquatableArray<ArgumentModel>(arguments.ToImmutable());
				return model;
			}

			IPropertySymbol property = (IPropertySymbol)member;
			string propertyRange = ReadRange(property, display, location, out DiagnosticInfo propertyRangeError);
			if (propertyRangeError != null)
			{
				model.Error = propertyRangeError;
				return model;
			}

			string propertyOptions = ReadOptions(property, type, property.Type, compilation, display, location,
				out DiagnosticInfo propertyOptionsError);
			if (propertyOptionsError != null)
			{
				model.Error = propertyOptionsError;
				return model;
			}

			IMethodSymbol setter = property.SetMethod;
			bool writable = setter != null && !setter.IsInitOnly && IsAccessible(setter.DeclaredAccessibility);

			model.Kind = writable ? MemberKind.Value : MemberKind.ReadonlyValue;
			model.Arguments = new EquatableArray<ArgumentModel>(ImmutableArray.Create(new ArgumentModel
			{
				Name = property.Name,
				Type = FormatType(property.Type),
				Range = propertyRange,
				Options = propertyOptions,
			}));

			return model;
		}

		private static DiagnosticInfo CheckType(INamedTypeSymbol type, Location location)
		{
			if (type.TypeKind != TypeKind.Class)
			{
				return new DiagnosticInfo(Diagnostics.NotAClass, location, type.Name);
			}

			for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
			{
				if (current.IsGenericType)
				{
					return new DiagnosticInfo(Diagnostics.GenericType, location, type.Name);
				}

				if (!IsAccessible(current.DeclaredAccessibility))
				{
					return new DiagnosticInfo(Diagnostics.InaccessibleType, location, type.Name);
				}
			}

			return null;
		}

		private static DiagnosticInfo CheckMember(ISymbol member, string display, Location location)
		{
			if (member.IsStatic)
			{
				return new DiagnosticInfo(Diagnostics.StaticMember, location, display);
			}

			if (!IsAccessible(member.DeclaredAccessibility))
			{
				return new DiagnosticInfo(Diagnostics.InaccessibleMember, location, display);
			}

			if (member is IMethodSymbol method)
			{
				if (!method.ReturnsVoid)
				{
					return new DiagnosticInfo(Diagnostics.NonVoidMethod, location, display,
						method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
				}

				if (method.IsGenericMethod)
				{
					return new DiagnosticInfo(Diagnostics.GenericMethod, location, display);
				}

				foreach (IParameterSymbol parameter in method.Parameters)
				{
					if (parameter.RefKind != RefKind.None || parameter.Type.TypeKind == TypeKind.Pointer ||
						parameter.Type.IsRefLikeType)
					{
						return new DiagnosticInfo(Diagnostics.ByRefParameter, location, display, parameter.Name);
					}
				}

				return null;
			}

			if (member is IPropertySymbol property)
			{
				if (property.IsIndexer)
				{
					return new DiagnosticInfo(Diagnostics.Indexer, location, display);
				}

				if (property.GetMethod == null || !IsAccessible(property.GetMethod.DeclaredAccessibility))
				{
					return new DiagnosticInfo(Diagnostics.NoGetter, location, display);
				}

				if (property.Type.IsRefLikeType || property.Type.TypeKind == TypeKind.Pointer || property.ReturnsByRef)
				{
					return new DiagnosticInfo(Diagnostics.ByRefParameter, location, display, property.Name);
				}
			}

			return null;
		}

		private static bool IsAccessible(Accessibility accessibility) =>
			accessibility == Accessibility.Public ||
			accessibility == Accessibility.Internal ||
			accessibility == Accessibility.ProtectedOrInternal;

		private static ImmutableArray<string> ReadTags(ISymbol member)
		{
			ImmutableArray<string>.Builder tags = ImmutableArray.CreateBuilder<string>();

			foreach (AttributeData attribute in member.GetAttributes())
			{
				if (attribute.AttributeClass?.ToDisplayString() != TagsAttribute ||
					attribute.ConstructorArguments.Length == 0)
				{
					continue;
				}

				TypedConstant values = attribute.ConstructorArguments[0];
				IEnumerable<TypedConstant> items = values.Kind == TypedConstantKind.Array
					? values.Values
					: new[] { values };

				foreach (TypedConstant item in items)
				{
					if (item.Value is string tag && !string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag))
					{
						tags.Add(tag);
					}
				}
			}

			return tags.ToImmutable();
		}

		private static string ReadIcon(ISymbol member)
		{
			foreach (AttributeData attribute in member.GetAttributes())
			{
				if (attribute.AttributeClass?.ToDisplayString() == IconAttribute &&
					attribute.ConstructorArguments.Length > 0 &&
					attribute.ConstructorArguments[0].Value is string key &&
					!string.IsNullOrWhiteSpace(key))
				{
					return key.Trim();
				}
			}

			return null;
		}

		private static string ReadRange(ISymbol symbol, string display, Location location, out DiagnosticInfo error)
		{
			error = null;

			foreach (AttributeData attribute in symbol.GetAttributes())
			{
				if (attribute.AttributeClass?.ToDisplayString() != RangeAttribute ||
					attribute.ConstructorArguments.Length < 2)
				{
					continue;
				}

				double min = Convert.ToDouble(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
				double max = Convert.ToDouble(attribute.ConstructorArguments[1].Value, CultureInfo.InvariantCulture);
				double step = 0.0;

				foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
				{
					if (argument.Key == "Step" && argument.Value.Value != null)
					{
						step = Convert.ToDouble(argument.Value.Value, CultureInfo.InvariantCulture);
					}
				}

				string reason =
					double.IsNaN(min) || double.IsInfinity(min) || double.IsNaN(max) || double.IsInfinity(max)
						? "the bounds have to be finite"
						: !(max > min)
							? $"the maximum ({FormatDouble(max)}) has to be above the minimum ({FormatDouble(min)})"
							: double.IsNaN(step) || double.IsInfinity(step) || step < 0.0
								? "the step has to be zero or positive"
								: null;

				if (reason != null)
				{
					error = new DiagnosticInfo(Diagnostics.InvalidRange, location, display, reason);
					return null;
				}

				return $"new global::{Namespace}.ArgumentRange({FormatDouble(min)}, {FormatDouble(max)}, {FormatDouble(step)})";
			}

			return null;
		}

		private static string ReadOptions(
			ISymbol symbol,
			INamedTypeSymbol type,
			ITypeSymbol argumentType,
			CSharpCompilation compilation,
			string display,
			Location location,
			out DiagnosticInfo error)
		{
			error = null;

			foreach (AttributeData attribute in symbol.GetAttributes())
			{
				if (attribute.AttributeClass?.ToDisplayString() != OptionsAttribute ||
					attribute.ConstructorArguments.Length == 0)
				{
					continue;
				}

				string memberName = (attribute.ConstructorArguments[0].Value as string)?.Trim() ?? string.Empty;

				if (!TryFindOptionsSource(type, memberName, out ISymbol source, out bool found))
				{
					error = new DiagnosticInfo(found ? Diagnostics.OptionsUnusable : Diagnostics.OptionsNotFound,
						location, display, memberName);
					return null;
				}

				string owner = source.IsStatic ? source.ContainingType.ToDisplayString(_typeFormat) : "target";
				string expression = owner + "." + Escape(source.Name);
				ITypeSymbol sourceType;

				switch (source)
				{
					case IPropertySymbol property:
						sourceType = property.Type;
						break;

					case IFieldSymbol field:
						sourceType = field.Type;
						break;

					default:
						sourceType = ((IMethodSymbol)source).ReturnType;
						expression += "()";
						break;
				}

				if (TryGetFuncResult(sourceType, out ITypeSymbol result))
				{
					sourceType = result;
					expression += "?.Invoke()";
				}

				string reason = CheckOptionsType(sourceType, argumentType, compilation);
				if (reason != null)
				{
					error = new DiagnosticInfo(Diagnostics.OptionsMismatch, location, display, memberName, reason);
					return null;
				}

				return expression;
			}

			return null;
		}

		private static bool TryFindOptionsSource(INamedTypeSymbol type, string name, out ISymbol source, out bool found)
		{
			source = null;
			found = false;

			if (name.Length == 0)
			{
				return false;
			}

			for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
			{
				foreach (ISymbol member in current.GetMembers(name))
				{
					found = true;

					if (IsOptionsSource(member))
					{
						source = member;
						return true;
					}
				}

				if (found)
				{
					return false;
				}
			}

			return false;
		}

		private static bool IsOptionsSource(ISymbol member)
		{
			if (!IsAccessible(member.DeclaredAccessibility))
			{
				return false;
			}

			switch (member)
			{
				case IPropertySymbol property:
					return !property.IsIndexer && !property.ReturnsByRef && property.GetMethod != null &&
						IsAccessible(property.GetMethod.DeclaredAccessibility);

				case IFieldSymbol field:
					return !field.Type.IsRefLikeType && field.Type.TypeKind != TypeKind.Pointer;

				case IMethodSymbol method:
					return method.MethodKind == MethodKind.Ordinary && method.Parameters.Length == 0 &&
						!method.IsGenericMethod && !method.ReturnsVoid && !method.ReturnsByRef;

				default:
					return false;
			}
		}

		private static bool TryGetFuncResult(ITypeSymbol type, out ITypeSymbol result)
		{
			if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1 &&
				named.OriginalDefinition.ToDisplayString() == "System.Func<TResult>")
			{
				result = named.TypeArguments[0];
				return true;
			}

			result = null;
			return false;
		}

		private static string CheckOptionsType(ITypeSymbol sourceType, ITypeSymbol argumentType, CSharpCompilation compilation)
		{
			List<ITypeSymbol> elements = new List<ITypeSymbol>();
			bool enumerable = false;

			if (sourceType is IArrayTypeSymbol array)
			{
				elements.Add(array.ElementType);
			}

			foreach (ITypeSymbol candidate in new[] { sourceType }.Concat<ITypeSymbol>(sourceType.AllInterfaces))
			{
				if (candidate.SpecialType == SpecialType.System_Collections_IEnumerable)
				{
					enumerable = true;
				}
				else if (candidate is INamedTypeSymbol named &&
					named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
				{
					elements.Add(named.TypeArguments[0]);
				}
			}

			if (elements.Count == 0)
			{
				return enumerable ? null : "is not a collection or a Func<T> returning one";
			}

			foreach (ITypeSymbol element in elements)
			{
				Conversion conversion = compilation.ClassifyConversion(element, argumentType);

				if (conversion.IsIdentity || (conversion.IsImplicit &&
					(conversion.IsReference || conversion.IsBoxing || conversion.IsNumeric || conversion.IsNullable)))
				{
					return null;
				}
			}

			return $"yields {elements[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}, " +
				$"which cannot be used as {argumentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
		}

		private static string FormatType(ITypeSymbol type) =>
			type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(_typeFormat);

		private static string FormatDefault(IParameterSymbol parameter, string typeName)
		{
			if (!parameter.HasExplicitDefaultValue)
			{
				return $"default({typeName})";
			}

			object value = parameter.ExplicitDefaultValue;

			if (value == null)
			{
				bool nullable = !parameter.Type.IsValueType ||
					parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

				return nullable ? "null" : $"default({typeName})";
			}

			string literal = value switch
			{
				double number => FormatDouble(number),
				float number => FormatSingle(number),
				_ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
			};

			return $"({typeName})({literal})";
		}

		private static string FormatDouble(double value)
		{
			if (double.IsNaN(value))
			{
				return "double.NaN";
			}

			if (double.IsPositiveInfinity(value))
			{
				return "double.PositiveInfinity";
			}

			if (double.IsNegativeInfinity(value))
			{
				return "double.NegativeInfinity";
			}

			string text = value.ToString("R", CultureInfo.InvariantCulture);
			return text.IndexOfAny(new[] { '.', 'E', 'e' }) >= 0 ? text : text + ".0";
		}

		private static string FormatSingle(float value)
		{
			if (float.IsNaN(value))
			{
				return "float.NaN";
			}

			if (float.IsPositiveInfinity(value))
			{
				return "float.PositiveInfinity";
			}

			if (float.IsNegativeInfinity(value))
			{
				return "float.NegativeInfinity";
			}

			return value.ToString("R", CultureInfo.InvariantCulture) + "f";
		}

		private static string NormalizePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return string.Empty;
			}

			return string.Join("/", path.Split('/')
				.Select(segment => segment.Trim())
				.Where(segment => segment.Length > 0));
		}

		private static string Escape(string identifier) =>
			SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;

		private static string MakeIdentifier(string text)
		{
			StringBuilder builder = new StringBuilder(text.Length);

			foreach (char character in text)
			{
				builder.Append(char.IsLetterOrDigit(character) ? character : '_');
			}

			if (builder.Length == 0 || char.IsDigit(builder[0]))
			{
				builder.Insert(0, '_');
			}

			return builder.ToString();
		}

		private static string Literal(string value) =>
			value == null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

		private static void Emit(SourceProductionContext context, ImmutableArray<CommandModel> models, string assemblyName)
		{
			List<CommandModel> valid = new List<CommandModel>(models.Length);

			foreach (CommandModel model in models)
			{
				if (model.Error != null)
				{
					context.ReportDiagnostic(model.Error.ToDiagnostic());
					continue;
				}

				valid.Add(model);
			}

			if (valid.Count == 0)
			{
				return;
			}

			List<IGrouping<string, CommandModel>> types = valid
				.GroupBy(model => model.TypeName)
				.OrderBy(group => group.Key, StringComparer.Ordinal)
				.ToList();

			string className = "OmniDebuggerCommands_" + MakeIdentifier(assemblyName);
			StringBuilder code = new StringBuilder();

			code.AppendLine("// <auto-generated>");
			code.AppendLine("// Generated by the OmniDebugger source generator from [DebugCommand] members. Do not edit.");
			code.AppendLine("// </auto-generated>");
			code.AppendLine("#nullable disable");
			code.AppendLine("#pragma warning disable");
			code.AppendLine();
			code.AppendLine($"namespace {Namespace}.Generated");
			code.AppendLine("{");
			code.AppendLine($"\t[global::System.CodeDom.Compiler.GeneratedCode(\"DTech.OmniDebugger.SourceGenerator\", \"{_version}\")]");
			code.AppendLine($"\tinternal static class {className}");
			code.AppendLine("\t{");
			code.AppendLine("\t\t[global::UnityEngine.Scripting.Preserve]");
			code.AppendLine("\t\t[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
			code.AppendLine("\t\tprivate static void RegisterAtRuntime() => Register();");
			code.AppendLine();
			code.AppendLine("#if UNITY_EDITOR");
			code.AppendLine("\t\t[global::UnityEditor.InitializeOnLoadMethod]");
			code.AppendLine("\t\tprivate static void RegisterInEditor() => Register();");
			code.AppendLine("#endif");
			code.AppendLine();
			code.AppendLine("\t\tprivate static void Register()");
			code.AppendLine("\t\t{");

			foreach (IGrouping<string, CommandModel> type in types)
			{
				string binder = type.First().TypeId;
				code.AppendLine($"\t\t\tglobal::{Namespace}.CommandBinders.Add(typeof({type.Key}), {binder}.Bind);");
			}

			code.AppendLine("\t\t}");

			foreach (IGrouping<string, CommandModel> type in types)
			{
				EmitType(code, type.Key, type.First().TypeId, type.ToList());
			}

			code.AppendLine("\t}");
			code.AppendLine("}");

			context.AddSource(className + ".g.cs", code.ToString());
		}

		private static void EmitType(StringBuilder code, string typeName, string binder, List<CommandModel> commands)
		{
			code.AppendLine();
			code.AppendLine($"\t\tprivate static class {binder}");
			code.AppendLine("\t\t{");

			for (int i = 0; i < commands.Count; i++)
			{
				EmitDefinition(code, "_" + i.ToString(CultureInfo.InvariantCulture), commands[i]);
			}

			code.AppendLine();
			code.AppendLine($"\t\t\tpublic static void Bind(object instance, global::System.Collections.Generic.ICollection<global::{Namespace}.DebugCommand> commands)");
			code.AppendLine("\t\t\t{");
			code.AppendLine($"\t\t\t\t{typeName} target = ({typeName})instance;");

			for (int i = 0; i < commands.Count; i++)
			{
				CommandModel command = commands[i];
				string definition = "_" + i.ToString(CultureInfo.InvariantCulture);
				string create = $"new global::{Namespace}.DebugCommand({definition}, ";
				string options = FormatOptions(command);

				switch (command.Kind)
				{
					case MemberKind.Method:
						string arguments = string.Join(", ", command.Arguments.Select((argument, index) =>
							$"({argument.Type})arguments[{index.ToString(CultureInfo.InvariantCulture)}]"));

						code.AppendLine($"\t\t\t\tcommands.Add({create}invoke: arguments => target.{command.MemberName}({arguments}){options}));");
						break;

					case MemberKind.Value:
						code.AppendLine($"\t\t\t\tcommands.Add({create}get: () => target.{command.MemberName}, " +
							$"set: value => target.{command.MemberName} = ({command.Arguments[0].Type})value{options}));");
						break;

					default:
						code.AppendLine($"\t\t\t\tcommands.Add({create}get: () => target.{command.MemberName}{options}));");
						break;
				}
			}

			code.AppendLine("\t\t\t}");
			code.AppendLine("\t\t}");
		}

		private static string FormatOptions(CommandModel command)
		{
			if (command.Arguments.All(argument => argument.Options == null))
			{
				return string.Empty;
			}

			string sources = string.Join(", ", command.Arguments.Select(argument =>
				argument.Options == null ? "null" : $"() => {argument.Options}"));

			return $", options: new {OptionsType}[] {{ {sources} }}";
		}

		private static void EmitDefinition(StringBuilder code, string field, CommandModel command)
		{
			string kind = command.Kind switch
			{
				MemberKind.Method => "Action",
				MemberKind.Value => "Value",
				_ => "ReadonlyValue",
			};

			string tags = command.Tags.Length == 0
				? "null"
				: "new string[] { " + string.Join(", ", command.Tags.Select(Literal)) + " }";

			code.AppendLine($"\t\t\tprivate static readonly global::{Namespace}.CommandDefinition {field} = new global::{Namespace}.CommandDefinition(");
			code.AppendLine($"\t\t\t\t{Literal(command.Name)},");
			code.AppendLine($"\t\t\t\t{Literal(command.GroupPath)},");
			code.AppendLine($"\t\t\t\tglobal::{Namespace}.CommandKind.{kind},");
			code.AppendLine($"\t\t\t\t{command.Order.ToString(CultureInfo.InvariantCulture)},");

			if (command.Arguments.Length == 0)
			{
				code.AppendLine($"\t\t\t\tglobal::System.Array.Empty<global::{Namespace}.ArgumentDefinition>(),");
			}
			else
			{
				code.AppendLine($"\t\t\t\tnew global::{Namespace}.ArgumentDefinition[]");
				code.AppendLine("\t\t\t\t{");

				foreach (ArgumentModel argument in command.Arguments)
				{
					string defaultValue = argument.IsOptional ? argument.DefaultValue : "null";
					string optional = argument.IsOptional ? "true" : "false";
					string range = argument.Range ?? "default";
					string hasOptions = argument.Options != null ? "true" : "false";

					code.AppendLine($"\t\t\t\t\tnew global::{Namespace}.ArgumentDefinition({Literal(argument.Name)}, typeof({argument.Type}), {defaultValue}, {optional}, {range}, {hasOptions}),");
				}

				code.AppendLine("\t\t\t\t},");
			}

			code.AppendLine($"\t\t\t\t{tags},");
			code.AppendLine($"\t\t\t\t{Literal(command.Description)},");
			code.AppendLine($"\t\t\t\t{Literal(command.IconKey)});");
		}
	}
}
