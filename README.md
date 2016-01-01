# AzureMailer
This library use RazorEngine to compile email templates and store those templates in Azure. It also include service to queue emails and batch sending emails.

####How to use
1. This library use [Azure Storage Extension](https://github.com/chaowlert/AzureStorageExtensions) to connect to blob.  You need to setup connection string.
2. Add template in `Templates` table. 
`PartitionKey` might include language such as `signup.th`. If `signup.th` is not found, it will fallback to `signup`. 
`RowKey` must be empty string. 
`subject` is email subject. 
`body` is email body template. 
3. To send email, call `Mailer.SendEmailWithFallback(templateName, toEmail, model, language)`, this will send email. If email is failed to send, it will be added to queue. And you might have periodically service to run `Mailer.SendEmails()` to resend failed emails.

####Example
In `web.config`, add following to connectionStrings
```
<connectionStrings>
  <add name="MailerContext" connectionString="DefaultEndpointsProtocol=https;AccountName={name};AccountKey={key}" />
</connectionStrings>
```
And you might need to add email setting in mailSettings
```
<system.net>
  <mailSettings>
    <smtp from="test@gmail.com">
      <network enableSsl="true" host="smtp.gmail.com" port="587" userName="test@gmail.com" password="xxxxx" />
    </smtp>
  </mailSettings>
</system.net>
```

In `Global.asax.cs` add following to invalidate template cache.
```
var razorConfig = new TemplateServiceConfiguration();
var cachingProvider = new InvalidatingCachingProvider();
razorConfig.CachingProvider = cachingProvider;
razorConfig.TemplateManager = new InvalidatingTemplateManager(cachingProvider);
Engine.Razor = RazorEngineService.Create(razorConfig);
```

Add row to `Templates` table.
- PartitionKey: `signup.en`
- RowKey: (empty string)
- subject: `Thank you for signup`
- body: `<div>Dear @Model.name</div><div>Thank you for signup</div>`

Call to send email.
```
mailer.SendEmailWithFallback("signup", "test@gmail.com", { name = "test" }, "en");
```
