using MailKit.Net.Smtp;
using MimeKit;

public class EmailService
{
    public async Task<bool> SendVerificationEmailAsync(string toEmail, string code)
    {
        var email = new MimeMessage();
        email.From.Add(MailboxAddress.Parse("ziadibrahim545@gmail.com"));
        email.To.Add(MailboxAddress.Parse(toEmail));
        email.Subject = "Verification Code";
        email.Body = new TextPart("plain")
        {
            Text = $"Your verification code is: {code}"
        };

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync("ziadibrahim545@gmail.com", "zhsd jljf xexg iecg");
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
