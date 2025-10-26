using System;
using System.Linq;
using System.Threading.Tasks;
using ASM_1.Data;
using ASM_1.Models.Food;
using ASM_1.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ASM_1.Controllers
{
    [Route("{tableCode:regex(^((?!admin$).)*$)}")]
    public class FoodController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly TableCodeService _tableCodeService;
        private readonly UserSessionService _userSessionService;
        private readonly ITableTrackerService _tableTracker;

        public FoodController(ApplicationDbContext context, TableCodeService tableCodeService, UserSessionService userSessionService, ITableTrackerService tableTracker)
        {
            _context = context;
            _tableCodeService = tableCodeService;
            _userSessionService = userSessionService;
            _tableTracker = tableTracker;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string tableCode)
        {
            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null)
            {
                return RedirectToAction("InvalidTable");
            }

            var table = await _context.Tables.FirstOrDefaultAsync(b => b.TableId == tableId);
            if (table == null)
            {
                return RedirectToAction("InvalidTable");
            }

            var sessionId = _userSessionService.GetOrCreateUserSessionId(tableCode);
            _tableTracker.AddGuest(table.TableId, sessionId);

            var now = DateTime.UtcNow;
            decimal? dynamicFactor = null;
            string? dynamicLabel = null;
            if (PricingHelper.TryGetDynamicFactor(table, now, out var factor, out var label))
            {
                dynamicFactor = factor;
                dynamicLabel = label;
            }

            var model = new MenuOverviewViewModel
            {
                Categories = await _context.Categories.ToListAsync(),
                Combos = await _context.Combos.Include(c => c.ComboDetails!).ThenInclude(cd => cd.FoodItem).ToListAsync(),
                FoodItems = await _context.FoodItems.Include(f => f.Category).ToListAsync(),
                DynamicPricingLabel = dynamicLabel,
                DynamicPriceFactor = dynamicFactor,
                TableName = table.TableName
            };

            if (dynamicFactor.HasValue && dynamicFactor.Value > 0 && dynamicFactor.Value != 1m)
            {
                foreach (var food in model.FoodItems)
                {
                    var basePrice = PricingHelper.CalculateEffectiveBasePrice(food);
                    model.FoodPriceOverrides[food.FoodItemId] = PricingHelper.ApplyDynamicFactor(basePrice, dynamicFactor);
                }

                foreach (var combo in model.Combos)
                {
                    var comboPrice = PricingHelper.CalculateComboPrice(combo);
                    model.ComboPriceOverrides[combo.ComboId] = PricingHelper.ApplyDynamicFactor(comboPrice, dynamicFactor);
                }
            }

            HttpContext.Session.SetString("CurrentTableCode", tableCode);
            ViewBag.DynamicPricingLabel = dynamicLabel;
            ViewBag.DynamicPriceFactor = dynamicFactor;
            ViewBag.CurrentTableName = table.TableName;

            return View(model);
        }

        [HttpGet("/food/{slug}")]
        public IActionResult Detail(string tableCode, string slug)
        {
            var tableId = _tableCodeService.DecryptTableCode(tableCode);
            if (tableId == null) return RedirectToAction("InvalidTable");

            var item = _context.FoodItems
                               .AsNoTracking()
                               .FirstOrDefault(f => f.Slug == slug);
            if (item == null) return NotFound();

            decimal basePrice = PricingHelper.CalculateEffectiveBasePrice(item);

            var migs = _context.MenuItemOptionGroups
                               .AsNoTracking()
                               .Where(m => m.FoodItemId == item.FoodItemId)
                               .Include(m => m.OptionGroup)
                                   .ThenInclude(g => g.Values)
                               .OrderBy(m => m.DisplayOrder)
                               .AsSplitQuery()
                               .ToList();

            var valueOverrides = _context.MenuItemOptionValues
                                         .AsNoTracking()
                                         .Where(v => v.FoodItemId == item.FoodItemId)
                                         .ToList();

            var groups = migs.Select(m =>
            {
                var g = m.OptionGroup;

                bool required = m.Required ?? g.Required;
                int min = m.MinSelect ?? g.MinSelect;
                int max = m.MaxSelect ?? g.MaxSelect;

                var values = g.Values
                              .Select(v =>
                              {
                                  var ov = valueOverrides.FirstOrDefault(o => o.OptionValueId == v.OptionValueId);
                                  return new ProductDetailViewModel.ValueVM
                                  {
                                      ValueId = v.OptionValueId,
                                      Name = v.Name,
                                      Code = v.Code,
                                      PriceDelta = ov?.PriceDeltaOverride ?? v.PriceDelta,
                                      IsDefault = ov?.IsDefaultOverride ?? v.IsDefault,
                                      IsHidden = ov?.IsHidden ?? false,
                                      SortOrder = ov?.SortOrderOverride ?? v.SortOrder,
                                      ScaleValue = v.ScaleValue
                                  };
                              })
                              .Where(v => !v.IsHidden)
                              .OrderBy(v => v.SortOrder)
                              .ToList();

                return new ProductDetailViewModel.GroupVM
                {
                    GroupId = g.OptionGroupId,
                    Name = g.Name,
                    GroupType = g.GroupType,
                    Required = required,
                    Min = min,
                    Max = max,
                    ScaleMin = g.ScaleMin,
                    ScaleMax = g.ScaleMax,
                    ScaleStep = g.ScaleStep,
                    ScaleUnit = g.ScaleUnit,
                    Values = values
                };
            })
            .ToList();

            var table = _context.Tables.AsNoTracking().FirstOrDefault(b => b.TableId == tableId);
            decimal finalPrice = basePrice;
            decimal? dynamicFactor = null;
            string? dynamicLabel = null;
            if (PricingHelper.TryGetDynamicFactor(table, DateTime.UtcNow, out var factor, out var label))
            {
                dynamicFactor = factor;
                dynamicLabel = label;
                finalPrice = PricingHelper.ApplyDynamicFactor(basePrice, dynamicFactor);
            }

            var vm = new ProductDetailViewModel
            {
                Item = item,
                BasePriceEffective = basePrice,
                FinalPrice = finalPrice,
                Groups = groups,
                DynamicPriceFactor = dynamicFactor,
                DynamicPricingLabel = dynamicLabel
            };

            return View(vm);
        }
    }
}
