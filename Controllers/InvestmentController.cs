using Microsoft.AspNetCore.Mvc;
using InvestmentApi.Models;
using System.Timers;
using Timer = System.Timers.Timer;

namespace InvestmentApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvestmentController : ControllerBase
    {
        private static readonly List<Security> _securities = new()
        {
            new() { Id = 1, Ticker = "AAPL", Name = "Apple Inc.", CurrentPrice = 170.0m, BasePrice = 170.0m, MinPrice = 150.0m, MaxPrice = 190.0m, PriceChangeRange = 5.0m },
            new() { Id = 2, Ticker = "GAZP", Name = "Газпром", CurrentPrice = 160.0m, BasePrice = 160.0m, MinPrice = 140.0m, MaxPrice = 180.0m, PriceChangeRange = 3.0m },
            new() { Id = 3, Ticker = "TSLA", Name = "Tesla Inc.", CurrentPrice = 250.0m, BasePrice = 250.0m, MinPrice = 220.0m, MaxPrice = 280.0m, PriceChangeRange = 8.0m }
        };

        private static readonly List<InvestmentOperation> _operations = new();
        private static readonly List<TriggerHistory> _triggerHistory = new();
        private static readonly Random _random = new();

        // Статический таймер
        private static Timer? _priceUpdateTimer;
        private static bool _timerInitialized = false;
        private static readonly object _timerLock = new object();

        public class TriggerHistory
        {
            public int OperationId { get; set; }
            public int SecurityId { get; set; }
            public string SecurityTicker { get; set; } = string.Empty;
            public decimal TriggeredPrice { get; set; }
            public decimal TargetPrice { get; set; }
            public DateTime TriggeredAt { get; set; }
            public bool IsProcessed { get; set; } = false;
        }

        public InvestmentController()
        {
            InitializeTimer();
        }

        private static void InitializeTimer()
        {
            lock (_timerLock)
            {
                if (!_timerInitialized)
                {
                    _priceUpdateTimer = new Timer(30000); // 30 секунд
                    _priceUpdateTimer.Elapsed += UpdatePrices;
                    _priceUpdateTimer.AutoReset = true;
                    _priceUpdateTimer.Enabled = true;
                    _timerInitialized = true;

                    Console.WriteLine("Таймер обновления цен запущен (каждые 30 секунд)");

                    // Запускаем первое обновление сразу
                    Task.Run(() => UpdatePrices(null, null));
                }
            }
        }

        private static void UpdatePrices(object? sender, ElapsedEventArgs? e)
        {
            try
            {
                lock (_securities)
                {
                    _securities.ForEach(s =>
                        s.CurrentPrice = Math.Round(Math.Max(s.MinPrice,
                            Math.Min(s.MaxPrice, s.CurrentPrice + (decimal)((_random.NextDouble() * 2 - 1) * (double)s.PriceChangeRange))), 2));

                    Console.WriteLine($"Цены обновлены: {string.Join(", ", _securities.Select(s => $"{s.Ticker}=${s.CurrentPrice}"))}");
                    CheckTriggers();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обновлении цен: {ex.Message}");
            }
        }

        private static void CheckTriggers()
        {
            try
            {
                var newTriggers = _operations
                    .Where(op => op.TargetBuyPrice.HasValue && !_triggerHistory.Any(th => th.OperationId == op.Id))
                    .Select(op => (op, security: _securities.FirstOrDefault(s => s.Id == op.SecurityId)))
                    .Where(t => t.security?.CurrentPrice <= t.op.TargetBuyPrice)
                    .Select(t => new TriggerHistory
                    {
                        OperationId = t.op.Id,
                        SecurityId = t.op.SecurityId,
                        SecurityTicker = t.security?.Ticker ?? string.Empty,
                        TriggeredPrice = t.security?.CurrentPrice ?? 0,
                        TargetPrice = t.op.TargetBuyPrice ?? 0,
                        TriggeredAt = DateTime.Now
                    }).ToList();

                newTriggers.ForEach(th => _triggerHistory.Add(th));
                if (newTriggers.Any())
                    Console.WriteLine($"Автоматически сработало триггеров: {newTriggers.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке триггеров: {ex.Message}");
            }
        }

        [HttpGet("securities")]
        public ActionResult<IEnumerable<Security>> GetSecurities()
        {
            lock (_securities)
            {
                return Ok(_securities);
            }
        }

        [HttpGet("operations")]
        public ActionResult<IEnumerable<object>> GetOperations()
        {
            lock (_securities)
                lock (_operations)
                    lock (_triggerHistory)
                    {
                        return Ok(_operations.Select(op =>
                        {
                            var security = _securities.FirstOrDefault(s => s.Id == op.SecurityId);
                            var triggered = _triggerHistory.FirstOrDefault(th => th.OperationId == op.Id);
                            return new
                            {
                                op.Id,
                                op.SecurityId,
                                SecurityTicker = security?.Ticker,
                                op.Quantity,
                                op.PurchasePricePerShare,
                                op.Commission,
                                op.TotalCost,
                                op.TargetBuyPrice,
                                op.NotificationEmail,
                                IsTriggered = triggered != null,
                                TriggeredAt = triggered?.TriggeredAt
                            };
                        }));
                    }
        }

        [HttpPost("calculate")]
        public ActionResult<object> CalculateOperation([FromBody] CalculateRequest request)
        {
            if (request.Quantity <= 0 || request.PurchasePricePerShare <= 0 || request.Commission < 0)
                return BadRequest("Некорректные данные для расчёта.");

            var total = request.Quantity * request.PurchasePricePerShare + request.Commission;
            return Ok(new
            {
                TotalCost = total,
                request.Quantity,
                PricePerShare = request.PurchasePricePerShare,
                request.Commission,
                HasTrigger = request.TargetBuyPrice.HasValue,
                TriggerMessage = request.TargetBuyPrice.HasValue ? $"Триггер установлен на цену: ${request.TargetBuyPrice}" : "Триггер не установлен",
                Details = $"{request.Quantity} × ${request.PurchasePricePerShare} = ${request.Quantity * request.PurchasePricePerShare} + Комиссия: ${request.Commission} = ${total}"
            });
        }

        public class CalculateRequest
        {
            public int SecurityId { get; set; }
            public int Quantity { get; set; }
            public decimal PurchasePricePerShare { get; set; }
            public decimal Commission { get; set; }
            public decimal? TargetBuyPrice { get; set; }
        }

        [HttpPost("operation")]
        public IActionResult AddOperation([FromBody] InvestmentOperation operation)
        {
            if (operation.NotificationEmail == null)
                return BadRequest("Email для уведомлений обязателен.");

            lock (_securities)
            {
                if (!_securities.Any(s => s.Id == operation.SecurityId))
                    return BadRequest("Ценная бумага с указанным ID не найдена.");
            }

            lock (_operations)
                lock (_triggerHistory)
                {
                    operation.Id = _operations.Any() ? _operations.Max(op => op.Id) + 1 : 1;
                    _operations.Add(operation);

                    var security = _securities.First(s => s.Id == operation.SecurityId);
                    var triggerMessage = "";
                    var triggerActivated = false;

                    if (operation.TargetBuyPrice.HasValue && !_triggerHistory.Any(th => th.OperationId == operation.Id))
                    {
                        if (security.CurrentPrice <= operation.TargetBuyPrice.Value)
                        {
                            triggerMessage = $" ВНИМАНИЕ: Триггер сработал сразу! Текущая цена ({security.CurrentPrice}) ниже целевой ({operation.TargetBuyPrice}).";
                            triggerActivated = true;
                            _triggerHistory.Add(new TriggerHistory
                            {
                                OperationId = operation.Id,
                                SecurityId = operation.SecurityId,
                                SecurityTicker = security.Ticker,
                                TriggeredPrice = security.CurrentPrice,
                                TargetPrice = operation.TargetBuyPrice.Value,
                                TriggeredAt = DateTime.Now
                            });
                        }
                    }

                    return CreatedAtAction(nameof(GetOperations), new { id = operation.Id }, new
                    {
                        Operation = operation,
                        Message = "Операция успешно добавлена!" + triggerMessage,
                        TriggerActivated = triggerActivated
                    });
                }
        }

        [HttpPost("check-all-triggers")]
        public ActionResult<object> CheckAllTriggersForced()
        {
            lock (_securities)
                lock (_operations)
                    lock (_triggerHistory)
                    {
                        var activatedTriggers = _operations
                            .Where(op => op.TargetBuyPrice.HasValue && !_triggerHistory.Any(th => th.OperationId == op.Id))
                            .Select(op => (op, security: _securities.FirstOrDefault(s => s.Id == op.SecurityId)))
                            .Where(t => t.security?.CurrentPrice <= t.op.TargetBuyPrice)
                            .Select(t => new
                            {
                                t.op.Id,
                                SecurityTicker = t.security?.Ticker ?? string.Empty,
                                CurrentPrice = t.security?.CurrentPrice ?? 0,
                                TargetPrice = t.op.TargetBuyPrice ?? 0,
                                Message = $"СРАБОТАЛ ТРИГГЕР! {t.security?.Ticker}"
                            }).ToList();

                        activatedTriggers.ForEach(t => _triggerHistory.Add(new TriggerHistory
                        {
                            OperationId = t.Id,
                            SecurityId = _securities.First(s => s.Ticker == t.SecurityTicker).Id,
                            SecurityTicker = t.SecurityTicker,
                            TriggeredPrice = t.CurrentPrice,
                            TargetPrice = t.TargetPrice,
                            TriggeredAt = DateTime.Now
                        }));

                        return Ok(new
                        {
                            CheckedAt = DateTime.Now,
                            ActivatedTriggers = activatedTriggers,
                            TotalChecked = _operations.Count(op => op.TargetBuyPrice.HasValue)
                        });
                    }
        }

        [HttpGet("active-triggers")]
        public ActionResult<IEnumerable<object>> GetActiveTriggers()
        {
            lock (_securities)
                lock (_operations)
                    lock (_triggerHistory)
                    {
                        return Ok(
                            _operations.Where(op => op.TargetBuyPrice.HasValue && !_triggerHistory.Any(th => th.OperationId == op.Id))
                                .Select(op => (op, security: _securities.FirstOrDefault(s => s.Id == op.SecurityId)))
                                .Where(t => t.security?.CurrentPrice > t.op.TargetBuyPrice)
                                .Select(t => new
                                {
                                    t.op.Id,
                                    SecurityTicker = t.security?.Ticker ?? string.Empty,
                                    CurrentPrice = t.security?.CurrentPrice ?? 0,
                                    TargetPrice = t.op.TargetBuyPrice ?? 0,
                                    Message = $"Ожидаем падения {t.security?.Ticker} до ${t.op.TargetBuyPrice.Value}",
                                    DistancePercent = t.op.TargetBuyPrice.HasValue ?
                                        Math.Round(((t.security?.CurrentPrice ?? 0 - t.op.TargetBuyPrice.Value) / t.op.TargetBuyPrice.Value * 100), 2) : 0
                                }));
                    }
        }

        [HttpGet("trigger-history")]
        public ActionResult<IEnumerable<TriggerHistory>> GetTriggerHistory()
        {
            lock (_triggerHistory)
            {
                return Ok(_triggerHistory.Where(t => !t.IsProcessed));
            }
        }

        [HttpPost("mark-processed/{operationId}")]
        public IActionResult MarkTriggerAsProcessed(int operationId)
        {
            lock (_triggerHistory)
            {
                var trigger = _triggerHistory.FirstOrDefault(t => t.OperationId == operationId);
                if (trigger == null) return NotFound();
                trigger.IsProcessed = true;
                return Ok();
            }
        }

        [HttpGet("current-prices")]
        public ActionResult<object> GetCurrentPrices()
        {
            lock (_securities)
            {
                return Ok(new
                {
                    Prices = _securities.Select(s => new
                    {
                        s.Id,
                        s.Ticker,
                        s.CurrentPrice,
                        PriceChange = Math.Round(s.CurrentPrice - s.BasePrice, 2),
                        PriceChangePercent = Math.Round((s.CurrentPrice - s.BasePrice) / s.BasePrice * 100, 2)
                    }),
                    LastUpdate = DateTime.Now
                });
            }
        }
    }
}
