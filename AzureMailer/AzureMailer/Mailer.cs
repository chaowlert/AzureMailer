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

        public SmtpStatusCode SendEmail(string templateName, string to, object model, string language = null)
        {
            var outbox = MemoryCache.Default.RunTemplate(templateName, to, model, language);
            return SafeSendEmail(outbox);
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
                var result = SafeSendEmail(email);
                if (result == SmtpStatusCode.Ok)
                    context.Outboxes.Delete(email);
                else if (email.DequeueCount >= 3 || !ShouldRetry(result))
                {
                    var op = TableOperation.Insert(email);
                    context.DeadEmails.Execute(op);
                    context.Outboxes.Delete(email);
                }
                else
                    context.Outboxes.Replace(email);
            }
        }

        public static bool ShouldRetry(SmtpStatusCode statusCode)
        {
            switch (statusCode)
            {
                //these can be retry
                case SmtpStatusCode.ServiceNotAvailable:
                case SmtpStatusCode.MailboxBusy:
                case SmtpStatusCode.LocalErrorInProcessing:
                case SmtpStatusCode.InsufficientStorage:
                case SmtpStatusCode.ClientNotPermitted:

                //these is not related but can be retry
                case SmtpStatusCode.SystemStatus:
                case SmtpStatusCode.HelpMessage:
                case SmtpStatusCode.ServiceReady:
                case SmtpStatusCode.ServiceClosingTransmissionChannel:
                case SmtpStatusCode.Ok:
                case SmtpStatusCode.UserNotLocalWillForward:
                case SmtpStatusCode.StartMailInput:
                case SmtpStatusCode.CannotVerifyUserWillAttemptDelivery:
                case SmtpStatusCode.ExceededStorageAllocation:
                    return true;

                //these are permanent failure
                case SmtpStatusCode.MailboxUnavailable:
                case SmtpStatusCode.MailboxNameNotAllowed:
                case SmtpStatusCode.UserNotLocalTryAlternatePath:
                case SmtpStatusCode.TransactionFailed:

                //these are serious failure
                case SmtpStatusCode.CommandUnrecognized:
                case SmtpStatusCode.SyntaxError:
                case SmtpStatusCode.CommandNotImplemented:
                case SmtpStatusCode.BadCommandSequence:
                case SmtpStatusCode.MustIssueStartTlsFirst:
                case SmtpStatusCode.CommandParameterNotImplemented:
                case SmtpStatusCode.GeneralFailure:
                default:
                    return false;
            }

        }

        static void SendEmail(Email email)
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
        }

        static SmtpStatusCode SafeSendEmail(Email email)
        {
            try
            {
                SendEmail(email);
                return SmtpStatusCode.Ok;
            }
            catch (SmtpException ex)
            {
                logger.Error("Error while sending email", ex);
                return ex.StatusCode;
            }
            catch (Exception ex)
            {
                logger.Error("Unknown error while sending email", ex);
                return SmtpStatusCode.GeneralFailure;
            }
        }
    }
}
