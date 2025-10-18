using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ML;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------
// Logging for debugging
// -----------------------------
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

// -----------------------------
// Controllers & JSON setup
// -----------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// -----------------------------
// Swagger configuration
// -----------------------------
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

// -----------------------------
// CORS for Swagger UI
// -----------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// -----------------------------
// Exception handling middleware
// -----------------------------
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

// -----------------------------
// Middleware
// -----------------------------
app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Drivee API v1");
});

// ============================================================
// EXISTING ENDPOINT — FORECAST
// ============================================================
app.MapPost("/predict", async ([FromBody] DriveeModel.ModelInput input) =>
{
    try
    {
        var prediction = DriveeModel.Predict(input, horizon: 1);
        return Results.Ok(prediction);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("PredictPrices")
.WithOpenApi();

// ============================================================
// NEW ENDPOINT — PRICE ANALYSIS WITH RISK CALCULATION & AGREEMENT
// ============================================================
app.MapPost("/analyze", ([FromBody] PriceAnalysisRequest request) =>
{

    try
    {
        // Проверка на положительные значения
        if (request.PriceStartLocal <= 0 || request.PriceBidLocal <= 0 || request.DriverRating <= 0)
        {
            return Results.BadRequest("Prices and Driver Rating must be positive values.");
        }

        // Проверка на допустимость статуса заказа
        if (request.IsDone != "done" && request.IsDone != "cancel")
        {
            return Results.BadRequest("IsDone must be 'done' or 'cancel'.");
        }

        // --- 1️⃣ Golden middle price ---
        double goldenMiddle = (request.PriceStartLocal + request.PriceBidLocal) / 2;

        // --- 2️⃣ Risk analysis ---
        double baseRisk = request.IsDone?.ToLower() == "cancel" ? 1.0 : 0.0;
        double adjustedRisk = baseRisk - (request.DriverRating / 5.0);

        // --- 3️⃣ Decision logic (passenger agreement) ---
        string passengerDecision = (request.PriceBidLocal <= goldenMiddle * 1.1)
            ? "agree"
            : "decline";

        // --- 4️⃣ Calculate percentage agreement ---
        double passengerAgreementPercentage = (request.PriceStartLocal / goldenMiddle) * 100;
        double driverAgreementPercentage = (request.PriceBidLocal / goldenMiddle) * 100;

        // --- 5️⃣ Final price calculation based on risk ---
        double finalPrice = PriceCalculator.CalculateFinalPrice(request.PriceStartLocal, request.PriceBidLocal, adjustedRisk);

        var result = new PriceAnalysisResult
        {
            PassengerPrice = request.PriceStartLocal,
            DriverPrice = request.PriceBidLocal,
            GoldenMiddlePrice = goldenMiddle,
            RiskFactor = Math.Round(adjustedRisk, 3),
            PassengerDecision = passengerDecision,
            PassengerAgreementPercentage = Math.Round(passengerAgreementPercentage, 2),
            DriverAgreementPercentage = Math.Round(driverAgreementPercentage, 2),
            FinalPrice = finalPrice
        };

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("AnalyzePrice")
.WithOpenApi();

// ============================================================
// Controllers (if any)
// ============================================================
app.MapControllers();

// ============================================================
// Run app
// ============================================================
app.Run();

// ============================================================
// DTOs (Request / Response models)
// ============================================================
public class PriceAnalysisRequest
{
    public double PriceStartLocal { get; set; }  // цена пассажира
    public double PriceBidLocal { get; set; }    // цена водителя
    public string IsDone { get; set; }           // done / cancel
    public double DriverRating { get; set; }     // рейтинг водителя
}

public class PriceAnalysisResult
{
    public double PassengerPrice { get; set; }
    public double DriverPrice { get; set; }
    public double GoldenMiddlePrice { get; set; }
    public double RiskFactor { get; set; }
    public string PassengerDecision { get; set; } // agree / decline
    public double PassengerAgreementPercentage { get; set; }
    public double DriverAgreementPercentage { get; set; }
    public double FinalPrice { get; set; }  // Цена на основе риска
}

// Method to calculate final price based on risk
public static class PriceCalculator
{
    public static double CalculateFinalPrice(double passengerPrice, double driverPrice, double riskFactor)
    {
        double finalPrice = (passengerPrice + driverPrice) / 2;
        finalPrice += finalPrice * riskFactor;  // Учитываем риск в расчете

        // Если цена водителя слишком высока, устанавливаем цену пассажира
        if (driverPrice > finalPrice * 1.1)
        {
            finalPrice = passengerPrice;
        }

        return finalPrice;
    }
}
