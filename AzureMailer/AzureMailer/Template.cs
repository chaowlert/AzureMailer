using AzureStorageExtensions;

namespace AzureMailer
{
    /// <summary>
    /// Email Template
    /// </summary>
    public class Template : ExpandableTableEntity
    {
        /// <summary>
        /// หัวข้อ Email
        /// </summary>
        public string subject { get; set; }

        /// <summary>
        /// เนื้อ email
        /// </summary>
        public string body { get; set; }
    }
}