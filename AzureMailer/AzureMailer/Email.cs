using System;
using AzureStorageExtensions;

namespace AzureMailer
{
    public class Email : ExpandableTableEntity, ILeasable
    {
        public string To { get; set; }
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
    }
}