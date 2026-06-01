using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RhMcp.Router.Codegen;
using Xunit;

namespace RhMcp.Router.Tests;

// Regression guard for the router source generator. Plugin tool sites write
// [McpServerTool(...)] positionally (name, title, readOnly, destructive). An
// earlier version only read named attribute args, so every tool's name came
// back null and zero proxies were emitted. These tests run the generator over
// synthetic tool files and assert a proxy IS produced.
public class CodegenTests
{
    [Fact]
    public void Emits_proxy_for_positional_attribute()
    {
        string output = RunGenerator(
            """
            namespace RhMcp.Tools;

            [McpServerToolType]
            public class XTool
            {
                [McpServerTool("x", "X", true, false)]
                public static string Run(RhinoDoc doc) => "ok";
            }
            """);

        Assert.Contains("public class XToolProxy", output);
        Assert.Contains("Name = \"x\"", output);
        Assert.Contains("Title = \"X\"", output);
        Assert.Contains("ReadOnly = true", output);
        Assert.Contains("Destructive = false", output);
        // Registrar must wire the proxy up so Program.cs picks it up.
        Assert.Contains("WithTools<global::RhMcp.Router.Tools.Generated.XToolProxy>", output);
    }

    [Fact]
    public void Named_argument_overrides_positional_slot()
    {
        string output = RunGenerator(
            """
            namespace RhMcp.Tools;

            [McpServerToolType]
            public class YTool
            {
                [McpServerTool("ignored", Name = "real_name", Destructive = true)]
                public static string Run(RhinoDoc doc) => "ok";
            }
            """);

        Assert.Contains("Name = \"real_name\"", output);
        Assert.DoesNotContain("Name = \"ignored\"", output);
        Assert.Contains("Destructive = true", output);
    }

    [Fact]
    public void Folds_concatenated_description_literals()
    {
        // AskUserTool builds its [Description] with + concatenation across lines.
        // The generator must fold the constant string so the proxy carries the full
        // text, not an empty Description("").
        string output = RunGenerator(
            """
            namespace RhMcp.Tools;

            [McpServerToolType]
            public class ZTool
            {
                [McpServerTool("z", "Z", true, false)]
                [Description("first part " + "second part " + "third part")]
                public static string Run(
                    RhinoDoc doc,
                    [Description("flag on " + "two lines")] bool flag = false) => "ok";
            }
            """);

        Assert.Contains("Description(\"first part second part third part\")", output);
        Assert.Contains("Description(\"flag on two lines\")", output);
        Assert.DoesNotContain("Description(\"\")", output);
    }

    private static string RunGenerator(string toolSource)
    {
        SyntaxTree compilationTree = CSharpSyntaxTree.ParseText("// placeholder compilation unit");
        CSharpCompilation compilation = CSharpCompilation.Create(
            "RouterCodegenTest",
            [compilationTree],
            references: [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The generator only scans AdditionalFiles whose path contains /plugin/Tools/.
        AdditionalText toolFile = new InMemoryAdditionalText(
            "/repo/rhino/plugin/Tools/SyntheticTool.cs",
            toolSource);

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new RouterToolGenerator().AsSourceGenerator()],
            additionalTexts: [toolFile]);

        GeneratorDriverRunResult result = driver.RunGenerators(compilation).GetRunResult();
        Assert.Empty(result.Diagnostics);

        GeneratedSourceResult generated = result.Results
            .Single()
            .GeneratedSources
            .Single();

        return generated.SourceText.ToString();
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content);
    }
}
