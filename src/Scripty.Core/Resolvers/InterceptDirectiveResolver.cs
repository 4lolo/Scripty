using System.Text;
using Scripty.Core.Tools;

namespace Scripty.Core.Resolvers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Text;

    /// <summary>
    ///     Intercepts #load directives that target .cs classes and provides the CSharpScript engine 
    /// consumables that mimic the original types. Any other referenced file extension is treated 
    /// with default SourceFileResolver behavior.
    /// </summary>
    /// <remarks>
    ///     CSharpScript needs an assembly with a location, and the location can only be had by writing 
    /// an assembly to disk.
    /// 
    ///     Finding this earlier would have saved me a lot of time. 
    /// http://www.strathweb.com/2016/06/implementing-custom-load-behavior-in-roslyn-scripting/
    /// </remarks>
    public class InterceptDirectiveResolver : SourceReferenceResolver
    {
        #region "fields"

        private readonly Dictionary<string, SourceText> _rewrittenSources = new Dictionary<string, SourceText>(StringComparer.OrdinalIgnoreCase);
        private readonly SourceFileResolver _sourceFileResolver;


        #endregion //#region "fields"

        #region "ctors"

        /// <summary>
        ///     Initializes a new instance of the <see cref="InterceptDirectiveResolver"/> class.
        /// </summary>
        public InterceptDirectiveResolver() : this(ImmutableArray<string>.Empty, AppContext.BaseDirectory)
        {

        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="InterceptDirectiveResolver"/> class.
        /// </summary>
        /// <param name="searchPaths">The search paths.</param>
        /// <param name="baseDirectory">The base directory.</param>
        public InterceptDirectiveResolver(ImmutableArray<string> searchPaths, string baseDirectory)
        {
            _sourceFileResolver = new SourceFileResolver(searchPaths, baseDirectory);
        }


        #endregion //#region "ctors"

        #region  "overrides"


        /// <summary>
        ///     Normalizes specified source path with respect to base file path.
        /// 
        ///     This is the first method that can be called for each reference
        /// </summary>
        /// <param name="path">The source path to normalize. May be absolute or relative.</param>
        /// <param name="baseFilePath">Path of the source file that contains the <paramref name="path"/> (may also be relative), or null if not available.</param>
        /// <returns>Normalized path, or null if <paramref name="path"/> can't be normalized. The resulting path doesn't need to exist.</returns>
        /// <remarks>
        ///     "Normalize" is a short word for what the underlying bits are doing here. MS should make the internal FileUtilities
        /// public - its vast and has a lot of useful things.
        /// </remarks>
        public override string NormalizePath(string path, string baseFilePath)
        {
            ResolutionTargetType candidateType = GetResolutionTargetType(path);
            string normalizedPath = _sourceFileResolver.NormalizePath(path, baseFilePath);
            // return normalizedPath;
            if (candidateType != ResolutionTargetType.Cs)
            {
                return normalizedPath;
            }

            string csFilePath = CsRewriter.GetRewriteFilePath(normalizedPath);

            return csFilePath;
        }

        /// <summary>
        ///     Resolves specified path with respect to base file path.
        ///     
        ///     This is the second method called for each reference
        /// </summary>
        /// <param name="path">The path to resolve. May be absolute or relative.</param>
        /// <param name="baseFilePath">Path of the source file that contains the <paramref name="path" /> (may also be relative), or null if not available.</param>
        /// <returns>
        /// Normalized path, or null if the file can't be resolved.
        /// </returns>
        public override string ResolveReference(string path, string baseFilePath)
        {
            return _sourceFileResolver.ResolveReference(path, baseFilePath);
        }

        /// <summary>
        /// Opens a <see cref="T:System.IO.Stream" /> that allows reading the content of the specified file.
        /// </summary>
        /// <param name="resolvedPath">Path returned by <see cref="M:Microsoft.CodeAnalysis.SourceReferenceResolver.ResolveReference(System.String,System.String)" />.</param>
        /// <returns></returns>
        public override Stream OpenRead(string resolvedPath)
        {
            ResolutionTargetType candidateType = GetResolutionTargetType(resolvedPath);
            if (candidateType != ResolutionTargetType.Cs)
            {
                StringBuilder source = new StringBuilder();
                SourceProcessor.ReadSource(resolvedPath, source, null);
                
                return new MemoryStream(Encoding.UTF8.GetBytes(source.ToString()));
                // return _sourceFileResolver.OpenRead(resolvedPath);
            }

            if (_rewrittenSources.ContainsKey(resolvedPath))
            {
                return GetStream(_rewrittenSources[resolvedPath]);
            }

            CsExtraction csExtract = CsRewriter.ExtractCompilationDetailFromClassFile(resolvedPath);
            if (csExtract.Errors.Any())
            {
                string errString = string.Join(",", csExtract.Errors);
                throw new InvalidOperationException($"Failed to get compilaitonTargets. {errString}");
            }
            SyntaxTree cs = csExtract.CompilationTargets.First();

            //foreach (var mra in csExtract.MetadataReferenceAssemblies)
            //{
            //    _dirtyRefs.Add(Assembly.LoadFile(mra.Display));
            //}

            SourceText sourceText = cs.GetText();
            _rewrittenSources.Add(resolvedPath, sourceText);

            string diagFile = CsRewriter.GetRewriteFilePath(resolvedPath);
            WriteSourceText(sourceText, diagFile);

            return GetStream(sourceText);
        }

        private void WriteSourceText(SourceText sourceText, string filePath)
        {
            StreamWriter tw = new StreamWriter(filePath);
            sourceText.Write(tw);
            tw.Flush();
            tw.Close();
        }

        private Stream GetStream(SourceText sourceText)
        {
            MemoryStream ms = new MemoryStream();
            StreamWriter tw = new StreamWriter(ms);
            sourceText.Write(tw);
            tw.Flush();
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        #endregion //#region  "overrides"

        #region "intercept handling"
            
        private ResolutionTargetType GetResolutionTargetType(string resolutionCandidateFilePath)
        {
            if (resolutionCandidateFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return ResolutionTargetType.Cs;
            }
            if (resolutionCandidateFilePath.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
            {
                return ResolutionTargetType.Csx;
            }
            return ResolutionTargetType.Other;
        }
        

        #endregion // #region "file handling"

        #region "equality members"

        protected bool Equals(InterceptDirectiveResolver other)
        {
            return _rewrittenSources.Equals(other._rewrittenSources) && _sourceFileResolver.Equals(other._sourceFileResolver);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != typeof(InterceptDirectiveResolver)) return false;
            return Equals((InterceptDirectiveResolver)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _rewrittenSources.GetHashCode();
                hashCode = (hashCode * 397) ^ _sourceFileResolver.GetHashCode();
                return hashCode;
            }
        }

        #endregion #region "equality members"
    }
}
