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






app.MapPost("/find-optimal-price", async ([FromBody] OptimalPriceRequest request) =>
{
    try
    {
 
        var driverInput = new MLModel1.ModelInput
        {
            Price_start_local = (float)request.PriceStartLocal,
            Price_bid_local = (float)request.PriceBidLocal,
            Driver_rating = (float)request.DriverRating,
            Distance_in_meters = (float)request.DistanceInMeters,
            Duration_in_seconds = (float)request.DurationInSeconds,
            Pickup_in_meters = (float)request.PickupInMeters,
            Pickup_in_seconds = (float)request.PickupInSeconds,
           
        };

        var driverPrediction = MLModel1.Predict(driverInput);
        double driverPrice = driverPrediction.Score;

        var passengerInput = new MLModel.ModelInput
        {
            Price_start_local = (float)request.PriceStartLocal,
            Price_bid_local = (float)request.PriceBidLocal,
            Driver_rating = (float)request.DriverRating,
            Distance_in_meters = (float)request.DistanceInMeters,
            Duration_in_seconds = (float)request.DurationInSeconds,
            Pickup_in_meters = (float)request.PickupInMeters,
            Pickup_in_seconds = (float)request.PickupInSeconds,
            
        };

        var passengerPrediction = MLModel.Predict(passengerInput);
        double passengerPrice = passengerPrediction.Score;


        double optimalPrice = (passengerPrice + driverPrice) / 2; 

        // 4. Возвращаем результаты
        return Results.Ok(new
        {
            ЦенаПассажира = Math.Round(passengerPrice, 2),
            ЦенаВодителя = Math.Round(driverPrice, 2),
            ОптимальнаяЦена = Math.Round(optimalPrice, 2),
            ЦенкаПас = passengerPrediction,
            ЦенкаВод = driverPrediction
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Ошибка при вычислении оптимальной цены: {ex.Message}");
    }
})
.WithName("FindOptimalPrice")
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
    public double PriceStartLocal { get; set; }  // Цена пассажира
    public double PriceBidLocal { get; set; }    // Цена водителя
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