using MailKit.Net.Smtp;
using MimeKit;
using System;
using System.Threading.Tasks;

public class EmailService
{
    private readonly string smtpHost = "smtp.gmail.com";
    private readonly int smtpPort = 587;
    private readonly string smtpUser = "markting.speed@gmail.com";
    private readonly string smtpPass = "otdc nfbw ypvx ohon"; // كلمة مرور التطبيق

    public async Task<bool> SendVerificationEmailAsync(string toEmail, string code)
    {
        var email = new MimeMessage();

        // الاسم اللي هيظهر للعميل + البريد الحقيقي
        email.From.Add(new MailboxAddress("سرعة التسويق", smtpUser));
        email.To.Add(MailboxAddress.Parse(toEmail));
        email.Subject = "Verification Code";
        email.Body = new TextPart("plain")
        {
            Text = $"Your verification code is: {code}"
        };

        using var smtp = new SmtpClient();
        try
        {
            // الاتصال بـ Gmail باستخدام STARTTLS
            await smtp.ConnectAsync(smtpHost, smtpPort, MailKit.Security.SecureSocketOptions.StartTls);

            // المصادقة باستخدام App Password
            await smtp.AuthenticateAsync(smtpUser, smtpPass);

            // إرسال البريد
            await smtp.SendAsync(email);

            await smtp.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Email sending failed: {ex.Message}");
            return false;
        }
    }
}
