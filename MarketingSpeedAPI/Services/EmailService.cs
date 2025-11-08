using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Threading.Tasks;

public class EmailService
{
    // ✅ الإعدادات من Zoho كما في الصورة
    private readonly string smtpHost = "smtp.zoho.com";
    private readonly int smtpPort = 465; // SSL مباشر
    private readonly string smtpUser = "966-547948416.501@zohomail.com";
    private readonly string smtpPass = "hcbSwGZyU93b"; // استخدم App Password لو عندك 2FA

    public async Task<bool> SendVerificationEmailAsync(string toEmail, string code)
    {
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress("MarketingSpeed", smtpUser));
        email.To.Add(MailboxAddress.Parse(toEmail));
        email.Subject = "Verification Code";
        email.Body = new TextPart("plain")
        {
            Text = $"Your verification code is: {code}"
        };

        using var smtp = new SmtpClient();
        try
        {
            // الاتصال الآمن بـ Zoho Mail
            await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.SslOnConnect);
            await smtp.AuthenticateAsync(smtpUser, smtpPass);

            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);

            Console.WriteLine("✅ Email sent successfully via Zoho Mail");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to send email: {ex.Message}");
            return false;
        }
    }
}
