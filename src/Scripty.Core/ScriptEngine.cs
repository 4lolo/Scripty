using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using NuGet.Configuration;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Scripty.Core.Output;
using Scripty.Core.ProjectTree;
using Scripty.Core.Resolvers;

namespace Scripty.Core
{
    public class ScriptEngine
    {
        private const string TargetFramework = "net462";
        
        private readonly string _projectFilePath;

        public ScriptEngine(string projectFilePath, string solutionFilePath, 
					IReadOnlyDictionary<string, string> properties,
					IReadOnlyDictionary<string, string> customProperties = null)
        {
            if (string.IsNullOrEmpty(projectFilePath))
            {
                throw new ArgumentException("Value cannot be null or empty.", nameof(projectFilePath));
            }
            if (!Path.IsPathRooted(projectFilePath))
            {
                throw new ArgumentException("Project path must be absolute", nameof(projectFilePath));
            }

            // The solution path is optional. If it's provided, the solution will be loaded and 
            // the project found in the solution. If not, then the project is loaded directly.
            if (solutionFilePath != null)
            {
                if (!Path.IsPathRooted(solutionFilePath))
                {
                    throw new ArgumentException("Solution path must be absolute", nameof(solutionFilePath));
                }
            }

            _projectFilePath = projectFilePath;
            ProjectRoot = new ProjectRoot(projectFilePath, solutionFilePath, properties, customProperties);
        }

        public ProjectRoot ProjectRoot { get; }

        public async Task<ScriptResult> Evaluate(ScriptSource source)
        {
            ScriptMetadataResolver metadataResolver = ScriptMetadataResolver.Default
                .WithSearchPaths(this.ResolveSearchPaths());
            InterceptDirectiveResolver sourceResolver = new InterceptDirectiveResolver();
            List<Assembly> assembliesToRef = new List<Assembly>
            {
                typeof(object).Assembly, //mscorlib
                typeof(Project).Assembly, // Microsoft.CodeAnalysis.Workspaces
                typeof(Microsoft.Build.Evaluation.Project).Assembly, // Microsoft.Build
                typeof(ScriptEngine).Assembly // Scripty.Core
            };

            List<string> namepspaces = new List<string>
            {
                "System",
                "Scripty.Core",
                "Scripty.Core.Output",
                "Scripty.Core.ProjectTree"
            };

            ScriptOptions options = ScriptOptions.Default
                .WithFilePath(source.FilePath)
                .WithReferences(assembliesToRef)
                .WithImports(namepspaces)
                .WithSourceResolver(sourceResolver)
                .WithMetadataResolver(metadataResolver);
            
            using (ScriptContext context = GetContext(source.FilePath))
            {
                try
                {
                    await CSharpScript.EvaluateAsync(source.Code, options, context);

                    foreach (IOutputFileInfo outputFile in context.Output.OutputFiles)
                    {
                        ((OutputFile) outputFile).Close();

                        if (outputFile.FormatterEnabled)
                        {
                            Document document = ProjectRoot.Analysis.AddDocument(outputFile.FilePath, File.ReadAllText(outputFile.FilePath));
                            
                            Document resultDocument = await Formatter.FormatAsync(
                                document,
                                outputFile.FormatterOptions.Apply(ProjectRoot.Workspace.Options)
                            );
                            SourceText resultContent = await resultDocument.GetTextAsync();

                            File.WriteAllText(outputFile.FilePath, resultContent.ToString());
                        }
                    }
                }
                catch (CompilationErrorException compilationError)
                {
                    return new ScriptResult(context.Output.OutputFiles,
                        compilationError.Diagnostics
                            .Select(x => new ScriptMessage
                            {
                                MessageType = MessageType.Error,
                                Message = x.GetMessage(),
                                Line = x.Location.GetLineSpan().StartLinePosition.Line,
                                Column = x.Location.GetLineSpan().StartLinePosition.Character
                            })
                            .ToList());
                }
                catch (AggregateException aggregateException)
                {
                    return new ScriptResult(context.Output.OutputFiles,
                        aggregateException.InnerExceptions
                            .Select(x => new ScriptMessage
                            {
                                MessageType = MessageType.Error,
                                Message = x.ToString()
                            }).ToList());
                }
                catch (Exception ex)
                {
                    return new ScriptResult(context.Output.OutputFiles,
                        new[]
                        {
                            new ScriptMessage
                            {
                                MessageType = MessageType.Error,
                                Message = ex.ToString()
                            }
                        });
                }
                return new ScriptResult(context.Output.OutputFiles, context.GetMessages());
            }
        }

        private IEnumerable<string> ResolveSearchPaths()
        {
            ISettings s = new Settings("configuration", @"NuGet.Config");
            string nugetBasePath = SettingsUtility.GetGlobalPackagesFolder(s);

            yield return nugetBasePath;

            if (this._projectFilePath != null && File.Exists(this._projectFilePath))
            {                
                XDocument proj = XDocument.Load(this._projectFilePath);
                string baseProjectPath = Path.Combine(Path.GetDirectoryName(this._projectFilePath), "bin");

                if (Directory.Exists(baseProjectPath))
                {
                    foreach (string target in Directory.EnumerateDirectories(baseProjectPath))
                    {
                        string projectTargetPath = Path.Combine(target, TargetFramework);
                        if (Directory.Exists(projectTargetPath))
                        {
                            yield return projectTargetPath;
                        }
                    }
                }
                
                foreach (XElement nuget in proj.Descendants("PackageReference"))
                {
                    string assembly = nuget.Attribute("Include")?.Value;
                    string version = nuget.Attribute("Version")?.Value;
                    string nugetPath = Path.Combine(nugetBasePath, assembly, version, "lib");

                    if (Directory.Exists(nugetPath))
                    {
                        string nugetDllPath;
                        if ((nugetDllPath = ResolveDllPath(nugetPath, TargetFramework, assembly)) != null)
                        {
                            yield return nugetDllPath;
                        }
                    }
                }
            }
        }

        private ScriptContext GetContext(string scriptFilePath)
        {
            if (scriptFilePath == null)
            {
                throw new ArgumentNullException(nameof(scriptFilePath));
            }

            return new ScriptContext(scriptFilePath, _projectFilePath, ProjectRoot);
        }

        private static string ResolveDllPath(string nugetPath, string targetFramework, string assembly)
        {
            IEnumerable<string> targetFrameworks =
                Directory.EnumerateDirectories(nugetPath).Select(Path.GetFileName).ToArray();

            foreach (string framework in targetFrameworks)
            {
                if (Regex.IsMatch(framework, $"(^|\\+){targetFramework}($|\\+)"))
                {
                    return Path.Combine(nugetPath, framework);
                }
            }

            int index = Array.IndexOf(Frameworks, targetFramework);
            if (index > 0)
            {
                return ResolveDllPath(nugetPath, Frameworks[index - 1], assembly);
            }
            
            return null;
        }
        
        private static readonly string[] Frameworks = { "net11", "net20", "net35", "net40", "net403", "net45", "net451", "net452", "net46", "net461", "net462", "net47", "net471", "net472" };
    }
}
