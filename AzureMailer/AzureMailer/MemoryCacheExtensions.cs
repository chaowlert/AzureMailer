using AzureMailer;
using Base64Url;
using RazorEngine;
using RazorEngine.Templating;

namespace System.Runtime.Caching
{
    public static class MemoryCacheExtensions
    {
        public static EmailMessage RunTemplate(this MemoryCache cache, string name, string to, object model, string language = null)
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
                Engine.Razor.AddTemplate(template.body, name);
                Engine.Razor.AddTemplate(template.subject, name + "|subject");
                return string.Empty;
            });
            var body = Engine.Razor.RunCompile(name, model: model);
            var subject = Engine.Razor.RunCompile(name + "|subject", model: model);
            return new EmailMessage
            {
                PartitionKey = TimeId.NewSortableId(),
                RowKey = string.Empty,
                Subject = subject,
                Body = body,
                To = to
            };
        }

    }
}
