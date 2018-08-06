using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Scripty
{
    public class Settings
    {
        public string ProjectFilePath;
        public string SolutionFilePath;
        public IReadOnlyList<string> ScriptFilePaths;
        public IReadOnlyDictionary<string, string> Properties;
        public bool Attach;
        public bool MessagesEnabled;
        public IReadOnlyDictionary<string, string> CustomProperties = null;
      
        private IReadOnlyList<KeyValuePair<string, string>> _properties;

        public bool ParseArgs(string[] args)
        {
            System.CommandLine.ArgumentSyntax parsed = System.CommandLine.ArgumentSyntax.Parse(args, syntax =>
            {
                syntax.DefineOption("enableMessages", ref MessagesEnabled, "Enables passing of information and warnings to MsBuild.");
                syntax.DefineOption("attach", ref Attach, "Pause execution at the start of the program until a debugger is attached.");
                syntax.DefineOption("solution", ref SolutionFilePath, "The full path of the solution file that contains the project.");
                syntax.DefineOptionList("p", ref _properties, ParseProperty, "The build properties.");
                syntax.DefineParameter(nameof(ProjectFilePath), ref ProjectFilePath, "The full path of the project file.");
                syntax.DefineParameterList(nameof(ScriptFilePaths), ref ScriptFilePaths, "The path(s) of script files to evaluate (can be absolute or relative to the project).");
            });

            if (_properties != null)
            {
                Dictionary<string, string> props = new Dictionary<string, string>();

                foreach (KeyValuePair<string, string> pair in _properties)
                {
                    props[pair.Key] = pair.Value;
                }

                Properties = props;
            }

            return true;
        }

        private KeyValuePair<string, string> ParseProperty(string argument)
        {
            int index = argument.IndexOf('=');

            if (index < 0)
            {
                throw new InvalidOperationException("Malformed property argument.");
            }

            return new KeyValuePair<string, string>(
                argument.Substring(0, index),
                argument.Substring(index + 1)
            );
        }

        public void ReadStdin()
        {
            string stdin = Console.In.ReadToEnd();
            JsonConvert.PopulateObject(stdin, this);
        }
    }
}
