using AzureStorageExtensions;

namespace AzureMailer
{
    /// <summary>
    /// Template ต่างๆ ที่ใช้ในระบบ
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