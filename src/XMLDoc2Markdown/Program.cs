using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Markdown;
using Microsoft.Extensions.CommandLineUtils;
using XMLDoc2Markdown.Utils;

namespace XMLDoc2Markdown;

internal class Program
{
    private static int Main(string[] args)
    {
        CommandLineApplication app = new()
        {
            Name = "xmldoc2md"
        };

        app.VersionOption("-v|--version", () =>
        {
            return string.Format(
                "Version {0}",
                Assembly.GetEntryAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    .InformationalVersion
                    .ToString());
        });
        app.HelpOption("-?|-h|--help");

        CommandArgument srcArg = app.Argument("src", "DLL source path");
        CommandArgument outArg = app.Argument("out", "Output directory");

        CommandOption indexPageNameOption = app.Option(
            "--index-page-name",
            "Name of the index page (default: \"index\")",
            CommandOptionType.SingleValue);

        CommandOption examplesPathOption = app.Option(
            "--examples-path",
            "Path to the code examples to insert in the documentation",
            CommandOptionType.SingleValue);

        CommandOption gitHubPagesOption = app.Option(
            "--github-pages",
            "Remove '.md' extension from links for GitHub Pages",
            CommandOptionType.NoValue);

        CommandOption gitlabWikiOption = app.Option(
            "--gitlab-wiki",
            "Remove '.md' extension and './' prefix from links for gitlab wikis",
            CommandOptionType.NoValue);

        CommandOption backButtonOption = app.Option(
            "--back-button",
            "Add a back button on each page",
            CommandOptionType.NoValue);

        CommandOption IncludePrivateMethodOption = app.Option(
            "--private-members",
            "Write documentation for private members.",
            CommandOptionType.NoValue);

        CommandOption generateMetadata = app.Option(
            "--generate-metadata",
            "Generate metadata files for each document",
            CommandOptionType.NoValue);
        
        CommandOption dependencyLinks = app.Option(
            "--dependency-links",
            "Create links to symbols within referenced assemblies",
            CommandOptionType.NoValue);

        app.OnExecute(() =>
        {
            string src = srcArg.Value;
            string @out = outArg.Value;
            string indexPageName = indexPageNameOption.Value() ?? "index";
            TypeDocumentationOptions options = new()
            {
                ExamplesDirectory = examplesPathOption.Value(),
                GitHubPages = gitHubPagesOption.HasValue(),
                GitlabWiki = gitlabWikiOption.HasValue(),
                BackButton = backButtonOption.HasValue(),
                IncludePrivateMembers = IncludePrivateMethodOption.HasValue(),
                GenerateMetadata = generateMetadata.HasValue(),
                DependencyLinks = dependencyLinks.HasValue()
            };
            int succeeded = 0;
            int failed = 0;

            if (!Directory.Exists(@out))
            {
                Directory.CreateDirectory(@out);
            }

            AssemblyLoadContext loadContext = new(src);
            Assembly assembly = loadContext
                .LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(src)));
            foreach (AssemblyName referencedAssembly in assembly.GetReferencedAssemblies())
            {
                loadContext.LoadFromAssemblyName(referencedAssembly);
            }

            string assemblyName = assembly.GetName().Name;
            XmlDocumentation documentation = new(src);
            Logger.Info($"Generation started: Assembly: {assemblyName}");

            IMarkdownDocument indexPage = new MarkdownDocument().AppendHeader(assemblyName, 1);

            IEnumerable<Type> types = assembly.GetTypes().Where(type => type.IsPublic);
            IEnumerable<IGrouping<string, Type>> typesByNamespace = types.GroupBy(type => type.Namespace).OrderBy(g => g.Key);
            foreach (IGrouping<string, Type> namespaceTypes in typesByNamespace)
            {
                indexPage.AppendHeader(namespaceTypes.Key, 2);

                foreach (Type type in namespaceTypes.OrderBy(x => x.Name))
                {
                    // exclude delegates
                    if (typeof(Delegate).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    string fileName = type.GetDocsFileName();
                    Logger.Info($"  {fileName}.md");

                    indexPage.AppendParagraph(type.GetDocsLink(assembly, noExtension: options.GitHubPages));

                    try
                    {
                        TypeDocumentation typeDoc = new(assembly, type, documentation, options)
                        {
                            Context = loadContext
                        };
                        File.WriteAllText(
                            Path.Combine(@out, $"{fileName}.md"),
                            typeDoc.ToString()
                        );
                        File.WriteAllText(
                            Path.Combine(@out, $"{fileName}.meta.json"),
                            JsonSerializer.Serialize(typeDoc.Metadata)
                        );
                        succeeded++;
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception.Message);
                        failed++;
                    }
                }
            }

            File.WriteAllText(Path.Combine(@out, $"{indexPageName}.md"), indexPage.ToString());

            Logger.Info($"Generation: {succeeded} succeeded, {failed} failed");

            return 0;
        });

        try
        {
            return app.Execute(args);
        }
        catch (CommandParsingException ex)
        {
            Logger.Error(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Unable to generate documentation:");
            Logger.Error(ex.Message);
        }

        return 1;
    }
}
