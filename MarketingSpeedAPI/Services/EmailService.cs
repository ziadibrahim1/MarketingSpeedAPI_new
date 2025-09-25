using MailKit.Net.Smtp;
using MimeKit;
public class EmailService
{
   

public async Task<bool> SendVerificationEmailAsync(string toEmail, string code)
{
    var email = new MimeMessage();

    email.From.Add(new MailboxAddress("Marketing Speed", "contact@marketingspeed.linkpc.net"));
    email.To.Add(MailboxAddress.Parse(toEmail));
    email.Subject = "Marketing Speed";
    email.Body = new TextPart("plain")
    {
        Text = $"Your verification code is: {code}"
    };

    using var smtp = new SmtpClient();
    try
    {
        await smtp.ConnectAsync("relay.dnsexit.com", 587, MailKit.Security.SecureSocketOptions.StartTls);

        await smtp.AuthenticateAsync("marketingspeed", "Ziad.@680");

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
