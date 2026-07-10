using System;
using System.IO;
using System.Linq;
using NetYamlForge.Services.Cli;
using NetYamlForge.Services;

namespace NetYamlForge.Tooling;

public class ToolingProgram
{
    public static void Main(string[] args)
    {
        var jsonMode = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));

        if (args.Any(a => a.Equals("--scaffold-entities", StringComparison.OrdinalIgnoreCase)))
        {
            var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
            var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
            var overwrite = !args.Any(a => a.Equals("--no-overwrite", StringComparison.OrdinalIgnoreCase));
            var outputDirArg = args.FirstOrDefault(a => a.StartsWith("--output-dir=", StringComparison.OrdinalIgnoreCase));
            var outputDirName = outputDirArg?.Split('=', 2).ElementAtOrDefault(1);
            var withLabelKeys = args.Any(a => a.Equals("--with-label-keys", StringComparison.OrdinalIgnoreCase));
            var scaffoldResult = new CliScaffoldResult { Command = "scaffold-entities" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = EntityYamlScaffolder.Run(
                Directory.GetCurrentDirectory(),
                projectName,
                overwrite,
                string.IsNullOrWhiteSpace(outputDirName) ? "entities.generated" : outputDirName,
                withLabelKeys,
                scaffoldResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--scaffold-hook", StringComparison.OrdinalIgnoreCase)))
        {
            var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
            var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
            var nameArg = args.FirstOrDefault(a => a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase));
            var hookName = nameArg?.Split('=', 2).ElementAtOrDefault(1);
            var withTests = args.Any(a => a.Equals("--with-tests", StringComparison.OrdinalIgnoreCase));
            var scaffoldResult = new CliScaffoldResult { Command = "scaffold-hook" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = HookScaffolder.Run(
                Directory.GetCurrentDirectory(),
                projectName,
                hookName,
                withTests,
                scaffoldResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--upgrade-entity-yaml", StringComparison.OrdinalIgnoreCase)))
        {
            var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
            var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
            var scaffoldResult = new CliScaffoldResult { Command = "upgrade-entity-yaml" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = EntityYamlModernizer.Run(Directory.GetCurrentDirectory(), projectName, scaffoldResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--init-project", StringComparison.OrdinalIgnoreCase)))
        {
            var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
            var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
            var displayNameArg = args.FirstOrDefault(a => a.StartsWith("--display-name=", StringComparison.OrdinalIgnoreCase));
            var displayName = displayNameArg?.Split('=', 2).ElementAtOrDefault(1);
            var dbTypeArg = args.FirstOrDefault(a => a.StartsWith("--db-type=", StringComparison.OrdinalIgnoreCase));
            var dbType = dbTypeArg?.Split('=', 2).ElementAtOrDefault(1);
            var dbPathArg = args.FirstOrDefault(a => a.StartsWith("--db-path=", StringComparison.OrdinalIgnoreCase));
            var dbPath = dbPathArg?.Split('=', 2).ElementAtOrDefault(1);
            var dbConnectionArg = args.FirstOrDefault(a => a.StartsWith("--db-connection=", StringComparison.OrdinalIgnoreCase));
            var dbConnection = dbConnectionArg?.Split('=', 2).ElementAtOrDefault(1);
            var i18nFallbackModeArg = args.FirstOrDefault(a => a.StartsWith("--i18n-fallback-mode=", StringComparison.OrdinalIgnoreCase));
            var i18nFallbackMode = i18nFallbackModeArg?.Split('=', 2).ElementAtOrDefault(1);
            var autoScaffold = !args.Any(a => a.Equals("--no-auto-scaffold", StringComparison.OrdinalIgnoreCase));
            var force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
            var scaffoldResult = new CliScaffoldResult { Command = "init-project" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = ProjectTemplateScaffolder.Run(
                Directory.GetCurrentDirectory(),
                projectName,
                displayName,
                force,
                dbType,
                dbPath,
                dbConnection,
                autoScaffold,
                i18nFallbackMode,
                scaffoldResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--scaffold-missing-hooks", StringComparison.OrdinalIgnoreCase)))
        {
            var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
            var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
            var withTests = args.Any(a => a.Equals("--with-tests", StringComparison.OrdinalIgnoreCase));
            var scaffoldResult = new CliScaffoldResult { Command = "scaffold-missing-hooks" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = MissingHookScaffolder.Run(
                Directory.GetCurrentDirectory(),
                projectName,
                withTests,
                scaffoldResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--scaffold-batch-job", StringComparison.OrdinalIgnoreCase)))
        {
            var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
            var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
            var nameArg = args.FirstOrDefault(a => a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase));
            var jobName = nameArg?.Split('=', 2).ElementAtOrDefault(1);
            var scaffoldResult = new CliScaffoldResult { Command = "scaffold-batch-job" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = BatchJobScaffolder.Run(
                Directory.GetCurrentDirectory(),
                projectName,
                jobName ?? "sample_job",
                scaffoldResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--validate-project", StringComparison.OrdinalIgnoreCase)))
        {
            var projectArg = args.FirstOrDefault(a => a.StartsWith("--project=", StringComparison.OrdinalIgnoreCase));
            var projectName = projectArg?.Split('=', 2).ElementAtOrDefault(1);
            var validateResult = new CliScaffoldResult { Command = "validate-project" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = ProjectValidator.Run(Directory.GetCurrentDirectory(), projectName, validateResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); validateResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--ai-scaffold", StringComparison.OrdinalIgnoreCase)))
        {
            var specArg = args.FirstOrDefault(a => a.StartsWith("--spec=", StringComparison.OrdinalIgnoreCase));
            var specPath = specArg?.Split('=', 2).ElementAtOrDefault(1);
            var enableAiReview = args.Any(a => a.Equals("--ai-review", StringComparison.OrdinalIgnoreCase));
            var scaffoldResult = new CliScaffoldResult { Command = "ai-scaffold" };
            if (jsonMode) Console.SetOut(TextWriter.Null);
            var exitCode = AiScaffoldOrchestrator.Run(
                Directory.GetCurrentDirectory(),
                specPath,
                enableAiReview,
                scaffoldResult);
            if (jsonMode) { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); scaffoldResult.WriteJson(); }
            Environment.Exit(exitCode);
            return;
        }

        if (args.Any(a => a.Equals("--export-json-schema", StringComparison.OrdinalIgnoreCase)))
        {
            var outArg = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.OrdinalIgnoreCase));
            var outDir = outArg?.Split('=', 2).ElementAtOrDefault(1);

            if (string.IsNullOrEmpty(outDir))
            {
                outDir = Path.Combine(Directory.GetCurrentDirectory(), "docs", "schemas");
            }

            try
            {
                JsonSchemaExporter.Export(outDir);
                Console.WriteLine($"Successfully exported JSON schemas to {outDir}");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to export JSON schemas: {ex.Message}");
                Environment.Exit(1);
            }
            return;
        }

        Console.WriteLine("NetYamlForge Tooling CLI");
        Console.WriteLine("Usage: dotnet run --project NetYamlForge.Tooling -- <command> [options]");
        Console.WriteLine("Available commands:");
        Console.WriteLine("  --scaffold-entities --project=<name>");
        Console.WriteLine("  --scaffold-hook --project=<name> --name=<hook>");
        Console.WriteLine("  --upgrade-entity-yaml --project=<name>");
        Console.WriteLine("  --init-project --project=<name>");
        Console.WriteLine("  --scaffold-missing-hooks --project=<name>");
        Console.WriteLine("  --scaffold-batch-job --project=<name> --name=<job>");
        Console.WriteLine("  --validate-project --project=<name>");
        Console.WriteLine("  --ai-scaffold --spec=<spec.yaml> [--ai-review]");
        Console.WriteLine("  --export-json-schema --out=<dir>");
        Environment.Exit(1);
    }
}
