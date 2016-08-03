using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Common.Logging;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureMailer
{
    public class Mailer
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof (Mailer));

        public void AddToMailQueue(EmailMessage message)
        {
            var context = new MailerContext();
            context.Outboxes.Insert(message);
        }

        public async Task<SmtpStatusCode> SendEmail(EmailMessage message, bool fallbackToQueue = true)
        {
            var result = await safeSendEmail(message, !fallbackToQueue);
            if (fallbackToQueue && result != SmtpStatusCode.Ok)
                this.AddToMailQueue(message);
            return result;
        }

        public async Task SendEmails(int timeoutMinutes = 3)
        {
            var context = new MailerContext();
            var start = DateTime.UtcNow;
            var leaseTime = TimeSpan.FromMinutes(timeoutMinutes);

            var query = context.Outboxes.Query();
            TableContinuationToken token = null;
            do
            {
                var segment = query.ExecuteSegmented(token);
                token = segment.ContinuationToken;
                if (segment.Results.Count <= 0)
                    continue;
                foreach (var email in segment.Results)
                {
                    if ((DateTime.UtcNow - start) > leaseTime)
                    {
                        token = null;
                        break;
                    }
                    if (email.LeaseExpire.GetValueOrDefault() > start || !context.Outboxes.Lease(email, leaseTime))
                        continue;

                    await sendEmailFromStore(context, email);
                }
            }
            while (token != null);
        }

        static async Task sendEmailFromStore(MailerContext context, EmailMessage email)
        {
            email.DequeueCount++;
            var result = await safeSendEmail(email);
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

                ////these are permanent failure
                //case SmtpStatusCode.MailboxUnavailable:
                //case SmtpStatusCode.MailboxNameNotAllowed:
                //case SmtpStatusCode.UserNotLocalTryAlternatePath:
                //case SmtpStatusCode.TransactionFailed:

                ////these are serious failure
                //case SmtpStatusCode.CommandUnrecognized:
                //case SmtpStatusCode.SyntaxError:
                //case SmtpStatusCode.CommandNotImplemented:
                //case SmtpStatusCode.BadCommandSequence:
                //case SmtpStatusCode.MustIssueStartTlsFirst:
                //case SmtpStatusCode.CommandParameterNotImplemented:
                //case SmtpStatusCode.GeneralFailure:
                default:
                    return false;
            }

        }

        static async Task sendEmail(EmailMessage email)
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
                if (!string.IsNullOrEmpty(email.From))
                    message.From = new MailAddress(email.From);
                if (!string.IsNullOrEmpty(email.Cc))
                    message.CC.Add(email.Cc);
                message.To.Add(email.To);

                if (email.Attachments != null)
                {
                    foreach (var path in email.Attachments)
                    {
                        var url = new Uri(path);
                        var response = await client.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        if (response.Content == null)
                            continue;
                        var stream = await response.Content.ReadAsStreamAsync();
                        var name = url.Segments.Last();
                        var attachment = new Attachment(stream, name, response.Content.Headers.ContentType.MediaType);
                        message.Attachments.Add(attachment);
                    }
                }

                smtpClient.Send(message);
            }
        }

        static async Task<SmtpStatusCode> safeSendEmail(EmailMessage email, bool @throw = false)
        {
            try
            {
                await sendEmail(email);
                return SmtpStatusCode.Ok;
            }
            catch (SmtpException ex)
            {
                if (@throw)
                    throw;
                logger.Error("Error while sending email", ex);
                return ex.StatusCode;
            }
            catch (Exception ex)
            {
                if (@throw)
                    throw;
                logger.Error("Unknown error while sending email", ex);
                return SmtpStatusCode.GeneralFailure;
            }
        }

        private static readonly HttpClient client = createHttpClient();
        static HttpClient createHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            return httpClient;
        }
    }
}
