using System;
using System.Runtime.Caching;
using AzureStorageExtensions;

namespace AzureMailer
{
    public class EmailMessage : ExpandableTableEntity, ILeasable
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Cc { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        
        public int DequeueCount { get; set; }
        public DateTime? LeaseExpire { get; set; }

        public string[] Attachments { get; set; }

        public string AttachmentList
        {
            get { return this.Attachments?.Join("|"); }
            set { this.Attachments = value?.Split('|'); }
        }

        public static EmailMessage Create(string templateName, string to, object model, string language = null)
        {
            return MemoryCache.Default.RunTemplate(templateName, to, model, language);
        }
    }
}