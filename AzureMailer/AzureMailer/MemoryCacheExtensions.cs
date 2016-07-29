using AzureMailer;
using Base64Url;
using RazorEngine;
using RazorEngine.Templating;

namespace System.Runtime.Caching
{
    public static class MemoryCacheExtensions
    {
        public static Email RunTemplate(this MemoryCache cache, string name, string to, object model, string language = null, params string[] attachments)
        {
            var key = "RunTemplate_" + name;
            cache.GetOrCreate(key, () =>
            {
                var context = new MailerContext();
                Template template = null;
                if (language != null)
                    template = context.Templates[name + "." + language, string.Empty];
                if (template == null)
                    template = context.Templates[name, string.Empty];
                if (template == null)
                {
                    Engine.Razor.Compile(string.Empty, name);
                    Engine.Razor.Compile(string.Empty, name + "|subject");
                    return string.Empty;
                }
                else
                {
                    Engine.Razor.Compile(template.body, name);
                    Engine.Razor.Compile(template.subject, name + "|subject");
                    return string.Empty;
                }
            });
            var body = Engine.Razor.Run(name, model: model);
            var subject = Engine.Razor.Run(name + "|subject", model: model);
            return new Email
            {
                PartitionKey = TimeId.NewSortableId(),
                RowKey = string.Empty,
                Subject = subject,
                Body = body,
                To = to,
                Attachments = attachments
            };
        }

    }
}
