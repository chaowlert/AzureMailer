using System;
using AzureStorageExtensions;

namespace AzureMailer
{
    public class Email : ExpandableTableEntity, ILeasable
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }

        public DateTime? LeaseExpire { get; set; }
        public int DequeueCount { get; set; }
    }
}