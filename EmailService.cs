using System.Net;
using System.Net.Mail;

/* O email deverá ser removido pra camada Infrastrucutre, porem, isso deve ser feito de maneira inteligente, e gradual. A estratégia é: Crie outra classe de mail mais limpa, que abstraia para que o application nao precise saber nada sobre detalhes de email*/
public class EmailService
{
    EmailSettings senderMailSettings;

    public EmailService(EmailSettings senderMailSettings)
    {
        this.senderMailSettings = senderMailSettings;
    }

    public async Task SendEmailAsync(EmailRequest mailRequest)
    {

        if (mailRequest.toEmail.Count == 0)
            return;

        MailMessage message = new MailMessage
        {
            Subject = mailRequest.subject
        };

        SmtpClient smtp = new SmtpClient();

        senderMailSettings.Mail = senderMailSettings.Mail.ToLower();
        message.From = new MailAddress(senderMailSettings.Mail, senderMailSettings.DisplayName);

        if (senderMailSettings.override_all_emails_to is not null && senderMailSettings.override_all_emails_to != "")
        {
            message.To.Add(senderMailSettings.override_all_emails_to);
            var bcc = (mailRequest.bcc ?? []).Where(x => x != "").ToList();
            if (bcc.Count > 0)
                message.Bcc.Add(string.Join(",", (IEnumerable<string>)bcc));
        }
        else
        {
            message.To.Add(string.Join(",", (IEnumerable<string>)mailRequest.toEmail));
            var bcc = mailRequest.bcc?.Where(x => x != "").ToList();
            if (bcc is not null && bcc.Count > 0)
                message.Bcc.Add(string.Join(",", (IEnumerable<string>)bcc));
        }

        if (mailRequest.attachments != null)
            foreach (var attachment in mailRequest.attachments)
                message.Attachments.Add(attachment);


        message.IsBodyHtml = mailRequest.isHtml;
        message.Body = mailRequest.body;
        smtp.Port = senderMailSettings.Port;
        smtp.Host = senderMailSettings.Host;
        smtp.EnableSsl = true;
        smtp.UseDefaultCredentials = false;
        smtp.Credentials = new NetworkCredential(senderMailSettings.Mail, senderMailSettings.Password);
        smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
        Console.WriteLine($"Sending email to {message.To}: {message.Body.Substring(0, 20)}...");
        if (!senderMailSettings.bypass.HasValue || senderMailSettings.bypass == false)
            await smtp.SendMailAsync(message);
        Console.WriteLine("Email sended");
    }

    public record EmailSettings
    {
        public required string Mail { get; set; }
        public required string DisplayName { get; set; }
        public required string Password { get; set; }
        public required string Host { get; set; }
        public required int Port { get; set; }
        public bool? bypass { get; set; }
        public string? override_all_emails_to { get; set; }
    }


    public record EmailRequest
    {
        public List<string> toEmail { get; set; }
        public List<string>? bcc { get; set; }
        public string subject { get; set; }
        public string? body { get; set; }
        public List<Attachment>? attachments { get; set; }
        public bool isHtml = false;

        public EmailRequest(List<string> toEmail, string subject, string? body = null, List<Attachment>? attachments = null, List<string>? bcc = null, bool isHtml = false)
        {
            this.toEmail = toEmail;
            this.bcc = bcc;
            this.subject = subject;
            this.body = body;
            this.attachments = attachments;
            this.isHtml = isHtml;
        }
    }
}
