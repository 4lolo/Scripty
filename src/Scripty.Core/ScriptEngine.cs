﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using NuGet.Configuration;
using Scripty.Core.Output;
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
        }

        public async Task<ScriptResult> Evaluate(ScriptSource source)
        {
            MetadataReferenceResolver metadataResolver = new CustomMetadataReferenceResolver(
                ScriptMetadataResolver.Default.WithSearchPaths(this.ResolveSearchPaths(source))                
            );
            
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
                "Scripty.Core.Output"
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

        private IEnumerable<string> ResolveSearchPaths(ScriptSource source)
        {
            string nugetBasePath = SettingsUtility.GetGlobalPackagesFolder(new NullSettings());

            yield return nugetBasePath;

            foreach (AssemblyName dependency in source.Dependencies.Where(d => d.Version != null))
            {
                string nugetPath = Path.Combine(nugetBasePath, dependency.Name, dependency.Version.ToString(3), "lib");

                if (Directory.Exists(nugetPath))
                {
                    string nugetDllPath;
                    if ((nugetDllPath = ResolveDllPath(nugetPath, TargetFramework)) != null)
                    {
                        yield return nugetDllPath;
                    }
                }
            }
            
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
        }

        private ScriptContext GetContext(string scriptFilePath)
        {
            if (scriptFilePath == null)
            {
                throw new ArgumentNullException(nameof(scriptFilePath));
            }

            return new ScriptContext(scriptFilePath, _projectFilePath);
        }

        private static string ResolveDllPath(string nugetPath, string targetFramework)
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
                return ResolveDllPath(nugetPath, Frameworks[index - 1]);
            }
            
            return null;
        }
        
        private static readonly string[] Frameworks = { "net11", "net20", "net35", "net40", "net403", "portable-net45", "net45", "net451", "net452", "net46", "net461", "net462", "net47", "net471", "net472" };
    }
}
