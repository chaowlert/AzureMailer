using AzureStorageExtensions;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureMailer
{
    public class MailerContext : BaseCloudContext
    {
        public CloudTable<Template> Templates { get; set; }
        public CloudTable<Email> Outboxes { get; set; }

        [Setting(Period = Period.Month)]
        public CloudTable DeadEmails { get; set; }
    }
}
