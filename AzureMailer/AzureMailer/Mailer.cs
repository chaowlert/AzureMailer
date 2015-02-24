using System;
using System.Net.Mail;
using System.Runtime.Caching;
using System.Text;
using Common.Logging;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureMailer
{
    public class Mailer
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof (Mailer));

        public virtual void AddToMailQueue(string templateName, string to, object model, string language = null)
        {
            var outbox = MemoryCache.Default.RunTemplate(templateName, to, model, language);
            var context = new MailerContext();
            context.Outboxes.Insert(outbox);
        }

        public virtual void SendEmails()
        {
            var context = new MailerContext();
            var emails = context.Outboxes.Query().Execute();
            var start = DateTime.UtcNow;
            var leaseTime = TimeSpan.FromMinutes(5);
            foreach (var email in emails)
            {
                if ((DateTime.UtcNow - start) > leaseTime)
                    break;
                if (email.LeaseExpire.GetValueOrDefault() > start ||
                    context.Outboxes.Lease(email, leaseTime))
                    continue;

                email.DequeueCount++;
                if (SendEmail(email))
                    context.Outboxes.Delete(email);
                else if (email.DequeueCount >= 3)
                {
                    var op = TableOperation.Insert(email);
                    context.DeadEmails.Execute(op);
                    context.Outboxes.Delete(email);
                }
                else
                    context.Outboxes.Replace(email);
            }
        }

        static bool SendEmail(Email email)
        {
            try
            {
                using (var smtpClient = new SmtpClient())
                using (var message = new MailMessage
                {
                    BodyEncoding = Encoding.UTF8,
                    Subject = email.Subject,
                    IsBodyHtml = true,
                    Body = email.Body,
                })
                {
                    message.To.Add(email.To);
                    smtpClient.Send(message);
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.Error("Error while sending email", ex);
                return false;
            }
        }
    }
}
