using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Text.Json;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CountriesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CountriesController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetCountries()
        {
            var countries = await _context.Countries
                .Where(c => c.IsActive)
                .ToListAsync();
            return Ok(countries);
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;
        public CategoriesController(AppDbContext context) => _context = context;

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .Where(c => c.IsActive)
                .ToListAsync();
            return Ok(categories);
        }
    }


    [ApiController]
    [Route("api/[controller]")]
    public class CompanyGroupsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly RestClient _client;
        private readonly string _apiKey;
        public CompanyGroupsController(AppDbContext context, IOptions<WasenderSettings> wasenderOptions)
        {
            _context = context;

            _apiKey = wasenderOptions.Value.ApiKey;
            _client = new RestClient(wasenderOptions.Value.BaseUrl.TrimEnd('/'));
        }

        [HttpGet("{CategoryID}/{CountryID}/{userId}")]
        public async Task<IActionResult> GetGroups(ulong userId, int CategoryID, int CountryID)
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var oneHourAgo = now.AddHours(-1);

            // 🔹 الاشتراكات النشطة
            var activeSubs = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .Select(s => s.Id)
                .ToListAsync();

            // ✅ اجلب كل الـ Feature Ids الخاصة بميزة "الحصول على المجموعات"
            var usageFeatureIds = await _context.PackageFeatures
                .Where(f => f.forGetingGruops && f.PlatformId == 1)
                .Select(f => f.Id)
                .ToListAsync();

            int totalLimit = 0, usedCount = 0, remaining = 0;

            if (usageFeatureIds.Count > 0 && activeSubs.Count > 0)
            {
                // 🔹 قراءة الاستخدام من subscription_usage عبر كل الباقات وكل الـ features المطابقة
                var usage = await _context.subscription_usage
                    .Where(u => activeSubs.Contains(u.SubscriptionId) &&
                                usageFeatureIds.Contains(u.FeatureId))
                    .ToListAsync();

                totalLimit = usage.Sum(u => u.LimitCount);
                usedCount = usage.Sum(u => u.UsedCount);
                remaining = Math.Max(totalLimit - usedCount, 0);
            }

            // 🔹 حساب المجموعات التي انضم إليها المستخدم في الساعة الماضية
            int joinedLastHour = await _context.user_joined_groups
                .CountAsync(j => j.user_id == (int)userId && j.joined_at >= oneHourAgo && j.is_active);

            int hourlyLimit = 20; // لا يزيد عن 20 في الساعة
            int remainingThisHour = Math.Max(hourlyLimit - joinedLastHour, 0);

            // (اختياري) القرار الفعلي المتاح الآن
            int effectiveRemaining = Math.Min(remaining, remainingThisHour);

            // 🔹 استبعاد المجموعات المنضم إليها مسبقًا
            var joinedInviteCodes = await _context.user_joined_groups
                .Where(j => j.user_id == (int)userId && j.is_active)
                .Select(j => "https://chat.whatsapp.com/" + j.group_invite_code)
                .ToListAsync();

            // 🔹 جلب المجموعات
            var groups = await _context.company_groups
                .Where(g => g.IsActive
                            && !g.IsHidden
                            && g.CountryId == CountryID
                            && g.CategoryId == CategoryID
                            && !joinedInviteCodes.Contains(g.InviteLink))
                .Include(g => g.OurGroupsCountry)
                .Include(g => g.OurGroupsCategory)
                .Select(g => new CompanyGroup
                {
                    Id = g.Id,
                    GroupName = g.GroupName,
                    InviteLink = g.InviteLink,
                    Description = g.Description,
                    Price = g.Price,
                    IsActive = g.IsActive,
                    IsHidden = g.IsHidden,
                    SendingStatus = g.SendingStatus,
                    CountryNameAr = g.OurGroupsCountry != null ? g.OurGroupsCountry.NameAr : "",
                    CountryNameEn = g.OurGroupsCountry != null ? g.OurGroupsCountry.NameEn : "",
                    CategoryNameAr = g.OurGroupsCategory != null ? g.OurGroupsCategory.NameAr : "",
                    CategoryNameEn = g.OurGroupsCategory != null ? g.OurGroupsCategory.NameEn : ""
                })
                .ToListAsync();

            // 🔹 النتيجة النهائية
            return Ok(new
            {
                success = true,
                message = "Groups and usage fetched successfully",
                totalLimit,        // مجموع الحدود عبر كل الباقات
                usedCount,         // مجموع الاستخدام عبر كل الباقات
                remaining,         // المتبقي الإجمالي
                hourlyLimit,
                joinedLastHour,
                remainingThisHour,
                effectiveRemaining, // (اختياري) أقل قيمة بين المتبقي الإجمالي وحد الساعة
                groups
            });
        }

        [HttpPost("update-usage/{userId}")]
        public async Task<IActionResult> UpdateUsage(int userId, [FromBody] UpdateUsageDto model)
        {
            var subs = await _context.UserSubscriptions
                .Where(s => s.UserId == userId && s.IsActive && s.PaymentStatus == "paid")
                .Select(s => s.Id)
                .ToListAsync();

            var featureId = await _context.PackageFeatures
                .Where(f => f.forGetingGruops)
                .Select(f => f.Id)
                .FirstOrDefaultAsync();

            var usages = await _context.subscription_usage
                .Where(u => subs.Contains(u.SubscriptionId) && u.FeatureId == featureId)
                .ToListAsync();

            // 👇 يخصم الاستخدام بالترتيب من الباقات حتى يستهلك العدد المطلوب
            int remainingToDeduct = model.used;
            foreach (var usage in usages)
            {
                var available = usage.LimitCount - usage.UsedCount;
                if (available <= 0) continue;

                int deduct = Math.Min(remainingToDeduct, available);
                usage.UsedCount += deduct;
                remainingToDeduct -= deduct;

                if (remainingToDeduct <= 0)
                    break;
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

       


        [HttpGet("group-size/{inviteCode}/{userId}")]
        public async Task<IActionResult> GetGroupSize(string inviteCode, ulong userId)
        {
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, message = "No account found" });

            try
            {
                var request = new RestRequest($"/api/groups/invite/{inviteCode}", Method.Get);
                request.AddHeader("Authorization", $"Bearer {account.AccessToken}");

                var response = await _client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        message = "Failed to fetch group size",
                        details = response.Content
                    });
                }

                var json = JObject.Parse(response.Content);

                if (json["success"]?.Value<bool>() == true)
                {
                    int size = json["data"]?["size"]?.Value<int>() ?? 0;
                    return Ok(new { success = true, size });
                }
                else
                {
                    return Ok(new { success = false, size = 0 });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Exception occurred", details = ex.Message });
            }
        }




        [HttpPost("accept-invite/{userId}")]
        public async Task<IActionResult> AcceptInvite(ulong userId, [FromBody] InviteRequest req)
        {
            var now = DateTime.UtcNow;

            // ✅ التحقق من عدد المجموعات التي انضم إليها المستخدم خلال آخر ساعة
            var oneHourAgo = now.AddHours(-1);
            int joinedCountLastHour = await _context.user_joined_groups
                .Where(g => g.user_id == (int)userId && g.joined_at >= oneHourAgo)
                .CountAsync();

            if (joinedCountLastHour > 21)
            {
                return Ok(new
                {
                    success = false,
                    status = "limit_reached",
                    message = "لقد وصلت للحد الأقصى للانضمام (20 مجموعة خلال ساعة). يرجى المحاولة لاحقاً."
                });
            }

            var today = now.Date;

            // ✅ 1) التحقق من الاشتراك
            var subscription = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.Add_groups_limit > 0 &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (subscription == null)
                return Ok(new { success = false, status = "0", message = "Subscription invalid" });

            // ✅ 2) التحقق من الحساب المتصل
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, status = "1", message = "No account found" });

            if (string.IsNullOrWhiteSpace(req.Code))
                return BadRequest(new { success = false, message = "Invite code is required" });

            // ✅ 3) جلب بيانات المجموعة من رابط الدعوة
            var inviteRequest = new RestRequest($"/api/groups/invite/{req.Code}", Method.Get);
            inviteRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var inviteResp = await _client.ExecuteAsync(inviteRequest);

            if (!inviteResp.IsSuccessful)
            {
                return StatusCode((int)inviteResp.StatusCode, new
                {
                    success = false,
                    message = "Failed to fetch invite group info",
                    details = inviteResp.Content
                });
            }

            var inviteJson = JObject.Parse(inviteResp.Content);
            var inviteGroupId = inviteJson["data"]?["id"]?.ToString();

            if (string.IsNullOrEmpty(inviteGroupId))
            {
                return BadRequest(new { success = false, message = "Invalid invite code response" });
            }

            // ✅ 4) التحقق من المجموعات الحالية للمستخدم
            var groupsRequest = new RestRequest("/api/groups", Method.Get);
            groupsRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var groupsResp = await _client.ExecuteAsync(groupsRequest);
            if (!groupsResp.IsSuccessful)
                return StatusCode((int)groupsResp.StatusCode, groupsResp.Content);

            var groupsJson = JObject.Parse(groupsResp.Content);
            var existingGroups = groupsJson["data"]?.ToObject<List<JObject>>() ?? new List<JObject>();

            bool alreadyMember = existingGroups.Any(g =>
                string.Equals(g["id"]?.ToString(), inviteGroupId, StringComparison.OrdinalIgnoreCase));

            if (alreadyMember)
            {
                return Ok(new
                {
                    success = true,
                    message = "Already a member of this group",
                    skipped = true,
                    groupId = inviteGroupId
                });
            }

            // ✅ 5) الانضمام للمجموعة فعليًا
            var joinRequest = new RestRequest("/api/groups/invite/accept", Method.Post);
            joinRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            joinRequest.AddHeader("Content-Type", "application/json");
            joinRequest.AddJsonBody(new { code = req.Code });

            var response = await _client.ExecuteAsync(joinRequest);

            if (!response.IsSuccessful)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    success = false,
                    message = "Failed to accept invite",
                    details = response.Content
                });
            }

            try
            {
                var json = JObject.Parse(response.Content);

                var existingGroup = await _context.user_joined_groups
                    .FirstOrDefaultAsync(g => g.user_id == (int)userId && g.group_invite_code == req.Code);

                if (existingGroup == null)
                {
                    var joinedGroup = new UserJoinedGroup
                    {
                        user_id = (int)userId,
                        group_invite_code = req.Code,
                        group_name = json["data"]?["subject"]?.ToString() ?? "",
                        joined_at = DateTime.UtcNow,
                        is_active = true
                    };

                    _context.user_joined_groups.Add(joinedGroup);
                    subscription.Add_groups_limit -= 1;
                    _context.UserSubscriptions.Update(subscription);
                }
                else if (!existingGroup.is_active)
                {
                    existingGroup.is_active = true;
                    existingGroup.joined_at = DateTime.UtcNow;
                    _context.user_joined_groups.Update(existingGroup);

                    subscription.Add_groups_limit -= 1;
                    _context.UserSubscriptions.Update(subscription);
                }

                var leftGroup = await _context.LeftGroups
                    .FirstOrDefaultAsync(l => l.UserId == (int)userId && l.InviteLink == req.Code);

                if (leftGroup != null)
                    _context.LeftGroups.Remove(leftGroup);

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Invite accepted successfully",
                    data = json,
                    remainingLimit = subscription.Add_groups_limit
                });
            }
            catch
            {
                return Ok(new
                {
                    success = true,
                    message = "Invite accepted",
                    rawResponse = response.Content
                });
            }
        }


        [HttpPost("leave-group/{userId}")]
        public async Task<IActionResult> LeaveGroup(ulong userId, [FromBody] LeaveGroupRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Jid))
                return BadRequest(new { success = false, message = "Jid is required" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, message = "No account found" });

            try
            {
                var linkRequest = new RestRequest($"/api/groups/{req.Jid}/invite-link", Method.Get);
                linkRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                var linkResponse = await _client.ExecuteAsync(linkRequest);

                string inviteCode = "";
                if (linkResponse.IsSuccessful)
                {
                    var jsonLink = JObject.Parse(linkResponse.Content);
                    string inviteLink = jsonLink["inviteLink"]?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(inviteLink))
                    {
                        try
                        {
                            inviteCode = inviteLink.Split('/').Last();
                        }
                        catch
                        {
                            inviteCode = "";
                        }
                    }
                }

                var leaveRequest = new RestRequest($"/api/groups/{req.Jid}/leave", Method.Post);
                leaveRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
                var leaveResponse = await _client.ExecuteAsync(leaveRequest);

                if (!leaveResponse.IsSuccessful)
                    return StatusCode((int)leaveResponse.StatusCode, new { success = false, message = "Failed to leave group", details = leaveResponse.Content });

                var joinedGroup = await _context.user_joined_groups
                    .FirstOrDefaultAsync(g => g.user_id == (int)userId && g.group_invite_code == inviteCode);

                if (joinedGroup != null)
                {
                    joinedGroup.is_active = false;
                    _context.user_joined_groups.Update(joinedGroup);
                }

                var leftGroup = new LeftGroup
                {
                    UserId = (int)userId,
                    GroupId = req.Jid,
                    GroupName = req.GroupName ?? "",
                    InviteLink = inviteCode,
                    LeftAt = DateTime.UtcNow
                };

                _context.LeftGroups.Add(leftGroup);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Group left successfully", inviteCode });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Exception occurred", details = ex.Message });
            }
        }



    }

}

