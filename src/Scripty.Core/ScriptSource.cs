using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Scripty.Core.Tools;

namespace Scripty.Core
{
    public class ScriptSource
    {
        public string FilePath { get; }
        public string Code { get; }
        public IList<AssemblyName> Dependencies { get; } = new List<AssemblyName>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScriptSource"/> class.
        /// </summary>
        /// <remarks>
        ///     Script source needs to have file pathing in addition to the code in 
        /// order to report diagnostics correctly.
        /// </remarks>
        /// <param name="filePath">The file path.</param>
        /// <exception cref="ArgumentException">
        ///     Value cannot be null or empty - filePath
        ///         or
        ///     The file path must be rooted - filePath
        /// </exception>
        /// <exception cref="ArgumentNullException">code</exception>
        public ScriptSource(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("Value cannot be null or empty", nameof(filePath));
            }

            if (Path.IsPathRooted(filePath) == false)
            {
                //The Pathesolver may be able to locate this root, but the caller could use it too
                throw new ArgumentException("The file path must be rooted", nameof(filePath));
            }
            if (!File.Exists(filePath))
            {
                throw new ArgumentException("The file does not exist", nameof(filePath));
            }

            FilePath = filePath;
            Code = $"#load \".\\{Path.GetFileName(filePath)}\"";

            SourceProcessor.ReadSource(filePath, null, this.Dependencies);
        }
    }
}