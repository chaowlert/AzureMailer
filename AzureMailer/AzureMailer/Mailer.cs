using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using Common.Logging;
using Microsoft.WindowsAzure.Storage.Table;

namespace AzureMailer
{
    public class Mailer
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof (Mailer));

        public void AddToMailQueue(string templateName, string to, object model, string language = null, params string[] attachments)
        {
            var outbox = MemoryCache.Default.RunTemplate(templateName, to, model, language, attachments);
            var context = new MailerContext();
            context.Outboxes.Insert(outbox);
        }

        public Task<SmtpStatusCode> SendEmail(string templateName, string to, object model, string language = null, params string[] attachments)
        {
            var outbox = MemoryCache.Default.RunTemplate(templateName, to, model, language, attachments);
            return SafeSendEmail(outbox);
        }

        public async Task SendEmailWithFallback(string templateName, string to, object model, string language = null, params string[] attachments)
        {
            var result = await this.SendEmail(templateName, to, model, language, attachments);
            if (result != SmtpStatusCode.Ok)
                this.AddToMailQueue(templateName, to, model, language, attachments);
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

                    await SendEmailFromStore(context, email);
                }
            }
            while (token != null);
        }

        static async Task SendEmailFromStore(MailerContext context, Email email)
        {
            email.DequeueCount++;
            var result = await SafeSendEmail(email);
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

        static async Task SendEmail(Email email)
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

                if (email.Attachments != null)
                {
                    foreach (var path in email.Attachments)
                    {
                        var url = new Uri(path);
                        var response = await _client.GetAsync(url);
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

        static async Task<SmtpStatusCode> SafeSendEmail(Email email)
        {
            try
            {
                await SendEmail(email);
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

        private static readonly HttpClient _client = createHttpClient();
        static HttpClient createHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            return client;
        }
    }
}
