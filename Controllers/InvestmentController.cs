using Microsoft.AspNetCore.Mvc;
using InvestmentApi.Models;
using System.Timers;
using System.Threading.Tasks;
using System.Linq;

namespace InvestmentApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvestmentController : ControllerBase
    {
        private static List<Security> _securities = new List<Security>
        {
            new Security
            {
                Id = 1,
                Ticker = "AAPL",
                Name = "Apple Inc.",
                CurrentPrice = 170.0m,
                BasePrice = 170.0m,
                MinPrice = 150.0m,
                MaxPrice = 190.0m,
                PriceChangeRange = 5.0m
            },
            new Security
            {
                Id = 2,
                Ticker = "GAZP",
                Name = "Газпром",
                CurrentPrice = 160.0m,
                BasePrice = 160.0m,
                MinPrice = 140.0m,
                MaxPrice = 180.0m,
                PriceChangeRange = 3.0m
            },
            new Security
            {
                Id = 3,
                Ticker = "TSLA",
                Name = "Tesla Inc.",
                CurrentPrice = 250.0m,
                BasePrice = 250.0m,
                MinPrice = 220.0m,
                MaxPrice = 280.0m,
                PriceChangeRange = 8.0m
            }
        };

        private static List<InvestmentOperation> _operations = new List<InvestmentOperation>();
        private static List<TriggerHistory> _triggerHistory = new List<TriggerHistory>();
        private static System.Timers.Timer _priceUpdateTimer;
        private static readonly Random _random = new Random();

        public InvestmentController()
        {
            // Запускаем таймер для изменения цен каждые 30 секунд
            if (_priceUpdateTimer == null)
            {
                _priceUpdateTimer = new System.Timers.Timer(30000);
                _priceUpdateTimer.Elapsed += UpdatePrices;
                _priceUpdateTimer.AutoReset = true;
                _priceUpdateTimer.Enabled = true;
                Console.WriteLine(" Таймер обновления цен запущен (каждые 30 секунд)");
            }
        }

        // Метод для обновления цен
        private void UpdatePrices(object sender, ElapsedEventArgs e)
        {
            lock (_securities)
            {
                foreach (var security in _securities)
                {
                    decimal change = (decimal)((_random.NextDouble() * 2 - 1) * (double)security.PriceChangeRange);
                    decimal newPrice = security.CurrentPrice + change;
                    newPrice = Math.Max(security.MinPrice, Math.Min(security.MaxPrice, newPrice));
                    security.CurrentPrice = Math.Round(newPrice, 2);
                }

                Console.WriteLine($" Цены обновлены: AAPL=${_securities[0].CurrentPrice}, GAZP=${_securities[1].CurrentPrice}, TSLA=${_securities[2].CurrentPrice}");

                // Проверяем триггеры при изменении цен
                Task.Run(() => CheckTriggersOnPriceChange());
            }
        }

        // Метод для автоматической проверки триггеров при изменении цен
        private async Task CheckTriggersOnPriceChange()
        {
            var newlyActivatedTriggers = new List<object>();

            foreach (var operation in _operations.Where(op => op.TargetBuyPrice.HasValue))
            {
                bool alreadyTriggered = _triggerHistory.Any(th => th.OperationId == operation.Id);
                if (alreadyTriggered) continue;

                var security = _securities.FirstOrDefault(s => s.Id == operation.SecurityId);
                if (security != null && security.CurrentPrice <= operation.TargetBuyPrice.Value)
                {
                    _triggerHistory.Add(new TriggerHistory
                    {
                        OperationId = operation.Id,
                        SecurityId = operation.SecurityId,
                        SecurityTicker = security.Ticker,
                        TriggeredPrice = security.CurrentPrice,
                        TargetPrice = operation.TargetBuyPrice.Value,
                        TriggeredAt = DateTime.Now
                    });

                    newlyActivatedTriggers.Add(new
                    {
                        OperationId = operation.Id,
                        SecurityTicker = security.Ticker,
                        SecurityName = security.Name,
                        CurrentPrice = security.CurrentPrice,
                        TargetPrice = operation.TargetBuyPrice.Value,
                        Message = $"АВТО: Сработал триггер! {security.Ticker} достиг ${security.CurrentPrice}"
                    });
                }
            }

            if (newlyActivatedTriggers.Any())
            {
                Console.WriteLine($" Автоматически сработало триггеров: {newlyActivatedTriggers.Count}");
            }
        }

        [HttpGet("securities")]
        public ActionResult<IEnumerable<Security>> GetSecurities()
        {
            Console.WriteLine(" Returning securities list");
            return Ok(_securities);
        }

        [HttpGet("operations")]
        public ActionResult<IEnumerable<object>> GetOperations()
        {
            var operationsWithDetails = _operations.Select(op =>
            {
                var security = _securities.FirstOrDefault(s => s.Id == op.SecurityId);
                bool isTriggered = _triggerHistory.Any(th => th.OperationId == op.Id);

                return new
                {
                    op.Id,
                    op.SecurityId,
                    SecurityTicker = security?.Ticker,
                    SecurityName = security?.Name,
                    op.Quantity,
                    op.PurchasePricePerShare,
                    op.Commission,
                    op.TotalCost,
                    op.TargetBuyPrice,
                    op.NotificationEmail,
                    HasTrigger = op.TargetBuyPrice.HasValue,
                    IsTriggered = isTriggered,
                    TriggeredAt = isTriggered ?
                        _triggerHistory.First(th => th.OperationId == op.Id).TriggeredAt : (DateTime?)null
                };
            });
            return Ok(operationsWithDetails);
        }

        [HttpPost("calculate")]
        public ActionResult<object> CalculateOperation([FromBody] CalculateRequest request)
        {
            if (request.Quantity <= 0 || request.PurchasePricePerShare <= 0 || request.Commission < 0)
            {
                return BadRequest("Некорректные данные для расчёта.");
            }

            // Создаем временную операцию для расчета
            var tempOperation = new InvestmentOperation
            {
                SecurityId = request.SecurityId,
                Quantity = request.Quantity,
                PurchasePricePerShare = request.PurchasePricePerShare,
                Commission = request.Commission,
                TargetBuyPrice = request.TargetBuyPrice
            };

            var result = new
            {
                TotalCost = tempOperation.TotalCost, // Используем вычисляемое свойство
                Quantity = tempOperation.Quantity,
                PricePerShare = tempOperation.PurchasePricePerShare,
                Commission = tempOperation.Commission,
                HasTrigger = tempOperation.TargetBuyPrice.HasValue,
                TriggerMessage = tempOperation.TargetBuyPrice.HasValue ?
                    $"Триггер установлен на цену: ${tempOperation.TargetBuyPrice}" :
                    "Триггер не установлен",
                Details = $"Количество: {tempOperation.Quantity} × Цена: ${tempOperation.PurchasePricePerShare} = ${tempOperation.Quantity * tempOperation.PurchasePricePerShare} + Комиссия: ${tempOperation.Commission} = Итого: ${tempOperation.TotalCost}"
            };

            Console.WriteLine($" Расчет стоимости: {result.Details}");
            return Ok(result);
        }

        // Класс для запроса расчета
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
            var securityExists = _securities.Any(s => s.Id == operation.SecurityId);
            if (!securityExists)
            {
                return BadRequest("Ценная бумага с указанным ID не найдена.");
            }

            operation.Id = _operations.Any() ? _operations.Max(op => op.Id) + 1 : 1;
            _operations.Add(operation);

            var security = _securities.First(s => s.Id == operation.SecurityId);
            string triggerMessage = "";
            bool triggerActivated = false;
            bool alreadyTriggered = _triggerHistory.Any(th => th.OperationId == operation.Id);

            // Проверка триггера (только если еще не срабатывал)
            if (operation.TargetBuyPrice.HasValue && !alreadyTriggered)
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

            var response = new
            {
                Operation = operation,
                Message = "Операция успешно добавлена!" + triggerMessage,
                TriggerActivated = triggerActivated,
                AlreadyTriggered = alreadyTriggered
            };

            return CreatedAtAction(nameof(GetOperations), new { id = operation.Id }, response);
        }

        [HttpPost("check-all-triggers")]
        public ActionResult<IEnumerable<object>> CheckAllTriggersForced()
        {
            var activatedTriggers = new List<object>();

            foreach (var operation in _operations.Where(op => op.TargetBuyPrice.HasValue))
            {
                bool alreadyTriggered = _triggerHistory.Any(th => th.OperationId == operation.Id);
                if (alreadyTriggered) continue;

                var security = _securities.FirstOrDefault(s => s.Id == operation.SecurityId);
                if (security != null && security.CurrentPrice <= operation.TargetBuyPrice.Value)
                {
                    _triggerHistory.Add(new TriggerHistory
                    {
                        OperationId = operation.Id,
                        SecurityId = operation.SecurityId,
                        SecurityTicker = security.Ticker,
                        TriggeredPrice = security.CurrentPrice,
                        TargetPrice = operation.TargetBuyPrice.Value,
                        TriggeredAt = DateTime.Now
                    });

                    activatedTriggers.Add(new
                    {
                        OperationId = operation.Id,
                        SecurityTicker = security.Ticker,
                        SecurityName = security.Name,
                        CurrentPrice = security.CurrentPrice,
                        TargetPrice = operation.TargetBuyPrice.Value,
                        Message = $"СРАБОТАЛ ТРИГГЕР! {security.Ticker}"
                    });
                }
            }

            return Ok(new
            {
                CheckedAt = DateTime.Now,
                ActivatedTriggers = activatedTriggers,
                TotalChecked = _operations.Count(op => op.TargetBuyPrice.HasValue),
                AlreadyTriggeredCount = _triggerHistory.Count
            });
        }

        [HttpGet("active-triggers")]
        public ActionResult<IEnumerable<object>> GetActiveTriggers()
        {
            var activeTriggers = new List<object>();

            foreach (var operation in _operations.Where(op => op.TargetBuyPrice.HasValue))
            {
                bool alreadyTriggered = _triggerHistory.Any(th => th.OperationId == operation.Id);
                if (alreadyTriggered) continue;

                var security = _securities.FirstOrDefault(s => s.Id == operation.SecurityId);
                if (security != null && security.CurrentPrice > operation.TargetBuyPrice.Value)
                {
                    activeTriggers.Add(new
                    {
                        OperationId = operation.Id,
                        SecurityTicker = security.Ticker,
                        SecurityName = security.Name,
                        CurrentPrice = security.CurrentPrice,
                        TargetPrice = operation.TargetBuyPrice.Value,
                        Message = $"Ожидаем падения {security.Ticker} до ${operation.TargetBuyPrice.Value}",
                        DistancePercent = Math.Round(((security.CurrentPrice - operation.TargetBuyPrice.Value) / operation.TargetBuyPrice.Value * 100), 2),
                        IsActive = true
                    });
                }
            }

            return Ok(activeTriggers);
        }

        [HttpGet("trigger-history")]
        public ActionResult<IEnumerable<TriggerHistory>> GetTriggerHistory()
        {
            // Возвращаем только необработанные триггеры
            var unprocessedTriggers = _triggerHistory
                .Where(t => !t.IsProcessed)
                .ToList();

            return Ok(unprocessedTriggers);
        }

        [HttpPost("mark-processed/{id}")]
        public IActionResult MarkTriggerAsProcessed(int OperationId)
        {
            var trigger = _triggerHistory.FirstOrDefault(t => t.OperationId == OperationId);
            if (trigger != null)
            {
                trigger.IsProcessed = true;
                return Ok();
            }
            return NotFound();
        }

        [HttpGet("current-prices")]
        public ActionResult<object> GetCurrentPrices()
        {
            var prices = _securities.Select(s => new
            {
                s.Id,
                s.Ticker,
                s.CurrentPrice,
                PriceChange = Math.Round(s.CurrentPrice - s.BasePrice, 2),
                PriceChangePercent = Math.Round((s.CurrentPrice - s.BasePrice) / s.BasePrice * 100, 2),
                LastUpdated = DateTime.Now
            });

            return Ok(new
            {
                Prices = prices,
                LastUpdate = DateTime.Now,
                TotalSecurities = _securities.Count
            });
        }
    }
}

public class TriggerHistory
{
    public int OperationId { get; set; }
    public int SecurityId { get; set; }
    public string SecurityTicker { get; set; }
    public decimal TriggeredPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public DateTime TriggeredAt { get; set; }
    public bool IsProcessed { get; set; } = false;
}
