using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ML;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;
using Drivee_Model_WebApi2;


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


app.MapPost("/predict", async ([FromBody] DriveeModel.ModelInput input) =>
{
    try
    {
        var prediction = DriveeModel.Predict(input);
        return Results.Ok(prediction);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("PredictPrices")
.WithOpenApi();


app.MapControllers();

app.Run();




