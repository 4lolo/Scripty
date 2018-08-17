using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Scripty.Core.Resolvers
{
    public class CustomMetadataReferenceResolver : MetadataReferenceResolver
    {
        private readonly ScriptMetadataResolver _resolver;

        public CustomMetadataReferenceResolver(ScriptMetadataResolver resolver)
        {
            this._resolver = resolver;
        }

        public override bool ResolveMissingAssemblies => true;

        public override PortableExecutableReference ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity)
        {
            PortableExecutableReference result = this._resolver.ResolveMissingAssembly(definition, referenceIdentity);
            return result;
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            ImmutableArray<PortableExecutableReference> result = this._resolver.ResolveReference(reference, baseFilePath, properties);

            if (result.Length == 0)
            {
                result = this._resolver.ResolveReference(reference + ".dll", baseFilePath, properties);
            }
            if (result.Length == 0)
            {
                result = this._resolver.ResolveReference(reference + ".exe", baseFilePath, properties);
            }
                
            return result;
        }

        public override bool Equals(object other)
        {
            return this.Equals((other as CustomMetadataReferenceResolver)?._resolver);
        }

        public override int GetHashCode()
        {
            return this._resolver.GetHashCode();
        }
    }
}