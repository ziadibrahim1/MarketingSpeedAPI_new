using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketingSpeedAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PackagesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PackagesController(AppDbContext context)
        {
            _context = context;
        }

        // ✅ تعديل GetAll Packages
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PackageDto>>> GetAll(
     [FromQuery] int? categoryId,
     [FromQuery] string? status,           // ✅ فلترة بالحالة (active/inactive)
     [FromQuery] bool? archived,           // ✅ فلترة بالأرشفة
     [FromQuery] decimal? minPrice,        // ✅ فلترة بالسعر الأدنى
     [FromQuery] decimal? maxPrice,        // ✅ فلترة بالسعر الأعلى
     [FromQuery] int? minDuration,         // ✅ فلترة بالمدة الأدنى
     [FromQuery] int? maxDuration,         // ✅ فلترة بالمدة الأعلى
     [FromQuery] string? keyword           // ✅ بحث بالكلمة (اسم عربي/إنجليزي/مميزات)
 )
        {
            var query = _context.Packages
                .Include(p => p.Features)
                .Include(p => p.Category)
                .AsQueryable();

            // فلترة بالفئة
            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId.Value);

            
                query = query.Where(p => p.Status.ToLower()  == "active");

            // فلترة بالأرشفة
            if (archived.HasValue)
                query = query.Where(p => p.Archived == archived.Value);

            // فلترة بالسعر
            if (minPrice.HasValue)
                query = query.Where(p => p.Price >= minPrice.Value);
            if (maxPrice.HasValue)
                query = query.Where(p => p.Price <= maxPrice.Value);

            // فلترة بالمدة
            if (minDuration.HasValue)
                query = query.Where(p => p.DurationDays >= minDuration.Value);
            if (maxDuration.HasValue)
                query = query.Where(p => p.DurationDays <= maxDuration.Value);

            // فلترة بالبحث (اسم عربي / إنجليزي / ميزة)
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(p =>
                    p.Name.Contains(keyword) ||
                    p.NameEn.Contains(keyword) ||
                    p.Features.Any(f => f.feature.Contains(keyword) || f.FeatureEn.Contains(keyword))
                );
            }

            var packages = await query
                .Select(p => new PackageDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    color = p.color,
                    Fcolor = p.Fcolor,
                    NameEn = p.NameEn,
                    Price = (double)p.Price,
                    DurationDays = (int)p.DurationDays,
                    Discount = (double)p.Discount,
                    Features = p.Features.Select(f => new FeatureDto
                    {
                        feature = f.feature ?? "",
                        FeatureEn = f.FeatureEn ?? ""
                    }).ToList(),

                    FeaturesEn = p.Features.Select(f => new FeatureDto
                    {
                        feature = f.feature ?? "",
                        FeatureEn = f.FeatureEn ?? ""
                    }).ToList()
,
                    SubscriberCount = (int)p.SubscriberCount,
                    ImageUrl = p.ImageUrl,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category.Name,
                    CategoryNameEN = p.Category.NameEn
                }).ToListAsync();

            return Ok(packages);
        }

        [HttpGet("mysubscription")]
        public async Task<IActionResult> GetMySubscriptions(int userId)
        {
            try
            {
                var subscriptions = await _context.UserSubscriptions
                    .Where(s => s.UserId == userId && s.IsActive && s.PaymentStatus == "paid" && s.EndDate.Date >= DateTime.Now.Date)
                    .Include(s => s.Package)
                        .ThenInclude(p => p.Features)
                    .OrderByDescending(s => s.CreatedAt)
                    .ToListAsync();

                if (!subscriptions.Any())
                    return NotFound(new { message = "لا يوجد اشتراكات حالية" });

                var subscriptionIds = subscriptions.Select(s => s.Id).ToList();

                // 🧮 جلب الاستخدامات الحديثة
                var usageList = await _context.subscription_usage
                    .Where(u => subscriptionIds.Contains(u.SubscriptionId))
                    .ToListAsync();

                // 📦 تجميع حسب الفئة (Category)
                var groupedByCategory = subscriptions
                    .Where(s => s.Package != null)
                    .GroupBy(s => s.Package.CategoryId)
                    .ToList();

                var results = new List<object>();

                foreach (var group in groupedByCategory)
                {
                    if (group.Count() == 1)
                    {
                        var s = group.First();
                        var pkg = s.Package;

                        var pkgFeatures = pkg?.Features.Where(f => f.isMain).ToList() ?? new();

                        var featuresData = pkgFeatures.Select(f =>
                        {
                            var usage = usageList.FirstOrDefault(u => u.SubscriptionId == s.Id && u.FeatureId == f.Id);
                            int used = usage?.UsedCount ?? 0;
                            int limit = usage?.LimitCount ?? f.LimitCount;
                            int remaining = usage?.RemainingCount ?? Math.Max(limit - used, 0);

                            return new
                            {
                                f.Id,
                                f.feature,
                                f.FeatureEn,
                                f.forMembers,
                                f.forCreatingGroups,
                                f.forGetingGruops,
                                f.LimitCount,
                                UsedCount = used,
                                RemainingCount = remaining,
                                IsExceeded = remaining <= 0
                            };
                        }).ToList();

                        results.Add(new
                        {
                            subscription = new
                            {
                                s.Id,
                                s.PlanName,
                                s.Price,
                                StartDate = s.StartDate.ToString("yyyy-MM-dd"),
                                EndDate = s.EndDate.ToString("yyyy-MM-dd"),
                                s.PaymentStatus,
                                s.IsActive,
                                DaysLeft = (s.EndDate.Date - DateTime.Now.Date).Days
                            },
                            package = new
                            {
                                pkg?.Id,
                                pkg?.Name,
                                pkg?.color,
                                pkg?.Fcolor,
                                pkg?.CategoryId,
                                pkg?.NameEn,
                                pkg?.ImageUrl,
                                pkg?.DurationDays,
                                pkg?.Discount,
                                Features = featuresData,
                                pkg?.SubscriberCount
                            }
                        });
                    }
                    else
                    {
                        // 🔹 حالة الدمج (أكثر من باقة في نفس الفئة)
                        var firstSub = group.First();
                        var mergedName = string.Join(" + ", group.Select(s => s.Package?.Name ?? "Unknown"));
                        var mergedFeatures = new List<object>();

                        // جلب جميع الميزات الفريدة من جميع الباقات في هذا الكاتيجوري
                        var allFeatures = group
                            .SelectMany(s => s.Package.Features.Where(f => f.isMain))
                            .GroupBy(f => f.feature)
                            .ToList();

                        foreach (var featureGroup in allFeatures)
                        {
                            var baseFeature = featureGroup.First();
                            int totalLimit = usageList
                                .Where(u => group.Any(s => s.Id == u.SubscriptionId) &&
                                             featureGroup.Any(f => f.Id == u.FeatureId))
                                .Sum(u => u.LimitCount);


                            // 🔹 حساب الاستخدام المجمع
                            int totalUsed = usageList
                                .Where(u => group.Any(s => s.Id == u.SubscriptionId) &&
                                            featureGroup.Any(f => f.Id == u.FeatureId))
                                .Sum(u => u.UsedCount);

                            int remaining = Math.Max(totalLimit - totalUsed, 0);

                            mergedFeatures.Add(new
                            {
                                baseFeature.Id,
                                baseFeature.feature,
                                baseFeature.FeatureEn,
                                baseFeature.forMembers,
                                baseFeature.forCreatingGroups,
                                baseFeature.forGetingGruops,
                                LimitCount = totalLimit,
                                UsedCount = totalUsed,
                                RemainingCount = remaining,
                                IsExceeded = remaining <= 0
                            });
                        }

                        results.Add(new
                        {
                            subscription = new
                            {
                                Id = firstSub.Id,
                                PlanName = mergedName,
                                Price = group.Sum(s => s.Price),
                                StartDate = group.Min(s => s.StartDate).ToString("yyyy-MM-dd"),
                                EndDate = group.Max(s => s.EndDate).ToString("yyyy-MM-dd"),
                                PaymentStatus = "Merged",
                                IsActive = true,
                                DaysLeft = (group.Max(s => s.EndDate).Date - DateTime.Now.Date).Days,
                                MergedSubscriptionIds = group.Select(s => s.Id).ToList(),
                                MergedPackages = group.Select(s => new
                                {
                                    PackageId = s.PackageId,
                                    PackageName = s.Package?.Name,
                                    PackageNameEn = s.Package?.NameEn,
                                    s.Package?.ImageUrl
                                }).ToList()
                            },
                            package = new
                            {
                                Id = firstSub.Package?.Id,
                                Name = mergedName,
                                firstSub.Package?.color,
                                firstSub.Package?.Fcolor,
                                firstSub.Package?.CategoryId,
                                firstSub.Package?.ImageUrl,
                                firstSub.Package?.DurationDays,
                                firstSub.Package?.Discount,
                                Features = mergedFeatures,
                                firstSub.Package?.SubscriberCount
                            }
                        });
                    }
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error in GetMySubscriptions: {ex.Message}");
                return StatusCode(500, new { message = "حدث خطأ في الخادم", error = ex.Message });
            }
        }


        [HttpGet("user-subscription")]
        public async Task<IActionResult> GetUserSubscription([FromQuery] int userId)
        {
            var subscription = await _context.UserSubscriptions
                .Where(s => s.UserId == userId && s.IsActive)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (subscription == null)
                return NotFound(new { message = "No active subscription" });

            var package = await _context.Packages
                .FirstOrDefaultAsync(p => p.Name == subscription.PlanName);

            return Ok(new
            {
                subscription.Id,
                subscription.PlanName,
                subscription.Price,
                subscription.StartDate,
                subscription.EndDate,
                subscription.PaymentStatus,
                subscription.IsActive,
                PackageDetails = package
            });
        }

        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe([FromQuery] int id, [FromQuery] int userId)
        {
            var pkg = await _context.Packages.FindAsync(id);
            if (pkg == null)
                return NotFound(new { message = "Package not found" });

            var existingSubscription = await _context.UserSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.PackageId == pkg.Id && s.IsActive);

            if (existingSubscription != null)
                return BadRequest(new { message = "1" });

            var subscription = new UserSubscription
            {
                UserId = userId,
                PackageId = pkg.Id,
                PlanName = pkg.Name,
                Price = pkg.Price,
                StartDate = DateTime.Now,
                EndDate = DateTime.Now.AddDays(pkg.DurationDays),
                PaymentStatus = "paid",
                IsActive = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.UserSubscriptions.Add(subscription);
            pkg.SubscriberCount += 1;
            _context.Entry(pkg).State = EntityState.Modified;
            _context.Entry(pkg).Collection(p => p.Features).IsModified = false;

            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                user.subscreption = subscription.Id;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Subscription successful", subscriptionId = subscription.Id });
        }

        [HttpPost("unsubscribe")]
        public async Task<IActionResult> Unsubscribe([FromQuery] int id, [FromQuery] int userId)
        {
            // ابحث عن الاشتراك الحالي للمستخدم في هذه الباقة
            var subscription = await _context.UserSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.PackageId == id && s.IsActive);

            if (subscription == null)
            {
                return BadRequest(new { message = "2" }); // لا يوجد اشتراك نشط
            }

            // قم بإلغاء تفعيل الاشتراك
            subscription.IsActive = false;
            subscription.UpdatedAt = DateTime.Now;
            subscription.PaymentStatus = "cancelled";

            // قلل عدد المشتركين في الباقة
            var pkg = await _context.Packages.FindAsync(id);
            if (pkg != null && pkg.SubscriberCount > 0)
            {
                pkg.SubscriberCount -= 1;
            }

            // حدّث المستخدم
            var user = await _context.Users.FindAsync(userId);
            if (user != null && user.subscreption == subscription.Id)
            {
                user.subscreption = null;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Unsubscribed successfully" });
        }


        [HttpPost("add-usage")]
        public async Task<IActionResult> AddUsage([FromBody] SubscriptionUsage usage)
        {
            var subscription = await _context.UserSubscriptions
                .FirstOrDefaultAsync(s => s.Id == usage.SubscriptionId && s.IsActive);

            if (subscription == null)
                return BadRequest(new { message = "Subscription not found or inactive" });

            // ✅ التحقق من وجود سجل usage سابق لنفس الميزة
            var existingUsage = await _context.subscription_usage
                .FirstOrDefaultAsync(u =>
                    u.SubscriptionId == usage.SubscriptionId &&
                    u.FeatureId == usage.FeatureId);

            if (existingUsage != null)
            {
                existingUsage.UsedCount += usage.UsedCount;
                existingUsage.RemainingCount = Math.Max(existingUsage.LimitCount - existingUsage.UsedCount, 0);
                existingUsage.LastUsedAt = DateTime.Now;
                existingUsage.UpdatedAt = DateTime.Now;
                _context.subscription_usage.Update(existingUsage);
            }
            else
            {
                usage.CreatedAt = DateTime.Now;
                usage.UpdatedAt = DateTime.Now;
                usage.LastUsedAt = DateTime.Now;
                _context.subscription_usage.Add(usage);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Usage updated successfully" });
        }


        [HttpGet("usage-summary")]
        public async Task<IActionResult> GetUsageSummary([FromQuery] int subscriptionId)
        {
            var subscription = await _context.UserSubscriptions
                .Include(s => s.Package)
                .ThenInclude(p => p.Features)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.IsActive && s.PaymentStatus == "paid");

            if (subscription == null)
                return NotFound(new { message = "Subscription not found or inactive" });

            // 🧮 الاستخدامات من الجدول الجديد
            var usages = await _context.subscription_usage
                .Where(u => u.SubscriptionId == subscriptionId)
                .ToListAsync();

            // 🧩 ربط الاستخدام بالميزات
            var packageFeatures = subscription.Package?.Features ?? new List<PackageFeature>();

            var featureSummaries = packageFeatures.Select(f =>
            {
                var usage = usages.FirstOrDefault(u => u.FeatureId == f.Id);
                int used = usage?.UsedCount ?? 0;
                int limit = f.LimitCount;
                int remaining = Math.Max(limit - used, 0);

                return new
                {
                    f.Id,
                    f.feature,
                    f.FeatureEn,
                    f.forMembers,
                    f.forCreatingGroups,
                    f.forGetingGruops,
                    LimitCount = limit,
                    UsedCount = used,
                    RemainingCount = remaining,
                    LastUsedAt = usage?.LastUsedAt
                };
            }).ToList();

            // 📊 حساب الإجماليات
            int totalLimit = featureSummaries.Sum(f => f.LimitCount);
            int totalUsed = featureSummaries.Sum(f => f.UsedCount);
            int totalRemaining = featureSummaries.Sum(f => f.RemainingCount);
            DateTime? lastUsage = usages.OrderByDescending(u => u.LastUsedAt).FirstOrDefault()?.LastUsedAt;

            // 🔹 تلخيص حسب نوع الميزة
            int memberUsed = featureSummaries.Where(f => f.forMembers == true).Sum(f => f.UsedCount);
            int createGroupUsed = featureSummaries.Where(f => f.forCreatingGroups == true).Sum(f => f.UsedCount);
            int getGroupUsed = featureSummaries.Where(f => f.forGetingGruops == true).Sum(f => f.UsedCount);

            int memberRemaining = featureSummaries.Where(f => f.forMembers == true).Sum(f => f.RemainingCount);
            int createGroupRemaining = featureSummaries.Where(f => f.forCreatingGroups == true).Sum(f => f.RemainingCount);
            int getGroupRemaining = featureSummaries.Where(f => f.forGetingGruops == true).Sum(f => f.RemainingCount);

            return Ok(new
            {
                subscription.Id,
                subscription.PlanName,
                subscription.Price,
                StartDate = subscription.StartDate.ToString("yyyy-MM-dd"),
                EndDate = subscription.EndDate.ToString("yyyy-MM-dd"),
                DaysLeft = (subscription.EndDate.Date - DateTime.Now.Date).Days,

                Summary = new
                {
                    TotalUsed = totalUsed,
                    TotalLimit = totalLimit,
                    TotalRemaining = totalRemaining,
                    LastUsage = lastUsage
                },

                FeatureSummary = new
                {
                    forMembers = new { Used = memberUsed, Remaining = memberRemaining },
                    forCreatingGroups = new { Used = createGroupUsed, Remaining = createGroupRemaining },
                    forGetingGroups = new { Used = getGroupUsed, Remaining = getGroupRemaining }
                },

                Details = featureSummaries
            });
        }


        [HttpGet("{id}")]
        public async Task<ActionResult<Package>> GetById(int id)
        {
            var pkg = await _context.Packages
                .Include(p => p.Features)
                .Include(p => p.Logs)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pkg == null) return NotFound();
            return pkg;
        }

        [HttpPost]
        public async Task<ActionResult<Package>> Create(Package package)
        {
            _context.Packages.Add(package);
            await _context.SaveChangesAsync();

            // log
            _context.PackageLogs.Add(new PackageLog
            {
                PackageId = package.Id,
                UserId = 1, // TODO: استبدلها بالـ UserId من الـ JWT أو السياق
                Action = "create",
                Description = $"Package {package.Name} created"
            });
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = package.Id }, package);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, Package package)
        {
            if (id != package.Id) return BadRequest();

            _context.Entry(package).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            // log
            _context.PackageLogs.Add(new PackageLog
            {
                PackageId = package.Id,
                UserId = 1,
                Action = "update",
                Description = $"Package {package.Name} updated"
            });
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Archive(int id)
        {
            var pkg = await _context.Packages.FindAsync(id);
            if (pkg == null) return NotFound();

            pkg.Archived = true;
            await _context.SaveChangesAsync();

            _context.PackageLogs.Add(new PackageLog
            {
                PackageId = pkg.Id,
                UserId = 1,
                Action = "archive",
                Description = $"Package {pkg.Name} archived"
            });
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // 📌 إحصائية: أكثر الباقات استخداماً (Top Packages by Subscribers)
        [HttpGet("top/{count}")]
        public async Task<ActionResult<IEnumerable<object>>> GetTopPackages(int count = 5)
        {
            var top = await _context.Packages
                .Where(p => !p.Archived && p.Status == "active")
                .OrderByDescending(p => p.SubscriberCount)
                .Take(count)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.SubscriberCount,
                    p.Price,
                    p.DurationDays
                })
                .ToListAsync();

            return Ok(top);
        }

        // 📌 إحصائية: الباقات التي لا تحتوي مشتركين منذ أكثر من 30 يوم
        [HttpGet("inactive-over-30-days")]
        public async Task<ActionResult<IEnumerable<object>>> GetInactivePackages()
        {
            var dateThreshold = DateTime.Now.AddDays(-30);

            var inactive = await _context.Packages
                .Where(p => p.SubscriberCount == 0 && p.UpdatedAt < dateThreshold && !p.Archived)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.UpdatedAt,
                    p.Status
                })
                .ToListAsync();

            return Ok(inactive);
        }

        // 📌 إحصائية: عدد الباقات حسب الحالة (active / inactive / archived)
        [HttpGet("stats/status")]
        public async Task<ActionResult<object>> GetPackageStatusStats()
        {
            var stats = await _context.Packages
                .GroupBy(p => p.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            var archivedCount = await _context.Packages.CountAsync(p => p.Archived);

            return Ok(new { Stats = stats, Archived = archivedCount });
        }

        // 📌 إحصائية: مجموع إيرادات الباقات (على السعر × عدد المشتركين)
        [HttpGet("stats/revenue")]
        public async Task<ActionResult<object>> GetRevenue()
        {
            var revenue = await _context.Packages
                .Where(p => !p.Archived)
                .SumAsync(p => p.Price * p.SubscriberCount);

            return Ok(new { TotalRevenue = revenue });
        }

        // 📌 إحصائية: الباقات المجدولة للنشر في المستقبل
        [HttpGet("scheduled")]
        public async Task<ActionResult<IEnumerable<object>>> GetScheduledPackages()
        {
            var scheduled = await _context.Packages
                .Where(p => p.ScheduledAt != null && p.ScheduledAt > DateTime.Now)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.ScheduledAt,
                    p.Price,
                    p.DurationDays
                })
                .ToListAsync();

            return Ok(scheduled);
        }

    }
}
