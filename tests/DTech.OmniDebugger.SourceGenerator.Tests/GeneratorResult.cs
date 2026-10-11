using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace DTech.OmniDebugger.SourceGenerator.Tests
{
	internal sealed class GeneratorResult
	{
		public Compilation Compilation { get; }
		public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }
		public string Generated { get; }

		public IEnumerable<string> GeneratorIds => GeneratorDiagnostics.Select(diagnostic => diagnostic.Id);

		public IEnumerable<Diagnostic> CompileErrors =>
			Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

		public GeneratorResult(Compilation compilation, ImmutableArray<Diagnostic> generatorDiagnostics, string generated)
		{
			Compilation = compilation;
			GeneratorDiagnostics = generatorDiagnostics;
			Generated = generated;
		}

		public Assembly Load()
		{
			using MemoryStream stream = new MemoryStream();
			EmitResult emit = Compilation.Emit(stream);
			if (!emit.Success)
			{
				throw new InvalidOperationException(string.Join("\n", emit.Diagnostics));
			}

			Assembly assembly = Assembly.Load(stream.ToArray());
			Type registrar = assembly.GetTypes().Single(type => type.Name.StartsWith("OmniDebuggerCommands_", StringComparison.Ordinal));
			registrar.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
			return assembly;
		}
	}
}
