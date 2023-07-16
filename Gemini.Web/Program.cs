using AyrA.AutoDI;
using Microsoft.OpenApi.Models;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var mvc = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    mvc.AddRazorRuntimeCompilation();
}

builder.Services.AddLogging(loggingBuilder =>
{
    //Standard log file
    loggingBuilder.AddFile(Path.Combine(AppContext.BaseDirectory, "Logs", "lastrun.log"), false);
});

//Enable CORS policy for the API
builder.Services.AddCors(options =>
{
    options.AddPolicy("API", policy => policy.AllowAnyOrigin().AllowAnyMethod());
});

//Autoregister services
AutoDIExtensions.DebugLogging = builder.Environment.IsDevelopment();
builder.Services.AutoRegisterCurrentAssembly();

//Add Swagger
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "Gemini browser API",
        Description = "API endpoint for working with the gemini protocol",
    });

    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Exception");
    app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");
}

//Enable API browser
app.UseSwagger();
app.UseSwaggerUI();

//Enable wwwroot
app.UseStaticFiles();

//Enable router system
app.UseRouting();

//CORS
app.UseCors();

//Map route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
