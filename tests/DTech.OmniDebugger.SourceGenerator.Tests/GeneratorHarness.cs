using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DTech.OmniDebugger.SourceGenerator.Tests
{
	internal static class GeneratorHarness
	{
		private const string PackagePathVariable = "OMNIDEBUGGER_PATH";

		private const string UnityStub = @"
namespace UnityEngine
{
	public enum RuntimeInitializeLoadType { SubsystemRegistration }

	[System.AttributeUsage(System.AttributeTargets.Method)]
	public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
	{
		public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
	}
}

namespace UnityEngine.Scripting
{
	public sealed class PreserveAttribute : System.Attribute { }
}";

		private static readonly string[] _contractFiles =
		{
			"Runtime/Contracts/Commands/ArgumentDefinition.cs",
			"Runtime/Contracts/Commands/ArgumentRange.cs",
			"Runtime/Contracts/Commands/CommandDefinition.cs",
			"Runtime/Contracts/Commands/CommandKind.cs",
			"Runtime/Contracts/Commands/CommandPath.cs",
			"Runtime/Contracts/Commands/DebugCommand.cs",
			"Runtime/Contracts/Commands/DebugCommandAttribute.cs",
			"Runtime/Contracts/Commands/DebugIconAttribute.cs",
			"Runtime/Contracts/Commands/DebugOptionsAttribute.cs",
			"Runtime/Contracts/Commands/DebugRangeAttribute.cs",
			"Runtime/Contracts/Commands/DebugTagsAttribute.cs",
			"Runtime/Contracts/Search/ISearchIndexable.cs",
			"Runtime/Commands/CommandBinders.cs",
		};

		public static GeneratorResult Run(string source)
		{
			List<SyntaxTree> trees = new List<SyntaxTree>
			{
				CSharpSyntaxTree.ParseText(source, path: "Test.cs"),
				CSharpSyntaxTree.ParseText(UnityStub, path: "UnityStub.cs"),
			};

			string root = FindPackageRoot();
			foreach (string file in _contractFiles)
			{
				string path = Path.Combine(root, file);
				trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));
			}

			CSharpCompilation compilation = CSharpCompilation.Create(
				"TestAssembly",
				trees,
				References(),
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

			GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator());
			driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

			GeneratorDriverRunResult run = driver.GetRunResult();
			string generated = string.Join("\n", run.GeneratedTrees.Select(tree => tree.ToString()));

			return new GeneratorResult(output, diagnostics, generated);
		}

		private static IEnumerable<MetadataReference> References()
		{
			string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
			return trusted.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
		}

		private static string FindPackageRoot()
		{
			string configured = Environment.GetEnvironmentVariable(PackagePathVariable);
			if (!string.IsNullOrEmpty(configured))
			{
				if (File.Exists(Path.Combine(configured, "package.json")))
				{
					return Path.GetFullPath(configured);
				}

				throw new InvalidOperationException($"{PackagePathVariable} is '{configured}', which holds no package.json.");
			}

			string submodule = Path.Combine("external", "OmniDebugger");

			for (DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
			{
				string package = Path.Combine(directory.FullName, submodule);
				if (File.Exists(Path.Combine(package, "package.json")))
				{
					return package;
				}
			}

			throw new InvalidOperationException(
				$"The OmniDebugger package was not found. Run 'git submodule update --init' or set {PackagePathVariable}.");
		}
	}
}
