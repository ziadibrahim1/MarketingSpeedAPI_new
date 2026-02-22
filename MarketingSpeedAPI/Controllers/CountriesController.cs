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
            var now = DateTime.Now;
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
                            && !g.IsHidden && g.PlatformId == 2
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

        [HttpGet("telegram/{CountryID}/{userId}")]
        public async Task<IActionResult> GetTelegramGroups(ulong userId, int CountryID)
        {
            var now = DateTime.Now;
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
                .Where(f => f.forGetingGruops && f.PlatformId == 2)
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
                .Select(j => "https://t.me/" + j.group_invite_code)
                .ToListAsync();

            // 🔹 جلب المجموعات
            var groups = await _context.company_groups
                .Where(g => g.IsActive
                            && !g.IsHidden && g.PlatformId==2
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
                .FirstOrDefaultAsync(a =>
                    a.UserId == (int)userId &&
                    a.PlatformId == 1 &&
                    a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            try
            {
                // 🔥 1) تجهيز الرابط الصحيح من Whapi
                var options = new RestClientOptions($"https://gate.whapi.cloud/groups/link/{inviteCode}");
                var client = new RestClient(options);

                var request = new RestRequest("", Method.Get);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                // 🔥 2) تنفيذ الطلب
                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
                {
                    return Ok(new
                    {
                        success = false,
                        size = 0,
                        error = response.Content
                    });
                }

                var json = JsonDocument.Parse(response.Content);
                var root = json.RootElement;

                // 🔥 3) قراءة participantsCount من الرد الجديد
                int size = 0;
                if (root.TryGetProperty("participantsCount", out var countProp))
                    size = countProp.GetInt32();

                return Ok(new
                {
                    success = true,
                    size = size
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    size = 0,
                    error = ex.Message
                });
            }
        }


        [HttpPost("accept-invite/{userId}")]
        public async Task<IActionResult> AcceptInvite(ulong userId, [FromBody] InviteRequest req)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var oneHourAgo = now.AddHours(-1);

            // 1️⃣ limit per hour
            var joinedCountLastHour = await _context.user_joined_groups
                .CountAsync(g => g.user_id == (int)userId && g.joined_at >= oneHourAgo);

            if (joinedCountLastHour >= 20)
            {
                return Ok(new
                {
                    success = false,
                    status = "limit_reached",
                    message = "لقد وصلت للحد الأقصى للانضمام (20 مجموعة خلال ساعة)."
                });
            }

            // 2️⃣ subscription check
            var activeSubs = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .ToListAsync();

            if (!activeSubs.Any())
                return Ok(new { success = false, status = "no_subscription", message = "No active subscription found" });

            var subIds = activeSubs.Select(s => s.Id).ToList();

            // 3️⃣ features
            var featureIds = await _context.PackageFeatures
                .Where(f => f.PlatformId == 1 && f.forGetingGruops)
                .Select(f => f.Id)
                .ToListAsync();

            if (!featureIds.Any())
                return Ok(new { success = false, status = "feature_not_found" });

            // 4️⃣ usage
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

            // 5️⃣ Account check
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a =>
                    a.UserId == (int)userId &&
                    a.PlatformId == 1 &&
                    a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, status = "no_account" });

            if (string.IsNullOrWhiteSpace(req.Code))
                return BadRequest(new { success = false, status = "invalid_code" });

            // 6️⃣ Execute WHAPI NEW JOIN request
            var joinClient = new RestClient("https://gate.whapi.cloud/groups");
            var joinRequest = new RestRequest("", Method.Put);

            joinRequest.AddHeader("accept", "application/json");
            joinRequest.AddHeader("authorization", $"Bearer {account.AccessToken}");
            joinRequest.AddJsonBody(new { invite_code = req.Code });

            var joinResp = await joinClient.ExecuteAsync(joinRequest);

            if (!joinResp.IsSuccessful)
            {
                return Ok(new
                {
                    success = false,
                    status = "join_failed",
                    message = "WhatsApp API failed",
                    reason = joinResp.Content,
                    code = joinResp.StatusCode
                });
            }

            // Parse result
            var joinJson = JObject.Parse(joinResp.Content);
            string groupId = joinJson["group_id"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(groupId))
            {
                return Ok(new
                {
                    success = false,
                    status = "invalid_code",
                    message = "Invite code invalid"
                });
            }

            // 7️⃣ Store join history
            var exist = await _context.user_joined_groups
                .FirstOrDefaultAsync(g => g.user_id == (int)userId &&
                                          g.group_invite_code == req.Code);

            if (exist == null)
            {
                _context.user_joined_groups.Add(new UserJoinedGroup
                {
                    user_id = (int)userId,
                    group_invite_code = req.Code,
                    group_name = groupId,
                    joined_at = now,
                    is_active = true
                });
            }
            else
            {
                exist.is_active = true;
                exist.joined_at = now;
                _context.user_joined_groups.Update(exist);
            }

            // 8️⃣ Deduct usage
            var orderedUsage = usageList.OrderBy(u => (u.LimitCount - u.UsedCount)).ToList();

            foreach (var u in orderedUsage)
            {
                int remain = u.LimitCount - u.UsedCount;

                if (remain > 0)
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
                groupName = groupId,
                remainingLimit = remaining - 1
            });
        }
        [HttpPost("accept-tele-invite/{userId}")]
        public async Task<IActionResult> AcceptTeleInvite(ulong userId, [FromBody] InviteRequest req)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var oneHourAgo = now.AddHours(-1);

            // 1️⃣ limit per hour
            var joinedCountLastHour = await _context.user_joined_groups
                .CountAsync(g => g.user_id == (int)userId && g.joined_at >= oneHourAgo);

            if (joinedCountLastHour >= 20)
            {
                return Ok(new
                {
                    success = false,
                    status = "limit_reached",
                    message = "لقد وصلت للحد الأقصى للانضمام (20 مجموعة خلال ساعة)."
                });
            }

            // 2️⃣ subscription check
            var activeSubs = await _context.UserSubscriptions
                .Where(s => s.UserId == (int)userId &&
                            s.IsActive &&
                            s.PaymentStatus == "paid" &&
                            s.StartDate <= today &&
                            s.EndDate >= today)
                .ToListAsync();

            if (!activeSubs.Any())
                return Ok(new { success = false, status = "no_subscription", message = "No active subscription found" });

            var subIds = activeSubs.Select(s => s.Id).ToList();

            // 3️⃣ features
            var featureIds = await _context.PackageFeatures
                .Where(f => f.PlatformId == 1 && f.forGetingGruops)
                .Select(f => f.Id)
                .ToListAsync();

            if (!featureIds.Any())
                return Ok(new { success = false, status = "feature_not_found" });

            // 4️⃣ usage
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

            // 5️⃣ Account check
            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a =>
                    a.UserId == (int)userId &&
                    a.PlatformId == 1 &&
                    a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, status = "no_account" });

            if (string.IsNullOrWhiteSpace(req.Code))
                return BadRequest(new { success = false, status = "invalid_code" });

            // 6️⃣ Execute WHAPI NEW JOIN request
            var joinClient = new RestClient("https://gate.whapi.cloud/groups");
            var joinRequest = new RestRequest("", Method.Put);

            joinRequest.AddHeader("accept", "application/json");
            joinRequest.AddHeader("authorization", $"Bearer {account.AccessToken}");
            joinRequest.AddJsonBody(new { invite_code = req.Code });

            var joinResp = await joinClient.ExecuteAsync(joinRequest);

            if (!joinResp.IsSuccessful)
            {
                return Ok(new
                {
                    success = false,
                    status = "join_failed",
                    message = "WhatsApp API failed",
                    reason = joinResp.Content,
                    code = joinResp.StatusCode
                });
            }

            // Parse result
            var joinJson = JObject.Parse(joinResp.Content);
            string groupId = joinJson["group_id"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(groupId))
            {
                return Ok(new
                {
                    success = false,
                    status = "invalid_code",
                    message = "Invite code invalid"
                });
            }

            // 7️⃣ Store join history
            var exist = await _context.user_joined_groups
                .FirstOrDefaultAsync(g => g.user_id == (int)userId &&
                                          g.group_invite_code == req.Code);

            if (exist == null)
            {
                _context.user_joined_groups.Add(new UserJoinedGroup
                {
                    user_id = (int)userId,
                    group_invite_code = req.Code,
                    group_name = groupId,
                    joined_at = now,
                    is_active = true
                });
            }
            else
            {
                exist.is_active = true;
                exist.joined_at = now;
                _context.user_joined_groups.Update(exist);
            }

            // 8️⃣ Deduct usage
            var orderedUsage = usageList.OrderBy(u => (u.LimitCount - u.UsedCount)).ToList();

            foreach (var u in orderedUsage)
            {
                int remain = u.LimitCount - u.UsedCount;

                if (remain > 0)
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
                groupName = groupId,
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

            if (account == null)
                return Ok(new { success = false, message = "No account found" });

            try
            {
                
                string inviteCode = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(req.Jid)
                ).Replace("=", "").Replace("/", "").Replace("+", "");

                if (inviteCode.Length > 12)
                    inviteCode = inviteCode.Substring(0, 12);

                // =============================
                // 🆕 WHAPI — Leave group
                // =============================
                var options = new RestClientOptions($"https://gate.whapi.cloud/groups/{req.Jid}");
                var client = new RestClient(options);

                var request = new RestRequest("", Method.Delete);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);

                if (!response.IsSuccessful)
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        message = "Failed to leave group",
                        details = response.Content
                    });
                }

                // =============================
                // 🔵 تحديث joined group — تعطيل
                // =============================
                var joinedGroup = await _context.user_joined_groups
                    .FirstOrDefaultAsync(g => g.user_id == (int)userId);

                if (joinedGroup != null)
                {
                    joinedGroup.is_active = false;
                    _context.user_joined_groups.Update(joinedGroup);
                }

                // =============================
                // 🟢 سجل مغادرة المجموعة في LeftGroups
                // =============================
                var leftGroup = new LeftGroup
                {
                    UserId = (int)userId,
                    GroupId = req.Jid,
                    GroupName = req.GroupName ?? "",
                    InviteLink = inviteCode,  // ❗ inviteCode الوهمي
                    LeftAt = DateTime.Now
                };
                _context.LeftGroups.Add(leftGroup);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Group left successfully",
                    inviteCode = inviteCode // 🔁 ترجع زي القديم
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Exception occurred",
                    details = ex.Message
                });
            }
        }


        [HttpPost("delete-group-chat/{userId}")]
        public async Task<IActionResult> DeleteGroupChat(long userId, [FromBody] LeaveGroupRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Jid))
                return BadRequest(new { success = false, message = "Jid is required" });

            var account = await _context.user_accounts
                .FirstOrDefaultAsync(a => a.UserId == (int)userId && a.PlatformId == 1 && a.Status == "connected");

            if (account == null)
                return Ok(new { success = false, message = "No connected account found" });

            try
            {
                // WHAPI DELETE CHAT
                var client = new RestClient($"https://gate.whapi.cloud/chats/{req.Jid}");
                var request = new RestRequest("", Method.Delete);
                request.AddHeader("accept", "application/json");
                request.AddHeader("authorization", $"Bearer {account.AccessToken}");

                var response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        message = "Failed to delete chat",
                        details = response.Content
                    });
                }

                // لا نحتاج DB Updates — فقط نرجّع success حتى لا تتأثر شاشة الفلاتر
                return Ok(new
                {
                    success = true,
                    message = "Chat cleared successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Exception occurred",
                    details = ex.Message
                });
            }
        }


    }

}

