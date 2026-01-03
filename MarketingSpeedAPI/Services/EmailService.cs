using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;
using System;
using System.Threading.Tasks;

public class EmailService
{
    private readonly string smtpHost = "smtp.zoho.com";
    private readonly int smtpPort = 465; // SSL مباشر
    private readonly string smtpUser = "966-547948416.501@zohomail.com";
    private readonly string smtpPass = "hcbSwGZyU93b";

    public async Task<bool> SendVerificationEmailAsync(string toEmail, string code)
    {
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress("MarketingSpeed", smtpUser));
        email.To.Add(MailboxAddress.Parse(toEmail));
        email.Subject = $"✨ كود التفعيل: {code}";

        var builder = new BodyBuilder();

        // معالجة الشعار
        var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "logo.png");
        var logo = builder.LinkedResources.Add(logoPath);
        logo.ContentId = MimeUtils.GenerateMessageId();

        string codeHtml = "";
        foreach (var digit in code)
        {
            codeHtml += $@"
            <div style='display:inline-block; width:42px; height:52px; line-height:52px; 
                        text-align:center; background:#f0f7ff; border:1px solid #cce3ff; 
                        color:#2b7bff; font-size:28px; font-weight:800; border-radius:12px; 
                        margin:0 4px; box-shadow: 0 4px 10px rgba(43,123,255,0.1); 
                        font-family: ""Courier New"", Courier, monospace;'>
                {digit}
            </div>";
        }

        builder.HtmlBody = $@"
    <div dir='rtl' style='background-color: #f3f4f6; padding: 40px 15px; font-family: ""Segoe UI"", Roboto, Helvetica, sans-serif;'>
        <div style='max-width: 480px; margin: 0 auto; background: #ffffff; border-radius: 35px; overflow: hidden; box-shadow: 0 20px 40px rgba(0,0,0,0.06);'>
            
            <div style='background: #2b7bff; background: linear-gradient(135deg, #2b7bff 0%, #00d2ff 100%); padding: 45px 20px; text-align: center;'>
                <img src='cid:{logo.ContentId}' alt='MarketingSpeed' style='width: 140px; filter: brightness(0) invert(1);' />
                <h2 style='color: #ffffff; margin-top: 15px; font-size: 22px; font-weight: 500;'>أهلاً بك في رحلتك الجديدة! 🚀</h2>
            </div>

            <div style='padding: 40px 30px; text-align: center;'>
                <p style='color: #374151; font-size: 18px; font-weight: 600; margin-bottom: 10px;'>خطوة واحدة تفصلك عن البداية</p>
                <p style='color: #6b7280; font-size: 15px; line-height: 1.6; margin-bottom: 30px;'>
                    سعداء جداً بانضمامك لأسرة <strong>MarketingSpeed</strong>. 
                    <br>أدخل الرمز التالي لتفعيل حسابك والبدء فوراً:
                </p>

                <div style='margin-bottom: 35px;'>
                    {codeHtml}
                </div>

                <div style='display: inline-block; background: #fffbeb; border: 1px solid #fef3c7; border-radius: 12px; padding: 12px 20px; margin-bottom: 30px;'>
                    <p style='color: #92400e; font-size: 13px; margin: 0;'>
                        ⚠️ الرمز صالح لمدة <strong>10 دقائق</strong> فقط. لا تشاركه مع أحد.
                    </p>
                </div>

                <div style='border-top: 2px solid #f3f4f6; margin-top: 10px; padding-top: 30px;'>
                    <p style='color: #9ca3af; font-size: 12px;'>
                        إذا لم تكن قد طلبت التسجيل في تطبيقنا، يمكنك ببساطة تجاهل هذه الرسالة.
                    </p>
                </div>
            </div>

            <div style='padding: 25px; background: #fafafa; text-align: center; border-top: 1px solid #f3f4f6;'>
                <p style='color: #2b7bff; font-weight: bold; font-size: 14px; margin-bottom: 10px;'>تابعنا لتطوير مهاراتك</p>
                <div style='margin-bottom: 15px;'>
                    <a href='#' style='text-decoration:none; margin:0 5px;'>🔵</a>
                    <a href='#' style='text-decoration:none; margin:0 5px;'>📸</a>
                    <a href='#' style='text-decoration:none; margin:0 5px;'>🐦</a>
                </div>
                <p style='color: #d1d5db; font-size: 10px; margin: 0;'>
                    © {DateTime.Now.Year} MarketingSpeed - سرعة، إبداع، تميز.
                </p>
            </div>
        </div>
    </div>";

        email.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.SslOnConnect);
            await smtp.AuthenticateAsync(smtpUser, smtpPass);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            return false;
        }
    }
}
