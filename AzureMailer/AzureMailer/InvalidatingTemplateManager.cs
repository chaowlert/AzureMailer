using System.Collections.Concurrent;
using RazorEngine.Templating;

namespace AzureMailer
{
    public class InvalidatingTemplateManager : ITemplateManager
    {
        public InvalidatingTemplateManager(InvalidatingCachingProvider cachingProvider)
        {
            this._cachingProvider = cachingProvider;
        }

        private readonly ConcurrentDictionary<ITemplateKey, ITemplateSource> _dynamicTemplates =
            new ConcurrentDictionary<ITemplateKey, ITemplateSource>();
        private readonly InvalidatingCachingProvider _cachingProvider;

        public void AddDynamic(ITemplateKey key, ITemplateSource source)
        {
            _dynamicTemplates.AddOrUpdate(key, source, (k, oldSource) =>
            {
                if (oldSource.Template != source.Template)
                {
                    _cachingProvider.InvalidateCache(key);
                }
                return source;
            });
        }

        public ITemplateKey GetKey(string name, ResolveType resolveType, ITemplateKey context)
        {
            return new NameOnlyTemplateKey(name, resolveType, context);
        }

        public ITemplateSource Resolve(ITemplateKey key)
        {
            if (_dynamicTemplates.TryGetValue(key, out ITemplateSource result))
            {
                return result;
            }
            return null;
        }
    }
}
