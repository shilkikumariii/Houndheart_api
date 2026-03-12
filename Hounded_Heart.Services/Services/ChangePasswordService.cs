using Google.Apis.Auth.OAuth2;
using Hounded_Heart.Models.Data;
using Hounded_Heart.Models.Models;
using Hounded_Heart.Services.ServiceResult;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Graph.ExternalConnectors;


namespace Hounded_Heart.Services.Services
{
    public class ChangePasswordService
    {
        private readonly AppDbContext _dbContext;
        private readonly IConfiguration _configuration;
        public ChangePasswordService(AppDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }
        public async Task<ApiResponse<string>> SendMailchangepassword(EmailSendModel emailSendModel)
        {
            var azureSettings = _configuration.GetSection("AzureAd");
            string clientId = azureSettings["ClientId"];
            string clientSecret = azureSettings["ClientSecret"];
            string tenantId = azureSettings["TenantId"];
            string objectId = azureSettings["ObjectId"];
            var userdetails = _dbContext.Users
                .FirstOrDefault(x => x.Email == emailSendModel.Email);

            if (userdetails == null)
            {
                return ResponseHelper.Fail<string>("User not found.", 404);
            }

            if (string.IsNullOrEmpty(emailSendModel.Email))
            {
                return ResponseHelper.Fail<string>("Email address not valid", 400);
            }

            try
            {
                //var authContext = new AuthenticationContext($"https://login.microsoftonline.com/{_tenantId}");
                //var clientCredential = new ClientCredential(_clientId, _clientSecret);
                //var result = await authContext.AcquireTokenAsync("https://graph.microsoft.com", clientCredential);



                var otp = new Random().Next(1000, 9999).ToString();

                // Check if OTP record already exists for this user
                var existingOtp = await _dbContext.UserOtps
                    .FirstOrDefaultAsync(x => x.UserId == userdetails.UserId);

                if (existingOtp != null)
                {
                    // ✅ Update existing OTP record for resend
                    existingOtp.OtpCode = otp;
                    existingOtp.ExpiryTime = DateTime.UtcNow.AddMinutes(10);
                    existingOtp.IsUsed = false;
                    existingOtp.CreatedAt = DateTime.UtcNow;
                    _dbContext.UserOtps.Update(existingOtp);
                }
                else
                {
                    // ✅ Create new record only if not exists
                    var newOtp = new UserOtp
                    {
                        Id = Guid.NewGuid(),
                        UserId = userdetails.UserId,
                        Email = emailSendModel.Email,
                        OtpCode = otp,
                        ExpiryTime = DateTime.UtcNow.AddMinutes(10),
                        IsUsed = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.UserOtps.Add(newOtp);
                }

                await _dbContext.SaveChangesAsync();

                string body = $@"
        <html>
        <body>
          <p>Dear,</p>
          <p>Thank you for your request!</p>
          <p><strong>Your Code is:</strong> {otp}</p>
          <p>Please use this Code to verify your account within the next 10 minutes.</p>
          <p>If you did not request this Code, please ignore this message.</p>
          <p>Thank you,</p>
          <p>The Support Team</p>
        </body>
        </html>";
                var scopes = new[] { "https://graph.microsoft.com/.default" };

                var app = ConfidentialClientApplicationBuilder.Create(clientId)
                    .WithClientSecret(clientSecret)
                    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                    .Build();

                var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();

                var graphServiceClient = new GraphServiceClient(
                    "https://graph.microsoft.com/v1.0",
                    new DelegateAuthenticationProvider((request) =>
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", result.AccessToken);
                        return Task.CompletedTask;
                    })
                );

                var message = new Microsoft.Graph.Message
                {
                    Subject = "OTP Verification",
                    Body = new ItemBody
                    {
                        ContentType = BodyType.Html,
                        Content = body
                    },
                    ToRecipients = new List<Recipient>
    {
        new Recipient
        {
            EmailAddress = new EmailAddress
            {
                Address = emailSendModel.Email
            }
        }
    }
                };

                await graphServiceClient.Users[objectId]
                    .SendMail(message, true)
                    .Request()
                    .PostAsync();

                return ResponseHelper.Success("Mail sent successfully", "Mail Sent", 200);
            }
            catch (Exception ex)
            {
                return ResponseHelper.Fail<string>(ex.Message, 500);
            }
        }
    }
}
