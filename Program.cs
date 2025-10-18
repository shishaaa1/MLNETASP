using Drivee_Model_WebApi2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ML;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.OpenApi.Models;
using static Drivee_Model_WebApi2.Drivee_Model;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(logging =>
{
logging.AddConsole();
logging.AddDebug();
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
{
options.JsonSerializerOptions.PropertyNamingPolicy = null;
options.JsonSerializerOptions.WriteIndented = true;
options.JsonSerializerOptions.DefaultIgnoreCondition =
    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
c.SwaggerDoc("v1", new OpenApiInfo
{
Title = "Drivee API",
Description = "ML.NET Forecast & Classification API",
Version = "v1"
});
var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
if (File.Exists(xmlPath))
{
c.IncludeXmlComments(xmlPath);
}
c.DocInclusionPredicate((docName, apiDesc) => true);
});

builder.Services.AddCors(options =>
{
options.AddPolicy("AllowAll", policy =>
{
policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
});
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
errorApp.Run(async context =>
{
var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
if (exception != null)
{
var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
logger.LogError(exception.Error, "Unhandled exception occurred");
await context.Response.WriteAsJsonAsync(new { error = exception.Error.Message });
}
});
});


app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI(c =>
{
c.SwaggerEndpoint("/swagger/v1/swagger.json", "Drivee API v1");
});


app.MapPost("/predict", async ([FromBody] Drivee_Model.ModelInput input) =>
{
try
{
var prediction = Drivee_Model.Predict(input);
return Results.Ok(prediction);
}
catch (Exception ex)
{
return Results.Problem(ex.Message);
}
}).WithName("PredictPrices")
.WithOpenApi();

app.MapGet("/model-status", () =>
{
try
{
var modelPath = Path.GetFullPath("Drivee_Model.mlnet");
var modelExists = File.Exists(modelPath);

return Results.Ok(new
{
modelExists = modelExists,
modelPath = modelPath,
status = modelExists ? "Ready" : "Model file not found",
lastModified = modelExists ? File.GetLastWriteTime(modelPath) : (DateTime?)null
});
}
catch (Exception ex)
{
return Results.Problem($"Error checking model status: {ex.Message}");
}
});
app.MapPost("/ai-predict", ([FromBody] FullPredictionRequest request) =>
{
    try
    {
        // Подготовка данных для модели
        var input = new Drivee_Model.ModelInput
        {
            Price_start_local = (float)request.PriceStartLocal,
            Price_bid_local = (float)request.PriceBidLocal,

            // Рейтинг водителя
            Driver_rating = (float)request.DriverRating,

            // Дистанции и время
            Distance_in_meters = (float)request.DistanceInMeters,
            Duration_in_seconds = (float)request.DurationInSeconds,
            Pickup_in_meters = (float)request.PickupInMeters,
            Pickup_in_seconds = (float)request.PickupInSeconds,

            // Категориальные данные
            Platform = request.Platform,
            Carmodel = request.CarModel,
            Carname = request.CarName
        };

        // Вызов модели
        var prediction = Drivee_Model.Predict(input);

        // Расчет результата
        bool willAccept = prediction.Score > 0.5f;
        double probability = Math.Round(prediction.Score, 4);

        // Простой ответ
        return Results.Ok(new
        {
            ЗаказПринят = willAccept,
            ВероятностьПринятия = Math.Round(probability, 4),
            Уверенность = probability > 0.8 ? "высокая" : probability > 0.7 ? "средняя" : "низкая",
            Рекомендация = willAccept ? "Предложить цену" : "Изменить цену"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Prediction error: {ex.Message}");
    }
});

app.MapPost("/ml-predict-acceptance", ([FromBody] MLAcceptanceRequest request) =>
{
try
{
// Создаем входные данные для модели
var modelInput = new Drivee_Model.ModelInput
{
Price_start_local = (float)request.PriceStartLocal,
Price_bid_local = (float)request.PriceBidLocal,
Driver_rating = (float)request.DriverRating,
Distance_in_meters = (float)request.DistanceInMeters,
Duration_in_seconds = (float)request.DurationInSeconds,
Pickup_in_meters = (float)request.PickupInMeters,
Pickup_in_seconds = (float)request.PickupInSeconds,
Platform = request.Platform,
Carmodel = request.CarModel,
Carname = request.CarName,
Driver_reg_date = DateTime.Now.ToString("yyyy-MM-dd")
};

        // Получаем предсказание от модели
        var prediction = Drivee_Model.Predict(modelInput);

        // Интерпретируем результат
        // Для бинарной классификации, Score[1] обычно представляет вероятность положительного класса ("done")
        bool isAccepted = prediction.Score > 0.5f; // Используем Score[1] для положительного класса
        double probability = isAccepted ? prediction.Score : (1.0 - prediction.Score);

        // Нормализуем вероятность к диапазону [0, 1]
        probability = Math.Max(0.0, Math.Min(1.0, probability));

var result = new MLAcceptanceResult
{
PriceBid = Math.Round(request.PriceBidLocal, 2),
PriceStart = Math.Round(request.PriceStartLocal, 2),
IsAccepted = isAccepted,
AcceptanceProbability = Math.Round(probability, 4),
ExpectedRevenue = Math.Round(request.PriceBidLocal * probability, 2),
Decision = isAccepted ? "accept" : "decline"
};

return Results.Ok(result);
}
catch (Exception ex)
{
return Results.Problem($"ML Prediction error: {ex.Message}");
}
})
.WithName("MLPredictAcceptance")
.WithOpenApi();
app.MapPost("/ai-optimal-price-between", ([FromBody] OptimalPriceBetweenRequest request) =>
{
    try
    {
        var input = new Drivee_Model.ModelInput
        {
            Price_start_local = (float)request.PriceStartLocal,
            Price_bid_local = (float)request.PriceBidLocal,
            Driver_rating = (float)request.DriverRating,
            Distance_in_meters = (float)request.DistanceInMeters,
            Duration_in_seconds = (float)request.DurationInSeconds,
            Platform = request.Platform ?? "android",
            Pickup_in_meters = (float)request.PickupInMeters,
            Pickup_in_seconds = 120,
            Driver_reg_date = "2020-01-01",
            Is_done = "done",
            Carmodel = "unknown",
            Carname = "unknown"
        };

        var prediction = Drivee_Model.Predict(input);
        var bestPrice = prediction.Price_bid_local;

        // Определяем минимальную и максимальную цену
        var minPrice = Math.Min(request.PriceStartLocal, request.PriceBidLocal);
        var maxPrice = Math.Max(request.PriceStartLocal, request.PriceBidLocal);

        // Создаем экземпляр OptimalPrice
        var optimalPriceCalculator = new OptimalPrice();
        // Вычисляем оптимальную цену с помощью формулы
        var optimalPrice = optimalPriceCalculator.CalculateOptimalPrice(minPrice, maxPrice, bestPrice, request.DriverRating, request.DistanceInMeters, request.DurationInSeconds);

        // Получаем вероятность принятия с учетом оптимальной цены
        var acceptanceProb = OptimalPrice.GetAcceptanceProbability(request.PriceStartLocal, optimalPrice, request.DriverRating);

        return Results.Ok(new
        {
            ЦенаПассажира = Math.Round(request.PriceStartLocal, 2),
            ЦенаВодителя = Math.Round(request.PriceBidLocal, 2),
            ОптимальнаяЦена = Math.Round(optimalPrice, 2),
            ВероятностьПринятия = Math.Round(acceptanceProb * 100, 1) + "%"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Ошибка ИИ: {ex.Message}");
    }
});

// ============================================================
// OPTIMAL PRICE FINDER ENDPOINT
// ============================================================
app.MapPost("/find-optimal-price", async ([FromBody] OptimalPriceRequest request) =>
{
try
{
var pricePoints = new List<double>();
var analyses = new List<PricePointAnalysis>();

// Генерируем диапазон цен для анализа (от 50% до 150% от начальной цены)
for (double ratio = 0.5; ratio <= 1.5; ratio += 0.05)
{
pricePoints.Add(request.PriceStartLocal * ratio);
}

double maxRevenue = 0;
double optimalPrice = request.PriceStartLocal;
double optimalProbability = 0;

foreach (var price in pricePoints)
{
// Создаем запрос для текущей цены
var predictionRequest = new MLAcceptanceRequest
{
PriceBidLocal = price,
PriceStartLocal = request.PriceStartLocal,
DriverRating = request.DriverRating,
DistanceInMeters = request.DistanceInMeters,
DurationInSeconds = request.DurationInSeconds,
PickupInMeters = request.PickupInMeters,
PickupInSeconds = request.PickupInSeconds,
Platform = request.Platform,
CarModel = request.CarModel,
CarName = request.CarName
};

// Получаем предсказание через существующий эндпоинт
var httpClient = new HttpClient();
var response = await httpClient.PostAsJsonAsync("http://localhost:63890/ml-predict-acceptance", predictionRequest);

if (response.IsSuccessStatusCode)
{
var predictionResult = await response.Content.ReadFromJsonAsync<MLAcceptanceResult>();

var revenue = price * predictionResult.AcceptanceProbability;
var analysis = new PricePointAnalysis
{
Price = Math.Round(price, 2),
AcceptanceProbability = predictionResult.AcceptanceProbability,
ExpectedRevenue = Math.Round(revenue, 2),
IsOptimal = revenue > maxRevenue
};

analyses.Add(analysis);

if (revenue > maxRevenue)
{
maxRevenue = revenue;
optimalPrice = price;
optimalProbability = predictionResult.AcceptanceProbability;
}
}
}

var result = new OptimalPriceResult
{
OptimalPrice = Math.Round(optimalPrice, 2),
OptimalProbability = Math.Round(optimalProbability, 4),
MaxExpectedRevenue = Math.Round(maxRevenue, 2),
PriceStartLocal = Math.Round(request.PriceStartLocal, 2),
AllOptions = analyses.OrderByDescending(a => a.ExpectedRevenue).ToList()
};

return Results.Ok(result);
}
catch (Exception ex)
{
return Results.Problem($"Optimal price search error: {ex.Message}");
}
})
.WithName("FindOptimalPrice")
.WithOpenApi();

// ============================================================
// MODEL RETRAINING ENDPOINT
// ============================================================
app.MapPost("/retrain-model", () =>
{
try
{
// Путь для сохранения новой модели
var outputModelPath = Path.GetFullPath("Drivee_Model.mlnet");

// Переобучаем модель
Drivee_Model.Train(outputModelPath);

return Results.Ok(new
{
message = "Model retrained successfully",
modelPath = outputModelPath,
timestamp = DateTime.UtcNow
});
}
catch (Exception ex)
{
return Results.Problem($"Model training error: {ex.Message}");
}
})
.WithName("RetrainModel")
.WithOpenApi();

app.MapControllers();
app.Run();


public class MLAcceptanceRequest
{
    public double PriceBidLocal { get; set; }
    public double PriceStartLocal { get; set; }
    public double DriverRating { get; set; }
    public double DistanceInMeters { get; set; } = 0;
    public double DurationInSeconds { get; set; } = 0;
    public double PickupInMeters { get; set; } = 0;
    public double PickupInSeconds { get; set; } = 0;
    public string Platform { get; set; } = "android";
    public string CarModel { get; set; } = "unknown";
    public string CarName { get; set; } = "unknown";
}

public class MLAcceptanceResult
{
    public double PriceBid { get; set; }
    public double PriceStart { get; set; }
    public bool IsAccepted { get; set; }
    public double AcceptanceProbability { get; set; }
    public double ExpectedRevenue { get; set; }
    public string Decision { get; set; }
}

public class OptimalPriceRequest
{
    public double PriceStartLocal { get; set; }
    public double DriverRating { get; set; }
    public double DistanceInMeters { get; set; } = 0;
    public double DurationInSeconds { get; set; } = 0;
    public double PickupInMeters { get; set; } = 0;
    public double PickupInSeconds { get; set; } = 0;
    public string Platform { get; set; } = "android";
    public string CarModel { get; set; } = "unknown";
    public string CarName { get; set; } = "unknown";
}

public class PricePointAnalysis
{
    public double Price { get; set; }
    public double AcceptanceProbability { get; set; }
    public double ExpectedRevenue { get; set; }
    public bool IsOptimal { get; set; }
}

public class OptimalPriceResult
{
    public double OptimalPrice { get; set; }
    public double OptimalProbability { get; set; }
    public double MaxExpectedRevenue { get; set; }
    public double PriceStartLocal { get; set; }
    public List<PricePointAnalysis> AllOptions { get; set; }
}
public class PredictionRequest
{
    public double PriceStart { get; set; }
    public double PriceBid { get; set; }
    public double DriverRating { get; set; }
    public double Distance { get; set; } = 0;
    public double Duration { get; set; } = 0;
}
public class FullPredictionRequest
{
    // Основные цены
    public double PriceStartLocal { get; set; }
    public double PriceBidLocal { get; set; }

    // Рейтинг и метрики водителя
    public double DriverRating { get; set; }

    // Дистанции и время
    public double DistanceInMeters { get; set; }
    public double DurationInSeconds { get; set; }
    public double PickupInMeters { get; set; }
    public double PickupInSeconds { get; set; }

    // Категориальные данные
    public string Platform { get; set; } = "android";
    public string CarModel { get; set; } = "unknown";
    public string CarName { get; set; } = "unknown";

}
public class OptimalPriceBetweenRequest
{

    public double PriceStartLocal { get; set; }  // Цена пассажира
    public double PriceBidLocal { get; set; }    // Цена водителя
    public double DriverRating { get; set; }
    public double DistanceInMeters { get; set; }
    public double DurationInSeconds { get; set; } = 600;
    public double PickupInMeters { get; set; } = 1000;
    public string Platform { get; set; } = "android";
}
public class OptimalPrice
{
    public double CalculateOptimalPrice(double minPrice, double maxPrice, double bestPrice, double driverRating, double distance, double duration)
    {
        // Весовые коэффициенты для различных факторов
        double driverRatingWeight = 0.2; // Влияние рейтинга водителя
        double distanceWeight = 0.3; // Влияние дистанции
        double durationWeight = 0.2; // Влияние продолжительности поездки
        double baseAdjustment = 0.3; // Базовая корректировка на основе минимальной и максимальной цены

        // Корректировка с учетом рейтинга водителя
        double ratingAdjustment = driverRating * driverRatingWeight;

        // Корректировка с учетом дистанции
        double distanceAdjustment = (distance / 1000) * distanceWeight; // Переводим дистанцию в километры

        // Корректировка с учетом длительности поездки
        double durationAdjustment = (duration / 60) * durationWeight; // Переводим время в минуты

        // Определяем корректированную цену
        double adjustedPrice = bestPrice + ratingAdjustment + distanceAdjustment + durationAdjustment;

        // Ограничиваем цену в пределах минимальной и максимальной
        return Math.Max(minPrice, Math.Min(maxPrice, adjustedPrice));
    }
    public static double GetAcceptanceProbability(double priceStart, double priceBid, double driverRating)
    {
        try
        {
            var input = new Drivee_Model.ModelInput
            {
                Price_start_local = (float)priceStart,
                Price_bid_local = (float)priceBid,
                Driver_rating = (float)driverRating,
                Distance_in_meters = 5000,
                Platform = "android",
                Is_done = "done"
            };

            var prediction = Drivee_Model.Predict(input);
            return Math.Max(0.1, Math.Min(0.99, prediction.Score));
        }
        catch
        {
            double ratio = priceBid / priceStart;
            double ratingFactor = driverRating / 5.0;
            return Math.Max(0.1, Math.Min(0.99, 1.0 / (1.0 + Math.Exp((ratio - 1.0) * 3)) * (0.8 + 0.2 * ratingFactor)));
        }
    }

}