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
            var today = now.Date;
            var oneHourAgo = now.AddHours(-1);

            // ================================
            // 1️⃣ حد الساعة (20 مجموعة لكل ساعة)
            // ================================
            int joinedCountLastHour = await _context.user_joined_groups
                .CountAsync(g => g.user_id == (int)userId && g.joined_at >= oneHourAgo);

            if (joinedCountLastHour >= 20)
            {
                return Ok(new
                {
                    success = false,
                    status = "limit_reached",
                    message = "لقد وصلت للحد الأقصى للانضمام (20 مجموعة خلال ساعة). يرجى المحاولة لاحقاً."
                });
            }

            // ================================
            // 2️⃣ الاشتراكات النشطة
            // ================================
            var activeSubs = await _context.UserSubscriptions
                .Where(s =>
                    s.UserId == (int)userId &&
                    s.IsActive &&
                    s.PaymentStatus == "paid" &&
                    s.StartDate <= today &&
                    s.EndDate >= today)
                .ToListAsync();

            if (!activeSubs.Any())
                return Ok(new { success = false, status = "no_subscription", message = "No active subscription found" });

            var subIds = activeSubs.Select(s => s.Id).ToList();

            // ================================
            // 3️⃣ جلب جميع الميزات الخاصة بإضافة المجموعات
            // ================================
            var featureIds = await _context.PackageFeatures
                .Where(f => f.PlatformId == 1 && f.forGetingGruops)
                .Select(f => f.Id)
                .ToListAsync();

            if (!featureIds.Any())
                return Ok(new { success = false, status = "feature_not_found", message = "Feature for joining groups not found" });

            // ================================
            // 4️⃣ جلب الاستخدام من كل الباقات
            // ================================
            var usageList = await _context.subscription_usage
                .Where(u => subIds.Contains(u.SubscriptionId) && featureIds.Contains(u.FeatureId))
                .ToListAsync();

            int totalLimit = usageList.Sum(u => u.LimitCount);
            int totalUsed = usageList.Sum(u => u.UsedCount);
            int remaining = Math.Max(totalLimit - totalUsed, 0);

            if (remaining <= 0)
            {
                return Ok(new
                {
                    success = false,
                    status = "package_limit",
                    message = "تم استهلاك الحد المسموح للانضمام إلى المجموعات"
                });
            }

            // ================================
            // 5️⃣ التحقق من حساب الواتساب المتصل
            // ================================
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a =>
                    a.UserId == (int)userId &&
                    a.PlatformId == 1 &&
                    a.Status == "connected");

            if (account == null || string.IsNullOrEmpty(account.WasenderSessionId?.ToString()))
                return Ok(new { success = false, status = "no_account", message = "No connected WhatsApp account" });

            if (string.IsNullOrWhiteSpace(req.Code))
                return BadRequest(new { success = false, status = "invalid_code", message = "Invite code is required" });

            // ================================
            // 6️⃣ جلب بيانات الدعوة للتحقق من صحتها
            // ================================
            var inviteRequest = new RestRequest($"/api/groups/invite/{req.Code}", Method.Get);
            inviteRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var inviteResp = await _client.ExecuteAsync(inviteRequest);

            if (!inviteResp.IsSuccessful)
            {
                return Ok(new
                {
                    success = false,
                    status = "invite_fetch_failed",
                    message = "Failed to fetch invite info",
                    reason = inviteResp.Content
                });
            }

            var inviteJson = JObject.Parse(inviteResp.Content);
            var inviteGroupId = inviteJson["data"]?["id"]?.ToString();

            if (string.IsNullOrEmpty(inviteGroupId))
                return Ok(new { success = false, status = "invalid_code", message = "Invalid invite code" });

            // ================================
            // 7️⃣ التأكد إن المستخدم مش عضو مسبقاً
            // ================================
            var groupsRequest = new RestRequest("/api/groups", Method.Get);
            groupsRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            var groupsResp = await _client.ExecuteAsync(groupsRequest);

            if (!groupsResp.IsSuccessful)
                return Ok(new { success = false, status = "groups_fetch_failed", message = groupsResp.Content });

            var groupsJson = JObject.Parse(groupsResp.Content);
            var existingGroups = groupsJson["data"]?.ToObject<List<JObject>>() ?? new List<JObject>();

            bool alreadyMember = existingGroups.Any(g => g["id"]?.ToString() == inviteGroupId);

            if (alreadyMember)
            {
                return Ok(new { success = true, skipped = true, message = "Already a member" });
            }

            // ================================
            // 8️⃣ محاولة الانضمام (مرة واحدة فقط)
            // ================================
            var joinRequest = new RestRequest("/api/groups/invite/accept", Method.Post);
            joinRequest.AddHeader("Authorization", $"Bearer {account.AccessToken}");
            joinRequest.AddHeader("Content-Type", "application/json");
            joinRequest.AddJsonBody(new { code = req.Code });

            var joinResp = await _client.ExecuteAsync(joinRequest);

            if (!joinResp.IsSuccessful)
            {
                return Ok(new
                {
                    success = false,
                    status = "join_failed",
                    message = "Failed to accept invite",
                    reason = joinResp.Content,
                    httpCode = joinResp.StatusCode
                });
            }

            JObject joinJson;
            try
            {
                joinJson = JObject.Parse(joinResp.Content);
            }
            catch
            {
                return Ok(new
                {
                    success = false,
                    status = "invalid_json",
                    message = "Invalid JSON from WhatsApp API",
                    raw = joinResp.Content
                });
            }

            string apiSuccess = joinJson["success"]?.ToString() ?? "false";
            string groupName = joinJson["data"]?["groupId"]?.ToString() ?? "";

            if (apiSuccess != "True")
            {
                return Ok(new
                {
                    success = false,
                    status = "api_rejected",
                    message = "WhatsApp API rejected the request",
                    reason = joinResp.Content
                });
            }

            // ================================
            // 9️⃣ تخزين الانضمام
            // ================================
            var existing = await _context.user_joined_groups
                .FirstOrDefaultAsync(g => g.user_id == (int)userId &&
                                          g.group_invite_code == req.Code);

            if (existing == null)
            {
                _context.user_joined_groups.Add(new UserJoinedGroup
                {
                    user_id = (int)userId,
                    group_invite_code = req.Code,
                    group_name = groupName,
                    joined_at = now,
                    is_active = true
                });
            }
            else
            {
                existing.is_active = true;
                existing.joined_at = now;
                _context.user_joined_groups.Update(existing);
            }

            // ================================
            // 🔟 توزيع الخصم على الباقات
            // ================================
            var orderedUsage = usageList
                .OrderBy(u => (u.LimitCount - u.UsedCount))
                .ToList();

            foreach (var u in orderedUsage)
            {
                int remainingCount = u.LimitCount - u.UsedCount;

                if (remainingCount > 0)
                {
                    u.UsedCount += 1;
                    _context.subscription_usage.Update(u);
                    break;
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                skipped = false,
                message = "Invite accepted successfully",
                groupName,
                remainingLimit = remaining - 1
            });
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

